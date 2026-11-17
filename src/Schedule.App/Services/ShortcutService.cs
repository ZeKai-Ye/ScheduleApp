using System.Runtime.InteropServices;
using Schedule.Core.Localization;
using Schedule.Core.Logging;

namespace Schedule.App.Services;

/// <summary>
/// 桌面 / 开始菜单 / 启动文件夹快捷方式（.lnk）。
/// 通过 COM 的 WScript.Shell 创建，不需要任何第三方库；
/// 拿不到 COM 或没有写权限时返回失败原因，由调用方原样展示。
/// </summary>
public static class ShortcutService
{
    public const string ShortcutFileName = "日程助手.lnk";
    public const string Description = "日程助手 ScheduleApp";

    public static string DesktopDirectory
        => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string StartMenuDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "日程助手");

    public static string StartupDirectory
        => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    /// <summary>快捷方式目标名称（desktop / startmenu / startup）。</summary>
    public static bool TryResolveDirectory(string? target, out string directory)
    {
        directory = (target ?? "desktop").Trim().ToLowerInvariant() switch
        {
            "desktop" or "桌面" => DesktopDirectory,
            "startmenu" or "start-menu" or "menu" or "开始菜单" => StartMenuDirectory,
            "startup" or "startupfolder" or "启动" => StartupDirectory,
            _ => string.Empty,
        };

        return directory.Length > 0;
    }

    public static string PathFor(string target, string? directoryOverride = null)
    {
        var directory = !string.IsNullOrWhiteSpace(directoryOverride)
            ? directoryOverride!
            : TryResolveDirectory(target, out var resolved) ? resolved : DesktopDirectory;

        return Path.Combine(directory, ShortcutFileName);
    }

    public static bool Exists(string target, string? directoryOverride = null)
    {
        try
        {
            return File.Exists(PathFor(target, directoryOverride));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>程序自身的可执行文件路径（用于快捷方式目标）。</summary>
    public static string ExecutablePath()
    {
        var processPath = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(processPath)
            && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        // dotnet run / 单文件宿主之外的情况：回退到程序集所在目录的 exe
        var candidate = Path.Combine(AppContext.BaseDirectory, "ScheduleApp.exe");
        return File.Exists(candidate) ? candidate : (processPath ?? candidate);
    }

    public static bool TryCreate(
        string target,
        string? arguments,
        string? directoryOverride,
        out string? path,
        out string? error)
    {
        path = PathFor(target, directoryOverride);
        error = null;

        try
        {
            var exe = ExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                error = Loc.F("找不到程序自身路径，无法创建快捷方式：{0}", "Cannot find the app's own path, so the shortcut cannot be created: {0}", exe);
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                error = Loc.T("系统中没有 WScript.Shell（无法生成 .lnk 快捷方式）",
                    "WScript.Shell is not available on this system (cannot create .lnk shortcuts)");
                return false;
            }

            object? shellObject = null;
            object? linkObject = null;

            try
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                shellObject = (object)shell;

                dynamic link = shell.CreateShortcut(path);
                linkObject = (object)link;

                link.TargetPath = exe;
                link.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
                link.Description = Description;
                link.IconLocation = exe + ",0";

                if (!string.IsNullOrWhiteSpace(arguments)) link.Arguments = arguments!.Trim();

                link.Save();
            }
            finally
            {
                Release(linkObject);
                Release(shellObject);
            }

            AppLog.Info("已创建快捷方式：" + path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            AppLog.Warn("创建快捷方式失败（" + path + "）：" + error);
            return false;
        }
    }

    public static bool TryDelete(string target, string? directoryOverride, out string? path, out string? error)
    {
        path = PathFor(target, directoryOverride);
        error = null;

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                AppLog.Info("已删除快捷方式：" + path);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject == null) return;

        try
        {
            if (Marshal.IsComObject(comObject)) Marshal.FinalReleaseComObject(comObject);
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
