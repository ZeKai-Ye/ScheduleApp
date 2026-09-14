using System.Windows;
using System.Windows.Media;

namespace Schedule.App.Mvvm;

/// <summary>
/// 供代码侧（视图模型）使用的主题画刷。
/// XAML 用 DynamicResource 就能跟着主题走，而代码里算出来的画刷必须显式读当前调色板，
/// 因此这里统一从应用资源取，取不到再退回硬编码的浅色值。
/// </summary>
public static class ThemeBrushes
{
    public static Brush Get(string key, string fallbackHex)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        }
        catch (Exception)
        {
            // 设计期 / 无应用上下文时退回默认色
        }

        return BrushCache.Get(fallbackHex);
    }

    public static Brush Accent => Get("Brush.Accent", "#2563EB");

    public static Brush AccentSoft => Get("Brush.AccentSoft", "#DBEAFE");

    public static Brush Card => Get("Brush.Card", "#FFFFFF");

    public static Brush CellMuted => Get("Brush.CellMuted", "#FAFAFB");

    public static Brush WeekendColumn => Get("Brush.WeekendColumn", "#FCFCFD");

    public static Brush Border => Get("Brush.Border", "#E5E7EB");

    public static Brush Text => Get("Brush.Text", "#111827");

    public static Brush Subtle => Get("Brush.Subtle", "#6B7280");

    public static Brush SubtleSoft => Get("Brush.SubtleSoft", "#9CA3AF");

    public static Brush OnAccent => Get("Brush.OnAccent", "#FFFFFF");

    public static Brush Danger => Get("Brush.Danger", "#DC2626");

    public static Brush DangerSoft => Get("Brush.DangerSoft", "#FEF2F2");

    public static Brush Hint => Get("Brush.Hint", "#F9FAFB");

    public static Brush HintBorder => Get("Brush.HintBorder", "#E5E7EB");
}
