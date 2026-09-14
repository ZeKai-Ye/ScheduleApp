using System.Globalization;

namespace Schedule.Core.Services;

/// <summary>
/// 「重启自身」的参数规划（纯逻辑，便于自测）。
///
/// 自我重启有一个绕不开的顺序问题：单实例互斥体由**旧进程**持有，
/// 新进程必须等旧进程真正退出后才能在同一个数据目录上启动。
/// 因此新进程会带上 <c>--restart-of &lt;pid&gt;</c>，启动时先等那个 PID 消失。
/// </summary>
public static class RestartPlan
{
    /// <summary>等待旧实例退出的参数名。</summary>
    public const string WaitArgument = "--restart-of";

    /// <summary>由重启逻辑自己决定、不接受透传的参数（透传会打架）。</summary>
    private static readonly string[] ManagedArguments = { "--data", WaitArgument, "--theme" };

    /// <summary>
    /// 拼出新进程的参数列表（不含可执行文件本身）。
    /// </summary>
    /// <param name="dataDirectory">当前数据目录：新进程必须用同一份数据，否则会另起一套。</param>
    /// <param name="currentProcessId">当前进程 PID。</param>
    /// <param name="extraArguments">额外透传的启动模式参数（如 --serve / --port）。</param>
    public static List<string> BuildArguments(
        string? dataDirectory,
        int currentProcessId,
        IEnumerable<string>? extraArguments = null)
    {
        var arguments = new List<string>();

        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            arguments.Add("--data");
            arguments.Add(dataDirectory.Trim());
        }

        arguments.Add(WaitArgument);
        arguments.Add(currentProcessId.ToString(CultureInfo.InvariantCulture));

        arguments.AddRange(Sanitize(extraArguments));

        return arguments;
    }

    /// <summary>
    /// 过滤掉受管参数（<c>--data</c> / <c>--restart-of</c> / <c>--theme</c>）及其取值，
    /// 避免透传时把数据目录或主题覆盖成旧值。
    /// </summary>
    public static List<string> Sanitize(IEnumerable<string>? arguments)
    {
        var result = new List<string>();
        if (arguments == null) return result;

        var list = arguments.ToList();

        for (var i = 0; i < list.Count; i++)
        {
            var argument = list[i]?.Trim();
            if (string.IsNullOrEmpty(argument)) continue;

            var name = argument;
            var hasInlineValue = false;
            var eq = argument.IndexOf('=');
            if (argument.StartsWith("--", StringComparison.Ordinal) && eq > 0)
            {
                name = argument.Substring(0, eq);
                hasInlineValue = true;
            }

            if (!ManagedArguments.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(argument);
                continue;
            }

            // "--data=值" 自带取值；"--data 值" 要把后面那个取值一起跳过
            // （但下一个参数若是另一个 -- 开头的开关，说明这个参数没有取值）
            var next = i + 1 < list.Count ? list[i + 1]?.Trim() : null;
            if (!hasInlineValue && !string.IsNullOrEmpty(next) && !next.StartsWith("--", StringComparison.Ordinal))
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>从命令行里取出「要等待的旧进程 PID」（支持 <c>--restart-of 123</c> 与 <c>--restart-of=123</c>）。</summary>
    public static bool TryReadWaitPid(IEnumerable<string>? arguments, out int processId)
    {
        processId = 0;
        if (arguments == null) return false;

        var list = arguments.ToList();

        for (var i = 0; i < list.Count; i++)
        {
            var argument = list[i]?.Trim();
            if (string.IsNullOrEmpty(argument)) continue;

            if (argument.StartsWith(WaitArgument + "=", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(argument.Substring(WaitArgument.Length + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out processId) && processId > 0;
            }

            if (string.Equals(argument, WaitArgument, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < list.Count
                    && int.TryParse(list[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out processId)
                    && processId > 0;
            }
        }

        return false;
    }

    /// <summary>是否真的需要等待（PID 无效或就是自己时不必等）。</summary>
    public static bool ShouldWaitFor(int? previousProcessId, int currentProcessId)
        => previousProcessId is > 0 && previousProcessId.Value != currentProcessId;

    /// <summary>把参数拼成一行（只用于日志 / 提示）。</summary>
    public static string ToCommandLine(IEnumerable<string>? arguments)
        => string.Join(" ", (arguments ?? Array.Empty<string>()).Select(Quote));

    /// <summary>带空格的参数加引号，便于人读。</summary>
    public static string Quote(string? argument)
    {
        var value = argument ?? string.Empty;
        return value.Length > 0 && value.Contains(' ') ? "\"" + value + "\"" : value;
    }
}
