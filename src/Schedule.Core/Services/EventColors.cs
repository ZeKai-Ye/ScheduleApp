using Schedule.Core.Models;

namespace Schedule.Core.Services;

/// <summary>
/// 事件配色：优先使用日程自带颜色，其次按分类映射，最后按分类名稳定散列到调色板。
/// 注意使用 FNV-1a 而非 string.GetHashCode()——后者的种子每个进程都不同，
/// 会导致同一条日程每次启动颜色都在变。
/// </summary>
public static class EventColors
{
    /// <summary>默认调色板（对浅色背景友好，且在色盲视角下区分度尚可）。</summary>
    public static readonly string[] Palette =
    {
        "#2563EB", // 蓝
        "#7C3AED", // 紫
        "#DB2777", // 玫红
        "#DC2626", // 红
        "#EA580C", // 橙
        "#CA8A04", // 琥珀
        "#16A34A", // 绿
        "#0891B2", // 青
        "#0D9488", // 蓝绿
        "#64748B", // 石板灰
        "#9333EA", // 深紫
        "#B45309", // 棕
    };

    /// <summary>供颜色选择器使用的预设（名称 + 色值）。</summary>
    public static readonly (string Name, string Hex)[] Presets =
    {
        ("蓝", "#2563EB"),
        ("紫", "#7C3AED"),
        ("玫红", "#DB2777"),
        ("红", "#DC2626"),
        ("橙", "#EA580C"),
        ("琥珀", "#CA8A04"),
        ("绿", "#16A34A"),
        ("青", "#0891B2"),
        ("蓝绿", "#0D9488"),
        ("石板灰", "#64748B"),
        ("深紫", "#9333EA"),
        ("棕", "#B45309"),
    };

    public const string AutoColor = "";
    public const string AutoColorLabel = "跟随分类";

    /// <summary>解析事件最终颜色（十六进制 #RRGGBB）。</summary>
    public static string Resolve(ScheduleItem item, IReadOnlyDictionary<string, string>? categoryColors = null)
    {
        if (!string.IsNullOrWhiteSpace(item.Color)) return Normalize(item.Color);

        var category = item.Category?.Trim();
        if (!string.IsNullOrEmpty(category))
        {
            if (categoryColors != null && categoryColors.TryGetValue(category, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                return Normalize(mapped);
            }

            return Palette[StableIndex(category, Palette.Length)];
        }

        // 无分类：优先级高的用暖色，便于一眼分辨
        return item.Priority switch
        {
            Priority.High => "#DC2626",
            Priority.Low => "#94A3B8",
            _ => Palette[0],
        };
    }

    /// <summary>按分类名取稳定的调色板下标。</summary>
    public static int StableIndex(string key, int modulo)
    {
        if (modulo <= 1) return 0;

        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            var hash = offset;
            foreach (var c in key)
            {
                hash ^= c;
                hash *= prime;
            }

            return (int)(hash % (uint)modulo);
        }
    }

    /// <summary>把任意写法归一化为 #RRGGBB；无法识别时返回空字符串。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Trim();

        if (text.StartsWith('#')) text = text[1..];
        else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];

        // #RGB / #RRGGBB
        if (text.Length == 3)
        {
            text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
        }

        if (text.Length != 6) return string.Empty;

        foreach (var c in text)
        {
            if (!Uri.IsHexDigit(c)) return string.Empty;
        }

        return "#" + text.ToUpperInvariant();
    }

    public static bool IsValid(string? value) => Normalize(value).Length == 7;

    /// <summary>中文 / 英文色名表（与十六进制写法共用一套解析入口）。</summary>
    private static readonly Dictionary<string, string> ColorNames = new(StringComparer.CurrentCultureIgnoreCase)
    {
        ["红"] = "#DC2626", ["红色"] = "#DC2626", ["red"] = "#DC2626",
        ["橙"] = "#EA580C", ["橙色"] = "#EA580C", ["orange"] = "#EA580C",
        ["黄"] = "#CA8A04", ["黄色"] = "#CA8A04", ["yellow"] = "#CA8A04", ["琥珀"] = "#CA8A04",
        ["绿"] = "#16A34A", ["绿色"] = "#16A34A", ["green"] = "#16A34A",
        ["青"] = "#0891B2", ["青色"] = "#0891B2", ["cyan"] = "#0891B2",
        ["蓝"] = "#2563EB", ["蓝色"] = "#2563EB", ["blue"] = "#2563EB",
        ["紫"] = "#7C3AED", ["紫色"] = "#7C3AED", ["purple"] = "#7C3AED",
        ["粉"] = "#DB2777", ["粉色"] = "#DB2777", ["玫红"] = "#DB2777", ["pink"] = "#DB2777",
        ["灰"] = "#64748B", ["灰色"] = "#64748B", ["gray"] = "#64748B", ["grey"] = "#64748B",
        ["棕"] = "#B45309", ["棕色"] = "#B45309", ["brown"] = "#B45309",
    };

    /// <summary>解析颜色：先按十六进制，再按中英文色名。无法识别返回 false。</summary>
    public static bool TryParse(string? value, out string color)
    {
        color = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();

        var hex = Normalize(text);
        if (hex.Length == 7)
        {
            color = hex;
            return true;
        }

        if (ColorNames.TryGetValue(text, out var mapped))
        {
            color = mapped;
            return true;
        }

        return false;
    }

    /// <summary>生成带透明度的画刷颜色字符串（用于日历色块背景）。</summary>
    public static string WithAlpha(string hex, byte alpha = 0x33)
    {
        var normalized = Normalize(hex);
        if (normalized.Length != 7) normalized = Palette[0];
        return "#" + alpha.ToString("X2") + normalized[1..];
    }
}
