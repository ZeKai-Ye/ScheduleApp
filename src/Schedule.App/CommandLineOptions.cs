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

    /// <summary>启动时临时指定主题（light / dark / system），不写回设置。</summary>
    public string? ThemeOverride { get; set; }

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
        || ShortcutTarget != null || RemoveShortcutTarget != null || StartupEnabled.HasValue || UiSmoke;

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
                case "--theme":
                    options.ThemeOverride = Value();
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
        "日程助手 ScheduleApp",
        "",
        "用法：ScheduleApp.exe [选项]",
        "",
        "无界面模式：",
        "  --import <csv>        导入 CSV 文件",
        "  --export <csv>        导出全部日程为 CSV",
        "  --mode <模式>         append（默认）/ upsert / replace",
        "  --encoding <编码>     auto（默认）/ utf-8 / gbk / gb18030 / utf-16",
        "  --skip-past           跳过已经过去的日程",
        "  --no-skip-duplicates  不跳过重复日程",
        "  --voices              列出系统已安装的语音",
        "  --speak <文本>        朗读指定文本（用于测试语音）",
        "  --voice <音色Id>      指定朗读音色",
        "  --rate <n>            语速 -10 ~ 10",
        "  --volume <n>          音量 0 ~ 100",
        "  --info                显示数据目录与统计信息",
        "  --out <文件>          把以上输出同时写入文件（便于脚本调用）",
        "",
        "本地接口（供 AI / 脚本直接读写日程）：",
        "  --serve               只启动接口与提醒，不显示主窗口",
        "  --port <端口>         指定接口端口（默认 43121）",
        "  --no-api              本次启动禁用接口",
        "  接口地址与令牌写在数据目录的 api.json，自描述在 GET /api/schema",
        "",
        "桌面集成：",
        "  --shortcut [desktop|startmenu]        创建快捷方式（默认桌面）",
        "  --remove-shortcut [desktop|startmenu] 删除快捷方式",
        "  --shortcut-dir <目录>                 快捷方式输出目录（调试用）",
        "  --startup on|off                      启用 / 停用开机启动",
        "  --startup-args <参数>                 开机启动附加参数（默认 --minimized）",
        "  --minimized                           启动后直接最小化到托盘",
        "  --ui-smoke                            界面冒烟自检：依次构造全部窗口后退出（可截图）",
        "  --theme light|dark|system             临时指定主题（不写回设置）",
        "",
        "通用选项：",
        "  --data <目录>         指定数据目录（默认 %APPDATA%\\ScheduleApp）",
        "  --help                显示帮助",
    });
}
