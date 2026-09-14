using System.Windows;
using Microsoft.Win32;
using Schedule.Core.Logging;
using Schedule.Core.Models;

namespace Schedule.App.Services;

/// <summary>
/// 主题切换：调色板放在独立的 Palette.*.xaml 里，切换时整块替换那个合并字典，
/// 界面上的画刷全部用 DynamicResource 引用，因此无需重建窗口即可即时生效。
/// </summary>
public static class ThemeService
{
    private const string LightSource = "Themes/Palette.Light.xaml";
    private const string DarkSource = "Themes/Palette.Dark.xaml";

    private static bool? _appliedDark;

    /// <summary>当前是否夜间主题。</summary>
    public static bool IsDark => _appliedDark ?? false;

    /// <summary>
    /// 应用主题。
    /// </summary>
    /// <param name="mode">设置里的主题模式。</param>
    /// <param name="overrideTheme">启动参数临时指定时用（不写回设置）。</param>
    /// <returns>是否真的切换了调色板。</returns>
    public static bool Apply(AppTheme mode, AppTheme? overrideTheme = null)
    {
        var dark = Resolve(overrideTheme ?? mode);
        if (_appliedDark == dark) return false;

        var app = Application.Current;
        if (app == null) return false;

        try
        {
            var palette = new ResourceDictionary
            {
                Source = new Uri(dark ? DarkSource : LightSource, UriKind.Relative),
            };

            // Theme.xaml 的第一个合并字典就是调色板
            foreach (var merged in app.Resources.MergedDictionaries)
            {
                if (merged.MergedDictionaries.Count == 0) continue;

                merged.MergedDictionaries[0] = palette;
                _appliedDark = dark;
                AppLog.Info("主题已切换为" + (dark ? "夜间" : "浅色"));
                return true;
            }

            AppLog.Warn("未找到调色板字典，主题未切换");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error("切换主题失败", ex);
            return false;
        }
    }

    public static bool Resolve(AppTheme mode) => mode switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => IsSystemDark(),
    };

    /// <summary>读 Windows 的「应用模式」是否深色。</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");

            return value is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string Describe(AppTheme mode) => mode switch
    {
        AppTheme.Dark => "夜间主题",
        AppTheme.Light => "浅色主题",
        _ => "跟随系统（当前" + (IsSystemDark() ? "夜间" : "浅色") + "）",
    };

    /// <summary>把主题名解析成模式（命令行用）。</summary>
    public static AppTheme? Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "dark" or "night" or "夜间" or "深色" => AppTheme.Dark,
        "light" or "浅色" or "日间" => AppTheme.Light,
        "system" or "auto" or "系统" => AppTheme.System,
        _ => null,
    };
}
