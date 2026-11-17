using Schedule.Core.Localization;

namespace Schedule.App;

/// <summary>命令行参数（同时服务于 GUI 启动与无界面模式）。</summary>
public sealed class CommandLineOptions
{
    public string? DataDirectory { get; set; }

    public string? ImportFile { get; set; }

    public string? ExportFile { get; set; }

    public string? OutFile { get; set; }

    public string? SpeakText { get; set; }

    /// <summary>导入模式：append / upsert / replace。</summary>
    public string ImportMode { get; set; } = "append";

    public string Encoding { get; set; } = "auto";

    public string? VoiceId { get; set; }

    public int Rate { get; set; }

    public int Volume { get; set; } = 100;

    public bool ListVoices { get; set; }

    public bool ShowInfo { get; set; }

    public bool Help { get; set; }

    /// <summary>只启动本地接口与提醒，不显示主窗口。</summary>
    public bool Serve { get; set; }

    /// <summary>覆盖接口端口。</summary>
    public int? ApiPort { get; set; }

    /// <summary>本次启动禁用本地接口。</summary>
    public bool NoApi { get; set; }

    /// <summary>启动后直接最小化到托盘。</summary>
    public bool Minimized { get; set; }

    /// <summary>界面冒烟自检：构造全部窗口后退出（开发 / CI 用）。</summary>
    public bool UiSmoke { get; set; }

    /// <summary>
    /// 本次运行不改动任何系统状态（不碰开机启动注册表 / 启动文件夹）。
    /// 自检、打包验证这类"只是跑一下看看"的场景必须打开它，
    /// 否则用的是临时数据目录，会把用户真实的开机启动设置当成"用户关掉了"而删掉。
    /// </summary>
    public bool NoSystemChanges { get; set; }

    /// <summary>启动时临时指定主题（light / dark / system），不写回设置。</summary>
    public string? ThemeOverride { get; set; }
    /// <summary>启动时临时指定界面语言（zh / en / system），不写回设置。</summary>
    public string? LanguageOverride { get; set; }

    /// <summary>命令行里跟 AI 说一句话并打印结果（无界面验证对话功能用）。</summary>
    public string? AiChatText { get; set; }

    /// <summary>列出模型服务上的模型后退出。</summary>
    public bool AiModels { get; set; }

    /// <summary>把统一格式的 CSV 模板写到指定文件后退出。</summary>
    public string? CsvTemplateFile { get; set; }

    /// <summary>创建快捷方式（desktop / startmenu）。</summary>
    public string? ShortcutTarget { get; set; }

    /// <summary>删除快捷方式（desktop / startmenu）。</summary>
    public string? RemoveShortcutTarget { get; set; }

    /// <summary>快捷方式输出目录（调试 / 便携场景）。</summary>
    public string? ShortcutDirectory { get; set; }

    /// <summary>开机启动开关（命令行动作）。</summary>
    public bool? StartupEnabled { get; set; }

    /// <summary>开机启动附加参数。</summary>
    public string? StartupArguments { get; set; }

    public bool SkipPast { get; set; }

    public bool NoSkipDuplicates { get; set; }

    public bool HasCliAction =>
        ImportFile != null || ExportFile != null || SpeakText != null || ListVoices || ShowInfo || Help
        || ShortcutTarget != null || RemoveShortcutTarget != null || StartupEnabled.HasValue || UiSmoke
        || AiChatText != null || AiModels || CsvTemplateFile != null;

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var key = raw;
            string? inlineValue = null;
            var eq = raw.IndexOf('=');
            if (raw.StartsWith("--", StringComparison.Ordinal) && eq > 0)
            {
                key = raw.Substring(0, eq);
                inlineValue = raw.Substring(eq + 1);
            }

            string? Value()
            {
                if (inlineValue != null) return inlineValue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return args[++i];
                }

                return null;
            }

            switch (key.ToLowerInvariant())
            {
                case "--data":
                    options.DataDirectory = Value();
                    break;
                case "--import":
                    options.ImportFile = Value();
                    break;
                case "--export":
                    options.ExportFile = Value();
                    break;
                case "--out":
                    options.OutFile = Value();
                    break;
                case "--speak":
                    options.SpeakText = Value();
                    break;
                case "--mode":
                    options.ImportMode = (Value() ?? "append").ToLowerInvariant();
                    break;
                case "--encoding":
                    options.Encoding = Value() ?? "auto";
                    break;
                case "--voice":
                    options.VoiceId = Value();
                    break;
                case "--rate":
                    if (int.TryParse(Value(), out var rate)) options.Rate = rate;
                    break;
                case "--volume":
                    if (int.TryParse(Value(), out var volume)) options.Volume = volume;
                    break;
                case "--voices":
                    options.ListVoices = true;
                    break;
                case "--info":
                    options.ShowInfo = true;
                    break;
                case "--skip-past":
                    options.SkipPast = true;
                    break;
                case "--no-skip-duplicates":
                    options.NoSkipDuplicates = true;
                    break;
                case "--serve":
                    options.Serve = true;
                    break;
                case "--port":
                    if (int.TryParse(Value(), out var apiPort)) options.ApiPort = apiPort;
                    break;
                case "--no-api":
                    options.NoApi = true;
                    break;
                case "--minimized":
                    options.Minimized = true;
                    break;
                case "--ui-smoke":
                    options.UiSmoke = true;
                    break;
                case "--lang":
                    options.LanguageOverride = Value();
                    break;
                case "--theme":
                    options.ThemeOverride = Value();
                    break;
                case "--ai-chat":
                    options.AiChatText = Value();
                    break;
                case "--ai-models":
                    options.AiModels = true;
                    break;
                case "--csv-template":
                    options.CsvTemplateFile = Value();
                    break;
                case "--shortcut":
                    options.ShortcutTarget = Value() ?? "desktop";
                    break;
                case "--remove-shortcut":
                    options.RemoveShortcutTarget = Value() ?? "desktop";
                    break;
                case "--shortcut-dir":
                    options.ShortcutDirectory = Value();
                    break;
                case "--startup":
                    options.StartupEnabled = (Value() ?? "on").ToLowerInvariant() is "on" or "true" or "1" or "yes";
                    break;
                case "--startup-args":
                    options.StartupArguments = Value();
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.Help = true;
                    break;
            }
        }

        return options;
    }

    public static string Usage => string.Join(Environment.NewLine, new[]
    {
        Loc.T("日程助手 ScheduleApp", "ScheduleApp"),
        "",
        Loc.T("用法：ScheduleApp.exe [选项]", "Usage: ScheduleApp.exe [options]"),
        "",
        Loc.T("无界面模式：", "Headless mode:"),
        Loc.T("  --import <文件>       导入日程：CSV（统一格式）或 iCalendar（.ics），按扩展名/内容自动识别",
              "  --import <file>       Import events from CSV (unified format) or iCalendar (.ics), detected automatically"),
        Loc.T("  --export <csv>        导出全部日程为 CSV", "  --export <csv>        Export all events to CSV"),
        Loc.T("  --mode <模式>         append（默认）/ upsert / replace", "  --mode <mode>         append (default) / upsert / replace"),
        Loc.T("  --encoding <编码>     auto（默认）/ utf-8 / gbk / gb18030 / utf-16",
              "  --encoding <name>     auto (default) / utf-8 / gbk / gb18030 / utf-16"),
        Loc.T("  --csv-template <文件> 导出统一格式的 CSV 模板（表头 + 两行示例，照着填即可导入）",
              "  --csv-template <file> Write a CSV template (header + two sample rows)"),
        Loc.T("  --skip-past           跳过已经过去的日程", "  --skip-past           Skip events that are already in the past"),
        Loc.T("  --no-skip-duplicates  不跳过重复日程", "  --no-skip-duplicates  Do not skip duplicate events"),
        Loc.T("  --voices              列出系统已安装的语音", "  --voices              List installed system voices"),
        Loc.T("  --speak <文本>        朗读指定文本（用于测试语音）", "  --speak <text>        Speak the given text (voice test)"),
        Loc.T("  --voice <音色Id>      指定朗读音色", "  --voice <id>          Use the given voice"),
        Loc.T("  --rate <n>            语速 -10 ~ 10", "  --rate <n>            Speaking rate, -10 to 10"),
        Loc.T("  --volume <n>          音量 0 ~ 100", "  --volume <n>          Volume, 0 to 100"),
        Loc.T("  --info                显示数据目录与统计信息", "  --info                Show data directory and statistics"),
        Loc.T("  --out <文件>          把以上输出同时写入文件（便于脚本调用）",
              "  --out <file>          Also write the output to a file (for scripts)"),
        "",
        Loc.T("本地接口（供 AI / 脚本直接读写日程）：", "Local API (read/write events from AI or scripts):"),
        Loc.T("  --serve               只启动接口与提醒，不显示主窗口", "  --serve               Run API and reminders only, no main window"),
        Loc.T("  --port <端口>         指定接口端口（默认 43121）", "  --port <port>         API port (default 43121)"),
        Loc.T("  --no-api              本次启动禁用接口", "  --no-api              Disable the API for this run"),
        Loc.T("  接口地址与令牌写在数据目录的 api.json，自描述在 GET /api/schema",
              "  Address and token live in api.json in the data directory; schema at GET /api/schema"),
        "",
        Loc.T("桌面集成：", "Desktop integration:"),
        Loc.T("  --shortcut [desktop|startmenu]        创建快捷方式（默认桌面）",
              "  --shortcut [desktop|startmenu]        Create a shortcut (desktop by default)"),
        Loc.T("  --remove-shortcut [desktop|startmenu] 删除快捷方式",
              "  --remove-shortcut [desktop|startmenu] Remove a shortcut"),
        Loc.T("  --shortcut-dir <目录>                 快捷方式输出目录（调试用）",
              "  --shortcut-dir <dir>                  Shortcut output directory (for debugging)"),
        Loc.T("  --startup on|off                      启用 / 停用开机启动",
              "  --startup on|off                      Enable / disable start with Windows"),
        Loc.T("  --startup-args <参数>                 开机启动附加参数（默认 --minimized）",
              "  --startup-args <args>                 Extra startup arguments (default --minimized)"),
        Loc.T("  --minimized                           启动后直接最小化到托盘",
              "  --minimized                           Start minimized to the tray"),
        Loc.T("  --ui-smoke                            界面冒烟自检：依次构造全部窗口后退出（可截图）",
              "  --ui-smoke                            UI smoke test: build every window, then exit (can screenshot)"),
        Loc.T("  --theme light|dark|system             临时指定主题（不写回设置）",
              "  --theme light|dark|system             Temporary theme override (not saved)"),
        Loc.T("  --lang zh|en|system                   临时指定界面语言（不写回设置）",
              "  --lang zh|en|system                   Temporary UI language override (not saved)"),
        "",
        Loc.T("AI 对话（模型跑在本地，默认 Ollama + qwen3:8b）：",
              "AI chat (model runs locally, Ollama + qwen3:8b by default):"),
        Loc.T("  --ai-chat <文本>      跟 AI 说一句话并打印回复（同时会把工具调用一并打印）",
              "  --ai-chat <text>      Send one message to the AI and print the reply (including tool calls)"),
        Loc.T("  --ai-models           列出模型服务上的可用模型", "  --ai-models           List models available on the model service"),
        Loc.T("  地址 / 模型在设置里改，或直接用 settings.json 的 aiBaseUrl / aiModel",
              "  Change endpoint / model in Settings, or edit aiBaseUrl / aiModel in settings.json"),
        "",
        Loc.T("通用选项：", "General options:"),
        Loc.T("  --data <目录>         指定数据目录（默认 %APPDATA%\\\\ScheduleApp）",
              "  --data <dir>          Data directory (default %APPDATA%\\\\ScheduleApp)"),
        Loc.T("  --help                显示帮助", "  --help                Show this help"),
    });
}
