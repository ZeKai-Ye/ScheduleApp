namespace Schedule.Core.Models;

/// <summary>
/// 全局设置。持久化为 settings.json，所有字段都有安全默认值。
/// </summary>
public sealed class AppSettings
{
    // ---------- 提醒 ----------
    /// <summary>新建日程默认提前提醒分钟数。</summary>
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
    /// <summary>是否朗读提醒。</summary>
    public bool SpeechEnabled { get; set; } = true;

    /// <summary>SAPI 音色 Id，空 = 系统默认。</summary>
    public string VoiceId { get; set; } = string.Empty;

    public string VoiceName { get; set; } = string.Empty;

    /// <summary>语速 -10 ~ 10。</summary>
    public int VoiceRate { get; set; } = 0;

    /// <summary>音量 0 ~ 100。</summary>
    public int VoiceVolume { get; set; } = 100;

    /// <summary>
    /// 自定义播报模板，支持占位符：
    /// {title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status}
    /// </summary>
    public string VoiceTemplate { get; set; } = "{time}，提醒您：{title}。{category}";

    /// <summary>提醒时播放提示音。</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>默认提示音文件（wav/mp3）；为空使用系统提示音。</summary>
    public string DefaultSoundFile { get; set; } = string.Empty;

    /// <summary>朗读前先播放提示音（否则二者同时进行）。</summary>
    public bool SoundBeforeSpeech { get; set; } = true;

    // ---------- 界面 ----------
    /// <summary>点击关闭按钮时最小化到托盘而不是退出。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>最小化时隐藏到托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>是否启用深色主题。</summary>
    public bool DarkTheme { get; set; }

    /// <summary>主窗口启动时展示的范围天数。</summary>
    public int LookAheadDays { get; set; } = 30;

    /// <summary>窗口是否显示已完成日程。</summary>
    public bool ShowCompleted { get; set; } = true;

    // ---------- 系统 ----------
    /// <summary>开机自动启动。</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>CSV 默认编码：auto / utf-8 / gbk / gb18030。</summary>
    public string CsvEncoding { get; set; } = "auto";

    /// <summary>CSV 导入时默认分类。</summary>
    public string CsvDefaultCategory { get; set; } = "导入";

    /// <summary>JSON 备份保留份数。</summary>
    public int BackupKeepCount { get; set; } = 10;

    /// <summary>最近一次使用的 CSV 目录。</summary>
    public string LastCsvDirectory { get; set; } = string.Empty;

    public void CopyFrom(AppSettings other)
    {
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
        CloseToTray = other.CloseToTray;
        MinimizeToTray = other.MinimizeToTray;
        DarkTheme = other.DarkTheme;
        LookAheadDays = other.LookAheadDays;
        ShowCompleted = other.ShowCompleted;
        StartWithWindows = other.StartWithWindows;
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
