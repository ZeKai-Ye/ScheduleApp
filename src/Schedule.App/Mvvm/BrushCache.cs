using System.Windows.Media;
using Schedule.Core.Services;

namespace Schedule.App.Mvvm;

/// <summary>
/// 十六进制颜色 → 冻结画刷的缓存。
/// 视图模型直接产出 Brush，避免在 XAML 里到处写转换器。
/// </summary>
public static class BrushCache
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();
    private static readonly object Gate = new();

    public static SolidColorBrush Get(string? hex, byte alpha = 255)
    {
        var normalized = EventColors.Normalize(hex);
        if (normalized.Length != 7) normalized = EventColors.Palette[0];

        var key = alpha.ToString("X2") + normalized;

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var color = (Color)ColorConverter.ConvertFromString(normalized)!;
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            Cache[key] = brush;
            return brush;
        }
    }

    /// <summary>带透明度的柔和底色，用于日历色块背景。</summary>
    public static SolidColorBrush Soft(string? hex) => Get(hex, 0x2E);

    /// <summary>更浅的底色（月视图小色条）。</summary>
    public static SolidColorBrush Softer(string? hex) => Get(hex, 0x1F);
}
