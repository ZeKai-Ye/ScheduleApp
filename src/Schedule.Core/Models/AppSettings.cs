using Schedule.Core.Localization;

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
/// 接口鉴权方式。取值顺序与设置页下拉一致（Bearer → api-key → 不带），
/// 免得以后有人调整界面顺序时又对不上号。
/// </summary>
public enum AiAuthMode
{
    /// <summary>Authorization: Bearer &lt;key&gt;（OpenAI / DeepSeek / 通义 / OpenRouter …，默认）。</summary>
    Bearer = 0,

    /// <summary>api-key 请求头（Azure OpenAI）。</summary>
    ApiKeyHeader = 1,

    /// <summary>不带鉴权（本地服务，显式关闭）。</summary>
    None = 2,
}

/// <summary>是否给模型服务发送推理强度参数（reasoning_effort）。</summary>
public enum AiReasoningEffortMode
{
    /// <summary>自动：本地服务一定发（实测能把思考压到 0 字）；云端只在模型看起来是推理模型时才发。</summary>
    Auto = 0,

    /// <summary>总是发送（明确知道服务端支持时用）。</summary>
    Always = 1,

    /// <summary>从不发送（某些云端服务端遇到不认识的字段会报 400）。</summary>
    Never = 2,
}

/// <summary>
/// 思考（思维链）等级。本地小模型基本不需要思考，等级越低越快、越省上下文。
/// 实测 qwen3:8b：关闭 6~10 秒 / 0 字思维链；low 40~50 秒 / 500~700 字；medium 51 秒 / 671 字。
/// </summary>
public enum AiThinkingLevel
{
    /// <summary>最低：完全不输出思考过程（默认）。</summary>
    Off = 0,

    /// <summary>低：仍会思考（实测并不比 medium 快多少）。</summary>
    Low = 1,

    Medium = 2,

    High = 3,
}

/// <summary>AI 对话使用的接口协议。</summary>
public enum AiApiMode
{
    /// <summary>自动判断：本地 Ollama 默认端口走原生接口，其余走 OpenAI 兼容。</summary>
    Auto = 0,

    /// <summary>强制 OpenAI 兼容（/v1/chat/completions）：LM Studio / llama.cpp / vLLM 等。</summary>
    OpenAi = 1,

    /// <summary>强制 Ollama 原生（/api/chat）：能真正关闭思维链，本地 Qwen3 明显更快。</summary>
    Ollama = 2,
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

    /// <summary>
    /// 界面语言：跟随系统 / 中文 / English。
    /// XAML 里的文案在窗口加载时求值，所以切换后需要重启界面才完全生效。
    /// </summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    // ---------- AI 助手（对话） ----------
    /// <summary>
    /// 对话模型服务地址（OpenAI 兼容根地址）。
    /// 默认本机 Ollama：http://127.0.0.1:11434/v1；LM Studio 是 http://127.0.0.1:1234/v1。
    /// </summary>
    public string AiBaseUrl { get; set; } = "http://127.0.0.1:11434/v1";

    /// <summary>
    /// 接口密钥（本地服务一般留空）。
    /// 也可以写 <c>env:OPENAI_API_KEY</c>，程序会在每次请求时去读环境变量，密钥不落盘。
    /// </summary>
    public string AiApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 鉴权方式：不带 / Bearer / api-key 头。
    /// 默认 Bearer——填了密钥就能直接用云端服务；本地服务密钥为空，带不带头都一样。
    /// </summary>
    public AiAuthMode AiAuthMode { get; set; } = AiAuthMode.Bearer;

    /// <summary>是否发送 reasoning_effort（默认自动：本地发、云端按模型判断）。</summary>
    public AiReasoningEffortMode AiReasoningEffort { get; set; } = AiReasoningEffortMode.Auto;

    /// <summary>是否使用系统代理（企业网络 / 需要科学上网时通常要开）。</summary>
    public bool AiUseSystemProxy { get; set; } = true;

    /// <summary>自定义代理地址（留空表示用系统代理），例如 http://127.0.0.1:7890。</summary>
    public string AiProxyUrl { get; set; } = string.Empty;

    /// <summary>上次选择的服务预设名（仅用于界面回显）。</summary>
    public string AiProvider { get; set; } = string.Empty;

    /// <summary>当前选择的模型名，例如 qwen3:8b。</summary>
    public string AiModel { get; set; } = "qwen3:8b";

    /// <summary>接口协议（自动 / OpenAI 兼容 / Ollama 原生）。</summary>
    public AiApiMode AiApiMode { get; set; } = AiApiMode.Auto;

    /// <summary>采样温度（工具调用建议 0 ~ 0.3）。</summary>
    public double AiTemperature { get; set; } = 0.2;

    /// <summary>单次请求超时秒数（本地大模型首次加载较慢）。</summary>
    public int AiTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// 思考（思维链）等级，默认 <see cref="AiThinkingLevel.Off"/>（最低）。
    /// 关掉思考是本地模型最重要的一条提速手段：实测 qwen3:8b 从 40~50 秒降到 6~10 秒。
    /// </summary>
    public AiThinkingLevel AiThinking { get; set; } = AiThinkingLevel.Off;

    /// <summary>单次最大输出 token（0 = 交给服务端默认）。</summary>
    public int AiMaxTokens { get; set; }

    /// <summary>最多携带多少条历史消息（控制上下文长度）。</summary>
    public int AiMaxHistoryMessages { get; set; } = 16;

    /// <summary>自定义系统提示，留空用内置的。</summary>
    public string AiSystemPrompt { get; set; } = string.Empty;

    /// <summary>在对话里显示工具调用详情（AI 具体做了什么）。</summary>
    public bool AiShowToolDetails { get; set; } = true;

    /// <summary>把 AI 的改动（新建 / 修改 / 删除）记入日志，便于回溯。</summary>
    public bool AiLogActions { get; set; } = true;

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

    /// <summary>CSV 导入的默认分类；留空表示按界面语言取「导入 / Imported」。</summary>
    public string CsvDefaultCategory { get; set; } = string.Empty;

    public int BackupKeepCount { get; set; } = 10;

    public string LastCsvDirectory { get; set; } = string.Empty;

    /// <summary>上次导出的格式（csv / ics），用于记住下拉选择。</summary>
    public string LastExportFormat { get; set; } = "csv";

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
        Language = other.Language;
        AiBaseUrl = other.AiBaseUrl;
        AiApiKey = other.AiApiKey;
        AiAuthMode = other.AiAuthMode;
        AiReasoningEffort = other.AiReasoningEffort;
        AiUseSystemProxy = other.AiUseSystemProxy;
        AiProxyUrl = other.AiProxyUrl;
        AiProvider = other.AiProvider;
        AiModel = other.AiModel;
        AiApiMode = other.AiApiMode;
        AiTemperature = other.AiTemperature;
        AiTimeoutSeconds = other.AiTimeoutSeconds;
        AiThinking = other.AiThinking;
        AiMaxTokens = other.AiMaxTokens;
        AiMaxHistoryMessages = other.AiMaxHistoryMessages;
        AiSystemPrompt = other.AiSystemPrompt;
        AiShowToolDetails = other.AiShowToolDetails;
        AiLogActions = other.AiLogActions;
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
        LastExportFormat = other.LastExportFormat;
    }

    public AppSettings Clone()
    {
        var copy = new AppSettings();
        copy.CopyFrom(this);
        return copy;
    }
}
