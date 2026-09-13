using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Schedule.App.Services;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Templating;

namespace Schedule.App.Views;

/// <summary>到点提醒弹窗。</summary>
public partial class ReminderWindow : Window
{
    private readonly AppHost _host;
    private readonly ScheduleOccurrence _occurrence;
    private readonly DispatcherTimer? _autoCloseTimer;

    public ReminderWindow(AppHost host, ScheduleOccurrence occurrence)
    {
        _host = host;
        _occurrence = occurrence;

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        TitleText.Text = occurrence.Item.Title;
        TimeText.Text = occurrence.Item.AllDay
            ? occurrence.Start.ToString("yyyy-MM-dd") + " 全天"
            : occurrence.Start.ToString("yyyy-MM-dd HH:mm") + " - " + occurrence.End.ToString("HH:mm");

        CategoryText.Text = string.IsNullOrWhiteSpace(occurrence.Item.Category) ? string.Empty : occurrence.Item.Category;
        CountdownText.Text = VoiceTemplate.DescribeCountdown(occurrence.Start, DateTime.Now);

        NotesText.Text = occurrence.Item.Notes ?? string.Empty;
        NotesText.Visibility = string.IsNullOrWhiteSpace(occurrence.Item.Notes) ? Visibility.Collapsed : Visibility.Visible;

        var template = string.IsNullOrWhiteSpace(occurrence.Item.VoiceText) ? host.Settings.VoiceTemplate : occurrence.Item.VoiceText;
        var spoken = VoiceTemplate.Render(template, occurrence.Item, occurrence.Start, DateTime.Now);
        VoiceText.Text = host.Settings.SpeechEnabled ? "语音播报：" + spoken : "已关闭语音播报";
        VoicePanel.Visibility = Visibility.Visible;

        Topmost = host.Settings.ReminderAlwaysOnTop;
        PositionBottomRight();

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

    /// <summary>把窗口放到工作区右下角，多个提醒依次向上错开。</summary>
    private void PositionBottomRight()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            var offset = (Application.Current.Windows.Count - 1) % 5;
            Left = area.Right - Width - 18;
            Top = area.Bottom - ActualHeight - 18 - (offset * 34);
        }
        catch (Exception)
        {
            Left = 100;
            Top = 100;
        }
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var item = _host.Repository.Find(_occurrence.Item.Id);
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

        _host.Reminders.Snooze(_occurrence, minutes);
        Close();
    }

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        var settings = _host.Settings;
        var template = string.IsNullOrWhiteSpace(_occurrence.Item.VoiceText) ? settings.VoiceTemplate : _occurrence.Item.VoiceText;

        if (settings.SoundEnabled)
        {
            var file = string.IsNullOrWhiteSpace(_occurrence.Item.SoundFile) ? settings.DefaultSoundFile : _occurrence.Item.SoundFile;
            _host.Audio.Play(file);
        }

        _host.Voice.Speak(new SpeechRequest
        {
            Text = VoiceTemplate.Render(template, _occurrence.Item, _occurrence.Start, DateTime.Now),
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
