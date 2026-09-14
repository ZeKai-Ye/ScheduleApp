using System.Globalization;
using System.Text;
using Schedule.Core.Models;

namespace Schedule.Core.Ics;

/// <summary>
/// 把日程写成 iCalendar（.ics）。
///
/// 用途有两个：导出给 Google 日历 / Outlook 用；以及给自测做"导出→导入"往返验证
/// （往返一致才说明两边的规则映射没有互相跑偏）。
/// </summary>
public static class IcsWriter
{
    public static string ToIcs(IEnumerable<ScheduleItem> items, string calendarName = "日程助手 ScheduleApp")
    {
        var sb = new StringBuilder();

        Append(sb, "BEGIN:VCALENDAR");
        Append(sb, "VERSION:2.0");
        Append(sb, "PRODID:-//ScheduleApp//Schedule Assistant//CN");
        Append(sb, "CALSCALE:GREGORIAN");
        Append(sb, "METHOD:PUBLISH");
        Append(sb, "X-WR-CALNAME:" + IcsText.Escape(calendarName));

        foreach (var item in items.OrderBy(i => i.Start))
        {
            AppendEvent(sb, item);
        }

        Append(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    public static void WriteFile(string path, IEnumerable<ScheduleItem> items)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // iCalendar 规定 UTF-8；带 BOM 更利于 Windows 记事本识别
        File.WriteAllText(path, ToIcs(items), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void AppendEvent(StringBuilder sb, ScheduleItem item)
    {
        Append(sb, "BEGIN:VEVENT");
        Append(sb, "UID:" + (item.Id == Guid.Empty ? Guid.NewGuid() : item.Id) + "@scheduleapp");
        Append(sb, "DTSTAMP:" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        Append(sb, "SUMMARY:" + IcsText.Escape(item.Title));

        Append(sb, "DTSTART" + (item.AllDay ? ";VALUE=DATE:" : ":") + Format(item.Start, item.AllDay));

        if (item.End.HasValue)
        {
            var end = item.End.Value;
            var endText = item.AllDay
                ? (end.TimeOfDay == TimeSpan.Zero ? end : end.Date.AddDays(1)).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : Format(end, false);

            Append(sb, "DTEND" + (item.AllDay ? ";VALUE=DATE:" : ":") + endText);
        }

        if (!string.IsNullOrWhiteSpace(item.Notes)) Append(sb, "DESCRIPTION:" + IcsText.Escape(item.Notes));
        if (!string.IsNullOrWhiteSpace(item.Category)) Append(sb, "CATEGORIES:" + IcsText.Escape(item.Category));

        Append(sb, "STATUS:" + item.Status switch
        {
            ItemStatus.Done => "COMPLETED",
            ItemStatus.Canceled => "CANCELLED",
            _ => "CONFIRMED",
        });

        Append(sb, "PRIORITY:" + item.Priority switch
        {
            Priority.High => "1",
            Priority.Low => "7",
            _ => "5",
        });

        var rule = BuildRule(item);
        if (rule.Length > 0) Append(sb, "RRULE:" + rule);

        foreach (var excluded in item.ExcludedOccurrences.Distinct().OrderBy(v => v))
        {
            Append(sb, "EXDATE:" + Format(excluded, false));
        }

        // 提醒：每个档位一个 VALARM
        foreach (var offset in item.NormalizedOffsets())
        {
            Append(sb, "BEGIN:VALARM");
            Append(sb, "ACTION:DISPLAY");
            Append(sb, "DESCRIPTION:" + IcsText.Escape(item.Title));
            Append(sb, "TRIGGER:-" + FormatDuration(TimeSpan.FromMinutes(offset)));
            Append(sb, "END:VALARM");
        }

        if (item.RemindAtOverride.HasValue)
        {
            Append(sb, "BEGIN:VALARM");
            Append(sb, "ACTION:DISPLAY");
            Append(sb, "DESCRIPTION:" + IcsText.Escape(item.Title));
            Append(sb, "TRIGGER;VALUE=DATE-TIME:" + Format(item.RemindAtOverride.Value, false));
            Append(sb, "END:VALARM");
        }

        Append(sb, "END:VEVENT");
    }

    private static string BuildRule(ScheduleItem item)
    {
        if (item.Repeat == RepeatKind.None) return string.Empty;

        var parts = new List<string>
        {
            "FREQ=" + item.Repeat switch
            {
                RepeatKind.Daily or RepeatKind.Weekdays => "DAILY",
                RepeatKind.Weekly => "WEEKLY",
                RepeatKind.Monthly => "MONTHLY",
                _ => "YEARLY",
            },
        };

        if (item.Repeat == RepeatKind.Weekdays) parts.Add("BYDAY=MO,TU,WE,TH,FR");

        if (item.RepeatInterval > 1) parts.Add("INTERVAL=" + item.RepeatInterval);

        if (item.Repeat == RepeatKind.Weekly && item.Repeat != RepeatKind.Weekdays && item.RepeatWeekdays.Count > 0)
        {
            parts.Add("BYDAY=" + string.Join(",", item.RepeatWeekdays.Distinct().OrderBy(d => d).Select(DayCode)));
        }

        if (item.RepeatCount is > 0) parts.Add("COUNT=" + item.RepeatCount.Value);
        else if (item.RepeatUntil.HasValue) parts.Add("UNTIL=" + item.RepeatUntil.Value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));

        return string.Join(";", parts);
    }

    private static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };

    private static string Format(DateTime value, bool dateOnly)
        => dateOnly
            ? value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan duration)
    {
        var text = "P";
        var days = (int)duration.TotalDays;

        if (days > 0) text += days + "D";

        var hours = duration.Hours;
        var minutes = duration.Minutes;

        if (hours > 0 || minutes > 0 || days == 0)
        {
            text += "T";
            if (hours > 0) text += hours + "H";
            if (minutes > 0) text += minutes + "M";
            if (hours == 0 && minutes == 0) text += "0M";
        }

        return text;
    }

    /// <summary>写一行（自动折行）。</summary>
    private static void Append(StringBuilder sb, string line)
        => sb.Append(IcsText.Fold(line)).Append("\r\n");
}
