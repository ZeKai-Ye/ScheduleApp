using Schedule.Core.Api;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Storage;

namespace Schedule.App.Services;

/// <summary>把应用能力（语音 / 提醒）暴露给本地接口。</summary>
public sealed class ApiActions : IApiActions
{
    private readonly AppHost _host;

    public ApiActions(AppHost host) => _host = host;

    public bool IsAvailable => true;

    public IReadOnlyList<VoiceDescriptor> GetVoices() => _host.Voice.GetVoices();

    public bool Speak(string text, string? voiceId, int? rate, int? volume, out string? error)
    {
        if (!_host.Voice.IsAvailable)
        {
            error = _host.Voice.LastError ?? "语音引擎不可用";
            return false;
        }

        var settings = _host.Settings;

        _host.Voice.Speak(new SpeechRequest
        {
            Text = text,
            VoiceId = string.IsNullOrWhiteSpace(voiceId) ? settings.VoiceId : voiceId!,
            Rate = rate ?? settings.VoiceRate,
            Volume = volume ?? settings.VoiceVolume,
        });

        error = _host.Voice.LastError;
        return string.IsNullOrEmpty(error);
    }

    public bool TriggerReminder(Guid itemId, int offsetMinutes, out string? error)
    {
        error = null;

        var item = _host.Repository.Find(itemId);
        if (item == null)
        {
            error = "未找到日程：" + itemId;
            return false;
        }

        var occurrence = OccurrenceCalculator.Create(item, item.Start);
        var plan = occurrence.Reminders.FirstOrDefault(p => p.OffsetMinutes == offsetMinutes)
                   ?? new ReminderPlan { OffsetMinutes = offsetMinutes, FireAt = DateTime.Now };

        _host.Reminders.FireNow(new DueReminder
        {
            Item = item,
            Occurrence = occurrence,
            Plan = plan,
        });

        return true;
    }

    public bool SetShortcut(string target, bool create, string? directoryOverride, out string? path, out string? error)
        => create
            ? ShortcutService.TryCreate(target, null, directoryOverride, out path, out error)
            : ShortcutService.TryDelete(target, directoryOverride, out path, out error);

    public bool SetAutostart(bool enabled, string? arguments, out string? mode, out string? error)
        => StartupService.Apply(enabled, arguments, out mode, out error);

    public SystemStatusInfo GetSystemStatus()
    {
        var (mode, command) = StartupService.Detect();

        return new SystemStatusInfo
        {
            Supported = true,
            ExecutablePath = ShortcutService.ExecutablePath(),
            DataDirectory = AppPaths.DataDirectory,
            DesktopShortcutExists = ShortcutService.Exists("desktop"),
            StartMenuShortcutExists = ShortcutService.Exists("startmenu"),
            AutostartEnabled = mode != AutostartMode.Disabled,
            AutostartMode = mode.ToString(),
            AutostartCommand = command,
        };
    }
}
