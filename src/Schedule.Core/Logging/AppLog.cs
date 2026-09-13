using System.Text;

namespace Schedule.Core.Logging;

/// <summary>轻量级日志：按天滚动写入 logs 目录，任何异常都不会向外抛出。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logDirectory;
    private static int _minLevel;

    public static bool Enabled { get; set; } = true;

    public static string? CurrentLogFile { get; private set; }

    public static void Initialize(string logDirectory, int minLevel = 1)
    {
        lock (Gate)
        {
            _logDirectory = logDirectory;
            _minLevel = minLevel;
            try
            {
                Directory.CreateDirectory(logDirectory);
            }
            catch
            {
                Enabled = false;
            }
        }
    }

    public static void Debug(string message) => Write(0, "DEBUG", message, null);

    public static void Info(string message) => Write(1, "INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write(2, "WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write(3, "ERROR", message, ex);

    private static void Write(int level, string tag, string message, Exception? ex)
    {
        if (!Enabled || level < _minLevel) return;

        try
        {
            var dir = _logDirectory;
            if (string.IsNullOrEmpty(dir)) return;

            lock (Gate)
            {
                var file = Path.Combine(dir, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                CurrentLogFile = file;

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(tag).Append("] ")
                  .Append(message);

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append("        ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    {
                        sb.AppendLine();
                        sb.Append(ex.StackTrace);
                    }
                }

                File.AppendAllText(file, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                Prune(dir);
            }
        }
        catch
        {
            // 日志失败必须静默
        }
    }

    private static void Prune(string dir)
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles("app-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(14)
                .ToList();
            foreach (var f in files) f.Delete();
        }
        catch
        {
            // 忽略
        }
    }
}
