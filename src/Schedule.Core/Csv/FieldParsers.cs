using System.Globalization;
using System.Text.RegularExpressions;
using Schedule.Core.Models;

namespace Schedule.Core.Csv;

/// <summary>提醒解析结果。</summary>
public readonly struct RemindSpec
{
    public bool Enabled { get; init; }

    /// <summary>提前分钟数。</summary>
    public int BeforeMinutes { get; init; }

    /// <summary>绝对提醒时间（若 CSV 中直接给了具体时刻）。</summary>
    public DateTime? Absolute { get; init; }
}

/// <summary>把 CSV 单元格文本解析成强类型枚举 / 数值。</summary>
public static class FieldParsers
{
    private static readonly Regex DurationRegex = new(
        @"^(?:提前|提早|事先)?\s*(\d+(?:\.\d+)?)\s*(分钟|分|min|mins|m|小时|时|hr|hrs|h|天|日|d|周|星期|w|个月|月|mon)?\s*(?:前|之前)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimeOfDayRegex = new(@"^\d{1,2}:\d{2}(:\d{2})?$", RegexOptions.Compiled);

    private static readonly string[] DisableWords = { "不提醒", "无", "否", "关闭", "不", "none", "off", "no", "false", "disabled", "never", "-" };

    /// <summary>解析提醒设置。返回 false 表示该单元格为空 / 无法识别（调用方应使用默认值）。</summary>
    public static bool TryParseRemind(string? text, DateTime itemStart, out RemindSpec spec)
    {
        spec = new RemindSpec { Enabled = false, BeforeMinutes = 0, Absolute = null };
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Clean(text);
        if (raw.Length == 0) return false;

        foreach (var word in DisableWords)
        {
            if (string.Equals(raw, word, StringComparison.OrdinalIgnoreCase))
            {
                spec = new RemindSpec { Enabled = false, BeforeMinutes = 0 };
                return true;
            }
        }

        // 具体时刻（当天或带日期）：提醒时间列写法
        if (TimeOfDayRegex.IsMatch(raw))
        {
            if (FlexibleDateTime.TryParseTimeOfDay(raw, out var tod))
            {
                var absoluteTime = itemStart.Date.Add(tod);
                var offsetMinutes = (int)Math.Round((itemStart - absoluteTime).TotalMinutes);
                spec = new RemindSpec
                {
                    Enabled = true,
                    BeforeMinutes = offsetMinutes > 0 ? offsetMinutes : 0,
                    Absolute = absoluteTime,
                };
                return true;
            }
        }

        var hasDateSeparator = raw.Contains('-') || raw.Contains('/') || raw.Contains("年") || raw.Contains("月");
        if (hasDateSeparator && FlexibleDateTime.TryParse(raw, out var absolute, itemStart.Date))
        {
            var offsetMinutes = (int)Math.Round((itemStart - absolute).TotalMinutes);
            spec = new RemindSpec
            {
                Enabled = true,
                BeforeMinutes = offsetMinutes > 0 ? offsetMinutes : 0,
                Absolute = absolute,
            };
            return true;
        }

        var match = DurationRegex.Match(raw);
        if (match.Success)
        {
            var amount = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = match.Groups[2].Value.ToLowerInvariant();
            var minutes = unit switch
            {
                "小时" or "时" or "hr" or "hrs" or "h" => amount * 60,
                "天" or "日" or "d" => amount * 60 * 24,
                "周" or "星期" or "w" => amount * 60 * 24 * 7,
                "个月" or "月" or "mon" => amount * 60 * 24 * 30,
                _ => amount,
            };

            spec = new RemindSpec { Enabled = true, BeforeMinutes = (int)Math.Round(minutes) };
            return true;
        }

        return false;
    }

    public static bool TryParsePriority(string? text, out Priority priority)
    {
        priority = Priority.Normal;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Key(text);
        switch (raw)
        {
            case "高" or "高优先" or "重要" or "紧急" or "最高" or "high" or "h" or "3" or "1" or "p1":
                priority = Priority.High;
                return true;
            case "中" or "普通" or "一般" or "normal" or "medium" or "m" or "2" or "p2":
                priority = Priority.Normal;
                return true;
            case "低" or "较低" or "次要" or "low" or "l" or "0" or "p3":
                priority = Priority.Low;
                return true;
            default:
                if (raw.Contains("高", StringComparison.Ordinal)) { priority = Priority.High; return true; }
                if (raw.Contains("低", StringComparison.Ordinal)) { priority = Priority.Low; return true; }
                return false;
        }
    }

    public static bool TryParseRepeat(string? text, out RepeatKind repeat)
    {
        repeat = RepeatKind.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Key(text);
        if (raw.Length == 0) return false;

        if (raw is "无" or "不重复" or "一次性" or "单次" or "none" or "no" or "once" or "0")
        {
            repeat = RepeatKind.None;
            return true;
        }

        if (raw.Contains("工作日", StringComparison.Ordinal) || raw.Contains("weekday", StringComparison.Ordinal))
        {
            repeat = RepeatKind.Weekdays;
            return true;
        }

        if (raw.Contains("每天", StringComparison.Ordinal) || raw.Contains("每日", StringComparison.Ordinal)
            || raw.Contains("daily", StringComparison.Ordinal) || raw == "天" || raw == "日")
        {
            repeat = RepeatKind.Daily;
            return true;
        }

        if (raw.Contains("每周", StringComparison.Ordinal) || raw.Contains("每星期", StringComparison.Ordinal)
            || raw.Contains("weekly", StringComparison.Ordinal) || raw.Contains("每周一", StringComparison.Ordinal))
        {
            repeat = RepeatKind.Weekly;
            return true;
        }

        if (raw.Contains("每月", StringComparison.Ordinal) || raw.Contains("monthly", StringComparison.Ordinal))
        {
            repeat = RepeatKind.Monthly;
            return true;
        }

        if (raw.Contains("每年", StringComparison.Ordinal) || raw.Contains("yearly", StringComparison.Ordinal) || raw.Contains("annually", StringComparison.Ordinal))
        {
            repeat = RepeatKind.Yearly;
            return true;
        }

        if (raw.Contains("重复", StringComparison.Ordinal)) return false;
        return false;
    }

    public static bool TryParseAllDay(string? text, out bool allDay)
    {
        allDay = false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Key(text);
        switch (raw)
        {
            case "是" or "y" or "yes" or "true" or "1" or "全天" or "整天" or "t":
                allDay = true;
                return true;
            case "否" or "n" or "no" or "false" or "0" or "不是" or "f":
                allDay = false;
                return true;
            default:
                return false;
        }
    }

    public static bool TryParseStatus(string? text, out ItemStatus status)
    {
        status = ItemStatus.Pending;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Key(text);
        if (raw.Length == 0) return false;

        if (raw.Contains("完成", StringComparison.Ordinal) || raw.Contains("已办", StringComparison.Ordinal)
            || raw.Contains("done", StringComparison.Ordinal) || raw.Contains("complete", StringComparison.Ordinal)
            || raw is "是" or "1" or "true" or "y" or "yes")
        {
            status = ItemStatus.Done;
            return true;
        }

        if (raw.Contains("取消", StringComparison.Ordinal) || raw.Contains("作废", StringComparison.Ordinal)
            || raw.Contains("cancel", StringComparison.Ordinal))
        {
            status = ItemStatus.Canceled;
            return true;
        }

        if (raw.Contains("待办", StringComparison.Ordinal) || raw.Contains("未完成", StringComparison.Ordinal)
            || raw.Contains("pending", StringComparison.Ordinal) || raw.Contains("todo", StringComparison.Ordinal)
            || raw is "否" or "0" or "false" or "n" or "no")
        {
            status = ItemStatus.Pending;
            return true;
        }

        return false;
    }
}
