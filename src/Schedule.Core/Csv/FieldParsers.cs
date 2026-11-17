using System.Globalization;
using System.Text.RegularExpressions;
using Schedule.Core.Models;
using Schedule.Core.Services;

namespace Schedule.Core.Csv;

/// <summary>单档提醒解析结果。</summary>
public readonly struct RemindSpec
{
    public bool Enabled { get; init; }

    /// <summary>提前分钟数。</summary>
    public int BeforeMinutes { get; init; }

    /// <summary>绝对提醒时间（若 CSV 中直接给了具体时刻）。</summary>
    public DateTime? Absolute { get; init; }
}

/// <summary>多档提醒解析结果。</summary>
public sealed class RemindOffsetsSpec
{
    public bool Enabled { get; init; }

    public List<int> Offsets { get; init; } = new();

    public DateTime? Absolute { get; init; }
}

/// <summary>重复规则解析结果（含间隔、多星期、次数、截止）。</summary>
public sealed class RepeatSpec
{
    public RepeatKind Kind { get; set; } = RepeatKind.None;

    public int Interval { get; set; } = 1;

    public List<DayOfWeek> Weekdays { get; set; } = new();

    public int? Count { get; set; }

    public DateTime? Until { get; set; }
}

/// <summary>把 CSV 单元格文本解析成强类型枚举 / 数值。</summary>
public static class FieldParsers
{
    private static readonly Regex DurationRegex = new(
        @"^(?:提前|提早|事先)?\s*(\d+(?:\.\d+)?)\s*(分钟|分|min|mins|m|小时|时|hr|hrs|h|天|日|d|周|星期|w|个月|月|mon)?\s*(?:前|之前)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimeOfDayRegex = new(@"^\d{1,2}:\d{2}(:\d{2})?$", RegexOptions.Compiled);

    private static readonly Regex IntervalRegex = new(
        @"每\s*(?:隔\s*)?(\d+|[两双])\s*(天|日|周|星期|个?月|年)",
        RegexOptions.Compiled);

    private static readonly Regex CountRegex = new(
        @"(?:共|总共|重复)?\s*(\d+)\s*次",
        RegexOptions.Compiled);

    private static readonly Regex UntilRegex = new(
        @"(?:到|至|截止|until)\s*(\d{4}[-/.]\d{1,2}[-/.]\d{1,2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OffsetTextPartRegex = new(
        @"^(?<key>\d+|\*|默认|通用)\s*[:：]\s*(?<value>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex WeekdayCharsRegex = new(
        @"(?:周|週|星期|礼拜)\s*([一二三四五六日天、,，和及\s]+)",
        RegexOptions.Compiled);

    private static readonly string[] DisableWords =
    {
        "不提醒", "无", "否", "关闭", "不", "none", "off", "no", "false", "disabled", "never", "-",
    };

    // ============ 提醒 ============

    /// <summary>解析单档提醒设置。返回 false 表示该单元格为空 / 无法识别。</summary>
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

        if (TimeOfDayRegex.IsMatch(raw) && FlexibleDateTime.TryParseTimeOfDay(raw, out var tod))
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
            spec = new RemindSpec
            {
                Enabled = true,
                BeforeMinutes = (int)Math.Round(ToMinutes(amount, match.Groups[2].Value)),
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// 解析多档提醒，支持逗号 / 顿号 / 分号 / 竖线 / 加号分隔，例如
    /// 「30,15,5」「提前30分钟、提前5分钟」「1小时;10」「不提醒」。
    /// </summary>
    public static bool TryParseRemindOffsets(string? text, DateTime itemStart, out RemindOffsetsSpec spec)
    {
        spec = new RemindOffsetsSpec { Enabled = false };
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Clean(text);
        if (raw.Length == 0) return false;

        var parts = raw.Split(new[] { ',', '，', '、', ';', '；', '|', '+', '＋' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0) return false;

        var offsets = new List<int>();
        DateTime? absolute = null;
        var recognized = false;
        var enabled = true;

        foreach (var part in parts)
        {
            if (!TryParseRemind(part, itemStart, out var single)) continue;

            recognized = true;
            if (!single.Enabled)
            {
                enabled = false;
                continue;
            }

            if (single.Absolute.HasValue) absolute = single.Absolute;
            offsets.Add(single.BeforeMinutes);
        }

        if (!recognized) return false;

        spec = new RemindOffsetsSpec
        {
            Enabled = enabled && (offsets.Count > 0 || absolute.HasValue),
            Offsets = offsets.Distinct().OrderByDescending(o => o).ToList(),
            Absolute = absolute,
        };

        return true;
    }

    private static double ToMinutes(double amount, string unit) => unit.ToLowerInvariant() switch
    {
        "小时" or "时" or "hr" or "hrs" or "h" => amount * 60,
        "天" or "日" or "d" => amount * 60 * 24,
        "周" or "星期" or "w" => amount * 60 * 24 * 7,
        "个月" or "月" or "mon" => amount * 60 * 24 * 30,
        _ => amount,
    };

    // ============ 重复 ============

    /// <summary>兼容旧接口：只取重复类型。</summary>
    public static bool TryParseRepeat(string? text, out RepeatKind repeat)
    {
        var ok = TryParseRepeatDetail(text, out var spec);
        repeat = ok ? spec.Kind : RepeatKind.None;
        return ok;
    }

    /// <summary>解析重复规则详情：类型 + 间隔 + 星期集合 + 次数 + 截止。</summary>
    public static bool TryParseRepeatDetail(string? text, out RepeatSpec spec)
    {
        spec = new RepeatSpec();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Clean(text);
        var key = TextNormalizer.Key(text);
        if (key.Length == 0) return false;

        if (key is "无" or "不重复" or "一次性" or "单次" or "none" or "no" or "once" or "0")
        {
            return true;
        }

        var interval = 1;
        RepeatKind? kindFromUnit = null;

        var intervalMatch = IntervalRegex.Match(raw);
        if (intervalMatch.Success)
        {
            var amount = intervalMatch.Groups[1].Value;
            interval = amount switch
            {
                "两" or "双" => 2,
                _ => int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? Math.Max(1, parsed)
                    : 1,
            };

            kindFromUnit = intervalMatch.Groups[2].Value switch
            {
                "天" or "日" => RepeatKind.Daily,
                "周" or "星期" => RepeatKind.Weekly,
                "月" or "个月" => RepeatKind.Monthly,
                "年" => RepeatKind.Yearly,
                _ => null,
            };
        }

        var weekdays = ParseWeekdayChars(raw);
        int? count = null;
        var countMatch = CountRegex.Match(raw);
        if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var parsedCount) && parsedCount > 0)
        {
            count = parsedCount;
        }

        DateTime? until = null;
        var untilMatch = UntilRegex.Match(raw);
        if (untilMatch.Success && FlexibleDateTime.TryParse(untilMatch.Groups[1].Value, out var untilDate))
        {
            until = untilDate.Date;
        }

        RepeatKind kind;
        if (key.Contains("工作日", StringComparison.Ordinal) || key.Contains("weekday", StringComparison.Ordinal))
        {
            kind = RepeatKind.Weekdays;
        }
        else if (kindFromUnit.HasValue)
        {
            kind = kindFromUnit.Value;
        }
        else if (weekdays.Count > 0 || key.Contains("每周", StringComparison.Ordinal)
                 || key.Contains("每星期", StringComparison.Ordinal) || key.Contains("weekly", StringComparison.Ordinal))
        {
            kind = RepeatKind.Weekly;
        }
        else if (key.Contains("每天", StringComparison.Ordinal) || key.Contains("每日", StringComparison.Ordinal)
                 || key.Contains("daily", StringComparison.Ordinal))
        {
            kind = RepeatKind.Daily;
        }
        else if (key.Contains("每月", StringComparison.Ordinal) || key.Contains("monthly", StringComparison.Ordinal))
        {
            kind = RepeatKind.Monthly;
        }
        else if (key.Contains("每年", StringComparison.Ordinal) || key.Contains("yearly", StringComparison.Ordinal)
                 || key.Contains("annually", StringComparison.Ordinal))
        {
            kind = RepeatKind.Yearly;
        }
        else
        {
            return false;
        }

        if (kind == RepeatKind.Weekdays) interval = 1;
        if (kind != RepeatKind.Weekly) weekdays = new List<DayOfWeek>();

        spec = new RepeatSpec
        {
            Kind = kind,
            Interval = interval,
            Weekdays = weekdays,
            Count = count,
            Until = until,
        };

        return true;
    }

    /// <summary>解析星期集合，例如「周一、三、五」「每周二四」「Monday,Friday」。</summary>
    public static List<DayOfWeek> ParseWeekdayChars(string? text)
    {
        var result = new List<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (Match match in WeekdayCharsRegex.Matches(text))
        {
            foreach (var c in match.Groups[1].Value)
            {
                var day = c switch
                {
                    '一' => DayOfWeek.Monday,
                    '二' => DayOfWeek.Tuesday,
                    '三' => DayOfWeek.Wednesday,
                    '四' => DayOfWeek.Thursday,
                    '五' => DayOfWeek.Friday,
                    '六' => DayOfWeek.Saturday,
                    '日' or '天' => DayOfWeek.Sunday,
                    _ => (DayOfWeek?)null,
                };

                if (day.HasValue && !result.Contains(day.Value)) result.Add(day.Value);
            }
        }

        // 英文写法
        var key = TextNormalizer.Key(text);
        foreach (var (token, day) in new (string, DayOfWeek)[]
                 {
                     ("monday", DayOfWeek.Monday), ("tuesday", DayOfWeek.Tuesday),
                     ("wednesday", DayOfWeek.Wednesday), ("thursday", DayOfWeek.Thursday),
                     ("friday", DayOfWeek.Friday), ("saturday", DayOfWeek.Saturday),
                     ("sunday", DayOfWeek.Sunday),
                 })
        {
            if (key.Contains(token, StringComparison.Ordinal) && !result.Contains(day)) result.Add(day);
        }

        // 英文缩写：本地化之后导出的「Mon、Wed」也要能重新导入
        foreach (var (token, day) in new (string, DayOfWeek)[]
                 {
                     ("mon", DayOfWeek.Monday), ("tue", DayOfWeek.Tuesday),
                     ("wed", DayOfWeek.Wednesday), ("thu", DayOfWeek.Thursday),
                     ("fri", DayOfWeek.Friday), ("sat", DayOfWeek.Saturday),
                     ("sun", DayOfWeek.Sunday),
                 })
        {
            if (Regex.IsMatch(key, "(^|[^a-z])" + token + "([^a-z]|$)") && !result.Contains(day)) result.Add(day);
        }

        return result.OrderBy(day => day).ToList();
    }

    public static bool TryParseWeekdays(string? text, out List<DayOfWeek> weekdays)
    {
        weekdays = ParseWeekdayChars(text);
        return weekdays.Count > 0;
    }

    // ============ 其他 ============

    public static bool TryParsePriority(string? text, out Priority priority)
    {
        priority = Priority.Normal;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Key(text);
        switch (raw)
        {
            case "高" or "高优先" or "高优先级" or "重要" or "紧急" or "最高" or "high" or "h" or "3" or "1" or "p1":
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

    /// <summary>
    /// 解析「按档位」的文案 / 铃声写法，例如 ~~30:还有30分钟|5:马上开始|*:通用文案~~。
    /// 只有当**每一段**都是 ~~档位:值~~ 形式时才按编码解析，否则整串作为普通文本返回，
    /// 这样像 ~~8:30 开会：记得带材料~~ 这样的普通文案不会被误判。
    /// 分隔符只用 ~~|~~（Windows 文件名里不允许出现），因此文件路径可以安全地放在值里。
    /// </summary>
    public static bool TryParseOffsetTexts(
        string? text,
        out Dictionary<int, string> perOffset,
        out string plain,
        IReadOnlyCollection<int>? knownOffsets = null)
    {
        perOffset = new Dictionary<int, string>();
        plain = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = text.Trim();
        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0)
        {
            plain = raw;
            return true;
        }

        foreach (var part in parts)
        {
            if (OffsetTextPartRegex.IsMatch(part)) continue;

            // 不是编码形式：整串当作普通文案
            perOffset.Clear();
            plain = raw;
            return true;
        }

        // 全部形如 键:值 时，还要确认键确实是「这个日程的提醒档位」，
        // 否则 "8:30 开会" 这类普通文案会被误判成档位 8。
        var keys = parts.Select(p => OffsetTextPartRegex.Match(p).Groups["key"].Value).ToList();

        if (knownOffsets != null)
        {
            foreach (var key in keys)
            {
                if (key is "*" or "默认" or "通用") continue;

                if (!int.TryParse(key, out var minutes) || !knownOffsets.Contains(minutes))
                {
                    perOffset.Clear();
                    plain = raw;
                    return true;
                }
            }
        }
        else if (!raw.Contains('|') && !keys.Any(k => k is "*" or "默认" or "通用"))
        {
            // 没有日程上下文时，要求显式分隔符或 *: 标记才认定为编码形式
            perOffset.Clear();
            plain = raw;
            return true;
        }

        var generic = new List<string>();

        foreach (var part in parts)
        {
            var match = OffsetTextPartRegex.Match(part);
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();

            if (key is "*" or "默认" or "通用")
            {
                if (value.Length > 0) generic.Add(value);
                continue;
            }

            if (int.TryParse(key, out var minutes) && minutes >= 0)
            {
                perOffset[minutes] = value;
            }
        }

        plain = string.Join("|", generic);
        return true;
    }

    /// <summary>把「档位 → 文案」编码成 CSV 单元格文本。</summary>
    public static string FormatOffsetTexts(IReadOnlyDictionary<int, string>? perOffset, string? fallback)
    {
        var parts = new List<string>();

        if (perOffset != null)
        {
            foreach (var pair in perOffset.OrderByDescending(p => p.Key))
            {
                if (!string.IsNullOrWhiteSpace(pair.Value)) parts.Add(pair.Key + ":" + pair.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(fallback)) parts.Add("*:" + fallback);
        return string.Join("|", parts);
    }

    /// <summary>解析事件颜色：支持 #RRGGBB / #RGB / 中文色名 / 英文色名。</summary>
    public static bool TryParseColor(string? text, out string color)
        => EventColors.TryParse(TextNormalizer.Clean(text), out color);

    /// <summary>解析非负整数（用于重复间隔 / 重复次数）。</summary>
    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return false;

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
