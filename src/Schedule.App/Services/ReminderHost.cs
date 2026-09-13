using System.Windows.Threading;
using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Templating;

namespace Schedule.App.Services;

/// <summary>
/// 提醒调度宿主：周期性检查到点的提醒档位，负责提示音、语音播报与弹窗，
/// 并处理「稍后提醒」。一条日程的多档提醒（30/15/5 分钟前等）各自独立触发。
/// </summary>
public sealed class ReminderHost : IDisposable
{
    private sealed class SnoozeEntry
    {
        public required DueReminder Due { get; init; }

        public required DateTime FireAt { get; init; }
    }

    private readonly AppHost _host;
    private readonly DispatcherTimer _timer;
    private readonly List<SnoozeEntry> _snoozes = new();
    private DateTime _lastCheck;
    private bool _disposed;

    public ReminderHost(AppHost host)
    {
        _host = host;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>有提醒档位到点（参数含日程、发生时间与档位）。</summary>
    public event EventHandler<DueReminder>? ReminderDue;

    /// <summary>调度状态发生变化（用于刷新界面倒计时）。</summary>
    public event EventHandler? Updated;

    public int IntervalSeconds
    {
        get => (int)_timer.Interval.TotalSeconds;
        set => _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(value, 5, 300));
    }

    public void Start()
    {
        _lastCheck = DateTime.Now;
        _timer.Start();
        Tick();
    }

    public void Stop() => _timer.Stop();

    /// <summary>安排一次「稍后提醒」。</summary>
    public void Snooze(DueReminder due, int minutes)
    {
        var entry = new SnoozeEntry
        {
            Due = due,
            FireAt = DateTime.Now.AddMinutes(Math.Max(1, minutes)),
        };

        _snoozes.Add(entry);
        AppLog.Info("稍后提醒：" + due.Item.Title + " -> " + entry.FireAt.ToString("HH:mm"));
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DateTime> PendingSnoozeTimes => _snoozes.Select(s => s.FireAt).ToList();

    public void Tick()
    {
        if (_disposed) return;

        try
        {
            CheckDueReminders();
            CheckSnoozes();
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("提醒检查失败", ex);
        }
    }

    private void CheckDueReminders()
    {
        var now = DateTime.Now;
        var from = _lastCheck;

        // 休眠 / 关机唤醒后避免一次性弹出大量历史提醒
        if (now - from > TimeSpan.FromHours(2))
        {
            AppLog.Warn("检测到时间跳跃（可能刚从休眠恢复），仅补发最近 2 小时内的提醒");
            from = now.AddHours(-2);
        }

        if (from >= now) from = now.AddSeconds(-1);
        _lastCheck = now;

        var items = _host.Repository.Snapshot();
        var due = ReminderPlanner.CollectDue(items, from, now);
        if (due.Count == 0) return;

        foreach (var reminder in due)
        {
            ReminderPlanner.MarkFired(reminder.Item, reminder);
            AppLog.Info("触发提醒：" + reminder.Item.Title + " @ " + reminder.Occurrence.Start.ToString("yyyy-MM-dd HH:mm")
                + "（" + reminder.Plan.OffsetText + "）");
            Fire(reminder);
        }

        _host.Repository.MarkDirty();
        _host.Repository.Save();
    }

    private void CheckSnoozes()
    {
        if (_snoozes.Count == 0) return;

        var now = DateTime.Now;
        var due = _snoozes.Where(s => s.FireAt <= now).ToList();
        foreach (var entry in due)
        {
            _snoozes.Remove(entry);
            Fire(entry.Due);
        }
    }

    private void Fire(DueReminder due)
    {
        PlaySoundAndSpeak(due);
        ReminderDue?.Invoke(this, due);
    }

    private void PlaySoundAndSpeak(DueReminder due)
    {
        var settings = _host.Settings;

        var template = string.IsNullOrWhiteSpace(due.Item.VoiceText)
            ? settings.VoiceTemplate
            : due.Item.VoiceText;

        var text = VoiceTemplate.Render(template, due.Item, due.Occurrence.Start, DateTime.Now, due.Plan.OffsetMinutes);

        if (settings.SoundEnabled)
        {
            var file = string.IsNullOrWhiteSpace(due.Item.SoundFile) ? settings.DefaultSoundFile : due.Item.SoundFile;
            _host.Audio.Play(file);
        }

        if (!settings.SpeechEnabled) return;

        var delay = settings.SoundEnabled && settings.SoundBeforeSpeech ? TimeSpan.FromSeconds(1.6) : TimeSpan.Zero;
        if (delay <= TimeSpan.Zero)
        {
            Speak(text, settings);
            return;
        }

        var oneShot = new DispatcherTimer(DispatcherPriority.Normal) { Interval = delay };
        oneShot.Tick += (_, _) =>
        {
            oneShot.Stop();
            Speak(text, settings);
        };
        oneShot.Start();
    }

    private void Speak(string text, AppSettings settings)
    {
        _host.Voice.Speak(new SpeechRequest
        {
            Text = text,
            VoiceId = settings.VoiceId,
            Rate = settings.VoiceRate,
            Volume = settings.VoiceVolume,
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _snoozes.Clear();
    }
}
