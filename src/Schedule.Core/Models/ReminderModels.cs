using Schedule.Core.Localization;

namespace Schedule.Core.Models;

/// <summary>一条日程的某一个「提醒档位」在某个发生时间上的具体触发计划。</summary>
public sealed class ReminderPlan
{
    /// <summary>相对开始时间的提前分钟数（负数表示在开始之后）。</summary>
    public int OffsetMinutes { get; init; }

    /// <summary>实际触发时刻。</summary>
    public DateTime FireAt { get; init; }

    /// <summary>是否由「绝对提醒时间」产生。</summary>
    public bool IsAbsolute { get; init; }

    public string OffsetText => IsAbsolute ? Loc.T("指定时间", "Specific time") : DescribeOffset(OffsetMinutes);

    /// <summary>把提前分钟数描述成人话。</summary>
    public static string DescribeOffset(int minutes)
    {
        if (minutes <= 0) return Loc.T("准点", "On time");

        if (minutes % (60 * 24) == 0)
        {
            var days = minutes / (60 * 24);
            return days == 1
                ? Loc.F("提前 1 天", "1 day before")
                : Loc.F("提前 {0} 天", "{0} days before", days);
        }

        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return hours == 1
                ? Loc.F("提前 1 小时", "1 hour before")
                : Loc.F("提前 {0} 小时", "{0} hours before", hours);
        }

        if (minutes > 60)
        {
            var hours = minutes / 60;
            var rest = minutes % 60;
            return Loc.F("提前 {0} 小时 {1} 分钟", "{0} h {1} " + (rest == 1 ? "min" : "mins") + " before", hours, rest);
        }

        return minutes == 1
            ? Loc.F("提前 1 分钟", "1 min before")
            : Loc.F("提前 {0} 分钟", "{0} mins before", minutes);
    }

    /// <summary>紧凑写法，用于列表与导出：30 / 15 / 5 / 0 / 1天。</summary>
    public static string FormatOffset(int minutes)
    {
        if (minutes <= 0) return "0";
        if (minutes % (60 * 24) == 0) return (minutes / (60 * 24)) + "天";
        if (minutes % 60 == 0) return (minutes / 60) + "小时";
        return minutes.ToString();
    }

    /// <summary>把多个档位格式化成 CSV 单元格，例如「30,15,5,0」。</summary>
    public static string FormatOffsets(IEnumerable<int> offsets)
        => string.Join(",", offsets.OrderByDescending(o => o).Select(FormatOffset));
}

/// <summary>提醒已触发的记录，用于避免重复提醒。</summary>
public sealed class ReminderFireRecord
{
    /// <summary>该提醒对应的日程发生时间。</summary>
    public DateTime OccurrenceStart { get; set; }

    /// <summary>触发的是哪个档位（提前分钟数）。</summary>
    public int OffsetMinutes { get; set; }

    /// <summary>实际触发时刻。</summary>
    public DateTime FiredAt { get; set; } = DateTime.Now;

    public ReminderFireRecord Clone() => new()
    {
        OccurrenceStart = OccurrenceStart,
        OffsetMinutes = OffsetMinutes,
        FiredAt = FiredAt,
    };
}

/// <summary>到点需要触发的提醒（日程 + 发生 + 档位）。</summary>
public sealed class DueReminder
{
    public required ScheduleItem Item { get; init; }

    public required ScheduleOccurrence Occurrence { get; init; }

    public required ReminderPlan Plan { get; init; }

    public DateTime FireAt => Plan.FireAt;

    public override string ToString()
        => FireAt.ToString("MM-dd HH:mm") + " " + Plan.OffsetText + " " + Item.Title;
}
