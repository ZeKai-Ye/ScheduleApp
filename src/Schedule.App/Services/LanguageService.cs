using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Schedule.Core.Localization;
using Schedule.Core.Logging;
using Schedule.Core.Models;

namespace Schedule.App.Services;

/// <summary>
/// 界面语言：把设置里的语言模式落到 <see cref="Loc"/> 与 WPF 的区域设置上。
///
/// XAML 里的文案在窗口加载时求值，因此**切换语言需要重启界面**才能全部生效，
/// 与主题切换的处理方式一致（设置页会提示并可直接重启）。
/// </summary>
public static class LanguageService
{
    private static bool _wpfLanguageApplied;

    /// <summary>当前生效的语言。</summary>
    public static AppLanguage Current => Loc.Current;

    /// <summary>当前是否英文界面。</summary>
    public static bool IsEnglish => Loc.IsEnglish;

    /// <summary>
    /// 应用语言设置。
    /// </summary>
    /// <param name="mode">设置里的语言模式。</param>
    /// <param name="overrideLanguage">启动参数临时指定时用（不写回设置）。</param>
    /// <returns>语言是否与之前不同。</returns>
    public static bool Apply(AppLanguage mode, AppLanguage? overrideLanguage = null)
    {
        var resolved = Loc.Resolve(overrideLanguage ?? mode);
        var changed = Loc.Current != resolved;

        Loc.Use(resolved);
        ApplyCultures();

        AppLog.Info("界面语言：" + Loc.Describe(resolved) + (changed ? "（已切换）" : string.Empty));
        return changed;
    }

    /// <summary>
    /// 让 WPF 的日期 / 数字格式化跟随界面语言。
    /// 只改 UI 文化（CurrentUICulture），不动 CurrentCulture——
    /// CSV 的日期解析依赖系统区域，改错了会把「2026年9月14日」这类写法解析坏。
    /// </summary>
    private static void ApplyCultures()
    {
        var culture = Loc.Culture;

        try
        {
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            AppLog.Warn("设置界面文化失败：" + ex.Message);
        }

        if (_wpfLanguageApplied) return;

        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            _wpfLanguageApplied = true;
        }
        catch (Exception ex)
        {
            // 已经有窗口实例时 WPF 会拒绝覆盖元数据，此时保持默认（en-US）即可
            AppLog.Warn("设置 WPF 语言失败：" + ex.Message);
        }
    }

    /// <summary>把语言模式转成说明（设置页用）。</summary>
    public static string Describe(AppLanguage mode) => Loc.Describe(mode);

    /// <summary>解析命令行里的语言写法（zh / en / system）。</summary>
    public static AppLanguage? Parse(string? text) => Loc.Parse(text);
}
