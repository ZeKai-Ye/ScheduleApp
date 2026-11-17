using Microsoft.Win32;
using Schedule.Core.Localization;
using Schedule.Core.Logging;

namespace Schedule.App.Services;

/// <summary>开机启动的落地方式。</summary>
public enum AutostartMode
{
    /// <summary>未启用。</summary>
    Disabled = 0,
    /// <summary>注册表 HKCU\\...\\Run（首选）。</summary>
    Registry = 1,
    /// <summary>启动文件夹里的快捷方式（注册表被拦时的回退）。</summary>
    StartupFolder = 2,
}

/// <summary>
/// 开机自启：优先写注册表 Run 键，被安全软件 / 策略拦住时自动回退为
/// 「启动文件夹里的快捷方式」，并把真实落地方式回报给界面与接口。
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScheduleApp";

    /// <summary>拼出开机启动要执行的命令行。</summary>
    public static string BuildCommand(string? arguments)
    {
        var exe = ShortcutService.ExecutablePath();
        var command = "\"" + exe + "\"";

        if (!string.IsNullOrWhiteSpace(arguments)) command += " " + arguments!.Trim();
        return command;
    }

    /// <summary>探测当前的实际启用方式与命令行。</summary>
    public static (AutostartMode Mode, string Command) Detect()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return (AutostartMode.Registry, value);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取开机启动注册表失败：" + ex.Message);
        }

        try
        {
            var link = ShortcutService.PathFor("startup");
            if (File.Exists(link)) return (AutostartMode.StartupFolder, link);
        }
        catch (Exception)
        {
            // 忽略
        }

        return (AutostartMode.Disabled, string.Empty);
    }

    public static bool IsEnabled() => Detect().Mode != AutostartMode.Disabled;

    public static string Describe(AutostartMode mode) => mode switch
    {
        AutostartMode.Registry => Loc.T("注册表 Run 键", "Registry Run key"),
        AutostartMode.StartupFolder => Loc.T("启动文件夹快捷方式", "Startup folder shortcut"),
        _ => Loc.T("未启用", "Disabled"),
    };

    /// <summary>启用 / 停用开机启动。</summary>
    /// <param name="enabled">是否启用。</param>
    /// <param name="arguments">附加命令行参数，例如 --minimized。</param>
    /// <param name="mode">实际落地的位置。</param>
    /// <param name="error">失败原因或回退说明。</param>
    public static bool Apply(bool enabled, string? arguments, out string? mode, out string? error)
    {
        mode = null;
        error = null;

        try
        {
            if (!enabled)
            {
                var removedRegistry = RemoveRegistry(out var removeError);
                ShortcutService.TryDelete("startup", null, out _, out _);
                mode = "Disabled";

                if (!removedRegistry) error = removeError;
                return removedRegistry;
            }

            var command = BuildCommand(arguments);

            if (TryWriteRegistry(command, out var registryError))
            {
                // 注册表生效后，清掉可能存在的旧回退快捷方式，避免重复启动
                ShortcutService.TryDelete("startup", null, out _, out _);
                mode = "Registry";
                return true;
            }

            if (ShortcutService.TryCreate("startup", arguments, null, out _, out var linkError))
            {
                mode = "StartupFolder";
                error = Loc.F("注册表写入失败（{0}），已改用启动文件夹快捷方式",
                    "Registry write failed ({0}); switched to a Startup folder shortcut", registryError);
                AppLog.Warn("注册表写入失败（" + registryError + "），已改用启动文件夹快捷方式");
                return true;
            }

            error = Loc.F("注册表写入失败：{0}；启动文件夹创建失败：{1}",
                "Registry write failed: {0}; startup folder shortcut failed: {1}", registryError, linkError);
            AppLog.Error("注册表写入失败：" + registryError + "；启动文件夹创建失败：" + linkError);
            return false;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            AppLog.Error("设置开机启动失败", ex);
            return false;
        }
    }

    /// <summary>兼容旧调用。</summary>
    public static bool SetEnabled(bool enabled) => Apply(enabled, null, out _, out _);

    private static bool TryWriteRegistry(string command, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (key == null)
            {
                error = Loc.T("无法打开注册表 Run 键", "Cannot open the registry Run key");
                return false;
            }

            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static bool RemoveRegistry(out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            error = Loc.F("清理注册表失败：{0}", "Failed to clean up the registry: {0}", ex.Message);
            return false;
        }
    }
}
