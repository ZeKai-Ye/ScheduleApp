namespace Schedule.Core.Models;

/// <summary>日程的某一次具体发生（重复日程会展开成多个）。</summary>
public sealed class ScheduleOccurrence
{
    public required ScheduleItem Item { get; init; }

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    /// <summary>该次发生对应的提醒时刻。</summary>
    public required DateTime ReminderAt { get; init; }

    public DateOnly Date => DateOnly.FromDateTime(Start);

    /// <summary>是否为日程的首次发生（即定义的基准时间）。</summary>
    public bool IsFirst => Start == Item.Start;

    public override string ToString() => Start.ToString("yyyy-MM-dd HH:mm") + " " + Item.Title;
}
