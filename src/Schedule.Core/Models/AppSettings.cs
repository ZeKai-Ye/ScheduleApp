namespace Schedule.Core.Models;

/// <summary>界面主题模式（叫 AppTheme 以免与 System.Windows.ThemeMode 撞名）。</summary>
public enum AppTheme
{
    /// <summary>浅色（默认）。</summary>
    Light = 0,
    /// <summary>夜间。</summary>
    Dark = 1,
    /// <summary>跟随 Windows 的应用模式。</summary>
    System = 2,
}

/// <summary>主界面视图模式。</summary>
public enum ScheduleViewMode
{
    List = 0,
    Week = 1,
    Month = 2,
}

/// <summary>
/// 全局设置。持久化为 settings.json，所有字段都有安全默认值。
/// </summary>
public sealed class AppSettings
{
    // ---------- 提醒 ----------
    /// <summary>新建日程默认的提醒档位（提前分钟数，可多档）。</summary>
    public List<int> DefaultReminderOffsets { get; set; } = new() { 10 };

    /// <summary>单档提醒（兼容旧数据与 CSV 默认值）。</summary>
    public int DefaultRemindBeforeMinutes { get; set; } = 10;

    /// <summary>默认是否启用提醒。</summary>
    public bool DefaultReminderEnabled { get; set; } = true;

    /// <summary>稍后提醒的默认分钟数。</summary>
    public int SnoozeMinutes { get; set; } = 5;

    /// <summary>提醒弹窗自动关闭秒数，0 = 不自动关闭。</summary>
    public int ReminderAutoCloseSeconds { get; set; } = 0;

    /// <summary>提醒弹窗是否置顶显示。</summary>
    public bool ReminderAlwaysOnTop { get; set; } = true;

    /// <summary>提醒弹窗出现的位置。</summary>
    public ReminderPositionMode ReminderPosition { get; set; } = ReminderPositionMode.BottomRight;

    /// <summary>
    /// 位置偏移 / 自定义绝对 X（含义随 ReminderPosition 变化）。
    /// 单位是设备无关单位（DIP），与窗口 Left 一致；定位时再按屏幕缩放换算成设备像素。
    /// </summary>
    public int ReminderPositionX { get; set; }

    /// <summary>位置偏移 / 自定义绝对 Y（含义随 ReminderPosition 变化，单位 DIP）。</summary>
    public int ReminderPositionY { get; set; }

    /// <summary>拖动提醒弹窗后自动记住位置（切换为自定义位置）。</summary>
    public bool ReminderRememberPosition { get; set; } = true;

    /// <summary>弹窗跟随鼠标所在显示器（关闭则使用主显示器）。</summary>
    public bool ReminderFollowCursorScreen { get; set; } = true;

    // ---------- 语音 ----------
    public bool SpeechEnabled { get; set; } = true;

    public string VoiceId { get; set; } = string.Empty;

    public string VoiceName { get; set; } = string.Empty;

    /// <summary>语速 -10 ~ 10。</summary>
    public int VoiceRate { get; set; } = 0;

    /// <summary>音量 0 ~ 100。</summary>
    public int VoiceVolume { get; set; } = 100;

    /// <summary>
    /// 自定义播报模板，支持占位符：
    /// {title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status} {offset}
    /// </summary>
    public string VoiceTemplate { get; set; } = "{time}，提醒您：{title}。{countdown}。{category}";

    public bool SoundEnabled { get; set; } = true;

    public string DefaultSoundFile { get; set; } = string.Empty;

    public bool SoundBeforeSpeech { get; set; } = true;

    /// <summary>
    /// 全局「按档位」的默认播报文案（键 = 提前分钟数）。
    /// 未被日程自身文案覆盖时生效，例如 { 30: "还有 30 分钟", 0: "现在开始" }。
    /// </summary>
    public Dictionary<int, string> OffsetVoiceTemplates { get; set; } = new();

    // ---------- 本地接口（供 AI / 脚本直接读写日程） ----------
    /// <summary>是否启用本地 HTTP JSON 接口。</summary>
    public bool ApiEnabled { get; set; } = true;

    /// <summary>接口端口（仅监听 127.0.0.1）。</summary>
    public int ApiPort { get; set; } = 43121;

    /// <summary>接口访问令牌，首次启动自动生成。</summary>
    public string ApiToken { get; set; } = string.Empty;

    // ---------- 外观 ----------
    /// <summary>分类 → 颜色（#RRGGBB）。未配置的分类按名称稳定散列取色。</summary>
    public Dictionary<string, string> CategoryColors { get; set; } = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>事件默认颜色；为空表示跟随分类。</summary>
    public string DefaultEventColor { get; set; } = string.Empty;

    /// <summary>周视图每小时的高度（像素）。</summary>
    public int HourHeight { get; set; } = 56;

    /// <summary>月视图每格最多显示的事件条数。</summary>
    public int MonthCellCapacity { get; set; } = 3;

    /// <summary>一周从周一（true）还是周日（false）开始。</summary>
    public bool WeekStartsOnMonday { get; set; } = true;

    /// <summary>启动时的默认视图。</summary>
    public ScheduleViewMode DefaultView { get; set; } = ScheduleViewMode.List;

    /// <summary>界面主题：浅色 / 夜间 / 跟随系统。</summary>
    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>
    /// 主题发生变化并保存后是否自动重启软件。
    /// 界面绝大部分是 DynamicResource，切主题即时生效；但已经算好的画刷（日历色块颜色等）
    /// 以及少数系统弹层需要整个界面重新加载才完全一致，重启一次最省心。
    /// </summary>
    public bool AutoRestartOnThemeChange { get; set; } = true;

    /// <summary>旧字段（仅兼容历史数据，请使用 Theme）。</summary>
    public bool DarkTheme { get; set; }

    public int LookAheadDays { get; set; } = 30;

    public bool ShowCompleted { get; set; } = true;

    // ---------- 系统 ----------
    public bool StartWithWindows { get; set; }

    /// <summary>开机启动时直接最小化到托盘（不弹主窗口）。</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>开机启动时附加的命令行参数（例如 --serve）。</summary>
    public string StartupArguments { get; set; } = string.Empty;

    public bool MinimizeToTray { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    /// <summary>CSV 默认编码：auto / utf-8 / gbk / gb18030。</summary>
    public string CsvEncoding { get; set; } = "auto";

    public string CsvDefaultCategory { get; set; } = "导入";

    public int BackupKeepCount { get; set; } = 10;

    public string LastCsvDirectory { get; set; } = string.Empty;

    public void CopyFrom(AppSettings other)
    {
        DefaultReminderOffsets = new List<int>(other.DefaultReminderOffsets);
        DefaultRemindBeforeMinutes = other.DefaultRemindBeforeMinutes;
        DefaultReminderEnabled = other.DefaultReminderEnabled;
        SnoozeMinutes = other.SnoozeMinutes;
        ReminderAutoCloseSeconds = other.ReminderAutoCloseSeconds;
        ReminderAlwaysOnTop = other.ReminderAlwaysOnTop;
        ReminderPosition = other.ReminderPosition;
        ReminderPositionX = other.ReminderPositionX;
        ReminderPositionY = other.ReminderPositionY;
        ReminderRememberPosition = other.ReminderRememberPosition;
        ReminderFollowCursorScreen = other.ReminderFollowCursorScreen;
        SpeechEnabled = other.SpeechEnabled;
        VoiceId = other.VoiceId;
        VoiceName = other.VoiceName;
        VoiceRate = other.VoiceRate;
        VoiceVolume = other.VoiceVolume;
        VoiceTemplate = other.VoiceTemplate;
        SoundEnabled = other.SoundEnabled;
        DefaultSoundFile = other.DefaultSoundFile;
        SoundBeforeSpeech = other.SoundBeforeSpeech;
        OffsetVoiceTemplates = new Dictionary<int, string>(other.OffsetVoiceTemplates);
        ApiEnabled = other.ApiEnabled;
        ApiPort = other.ApiPort;
        ApiToken = other.ApiToken;
        CategoryColors = new Dictionary<string, string>(other.CategoryColors, StringComparer.CurrentCultureIgnoreCase);
        DefaultEventColor = other.DefaultEventColor;
        HourHeight = other.HourHeight;
        MonthCellCapacity = other.MonthCellCapacity;
        WeekStartsOnMonday = other.WeekStartsOnMonday;
        DefaultView = other.DefaultView;
        Theme = other.Theme;
        AutoRestartOnThemeChange = other.AutoRestartOnThemeChange;
        DarkTheme = other.DarkTheme;
        LookAheadDays = other.LookAheadDays;
        ShowCompleted = other.ShowCompleted;
        StartWithWindows = other.StartWithWindows;
        StartMinimized = other.StartMinimized;
        StartupArguments = other.StartupArguments;
        MinimizeToTray = other.MinimizeToTray;
        CloseToTray = other.CloseToTray;
        CsvEncoding = other.CsvEncoding;
        CsvDefaultCategory = other.CsvDefaultCategory;
        BackupKeepCount = other.BackupKeepCount;
        LastCsvDirectory = other.LastCsvDirectory;
    }

    public AppSettings Clone()
    {
        var copy = new AppSettings();
        copy.CopyFrom(this);
        return copy;
    }
}
