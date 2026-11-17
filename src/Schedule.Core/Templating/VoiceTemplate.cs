using System.Text;
using Schedule.Core.Localization;
using Schedule.Core.Models;

namespace Schedule.Core.Templating;

/// <summary>
/// 自定义语音播报模板引擎。
/// 支持占位符：{title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status} {offset}
/// </summary>
public static class VoiceTemplate
{
    public static string Default => Loc.T("{time}，提醒您：{title}。{countdown}。{category}", "{time}, reminder: {title}. {countdown}. {category}");

    public static readonly string[] Tokens =
    {
        "{title}", "{time}", "{date}", "{weekday}", "{category}", "{notes}",
        "{priority}", "{countdown}", "{status}", "{offset}",
    };

    public static string Describe(string token) => token switch
    {
        "{title}" => Loc.T("日程标题", "Event title"),
        "{time}" => Loc.T("开始时间 HH:mm", "Start time HH:mm"),
        "{date}" => Loc.T("日期 2025-01-01", "Date 2025-01-01"),
        "{weekday}" => Loc.T("星期几", "Day of week"),
        "{category}" => Loc.T("分类", "Category"),
        "{notes}" => Loc.T("备注", "Notes"),
        "{priority}" => Loc.T("优先级（高/中/低）", "Priority (high/normal/low)"),
        "{countdown}" => Loc.T("还有多久（例如：还有 10 分钟）", "Time remaining (e.g. in 10 minutes)"),
        "{status}" => Loc.T("状态", "Status"),
        "{offset}" => Loc.T("本次提醒档位（提前 30 分钟 / 准点）", "This reminder's offset (30 mins before / On time)"),
        _ => token,
    };

    /// <summary>渲染播报文本。未知占位符原样保留，空值占位符会被清理掉多余标点。</summary>
    public static string Render(
        string? template,
        ScheduleItem item,
        DateTime occurrenceStart,
        DateTime now,
        int? offsetMinutes = null)
    {
        var text = string.IsNullOrWhiteSpace(template) ? Default : template!;

        var countdown = DescribeCountdown(occurrenceStart, now);
        var offset = offsetMinutes.HasValue ? ReminderPlan.DescribeOffset(offsetMinutes.Value) : Loc.T("提醒", "Reminder");

        text = text
            .Replace("{title}", item.Title ?? string.Empty)
            .Replace("{time}", occurrenceStart.ToString("HH:mm"))
            .Replace("{date}", occurrenceStart.ToString("yyyy-MM-dd"))
            .Replace("{weekday}", WeekdayText(occurrenceStart.DayOfWeek))
            .Replace("{category}", item.Category ?? string.Empty)
            .Replace("{notes}", SingleLine(item.Notes))
            .Replace("{priority}", PriorityText(item.Priority))
            .Replace("{countdown}", countdown)
            .Replace("{status}", StatusText(item.Status))
            .Replace("{offset}", offset);

        return Tidy(text);
    }

    /// <summary>用示例数据渲染，供设置界面实时预览。</summary>
    public static string Preview(string? template, DateTime now, int? offsetMinutes = 15)
    {
        var item = new ScheduleItem
        {
            Title = Loc.T("项目周会", "Weekly project meeting"),
            Category = Loc.T("工作", "Work"),
            Notes = Loc.T("会议室 A，带上周报", "Meeting room A, bring the weekly report"),
            Priority = Priority.High,
            Start = now.AddMinutes(offsetMinutes ?? 15),
            ReminderOffsets = new List<int> { 30, 15, 5 },
        };

        return Render(template, item, item.Start, now, offsetMinutes);
    }

    public static string DescribeCountdown(DateTime occurrenceStart, DateTime now)
    {
        var delta = occurrenceStart - now;
        if (delta.TotalSeconds >= -30 && delta.TotalSeconds <= 30) return Loc.T("现在开始", "starting now");

        if (delta < TimeSpan.Zero)
        {
            var overdue = -delta;
            if (overdue.TotalMinutes < 60) return Ago((int)Math.Round(overdue.TotalMinutes), "分钟", "minute", "minutes");
            if (overdue.TotalHours < 24) return Ago((int)Math.Round(overdue.TotalHours), "小时", "hour", "hours");
            return Ago((int)Math.Round(overdue.TotalDays), "天", "day", "days");
        }

        if (delta.TotalMinutes < 1) return Loc.T("还有不到 1 分钟", "in less than a minute");
        if (delta.TotalMinutes < 60) return Within((int)Math.Round(delta.TotalMinutes), "分钟", "minute", "minutes");
        if (delta.TotalHours < 24) return Within((int)Math.Round(delta.TotalHours), "小时", "hour", "hours");
        return Within((int)Math.Round(delta.TotalDays), "天", "day", "days");
    }

    /// <summary>「还有 N 单位」：数字交给 Loc.F，英文按单复数选一份文案。</summary>
    private static string Within(int value, string unitCn, string singular, string plural)
        => value == 1
            ? Loc.F("还有 1 " + unitCn, "in 1 " + singular)
            : Loc.F("还有 {0} " + unitCn, "in {0} " + plural, value);

    /// <summary>「已经开始 N 单位」：同上。</summary>
    private static string Ago(int value, string unitCn, string singular, string plural)
        => value == 1
            ? Loc.F("已经开始 1 " + unitCn, "started 1 " + singular + " ago")
            : Loc.F("已经开始 {0} " + unitCn, "started {0} " + plural + " ago", value);

    public static string WeekdayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Loc.T("星期一", "Monday"),
        DayOfWeek.Tuesday => Loc.T("星期二", "Tuesday"),
        DayOfWeek.Wednesday => Loc.T("星期三", "Wednesday"),
        DayOfWeek.Thursday => Loc.T("星期四", "Thursday"),
        DayOfWeek.Friday => Loc.T("星期五", "Friday"),
        DayOfWeek.Saturday => Loc.T("星期六", "Saturday"),
        _ => Loc.T("星期日", "Sunday"),
    };

    /// <summary>紧凑星期名（月/周视图表头用）。</summary>
    public static string ShortWeekdayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Loc.T("周一", "Mon"),
        DayOfWeek.Tuesday => Loc.T("周二", "Tue"),
        DayOfWeek.Wednesday => Loc.T("周三", "Wed"),
        DayOfWeek.Thursday => Loc.T("周四", "Thu"),
        DayOfWeek.Friday => Loc.T("周五", "Fri"),
        DayOfWeek.Saturday => Loc.T("周六", "Sat"),
        _ => Loc.T("周日", "Sun"),
    };

    public static string PriorityText(Priority priority) => priority switch
    {
        Priority.High => Loc.T("高优先级", "High priority"),
        Priority.Low => Loc.T("低优先级", "Low priority"),
        _ => Loc.T("普通优先级", "Normal priority"),
    };

    public static string StatusText(ItemStatus status) => status switch
    {
        ItemStatus.Done => Loc.T("已完成", "Done"),
        ItemStatus.Canceled => Loc.T("已取消", "Canceled"),
        _ => Loc.T("待办", "To do"),
    };

    public static string SingleLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    /// <summary>清理空占位符留下的重复标点。</summary>
    private static string Tidy(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        var result = sb.ToString().Trim();
        foreach (var pair in new[] { "，，", "。。", "；；", "，。", "。，", "，；", " ，", "。 " })
        {
            while (result.Contains(pair, StringComparison.Ordinal))
            {
                result = result.Replace(pair, pair.Substring(0, 1));
            }
        }

        result = result.Trim(' ', '，', '。', '；', '、', ',', '.', ';');
        return result.Length == 0 ? Loc.T("日程提醒", "Event reminder") : result;
    }
}
