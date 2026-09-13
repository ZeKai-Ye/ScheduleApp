using System.Globalization;
using System.Text.RegularExpressions;

namespace Schedule.Core.Csv;

/// <summary>
/// 宽容的日期时间解析器，专门服务于格式千奇百怪的 CSV：
/// 支持 2025-01-01 / 2025/1/1 / 2025年1月1日 / 20250101 / Excel 序列号 /
/// 今天明天后天 / 下周三 / 9:30 / 9点30分 / 下午3点 / 3天后 / ISO 8601。
/// </summary>
public static class FlexibleDateTime
{
    private static readonly Regex RelativeDayRegex = new(
        @"^(今天|今日|明天|明日|后天|大后天|昨天|昨日|前天)\s*(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex WeekdayRegex = new(
        @"^(下个|下|本|这|这个)?\s*(周|週|星期|礼拜)\s*([一二三四五六日天])\s*(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex OffsetRegex = new(
        @"^(\d+(?:\.\d+)?)\s*(分钟|分|小时|钟头|天|日|周|星期|月|个月|年)\s*(后|之后|以后|以内|内)?$",
        RegexOptions.Compiled);

    private static readonly Regex MeridiemRegex = new(
        @"^(上午|早上|早晨|凌晨|中午|下午|晚上|傍晚|夜里|夜间|晚)\s*(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex DigitsRegex = new(@"^\d+$", RegexOptions.Compiled);


    private static readonly string[] UnambiguousFormats =
    {
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
        "yyyy-M-d H:m:s", "yyyy-M-d H:m", "yyyy-M-d",
        "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/MM/dd",
        "yyyy/M/d H:m:s", "yyyy/M/d H:m", "yyyy/M/d", "yyyy/M/d HH:mm",
        "yyyy.M.d H:m", "yyyy.M.d", "yyyy.MM.dd HH:mm", "yyyy.MM.dd",
        "yyyy年M月d日 H:mm:ss", "yyyy年M月d日 H:mm", "yyyy年M月d日H:mm", "yyyy年M月d日",
        "yyyy年MM月dd日 HH:mm", "yyyy年MM月dd日",
        "M月d日 H:mm", "M月d日",
        "yyyyMMdd HHmmss", "yyyyMMdd HHmm", "yyyyMMddTHHmmss",
        "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm",
        "yyyy-MM", "yyyy年M月",
    };

    private static readonly string[] DayFirstFormats = { "d/M/yyyy H:m:s", "d/M/yyyy H:m", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yyyy HH:mm" };
    private static readonly string[] MonthFirstFormats = { "M/d/yyyy H:m:s", "M/d/yyyy H:m", "M/d/yyyy", "M-d-yyyy", "M.d.yyyy", "M/d/yyyy HH:mm", "M/d/yyyy h:mm tt" };

    private static readonly string[] TimeFormats =
    {
        "H:mm:ss.fff", "H:mm:ss", "H:mm", "HH:mm:ss", "HH:mm",
        "h:mm tt", "hh:mm tt", "h:mm:ss tt",
    };

    /// <summary>解析日期时间文本。</summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="value">解析结果（本地时间，Kind 统一为 Unspecified）。</param>
    /// <param name="defaultDate">文本只含时间时使用的日期（例如来自独立的「日期」列）。</param>
    /// <param name="dayFirst">对 03/04/2025 这类歧义格式按「日/月/年」解释。</param>
    public static bool TryParse(string? text, out DateTime value, DateTime? defaultDate = null, bool dayFirst = false)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Clean(text);
        if (raw.Length == 0) return false;

        // A. 相对日期：今天 / 明天 / 后天 / 昨天 ...
        var relative = RelativeDayRegex.Match(raw);
        if (relative.Success)
        {
            var offset = relative.Groups[1].Value switch
            {
                "今天" or "今日" => 0,
                "明天" or "明日" => 1,
                "后天" => 2,
                "大后天" => 3,
                "昨天" or "昨日" => -1,
                "前天" => -2,
                _ => 0,
            };

            var day = DateTime.Today.AddDays(offset);
            var rest = relative.Groups[2].Value.Trim();
            if (rest.Length == 0)
            {
                value = day;
                return true;
            }

            if (TryParseTimeOfDay(rest, out var timeOfDay))
            {
                value = day.Add(timeOfDay);
                return true;
            }

            if (TryParse(rest, out var nested, day, dayFirst))
            {
                value = nested;
                return true;
            }

            return false;
        }

        // B. 周几：本周三 / 下周一 / 星期三
        var weekday = WeekdayRegex.Match(raw);
        if (weekday.Success)
        {
            var target = weekday.Groups[3].Value switch
            {
                "一" => DayOfWeek.Monday,
                "二" => DayOfWeek.Tuesday,
                "三" => DayOfWeek.Wednesday,
                "四" => DayOfWeek.Thursday,
                "五" => DayOfWeek.Friday,
                "六" => DayOfWeek.Saturday,
                _ => DayOfWeek.Sunday,
            };

            var prefix = weekday.Groups[1].Value;
            var delta = ((int)target - (int)DateTime.Today.DayOfWeek + 7) % 7;
            if (prefix is "下" or "下个") delta += 7;

            var day = DateTime.Today.AddDays(delta);
            var rest = weekday.Groups[4].Value.Trim();
            if (rest.Length == 0)
            {
                value = day;
                return true;
            }

            if (TryParseTimeOfDay(rest, out var timeOfDay))
            {
                value = day.Add(timeOfDay);
                return true;
            }

            return false;
        }

        // C. 相对偏移：3天后 / 2小时后 / 30分钟后
        var offsetMatch = OffsetRegex.Match(raw);
        if (offsetMatch.Success)
        {
            var amount = double.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = offsetMatch.Groups[2].Value;
            var baseTime = DateTime.Now;
            value = unit switch
            {
                "分钟" or "分" => baseTime.AddMinutes(amount),
                "小时" or "钟头" => baseTime.AddHours(amount),
                "天" or "日" => baseTime.AddDays(amount),
                "周" or "星期" => baseTime.AddDays(amount * 7),
                "月" or "个月" => baseTime.AddMonths((int)amount),
                "年" => baseTime.AddYears((int)amount),
                _ => baseTime.AddMinutes(amount),
            };
            return true;
        }

        // D. 纯数字：20250101 / 202501011230 / 20250101123045 / 202501
        if (DigitsRegex.IsMatch(raw))
        {
            if (raw.Length == 8 && TryParseExact(raw, "yyyyMMdd", out value)) return true;
            if (raw.Length == 6 && TryParseExact(raw, "yyyyMM", out value)) return true;
            if (raw.Length == 12 && TryParseExact(raw, "yyyyMMddHHmm", out value)) return true;
            if (raw.Length == 14 && TryParseExact(raw, "yyyyMMddHHmmss", out value)) return true;

            // Excel 序列号（1900 日期系统）
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                && serial >= 20000 && serial <= 80000)
            {
                value = DateTime.SpecifyKind(DateTime.FromOADate(serial), DateTimeKind.Unspecified);
                return true;
            }
        }

        // E. 只含时间：9:30 / 09:30:15 / 930 / 下午3点
        if (TryParseTimeOfDay(raw, out var onlyTime))
        {
            var date = (defaultDate ?? DateTime.Today).Date;
            value = date.Add(onlyTime);
            return true;
        }

        // F. 精确格式
        var normalized = NormalizeChineseSeparators(raw);
        var formats = new List<string>(UnambiguousFormats);
        if (dayFirst)
        {
            formats.AddRange(DayFirstFormats);
            formats.AddRange(MonthFirstFormats);
        }
        else
        {
            formats.AddRange(MonthFirstFormats);
            formats.AddRange(DayFirstFormats);
        }

        var styles = DateTimeStyles.AllowWhiteSpaces;
        if (DateTime.TryParseExact(normalized, formats.ToArray(), CultureInfo.InvariantCulture, styles, out var exact))
        {
            value = NormalizeKind(exact);
            return true;
        }

        if (DateTime.TryParse(normalized, CultureInfo.GetCultureInfo("zh-CN"), styles, out var zh))
        {
            value = NormalizeKind(zh);
            return true;
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, styles, out var invariant))
        {
            value = NormalizeKind(invariant);
            return true;
        }

        return false;
    }

    /// <summary>解析纯时间（返回当天的时间偏移）。</summary>
    public static bool TryParseTimeOfDay(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = TextNormalizer.Clean(text);
        if (raw.Length == 0) return false;

        var shiftHours = 0;
        var meridiem = MeridiemRegex.Match(raw);
        if (meridiem.Success)
        {
            var word = meridiem.Groups[1].Value;
            raw = meridiem.Groups[2].Value.Trim();
            shiftHours = word switch
            {
                "下午" or "晚上" or "傍晚" or "夜里" or "夜间" or "晚" => 12,
                "中午" => 12,
                _ => 0,
            };
        }

        var normalized = NormalizeChineseSeparators(raw);

        // 注意：TimeSpan.TryParse 会把「930」解释成 930 天，必须限定在含冒号时才使用
        var parsed = TimeSpan.TryParseExact(normalized, TimeFormats, CultureInfo.InvariantCulture, out var exact);
        if (!parsed && normalized.Contains(':'))
        {
            parsed = TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out exact);
        }

        if (parsed)
        {
            if (normalized.Contains("PM", StringComparison.OrdinalIgnoreCase) || normalized.Contains("pm", StringComparison.Ordinal))
            {
                if (exact.Hours < 12) exact = exact.Add(TimeSpan.FromHours(12));
            }
            else if (shiftHours > 0 && exact.Hours < 12)
            {
                exact = exact.Add(TimeSpan.FromHours(shiftHours));
            }

            value = exact;
            return true;
        }

        // 930 -> 09:30, 9 -> 09:00（仅在没有日期语义时）
        if (DigitsRegex.IsMatch(normalized))
        {
            if (normalized.Length <= 2 && int.TryParse(normalized, out var hour) && hour <= 23)
            {
                if (shiftHours > 0 && hour < 12) hour += shiftHours;
                value = TimeSpan.FromHours(hour);
                return true;
            }

            if (normalized.Length is 3 or 4 && int.TryParse(normalized, out var hhmm) && hhmm <= 2359)
            {
                var h = hhmm / 100;
                var m = hhmm % 100;
                if (m < 60 && h <= 23)
                {
                    if (shiftHours > 0 && h < 12) h += shiftHours;
                    value = new TimeSpan(h, m, 0);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseExact(string text, string format, out DateTime value)
        => DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>把「2025年1月1日 9点30分」规整为「2025-1-1 9:30」。</summary>
    private static string NormalizeChineseSeparators(string text)
    {
        var s = text;

        // 中文日期分隔符
        s = Regex.Replace(s, @"(\d+)\s*年\s*", "$1-");
        s = Regex.Replace(s, @"(\d+)\s*月\s*", "$1-");
        s = Regex.Replace(s, @"(\d+)\s*日", "$1 ");

        // 时段词参与小时换算：下午3点 -> 15点
        s = Regex.Replace(s, @"(上午|早上|早晨|凌晨|中午|下午|晚上|傍晚|夜里|夜间)\s*(\d{1,2})", MeridiemEvaluator);
        foreach (var word in new[] { "上午", "早上", "早晨", "凌晨", "中午", "下午", "晚上", "傍晚", "夜里", "夜间" })
        {
            s = s.Replace(word, string.Empty);
        }

        // 点 / 时 转换为标准时间
        s = Regex.Replace(s, @"(\d{1,2})\s*点钟\s*(\d{1,2})?", "$1:00");
        s = Regex.Replace(s, @"(\d{1,2})\s*点半", "$1:30");
        s = Regex.Replace(s, @"(\d{1,2})\s*[点时]\s*(\d{1,2})?", PointEvaluator);

        // 分 / 秒
        s = Regex.Replace(s, @"(?<=\d)\s*分(?=(\d|\s|$))", string.Empty);
        s = Regex.Replace(s, @"(?<=\d)\s*秒", string.Empty);

        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    /// <summary>把「下午3」转换成 24 小时制的「15」。</summary>
    private static string MeridiemEvaluator(Match match)
    {
        var word = match.Groups[1].Value;
        if (!int.TryParse(match.Groups[2].Value, out var hour)) return match.Value;

        switch (word)
        {
            case "下午":
            case "晚上":
            case "傍晚":
            case "夜里":
            case "夜间":
                if (hour < 12) hour += 12;
                break;
            case "中午":
                if (hour is > 0 and < 11) hour += 12;
                break;
        }

        return hour.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>把「9点」补成「9:00」，把「9点30」补成「9:30」。</summary>
    private static string PointEvaluator(Match match)
    {
        var hour = match.Groups[1].Value;
        var minute = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
        if (minute.Length == 0) return hour + ":00";
        return hour + ":" + minute.PadLeft(2, '0');
    }

    private static DateTime NormalizeKind(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }
}
