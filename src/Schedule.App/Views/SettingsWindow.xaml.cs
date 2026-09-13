using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Schedule.App.Services;
using Schedule.Core.Models;
using Schedule.Core.Storage;
using Schedule.Core.Templating;

namespace Schedule.App.Views;

/// <summary>音色下拉项。</summary>
public sealed class VoiceChoice
{
    public VoiceDescriptor? Voice { get; init; }

    public string Text { get; init; } = string.Empty;

    public override string ToString() => Text;
}

/// <summary>设置窗口：提醒 / 语音播报 / 常规。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private AppSettings _draft;
    private List<VoiceChoice> _voiceChoices = new();
    private bool _loading = true;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        _draft = host.Settings.Clone();

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        LoadVoices();
        LoadValues();

        RateSlider.ValueChanged += (_, _) => { UpdateSliderTexts(); UpdatePreview(); };
        VolumeSlider.ValueChanged += (_, _) => UpdateSliderTexts();
        TemplateBox.TextChanged += (_, _) => UpdatePreview();
        VoiceBox.SelectionChanged += (_, _) => UpdatePreview();

        _loading = false;
        StatusText.Text = string.Empty;
        UpdatePreview();
    }

    private void LoadVoices()
    {
        var voices = _host.Voice.GetVoices();

        _voiceChoices = new List<VoiceChoice> { new() { Voice = null, Text = "（系统默认音色）" } };
        foreach (var voice in voices)
        {
            _voiceChoices.Add(new VoiceChoice { Voice = voice, Text = SapiVoiceService.FormatVoice(voice) });
        }

        VoiceBox.ItemsSource = _voiceChoices;
        VoiceBox.SelectedItem = _voiceChoices.FirstOrDefault(c => c.Voice != null && c.Voice.Id == _draft.VoiceId) ?? _voiceChoices[0];

        VoiceEngineText.Text = _host.Voice.IsAvailable
            ? "语音引擎：Windows SAPI（已安装 " + voices.Count + " 个音色）"
            : "语音引擎不可用：" + (_host.Voice.LastError ?? "未知原因") + "。可在「Windows 设置 → 时间和语言 → 语音」中添加语音包。";
    }

    private void LoadValues()
    {
        _loading = true;

        DefaultRemindBox.Text = _draft.DefaultRemindBeforeMinutes.ToString();
        DefaultReminderCheck.IsChecked = _draft.DefaultReminderEnabled;
        SnoozeBox.Text = _draft.SnoozeMinutes.ToString();
        AutoCloseBox.Text = _draft.ReminderAutoCloseSeconds.ToString();
        AlwaysOnTopCheck.IsChecked = _draft.ReminderAlwaysOnTop;

        SoundEnabledCheck.IsChecked = _draft.SoundEnabled;
        SoundFileBox.Text = _draft.DefaultSoundFile;
        SoundBeforeSpeechCheck.IsChecked = _draft.SoundBeforeSpeech;

        LookAheadBox.Text = _draft.LookAheadDays.ToString();
        CsvCategoryBox.Text = _draft.CsvDefaultCategory;
        BackupBox.Text = _draft.BackupKeepCount.ToString();

        SpeechEnabledCheck.IsChecked = _draft.SpeechEnabled;
        RateSlider.Value = Math.Clamp(_draft.VoiceRate, -10, 10);
        VolumeSlider.Value = Math.Clamp(_draft.VoiceVolume, 0, 100);
        TemplateBox.Text = _draft.VoiceTemplate;
        VoiceBox.SelectedItem = _voiceChoices.FirstOrDefault(c => c.Voice != null && c.Voice.Id == _draft.VoiceId) ?? _voiceChoices[0];

        MinimizeToTrayCheck.IsChecked = _draft.MinimizeToTray;
        CloseToTrayCheck.IsChecked = _draft.CloseToTray;
        StartWithWindowsCheck.IsChecked = _draft.StartWithWindows;
        ShowCompletedCheck.IsChecked = _draft.ShowCompleted;

        DataDirBox.Text = AppPaths.DataDirectory;
        AboutText.Text = "日程助手 ScheduleApp v" + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0")
            + "　·　基于 .NET 9 + WPF 的原生轻量实现，零第三方依赖"
            + Environment.NewLine
            + "语音引擎：Windows SAPI5（支持自定义音色、语速、音量与播报模板）";

        UpdateSliderTexts();
        _loading = false;
    }

    private void UpdateSliderTexts()
    {
        var rate = (int)Math.Round(RateSlider.Value);
        RateText.Text = rate > 0 ? "+" + rate : rate.ToString();
        VolumeText.Text = ((int)Math.Round(VolumeSlider.Value)) + "%";
    }

    private void UpdatePreview()
    {
        if (_loading) return;

        var template = string.IsNullOrWhiteSpace(TemplateBox.Text) ? VoiceTemplate.Default : TemplateBox.Text;
        PreviewText.Text = VoiceTemplate.Preview(template, DateTime.Now);

        var choice = VoiceBox.SelectedItem as VoiceChoice;
        var voiceName = choice?.Voice?.Name ?? "系统默认音色";
        StatusText.Text = "音色：" + voiceName + "　语速 " + (int)Math.Round(RateSlider.Value) + "　音量 " + (int)Math.Round(VolumeSlider.Value) + "%";
    }

    private void InsertToken_Click(object sender, RoutedEventArgs e)
    {
        var token = (sender as FrameworkElement)?.Tag?.ToString();
        if (string.IsNullOrEmpty(token)) return;

        var caret = TemplateBox.CaretIndex;
        var text = TemplateBox.Text ?? string.Empty;
        if (caret < 0 || caret > text.Length) caret = text.Length;

        TemplateBox.Text = text.Insert(caret, token);
        TemplateBox.CaretIndex = caret + token.Length;
        TemplateBox.Focus();
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择提醒提示音",
            Filter = "音频文件 (*.wav;*.mp3;*.wma;*.m4a)|*.wav;*.mp3;*.wma;*.m4a|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            SoundFileBox.Text = dialog.FileName;
        }
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
        => _host.Audio.Play((SoundFileBox.Text ?? string.Empty).Trim());

    private void ClearSound_Click(object sender, RoutedEventArgs e) => SoundFileBox.Text = string.Empty;

    private void TestVoice_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadValues();
        var text = VoiceTemplate.Preview(settings.VoiceTemplate, DateTime.Now);

        _host.Voice.Speak(new SpeechRequest
        {
            Text = text,
            VoiceId = settings.VoiceId,
            Rate = settings.VoiceRate,
            Volume = settings.VoiceVolume,
        });

        StatusText.Text = "正在试听：" + text;
    }

    private void StopVoice_Click(object sender, RoutedEventArgs e) => _host.Voice.Stop();

    private void RefreshVoices_Click(object sender, RoutedEventArgs e)
    {
        LoadVoices();
        StatusText.Text = "已刷新音色列表。";
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectories();
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开目录：" + ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "确定把设置恢复为默认值吗？", "恢复默认", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        _draft = new AppSettings();
        LoadValues();
        UpdatePreview();
        StatusText.Text = "已恢复默认值，点击「保存」生效。";
    }

    private AppSettings ReadValues()
    {
        var settings = _draft.Clone();

        settings.DefaultRemindBeforeMinutes = ReadInt(DefaultRemindBox.Text, settings.DefaultRemindBeforeMinutes, 0, 60 * 24 * 365);
        settings.DefaultReminderEnabled = DefaultReminderCheck.IsChecked == true;
        settings.SnoozeMinutes = ReadInt(SnoozeBox.Text, settings.SnoozeMinutes, 1, 24 * 60);
        settings.ReminderAutoCloseSeconds = ReadInt(AutoCloseBox.Text, settings.ReminderAutoCloseSeconds, 0, 600);
        settings.ReminderAlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;

        settings.SoundEnabled = SoundEnabledCheck.IsChecked == true;
        settings.DefaultSoundFile = (SoundFileBox.Text ?? string.Empty).Trim();
        settings.SoundBeforeSpeech = SoundBeforeSpeechCheck.IsChecked == true;

        settings.LookAheadDays = ReadInt(LookAheadBox.Text, settings.LookAheadDays, 1, 3650);
        settings.CsvDefaultCategory = (CsvCategoryBox.Text ?? string.Empty).Trim();
        settings.BackupKeepCount = ReadInt(BackupBox.Text, settings.BackupKeepCount, 0, 200);

        settings.SpeechEnabled = SpeechEnabledCheck.IsChecked == true;
        settings.VoiceRate = (int)Math.Round(RateSlider.Value);
        settings.VoiceVolume = (int)Math.Round(VolumeSlider.Value);
        settings.VoiceTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text) ? VoiceTemplate.Default : TemplateBox.Text;

        var choice = VoiceBox.SelectedItem as VoiceChoice;
        settings.VoiceId = choice?.Voice?.Id ?? string.Empty;
        settings.VoiceName = choice?.Voice?.Name ?? string.Empty;

        settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        settings.ShowCompleted = ShowCompletedCheck.IsChecked == true;

        return settings;
    }

    private static int ReadInt(string? text, int fallback, int min, int max)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var value)) return fallback;
        return Math.Clamp(value, min, max);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadValues();
            _host.SettingsService.Replace(settings);
            _host.NotifySettingsChanged();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存设置失败：" + ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
