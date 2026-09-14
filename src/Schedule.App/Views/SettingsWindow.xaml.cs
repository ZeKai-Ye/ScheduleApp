using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Schedule.App.Services;
using Schedule.App.ViewModels;
using Schedule.Core.Ai;
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
    private static readonly int[] OffsetPresets = { 0, 5, 10, 15, 30, 60, 120, 1440 };

    private readonly List<CategoryColorChoice> _categoryColors = new();
    private readonly List<OffsetTextRow> _offsetVoiceRows = new();
    private AppSettings _draft;
    private List<VoiceChoice> _voiceChoices = new();
    private bool _loading = true;

    /// <summary>「AI 助手」标签页下标（对话窗口的「AI 设置…」直接跳这里）。</summary>
    public const int AiTabIndex = 4;

    /// <summary>鉴权方式下拉的取值顺序（与 XAML 里的项目一一对应）。</summary>
    private static readonly AiAuthMode[] AuthModes = { AiAuthMode.Bearer, AiAuthMode.ApiKeyHeader, AiAuthMode.None };

    public SettingsWindow(AppHost host) : this(host, 0)
    {
    }

    public SettingsWindow(AppHost host, int initialTab)
    {
        _host = host;
        _draft = host.Settings.Clone();

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        LoadVoices();
        LoadCategoryColors();
        LoadValues();
        BuildOffsetVoiceRows();

        RateSlider.ValueChanged += (_, _) => { UpdateSliderTexts(); UpdatePreview(); };
        VolumeSlider.ValueChanged += (_, _) => UpdateSliderTexts();
        TemplateBox.TextChanged += (_, _) => UpdatePreview();
        VoiceBox.SelectionChanged += (_, _) => UpdatePreview();

        _loading = false;
        StatusText.Text = string.Empty;
        UpdatePreview();

        if (initialTab > 0 && initialTab < Tabs.Items.Count) Tabs.SelectedIndex = initialTab;
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

    /// <summary>构建「按档位全局默认文案」编辑行。</summary>
    private void BuildOffsetVoiceRows()
    {
        var minutes = new SortedSet<int>(OffsetPresets);

        foreach (var offset in _draft.DefaultReminderOffsets) minutes.Add(offset);
        foreach (var offset in _draft.OffsetVoiceTemplates.Keys) minutes.Add(offset);
        foreach (var offset in _offsetVoiceRows.Select(r => r.Minutes)) minutes.Add(offset);

        _offsetVoiceRows.Clear();

        foreach (var value in minutes.OrderByDescending(m => m))
        {
            _draft.OffsetVoiceTemplates.TryGetValue(value, out var text);

            _offsetVoiceRows.Add(new OffsetTextRow
            {
                Minutes = value,
                Label = value == 0 ? "准点提醒" : "提前 " + value + " 分钟",
                Text = text ?? string.Empty,
            });
        }

        GlobalOffsetVoiceList.ItemsSource = null;
        GlobalOffsetVoiceList.ItemsSource = _offsetVoiceRows;
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

        ThemeBox.SelectedIndex = (int)_draft.Theme;
        ThemeHintText.Text = Schedule.App.Services.ThemeService.Describe(_draft.Theme);
        AutoRestartThemeCheck.IsChecked = _draft.AutoRestartOnThemeChange;

        // 提醒弹窗位置
        ReminderPositionBox.SelectedIndex = (int)_draft.ReminderPosition;
        ReminderPositionXBox.Text = _draft.ReminderPositionX.ToString();
        ReminderPositionYBox.Text = _draft.ReminderPositionY.ToString();
        ReminderRememberPositionCheck.IsChecked = _draft.ReminderRememberPosition;
        ReminderFollowCursorCheck.IsChecked = _draft.ReminderFollowCursorScreen;
        UpdateReminderPositionHint();

        // AI 助手
        AiProviderBox.ItemsSource = AiProviderPresets.All;
        AiProviderBox.SelectedItem = AiProviderPresets.All.FirstOrDefault(p => p.Name == _draft.AiProvider)
            ?? AiProviderPresets.Match(_draft.AiBaseUrl)
            ?? AiProviderPresets.All[^1];

        AiBaseUrlBox.Text = _draft.AiBaseUrl;
        AiApiModeBox.SelectedIndex = (int)_draft.AiApiMode;
        AiModelBox.Text = _draft.AiModel;
        AiApiKeyBox.Text = _draft.AiApiKey;
        AiAuthModeBox.SelectedIndex = Math.Max(0, Array.IndexOf(AuthModes, _draft.AiAuthMode));
        AiReasoningBox.SelectedIndex = (int)_draft.AiReasoningEffort;
        AiProxyBox.Text = _draft.AiProxyUrl;
        AiSystemProxyCheck.IsChecked = _draft.AiUseSystemProxy;
        UpdateAiProviderHint();
        AiTemperatureBox.Text = _draft.AiTemperature.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        AiTimeoutBox.Text = _draft.AiTimeoutSeconds.ToString();
        AiHistoryBox.Text = _draft.AiMaxHistoryMessages.ToString();
        AiMaxTokensBox.Text = _draft.AiMaxTokens.ToString();
        AiThinkingBox.SelectedIndex = (int)_draft.AiThinking;
        AiShowToolDetailsCheck.IsChecked = _draft.AiShowToolDetails;
        AiLogActionsCheck.IsChecked = _draft.AiLogActions;
        AiSystemPromptBox.Text = _draft.AiSystemPrompt;
        UpdateAiStatusText();

        // 桌面集成
        StartupArgumentsBox.Text = _draft.StartupArguments;
        StartMinimizedCheck.IsChecked = _draft.StartMinimized;
        RefreshSystemStatus();

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

    /// <summary>刷新开机启动与快捷方式的真实状态（读系统而不是读设置）。</summary>
    private void RefreshSystemStatus()
    {
        var (mode, command) = StartupService.Detect();

        AutostartStatusText.Text = mode == AutostartMode.Disabled
            ? "当前未启用开机启动。"
            : "已启用（" + StartupService.Describe(mode) + "）：" + command;

        var desktop = ShortcutService.Exists("desktop");
        var startMenu = ShortcutService.Exists("startmenu");

        ShortcutStatusText.Text = "桌面：" + (desktop ? "已创建" : "未创建")
            + "　开始菜单：" + (startMenu ? "已创建" : "未创建")
            + "　（保存后按设置同步开机启动）";
    }

    private void UpdateReminderPositionHint()
    {
        if (ReminderPositionHint == null) return;

        var mode = (ReminderPositionMode)Math.Max(0, ReminderPositionBox.SelectedIndex);

        ReminderPositionHint.Text = mode switch
        {
            ReminderPositionMode.Custom => "X / Y 为弹窗左上角的屏幕坐标（设备无关单位，拖动弹窗会自动填这里）；越界会自动贴回可见区域，多屏时可跨屏。",
            ReminderPositionMode.Center => "X / Y 为相对屏幕中央的偏移（正数向右 / 向下）。",
            _ => "X / Y 为相对该角落的偏移（正数向内移动；默认留 18 单位边距）。",
        };
    }

    /// <summary>主题即时预览：改选即生效，取消时回滚为已保存的设置。</summary>
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var mode = (AppTheme)Math.Max(0, ThemeBox.SelectedIndex);

        if (ThemeService.Apply(mode))
        {
            // 调色板换了，通知主界面重建已经算好的画刷（日历色块等），否则要等下次刷新才变色
            _host.NotifyThemeChanged();
        }

        ThemeHintText.Text = ThemeService.Describe(mode)
            + (mode == AppTheme.System ? string.Empty : "（立即预览；取消会恢复原主题）");
    }

    // ================= AI 助手 =================

    /// <summary>按界面上的当前值拼一份 AI 设置（不落盘），用于「测试连接 / 获取模型列表」。</summary>
    private AppSettings BuildAiDraft()
    {
        var settings = _draft.Clone();

        settings.AiBaseUrl = (AiBaseUrlBox.Text ?? string.Empty).Trim();
        settings.AiApiMode = (AiApiMode)Math.Max(0, AiApiModeBox.SelectedIndex);
        settings.AiModel = (AiModelBox.Text ?? string.Empty).Trim();
        settings.AiApiKey = (AiApiKeyBox.Text ?? string.Empty).Trim();
        settings.AiAuthMode = AuthModes[Math.Clamp(AiAuthModeBox.SelectedIndex, 0, AuthModes.Length - 1)];
        settings.AiReasoningEffort = (AiReasoningEffortMode)Math.Max(0, AiReasoningBox.SelectedIndex);
        settings.AiUseSystemProxy = AiSystemProxyCheck.IsChecked == true;
        settings.AiProxyUrl = (AiProxyBox.Text ?? string.Empty).Trim();
        settings.AiTimeoutSeconds = ReadInt(AiTimeoutBox.Text, settings.AiTimeoutSeconds, 5, 3600);

        return settings;
    }

    private void UpdateAiStatusText()
    {
        if (AiStatusText == null) return;

        var draft = BuildAiDraft();

        AiStatusText.Text = "当前：" + (string.IsNullOrWhiteSpace(draft.AiModel) ? "(未选择模型)" : draft.AiModel)
            + "　" + OpenAiCompatibleClient.Normalize(draft.AiBaseUrl)
            + "　" + AiClientFactory.DescribeProtocol(draft)
            + "　密钥：" + AiSecrets.Describe(draft.AiApiKey)
            + "　（模型要事先在服务端存在，可点「获取模型列表」查看）";

        AiKeyHint.Text = "密钥：" + AiSecrets.Describe(draft.AiApiKey)
            + "　可以写 env:OPENAI_API_KEY 从环境变量读取，这样密钥不会写进 settings.json。"
            + "密钥只保存在本机，不会上传到任何地方。";

        UpdateAiCloudNotice();
    }

    /// <summary>云端要明确提示"数据会发出去"，本地则强调不出本机。</summary>
    private void UpdateAiCloudNotice()
    {
        if (AiCloudNotice == null) return;

        var draft = BuildAiDraft();
        var preset = AiProviderPresets.Match(draft.AiBaseUrl);

        if (AiEndpoint.IsLocal(draft.AiBaseUrl))
        {
            AiCloudNotice.Text = "本地模型：对话与日程数据都留在本机，断网也能用。";
            return;
        }

        AiCloudNotice.Text = "云端模型：对话内容（必要时包含相关日程标题 / 时间）会发送给你配置的服务商"
            + (preset != null && preset.NeedsKey ? "（" + preset.Name + "）" : string.Empty)
            + "。密钥只存在本机配置文件里，但请自行确认该服务商的隐私政策。";
    }

    /// <summary>选中预设：自动填地址 / 模型 / 鉴权方式，并显示该服务商的提示。</summary>
    private void AiProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        if (AiProviderBox.SelectedItem is not AiProviderPreset preset) return;

        AiBaseUrlBox.Text = preset.BaseUrl;
        AiApiModeBox.SelectedIndex = (int)AiApiMode.Auto;
        AiAuthModeBox.SelectedIndex = Math.Max(0, Array.IndexOf(AuthModes, preset.AuthMode));

        if (preset.Model.Length > 0) AiModelBox.Text = preset.Model;

        UpdateAiProviderHint();
        UpdateAiStatusText();
    }

    private void UpdateAiProviderHint()
        => AiProviderHint.Text = (AiProviderBox.SelectedItem as AiProviderPreset)?.Hint ?? string.Empty;

    private async void TestAi_Click(object sender, RoutedEventArgs e) => await CheckAiAsync(fillModels: false);

    private async void FetchAiModels_Click(object sender, RoutedEventArgs e) => await CheckAiAsync(fillModels: true);

    private async Task CheckAiAsync(bool fillModels)
    {
        var draft = BuildAiDraft();
        AiStatusText.Text = "正在连接 " + OpenAiCompatibleClient.Normalize(draft.AiBaseUrl) + " …";

        try
        {
            using var client = AiClientFactory.Create(draft);
            var models = await client.ListModelsAsync(CancellationToken.None);

            if (fillModels)
            {
                AiModelBox.ItemsSource = models;

                var match = models.FirstOrDefault(m => string.Equals(m.Id, draft.AiModel, StringComparison.OrdinalIgnoreCase));
                if (match != null) AiModelBox.SelectedItem = match;
                else if (models.Count == 1) AiModelBox.Text = models[0].Id;
            }

            AiStatusText.Text = "连接成功（" + AiClientFactory.DescribeProtocol(draft) + "）：共 " + models.Count + " 个模型　"
                + string.Join("、", models.Select(m => m.Id + (string.IsNullOrWhiteSpace(m.Detail) ? string.Empty : "（" + m.Detail + "）")));
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "连接失败：" + ex.Message;
        }
    }

    private static double ReadDouble(string? text, double fallback, double min, double max)
        => double.TryParse((text ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private void ReminderPosition_Changed(object sender, SelectionChangedEventArgs e) => UpdateReminderPositionHint();

    private void ReminderPositionInput_Changed(object sender, TextChangedEventArgs e) => UpdateReminderPositionHint();

    private void PreviewPosition_Click(object sender, RoutedEventArgs e)
    {
        var mode = (ReminderPositionMode)Math.Max(0, ReminderPositionBox.SelectedIndex);
        var x = ReadInt(ReminderPositionXBox.Text, 0, -20000, 20000);
        var y = ReadInt(ReminderPositionYBox.Text, 0, -20000, 20000);

        // 先按当前输入落地，再弹预览，所见即所得
        _host.SetReminderPosition(mode, x, y);
        _host.Reminders.ShowPositionPreview();

        StatusText.Text = "已按「" + ReminderPositionMath.Describe(mode) + "」弹出预览窗，拖动它可继续微调。";
    }

    private void DesktopShortcut_Click(object sender, RoutedEventArgs e) => CreateShortcut("desktop");

    private void StartMenuShortcut_Click(object sender, RoutedEventArgs e) => CreateShortcut("startmenu");

    private void CreateShortcut(string target)
    {
        if (_host.SetShortcut(target, true, null, out var path, out var error))
        {
            MessageBox.Show(this, "已创建快捷方式：\n" + path, "快捷方式", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, "创建失败：" + error, "快捷方式", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshSystemStatus();
    }

    private void DeleteDesktopShortcut_Click(object sender, RoutedEventArgs e) => DeleteShortcut("desktop");

    private void DeleteStartMenuShortcut_Click(object sender, RoutedEventArgs e) => DeleteShortcut("startmenu");

    private void DeleteShortcut(string target)
    {
        if (!_host.SetShortcut(target, false, null, out _, out var error))
        {
            MessageBox.Show(this, "删除失败：" + error, "快捷方式", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshSystemStatus();
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
        BuildOffsetVoiceRows();
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

        settings.Theme = (AppTheme)Math.Max(0, ThemeBox.SelectedIndex);
        settings.AutoRestartOnThemeChange = AutoRestartThemeCheck.IsChecked == true;
        settings.ReminderPosition = (ReminderPositionMode)Math.Max(0, ReminderPositionBox.SelectedIndex);
        settings.ReminderPositionX = ReadInt(ReminderPositionXBox.Text, settings.ReminderPositionX, -20000, 20000);
        settings.ReminderPositionY = ReadInt(ReminderPositionYBox.Text, settings.ReminderPositionY, -20000, 20000);
        settings.ReminderRememberPosition = ReminderRememberPositionCheck.IsChecked == true;
        settings.ReminderFollowCursorScreen = ReminderFollowCursorCheck.IsChecked == true;

        // AI 助手
        settings.AiProvider = (AiProviderBox.SelectedItem as AiProviderPreset)?.Name ?? string.Empty;
        settings.AiBaseUrl = (AiBaseUrlBox.Text ?? string.Empty).Trim();
        settings.AiApiMode = (AiApiMode)(AiApiModeBox.SelectedIndex < 0 ? 0 : AiApiModeBox.SelectedIndex);
        settings.AiModel = (AiModelBox.Text ?? string.Empty).Trim();
        settings.AiApiKey = (AiApiKeyBox.Text ?? string.Empty).Trim();
        settings.AiAuthMode = AuthModes[Math.Clamp(AiAuthModeBox.SelectedIndex, 0, AuthModes.Length - 1)];
        settings.AiReasoningEffort = (AiReasoningEffortMode)Math.Max(0, AiReasoningBox.SelectedIndex);
        settings.AiUseSystemProxy = AiSystemProxyCheck.IsChecked == true;
        settings.AiProxyUrl = (AiProxyBox.Text ?? string.Empty).Trim();
        settings.AiTemperature = ReadDouble(AiTemperatureBox.Text, settings.AiTemperature, 0, 2);
        settings.AiTimeoutSeconds = ReadInt(AiTimeoutBox.Text, settings.AiTimeoutSeconds, 5, 3600);
        settings.AiMaxHistoryMessages = ReadInt(AiHistoryBox.Text, settings.AiMaxHistoryMessages, 4, 200);
        settings.AiMaxTokens = ReadInt(AiMaxTokensBox.Text, settings.AiMaxTokens, 0, 32000);
        settings.AiThinking = (AiThinkingLevel)Math.Clamp(AiThinkingBox.SelectedIndex, 0, 3);
        settings.AiShowToolDetails = AiShowToolDetailsCheck.IsChecked == true;
        settings.AiLogActions = AiLogActionsCheck.IsChecked == true;
        settings.AiSystemPrompt = AiSystemPromptBox.Text ?? string.Empty;

        settings.StartupArguments = (StartupArgumentsBox.Text ?? string.Empty).Trim();
        settings.StartMinimized = StartMinimizedCheck.IsChecked == true;

        var offsets = ParseOffsets(DefaultOffsetsBox.Text);
        settings.DefaultReminderOffsets = offsets.Count > 0 ? offsets : new List<int> { 10 };
        if (settings.DefaultReminderOffsets.Count > 0)
        {
            settings.DefaultRemindBeforeMinutes = settings.DefaultReminderOffsets.Min();
        }

        settings.OffsetVoiceTemplates = _offsetVoiceRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .GroupBy(r => r.Minutes)
            .ToDictionary(g => g.Key, g => g.First().Text.Trim());

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
            var previousTheme = _host.Settings.Theme;
            var settings = ReadValues();

            _host.SettingsService.Replace(settings);
            _host.NotifySettingsChanged();

            DialogResult = true;

            // 主题换了：界面大部分已经即时生效，但已经算好的画刷（日历色块）与少数系统弹层
            // 需要整个界面重新加载才完全一致，按设置自动重启一次最省心。
            if (settings.Theme != previousTheme && settings.AutoRestartOnThemeChange)
            {
                RestartAfterClose();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存设置失败：" + ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>「立即重启软件」：先保存当前界面上的设置，再重启。</summary>
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "立即重启日程助手？" + Environment.NewLine + Environment.NewLine + "当前改动会先保存，数据与提醒计划不受影响。",
            "重启", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var settings = ReadValues();
            _host.SettingsService.Replace(settings);
            _host.NotifySettingsChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存设置失败：" + ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
        RestartAfterClose();
    }

    /// <summary>等设置窗口完全关闭后再重启，避免退出流程里还挂着模态窗口。</summary>
    private void RestartAfterClose()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_host.Restart(out var error) || string.IsNullOrWhiteSpace(error)) return;

            MessageBox.Show("自动重启失败：" + error + Environment.NewLine + Environment.NewLine
                    + "请手动关闭并重新打开日程助手。",
                "重启", MessageBoxButton.OK, MessageBoxImage.Warning);
        }), DispatcherPriority.Background);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // 主题是即时预览的，取消时回滚
        if (ThemeService.Apply(_host.Settings.Theme)) _host.NotifyThemeChanged();
        DialogResult = false;
    }
}
