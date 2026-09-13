using System.Diagnostics;

namespace Schedule.Core.Storage;

/// <summary>
/// 统一管理应用的数据文件位置。
/// 默认：%APPDATA%\ScheduleApp；便携模式：程序目录下的 data 文件夹。
/// 可在启动时通过命令行 <c>--data &lt;dir&gt;</c> 覆盖。
/// </summary>
public static class AppPaths
{
    private static readonly object Gate = new();
    private static string? _dataDirectory;
    private static bool _initialized;

    public const string AppFolderName = "ScheduleApp";
    public const string PortableMarkerFileName = "portable.txt";

    public static string DataDirectory
    {
        get
        {
            EnsureInitialized();
            return _dataDirectory!;
        }
    }

    public static string ItemsFile => Path.Combine(DataDirectory, "schedules.json");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ImportHistoryFile => Path.Combine(DataDirectory, "import-history.json");

    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string ExportDirectory => Path.Combine(DataDirectory, "export");

    /// <summary>
    /// 单实例互斥体名称按数据目录隔离：同一个数据目录只允许一个实例（避免两个进程同时写 schedules.json），
    /// 而使用不同 --data 的实例可以并行运行（例如便携版与安装版同时使用）。
    /// </summary>
    public static string SingleInstanceMutexName => @"Local\ScheduleApp.SingleInstance." + DirectoryKey;

    /// <summary>用于「激活已有实例」的命名事件（同样按数据目录隔离）。</summary>
    public static string ActivateEventName => @"Local\ScheduleApp.Activate." + DirectoryKey;

    /// <summary>数据目录的稳定短哈希（FNV-1a，跨进程一致）。</summary>
    private static string DirectoryKey
    {
        get
        {
            var path = DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

            unchecked
            {
                const ulong offset = 14695981039346656037;
                const ulong prime = 1099511628211;

                var hash = offset;
                foreach (var c in path)
                {
                    hash ^= c;
                    hash *= prime;
                }

                return hash.ToString("x16");
            }
        }
    }

    /// <summary>程序所在目录（发布后即 exe 目录）。</summary>
    public static string ExecutableDirectory
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                path = Process.GetCurrentProcess().MainModule?.FileName;
            }

            return string.IsNullOrEmpty(path)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// 初始化数据目录。dataDirectoryOverride 优先级最高，其次便携模式，最后 %APPDATA%。
    /// </summary>
    public static string Initialize(string? dataDirectoryOverride = null)
    {
        lock (Gate)
        {
            var preferred = Resolve(dataDirectoryOverride);
            _dataDirectory = preferred;
            _initialized = true;

            if (TryPrepare(preferred)) return preferred;

            // 首选目录不可写（受限环境 / 权限不足 / 只读介质）时逐个回退，避免直接崩溃
            foreach (var candidate in EnumerateFallbacks())
            {
                if (string.Equals(candidate, preferred, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryPrepare(candidate)) continue;

                _dataDirectory = candidate;
                LastFallbackReason = "无法写入 " + preferred + "，已改用 " + candidate;
                return candidate;
            }

            LastFallbackReason = "所有候选数据目录都不可写，日程可能无法保存。";
            return preferred;
        }
    }

    /// <summary>最近一次数据目录回退的原因（为空表示未回退）。</summary>
    public static string? LastFallbackReason { get; private set; }

    private static IEnumerable<string> EnumerateFallbacks()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            yield return Path.Combine(local, AppFolderName);
        }

        yield return Path.Combine(ExecutableDirectory, "data");
        yield return Path.Combine(Path.GetTempPath(), AppFolderName);
    }

    /// <summary>创建目录并真正验证可写性。</summary>
    private static bool TryPrepare(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            Directory.CreateDirectory(Path.Combine(root, "backups"));
            Directory.CreateDirectory(Path.Combine(root, "export"));

            var probe = Path.Combine(root, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void EnsureDirectories()
    {
        foreach (var directory in new[] { LogDirectory, BackupDirectory, ExportDirectory })
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception)
            {
                // 目录不可用时静默跳过，由调用方按需处理
            }
        }
    }

    /// <summary>是否处于便携模式（程序目录存在 portable.txt）。</summary>
    public static bool IsPortable
    {
        get
        {
            try
            {
                return File.Exists(Path.Combine(ExecutableDirectory, PortableMarkerFileName));
            }
            catch
            {
                return false;
            }
        }
    }

    private static string Resolve(string? dataDirectoryOverride)
    {
        if (!string.IsNullOrWhiteSpace(dataDirectoryOverride))
        {
            return Path.GetFullPath(dataDirectoryOverride.Trim().Trim('"'));
        }

        if (IsPortable)
        {
            return Path.Combine(ExecutableDirectory, "data");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = AppContext.BaseDirectory;
        }

        return Path.Combine(appData, AppFolderName);
    }

    private static void EnsureInitialized()
    {
        if (_initialized && _dataDirectory != null) return;
        Initialize();
    }
}
