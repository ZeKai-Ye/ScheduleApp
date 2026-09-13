using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Schedule.App.Services;
using Schedule.Core.Models;
using Schedule.Core.Templating;

namespace Schedule.App.Views;

/// <summary>到点提醒弹窗（对应某一个提醒档位）。</summary>
public partial class ReminderWindow : Window
{
    private readonly AppHost _host;
    private readonly DueReminder _due;
    private readonly DispatcherTimer? _autoCloseTimer;
    private bool _positioning;
    private bool _movedByUser;

    public ReminderWindow(AppHost host, DueReminder due)
    {
        _host = host;
        _due = due;

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        var item = due.Item;
        var occurrence = due.Occurrence;

        TitleText.Text = item.Title;
        TimeText.Text = item.AllDay
            ? occurrence.Start.ToString("yyyy-MM-dd") + " 全天"
            : occurrence.Start.ToString("yyyy-MM-dd HH:mm") + " - " + occurrence.End.ToString("HH:mm");

        OffsetText.Text = due.Plan.IsAbsolute
            ? "指定时刻提醒"
            : due.Plan.OffsetText;
        CountdownText.Text = VoiceTemplate.DescribeCountdown(occurrence.Start, DateTime.Now);

        NotesText.Text = item.Notes ?? string.Empty;
        NotesText.Visibility = string.IsNullOrWhiteSpace(item.Notes) ? Visibility.Collapsed : Visibility.Visible;

        var template = string.IsNullOrWhiteSpace(item.VoiceText) ? host.Settings.VoiceTemplate : item.VoiceText;
        var spoken = VoiceTemplate.Render(template, item, occurrence.Start, DateTime.Now, due.Plan.OffsetMinutes);
        VoiceText.Text = host.Settings.SpeechEnabled ? "语音播报：" + spoken : "已关闭语音播报";

        // 这条日程后续还有哪些档位没响
        var now = DateTime.Now;
        var remaining = occurrence.Reminders
            .Where(p => p.FireAt > now && !ReminderPlannerAlreadyFired(item, occurrence, p))
            .Select(p => p.OffsetText)
            .ToList();

        if (remaining.Count > 0)
        {
            MoreOffsetsText.Text = "本条日程还有 " + remaining.Count + " 档提醒：" + string.Join("、", remaining);
            MoreOffsetsText.Visibility = Visibility.Visible;
        }

        Topmost = host.Settings.ReminderAlwaysOnTop;

        // 位置在 Loaded 里计算：此时 ActualWidth / ActualHeight 才是最终尺寸
        Loaded += (_, _) =>
        {
            _positioning = true;
            ReminderPositioner.Place(this, _host.Settings, CascadeIndex());
            _positioning = false;
            UpdatePositionHint(false);
        };

        LocationChanged += (_, _) =>
        {
            if (!_positioning) _movedByUser = true;
        };

        MouseLeftButtonUp += (_, _) =>
        {
            if (_movedByUser) RememberPosition();
        };

        Closed += (_, _) => RememberPosition();

        var autoClose = host.Settings.ReminderAutoCloseSeconds;
        if (autoClose > 0)
        {
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(autoClose, 5, 600)) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            _autoCloseTimer.Start();
        }
    }

    private static bool ReminderPlannerAlreadyFired(ScheduleItem item, ScheduleOccurrence occurrence, ReminderPlan plan)
    {
        foreach (var record in item.FiredReminders)
        {
            if (record.OccurrenceStart == occurrence.Start && record.OffsetMinutes == plan.OffsetMinutes) return true;
        }

        return false;
    }

    /// <summary>同屏多个提醒依次错开的下标。</summary>
    private static int CascadeIndex() => Math.Max(0, Application.Current.Windows.Count - 1) % 5;

    /// <summary>用户拖动后记住位置（可在设置里关闭）。</summary>
    private void RememberPosition()
    {
        if (!_movedByUser) return;

        _movedByUser = false;
        if (!_host.Settings.ReminderRememberPosition) return;

        _host.RememberReminderPosition(Left, Top);
        UpdatePositionHint(true);
    }

    private void UpdatePositionHint(bool remembered)
    {
        var mode = _host.Settings.ReminderPosition;
        var text = "位置：" + ReminderPositionMath.Describe(mode);

        if (mode == ReminderPositionMode.Custom && !remembered)
        {
            text = "位置：自定义";
        }

        if (remembered)
        {
            text += "（已记住，可在设置里改）";
        }
        else if (_host.Settings.ReminderRememberPosition)
        {
            text += "（拖动弹窗可记住新位置）";
        }

        PositionHintText.Text = text;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var item = _host.Repository.Find(_due.Item.Id);
            if (item != null)
            {
                item.Status = ItemStatus.Done;
                item.UpdatedAt = DateTime.Now;
                _host.Repository.Update(item);
                _host.Repository.Save();
            }
        }
        catch (Exception)
        {
            // 忽略，弹窗仍需关闭
        }

        Close();
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        var minutes = _host.Settings.SnoozeMinutes;
        if (SnoozeBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var parsed))
        {
            minutes = parsed;
        }

        _host.Reminders.Snooze(_due, minutes);
        Close();
    }

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        var settings = _host.Settings;
        var template = string.IsNullOrWhiteSpace(_due.Item.VoiceText) ? settings.VoiceTemplate : _due.Item.VoiceText;

        if (settings.SoundEnabled)
        {
            var file = string.IsNullOrWhiteSpace(_due.Item.SoundFile) ? settings.DefaultSoundFile : _due.Item.SoundFile;
            _host.Audio.Play(file);
        }

        _host.Voice.Speak(new SpeechRequest
        {
            Text = VoiceTemplate.Render(template, _due.Item, _due.Occurrence.Start, DateTime.Now, _due.Plan.OffsetMinutes),
            VoiceId = settings.VoiceId,
            Rate = settings.VoiceRate,
            Volume = settings.VoiceVolume,
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        base.OnClosed(e);
    }
}
