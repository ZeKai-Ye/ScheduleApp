using Schedule.Core.Models;
using Schedule.Core.Services;

namespace Schedule.Core.Tests.Cases;

/// <summary>事件配色。</summary>
public static class ColorTests
{
    public static void Run()
    {
        TestRunner.Section("事件配色");

        // 显式颜色优先
        var explicitColor = new ScheduleItem { Title = "显式", Category = "工作", Color = "#123456" };
        TestRunner.Equal("#123456", EventColors.Resolve(explicitColor), "显式颜色优先");

        // 分类映射次之
        var mapped = new ScheduleItem { Title = "映射", Category = "工作" };
        var map = new Dictionary<string, string> { ["工作"] = "#abcdef" };
        TestRunner.Equal("#ABCDEF", EventColors.Resolve(mapped, map), "分类映射生效");

        // 无映射时按分类稳定散列
        var auto = new ScheduleItem { Title = "自动", Category = "工作" };
        var first = EventColors.Resolve(auto);
        var second = EventColors.Resolve(auto);
        TestRunner.Equal(first, second, "同一分类颜色稳定");
        TestRunner.Check(EventColors.Palette.Contains(first), "颜色来自调色板");
        TestRunner.Equal(EventColors.Palette[EventColors.StableIndex("工作", EventColors.Palette.Length)], first, "散列下标一致");

        var other = new ScheduleItem { Title = "另一个", Category = "生活" };
        TestRunner.Check(EventColors.Resolve(other) != first || EventColors.Palette.Length == 1, "不同分类通常不同色");

        // 稳定散列必须跨进程一致（不能依赖 string.GetHashCode）
        TestRunner.Equal(EventColors.StableIndex("工作", 12), EventColors.StableIndex("工作", 12), "散列可重复");
        TestRunner.Check(EventColors.StableIndex("工作", 12) >= 0 && EventColors.StableIndex("工作", 12) < 12, "散列在范围内");

        // 无分类时按优先级取色
        var highPriority = new ScheduleItem { Title = "高", Priority = Priority.High };
        TestRunner.Equal("#DC2626", EventColors.Resolve(highPriority), "高优先级红色");
        var lowPriority = new ScheduleItem { Title = "低", Priority = Priority.Low };
        TestRunner.Equal("#94A3B8", EventColors.Resolve(lowPriority), "低优先级灰色");

        // 归一化
        TestRunner.Equal("#AABBCC", EventColors.Normalize("#abc"), "三位缩写");
        TestRunner.Equal("#2563EB", EventColors.Normalize("2563eb"), "缺少井号");
        TestRunner.Equal("#2563EB", EventColors.Normalize("  0x2563EB "), "0x 前缀");
        TestRunner.Equal(string.Empty, EventColors.Normalize("xyz"), "非法值");
        TestRunner.Equal(string.Empty, EventColors.Normalize(""), "空值");
        TestRunner.Check(EventColors.IsValid("#00ff00"), "合法值判定");
        TestRunner.Check(!EventColors.IsValid("red"), "色名不是合法十六进制");

        // 半透明色（日历色块背景）
        TestRunner.Equal("#332563EB", EventColors.WithAlpha("#2563EB"), "透明度拼接");

        // CSV 里的色名
        TestRunner.Check(FieldParsersColor("红", out var red) && red == "#DC2626", "中文色名");
        TestRunner.Check(FieldParsersColor("蓝色", out var blue) && blue == "#2563EB", "中文色名（含「色」）");
        TestRunner.Check(FieldParsersColor("green", out var green) && green == "#16A34A", "英文色名");
        TestRunner.Check(FieldParsersColor("#0a0b0c", out var hex) && hex == "#0A0B0C", "十六进制色值");
        TestRunner.Check(!FieldParsersColor("不是颜色", out _), "无法识别的颜色");
    }

    private static bool FieldParsersColor(string text, out string color)
        => Schedule.Core.Csv.FieldParsers.TryParseColor(text, out color);
}
