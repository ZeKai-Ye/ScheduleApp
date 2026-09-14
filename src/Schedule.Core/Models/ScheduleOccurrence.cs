namespace Schedule.Core.Models;

/// <summary>日程的某一次具体发生（重复日程会展开成多个）。</summary>
public sealed class ScheduleOccurrence
{
    /// <summary>用于显示与播报的「有效日程」：没有例外时就是系列本体，有例外时是套用了覆盖的副本。</summary>
    public required ScheduleItem Item { get; init; }

    /// <summary>系列本体（持久化 / 提醒去重必须用它）；没有例外时为 null，表示 Item 就是本体。</summary>
    public ScheduleItem? SeriesRef { get; init; }

    /// <summary>系列本体（永远不为 null）。</summary>
    public ScheduleItem Series => SeriesRef ?? Item;

    /// <summary>本次发生的例外覆盖（无则为 null）。</summary>
    public OccurrenceOverride? Override { get; init; }

    /// <summary>被覆盖的那次发生的原始开始时间（改期后定位仍用它）。</summary>
    public DateTime? OriginalStart { get; init; }

    /// <summary>本次是否套用了例外。</summary>
    public bool IsOverride => Override != null;

    /// <summary>该次发生在系列中的原始开始时间（未改期时等于 Start）。</summary>
    public DateTime SeriesStart => OriginalStart ?? Start;

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    /// <summary>该次发生对应的最早提醒时刻（无提醒时等于开始时间）。</summary>
    public required DateTime ReminderAt { get; init; }

    /// <summary>该次发生的全部提醒档位（按触发时间升序）。</summary>
    public List<ReminderPlan> Reminders { get; init; } = new();

    public DateOnly Date => DateOnly.FromDateTime(Start);

    /// <summary>是否为日程的首次发生（即定义的基准时间）。</summary>
    public bool IsFirst => Start == Item.Start;

    public bool HasReminder => Reminders.Count > 0;

    public override string ToString()
        => Start.ToString("yyyy-MM-dd HH:mm") + " " + Item.Title;
}
