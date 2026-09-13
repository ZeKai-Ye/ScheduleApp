namespace Schedule.Core.Models;

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

    public bool DarkTheme { get; set; }

    public int LookAheadDays { get; set; } = 30;

    public bool ShowCompleted { get; set; } = true;

    // ---------- 系统 ----------
    public bool StartWithWindows { get; set; }

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
        SpeechEnabled = other.SpeechEnabled;
        VoiceId = other.VoiceId;
        VoiceName = other.VoiceName;
        VoiceRate = other.VoiceRate;
        VoiceVolume = other.VoiceVolume;
        VoiceTemplate = other.VoiceTemplate;
        SoundEnabled = other.SoundEnabled;
        DefaultSoundFile = other.DefaultSoundFile;
        SoundBeforeSpeech = other.SoundBeforeSpeech;
        CategoryColors = new Dictionary<string, string>(other.CategoryColors, StringComparer.CurrentCultureIgnoreCase);
        DefaultEventColor = other.DefaultEventColor;
        HourHeight = other.HourHeight;
        MonthCellCapacity = other.MonthCellCapacity;
        WeekStartsOnMonday = other.WeekStartsOnMonday;
        DefaultView = other.DefaultView;
        DarkTheme = other.DarkTheme;
        LookAheadDays = other.LookAheadDays;
        ShowCompleted = other.ShowCompleted;
        StartWithWindows = other.StartWithWindows;
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
