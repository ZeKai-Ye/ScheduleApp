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

    public bool SkipPast { get; set; }

    public bool NoSkipDuplicates { get; set; }

    public bool HasCliAction =>
        ImportFile != null || ExportFile != null || SpeakText != null || ListVoices || ShowInfo || Help;

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
        "通用选项：",
        "  --data <目录>         指定数据目录（默认 %APPDATA%\\ScheduleApp）",
        "  --help                显示帮助",
    });
}
