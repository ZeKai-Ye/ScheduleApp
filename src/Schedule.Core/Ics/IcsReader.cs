using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Schedule.Core.Csv;
using Schedule.Core.Models;

namespace Schedule.Core.Ics;

/// <summary>
/// 极简但够用的 iCalendar 解析器：只关心 VEVENT（含 VALARM），忽略 VTIMEZONE / VJOURNAL 等。
///
/// 支持 Google 日历、Outlook、Apple 日历导出的常见写法：
/// 折行、转义、DTSTART 的 DATE / 本地时间 / UTC(Z) / TZID、DTEND、DURATION、
/// RRULE（FREQ / INTERVAL / BYDAY / COUNT / UNTIL）、EXDATE、RECURRENCE-ID、VALARM TRIGGER。
/// 不支持的规则会**记成警告并把该日程按单次导入**，不会静默丢数据。
/// </summary>
public static class IcsReader
{
    private static readonly Regex DurationRegex = new(
        @"^(?<sign>[+-])?P(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+)S)?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>解析文本。</summary>
    public static IcsParseResult Parse(string? text)
    {
        var result = new IcsParseResult();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var lines = Unfold(text);
        IcsEvent? current = null;
        List<IcsProperty>? alarm = null;
        var depth = 0;
        var lineNumber = 0;

        foreach (var raw in lines)
        {
            lineNumber++;
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;

            var colon = FindValueSeparator(line);
            if (colon < 0)
            {
                result.Warnings.Add("第 " + lineNumber + " 行缺少「:」分隔符，已跳过：" + Shorten(line));
                continue;
            }

            var namePart = line.Substring(0, colon);
            var value = line.Substring(colon + 1);
            var (name, parameters) = ParseName(namePart);

            switch (name)
            {
                case "BEGIN":
                    depth++;

                    if (value.Equals("VCALENDAR", StringComparison.OrdinalIgnoreCase)) result.LooksLikeCalendar = true;
                    else if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new IcsEvent { Line = lineNumber };
                    }
                    else if (value.Equals("VALARM", StringComparison.OrdinalIgnoreCase) && current != null)
                    {
                        alarm = new List<IcsProperty>();
                    }

                    continue;

                case "END":
                    depth = Math.Max(0, depth - 1);

                    if (value.Equals("VALARM", StringComparison.OrdinalIgnoreCase))
                    {
                        if (alarm != null && current != null) current.Alarms.Add(alarm);
                        alarm = null;
                    }
                    else if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current != null) result.Events.Add(current);
                        current = null;
                    }

                    continue;
            }

            var property = new IcsProperty { Name = name, Parameters = parameters, Value = value };

            if (alarm != null) alarm.Add(property);
            else current?.Properties.Add(property);
        }

        if (result.Events.Count == 0 && result.LooksLikeCalendar) result.Warnings.Add("文件里没有找到任何日程（VEVENT）。");

        return result;
    }

    /// <summary>展开折行：以空格或制表符开头的行是上一行的续行。</summary>
    public static List<string> Unfold(string text)
    {
        var result = new List<string>();

        foreach (var raw in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && result.Count > 0)
            {
                result[^1] += raw.Substring(1);
                continue;
            }

            result.Add(raw);
        }

        return result;
    }

    /// <summary>找出属性名与取值之间的分隔冒号（跳过双引号里的冒号）。</summary>
    private static int FindValueSeparator(string line)
    {
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"') inQuotes = !inQuotes;
            else if (c == ':' && !inQuotes) return i;
        }

        return -1;
    }

    private static (string Name, Dictionary<string, string> Parameters) ParseName(string namePart)
    {
        var segments = namePart.Split(';');
        var name = segments[0].Trim().ToUpperInvariant();
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            var eq = segment.IndexOf('=');

            if (eq <= 0)
            {
                parameters[segment.Trim()] = string.Empty;
                continue;
            }

            var key = segment.Substring(0, eq).Trim();
            var value = segment.Substring(eq + 1).Trim().Trim('"');
            parameters[key] = value;
        }

        return (name, parameters);
    }

    /// <summary>解析 iCalendar 日期时间：DATE / 本地 / UTC(Z)。</summary>
    public static bool TryParseDate(IcsProperty? property, out DateTime value, out bool isDateOnly, out bool isUtc)
    {
        value = default;
        isDateOnly = false;
        isUtc = false;

        var raw = property?.Value.Trim();
        if (string.IsNullOrEmpty(raw)) return false;

        var isDateParameter = string.Equals(property!.Parameter("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase);
        isDateOnly = isDateParameter || raw.Length == 8;

        if (isDateOnly && raw.Length >= 8
            && DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            value = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            return true;
        }

        var text = raw;
        if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            isUtc = true;
            text = text.Substring(0, text.Length - 1);
        }

        // 允许 20260915T150000 / 20260915T1500 / 带时区偏移的少量写法
        string[] formats = { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd'T'HHmmss'Z'", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = isUtc ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            return true;
        }

        // 最后兜底：交给宽容解析器（有些实现会写 2026-09-15 15:00）
        if (Csv.FlexibleDateTime.TryParse(text, out var loose, null))
        {
            value = loose;
            return true;
        }

        return false;
    }

    /// <summary>把 UTC 时间换算成本地时间（本程序内部一律用本地时间）。</summary>
    public static DateTime ToLocal(DateTime value, bool isUtc)
        => isUtc ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime() : value;

    /// <summary>
    /// 解析 ISO 8601 时长（PT1H30M / P1D …）。
    /// 返回 false 表示**根本没写成时长**（与 "PT0M" 这种合法的零时长区分开——
    /// 后者表示"准点提醒"，不能当成解析失败丢掉）。
    /// </summary>
    public static bool TryParseDuration(string? text, out TimeSpan duration, out bool negative)
    {
        duration = TimeSpan.Zero;
        negative = false;

        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = DurationRegex.Match(text.Trim());
        if (!match.Success) return false;

        negative = match.Groups["sign"].Value == "-";

        var days = match.Groups["days"].Success ? int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture) : 0;
        var hours = match.Groups["hours"].Success ? int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0;
        var seconds = match.Groups["seconds"].Success ? int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture) : 0;

        duration = new TimeSpan(days, hours, minutes, seconds);
        if (negative) duration = -duration;

        return true;
    }

    /// <summary>解析 ISO 8601 时长；无法解析时返回 TimeSpan.Zero。</summary>
    public static TimeSpan ParseDuration(string? text, out bool negative)
    {
        negative = false;
        if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;

        var match = DurationRegex.Match(text.Trim());
        if (!match.Success) return TimeSpan.Zero;

        negative = match.Groups["sign"].Value == "-";

        var days = match.Groups["days"].Success ? int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture) : 0;
        var hours = match.Groups["hours"].Success ? int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0;
        var seconds = match.Groups["seconds"].Success ? int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture) : 0;

        var duration = new TimeSpan(days, hours, minutes, seconds);
        return negative ? -duration : duration;
    }

    /// <summary>把 RRULE 解析成程序内的重复规则。</summary>
    public static RepeatSpec ParseRule(string? rrule, DateTime start, List<string> warnings, string label)
    {
        var spec = new RepeatSpec();
        if (string.IsNullOrWhiteSpace(rrule)) return spec;

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;

            parts[segment.Substring(0, eq).Trim()] = segment.Substring(eq + 1).Trim();
        }

        var freq = parts.TryGetValue("FREQ", out var f) ? f.ToUpperInvariant() : string.Empty;

        spec = new RepeatSpec
        {
            Kind = freq switch
            {
                "DAILY" => RepeatKind.Daily,
                "WEEKLY" => RepeatKind.Weekly,
                "MONTHLY" => RepeatKind.Monthly,
                "YEARLY" => RepeatKind.Yearly,
                _ => RepeatKind.None,
            },
            Interval = parts.TryGetValue("INTERVAL", out var intervalText) && int.TryParse(intervalText, out var interval) && interval > 0
                ? interval
                : 1,
        };

        if (spec.Kind == RepeatKind.None)
        {
            if (freq.Length > 0)
            {
                warnings.Add(label + "：暂不支持重复规则 FREQ=" + freq + "，已按单次日程导入。");
            }

            return new RepeatSpec();
        }

        // BYDAY：MO,WE,FR（带序号的 2MO 我们按普通星期处理并提示）
        if (parts.TryGetValue("BYDAY", out var byDay) && byDay.Length > 0)
        {
            var weekdays = new List<DayOfWeek>();
            var ordinalSeen = false;

            foreach (var token in byDay.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var text = token.Trim().ToUpperInvariant();
                if (text.Length >= 2 && !char.IsLetter(text[0])) ordinalSeen = true;

                var code = text.Length >= 2 ? text.Substring(text.Length - 2) : text;
                var day = code switch
                {
                    "MO" => (DayOfWeek?)DayOfWeek.Monday,
                    "TU" => DayOfWeek.Tuesday,
                    "WE" => DayOfWeek.Wednesday,
                    "TH" => DayOfWeek.Thursday,
                    "FR" => DayOfWeek.Friday,
                    "SA" => DayOfWeek.Saturday,
                    "SU" => DayOfWeek.Sunday,
                    _ => null,
                };

                if (day.HasValue && !weekdays.Contains(day.Value)) weekdays.Add(day.Value);
            }

            if (weekdays.Count > 0)
            {
                spec.Weekdays = weekdays.OrderBy(d => d).ToList();
                if (spec.Kind != RepeatKind.Weekly) spec.Kind = RepeatKind.Weekly;
            }

            if (ordinalSeen)
            {
                warnings.Add(label + "：「每月第 N 个星期几」（BYDAY=" + byDay + "）暂不支持，已按每周导入。");
            }
        }

        if (parts.TryGetValue("BYMONTHDAY", out var byMonthDay) && byMonthDay.Length > 0
            && spec.Kind is RepeatKind.Monthly or RepeatKind.Yearly
            && !byMonthDay.Split(',').Any(day => int.TryParse(day, out var value) && value == start.Day))
        {
            warnings.Add(label + "：「每月 " + byMonthDay + " 日」与开始日期不一致，暂不支持，已按单次日程导入。");
            return new RepeatSpec();
        }

        if (parts.TryGetValue("COUNT", out var countText) && int.TryParse(countText, out var count) && count > 0)
        {
            spec.Count = count;
        }
        else if (parts.TryGetValue("UNTIL", out var untilText) && untilText.Length > 0)
        {
            var untilProperty = new IcsProperty { Name = "UNTIL", Value = untilText };
            if (TryParseDate(untilProperty, out var until, out var untilIsDate, out var untilIsUtc))
            {
                var local = ToLocal(until, untilIsUtc);
                spec.Until = untilIsDate ? local.Date : local;
            }
        }

        if (parts.ContainsKey("BYSETPOS") || parts.ContainsKey("BYWEEKNO") || parts.ContainsKey("BYYEARDAY"))
        {
            warnings.Add(label + "：重复规则里的 BYSETPOS / BYWEEKNO / BYYEARDAY 暂不支持，已忽略这些限定。");
        }

        return spec;
    }

    private static string Shorten(string text, int max = 80)
        => text.Length <= max ? text : text.Substring(0, max) + "…";
}
