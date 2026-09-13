using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Schedule.App.Services;
using Schedule.App.ViewModels;
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

/// <summary>设置窗口：提醒 / 语音播报 / 外观与默认值 / 常规。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly List<CategoryColorChoice> _categoryColors = new();
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
        LoadCategoryColors();
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

    /// <summary>分类配色列表 = 现有日程用到的分类 ∪ 已保存的映射。</summary>
    private void LoadCategoryColors()
    {
        var names = new List<string>();

        foreach (var item in _host.Repository.Snapshot())
        {
            var name = item.Category?.Trim();
            if (!string.IsNullOrEmpty(name) && !names.Contains(name, StringComparer.CurrentCultureIgnoreCase)) names.Add(name);
        }

        foreach (var key in _draft.CategoryColors.Keys)
        {
            if (!names.Contains(key, StringComparer.CurrentCultureIgnoreCase)) names.Add(key);
        }

        names.Sort(StringComparer.CurrentCulture);

        _categoryColors.Clear();
        foreach (var name in names)
        {
            _draft.CategoryColors.TryGetValue(name, out var hex);
            _categoryColors.Add(new CategoryColorChoice { Name = name, Hex = Schedule.Core.Services.EventColors.Normalize(hex) });
        }

        CategoryColorList.ItemsSource = _categoryColors;
        CategoryColorHint();
    }

    private void CategoryColorHint()
    {
        // 没有分类时给出提示（列表为空会看不出来）
        if (_categoryColors.Count == 0)
        {
            CategoryColorList.ItemsSource = new List<CategoryColorChoice>
            {
                new() { Name = "（暂无分类：给日程填上分类后即可在此配色）", Hex = string.Empty },
            };
        }
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

        // 外观与默认值
        DefaultViewBox.SelectedIndex = (int)_draft.DefaultView;
        HourHeightBox.Text = _draft.HourHeight.ToString();
        MonthCapacityBox.Text = _draft.MonthCellCapacity.ToString();
        DefaultOffsetsBox.Text = _draft.DefaultReminderOffsets.Count > 0
            ? string.Join(",", _draft.DefaultReminderOffsets)
            : _draft.DefaultRemindBeforeMinutes.ToString();
        WeekStartCheck.IsChecked = _draft.WeekStartsOnMonday;

        DataDirBox.Text = AppPaths.DataDirectory;
        AboutText.Text = "日程助手 ScheduleApp v" + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0")
            + "　·　基于 .NET 9 + WPF 的原生轻量实现，零第三方依赖"
            + Environment.NewLine
            + "支持：多档语音提醒（30/15/5 分钟前等）、周 / 月日历视图、事件配色、周期事件（间隔 / 多星期 / 次数）"
            + Environment.NewLine
            + "语音引擎：Windows SAPI5（音色、语速、音量、播报模板均可自定义）";

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

    private void CategoryColorCycle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CategoryColorChoice choice) choice.Cycle();
    }

    private void CategoryColorReset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CategoryColorChoice choice) choice.Reset();
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
        LoadCategoryColors();
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

        // 外观与默认值
        settings.DefaultView = (ScheduleViewMode)Math.Max(0, DefaultViewBox.SelectedIndex);
        settings.HourHeight = ReadInt(HourHeightBox.Text, settings.HourHeight, 32, 140);
        settings.MonthCellCapacity = ReadInt(MonthCapacityBox.Text, settings.MonthCellCapacity, 1, 8);
        settings.WeekStartsOnMonday = WeekStartCheck.IsChecked == true;

        var offsets = ParseOffsets(DefaultOffsetsBox.Text);
        settings.DefaultReminderOffsets = offsets.Count > 0 ? offsets : new List<int> { 10 };
        if (settings.DefaultReminderOffsets.Count > 0)
        {
            settings.DefaultRemindBeforeMinutes = settings.DefaultReminderOffsets.Min();
        }

        var colors = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in _categoryColors)
        {
            var hex = Schedule.Core.Services.EventColors.Normalize(item.Hex);
            if (hex.Length == 7) colors[item.Name] = hex;
        }

        settings.CategoryColors = colors;

        return settings;
    }

    private static List<int> ParseOffsets(string? text)
    {
        var result = new List<int>();

        foreach (var part in (text ?? string.Empty).Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var minutes) && minutes >= 0 && minutes <= 60 * 24 * 365)
            {
                result.Add(minutes);
            }
        }

        return result.Distinct().OrderByDescending(v => v).ToList();
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
