namespace Schedule.Core.Models;

/// <summary>日程的某一次具体发生（重复日程会展开成多个）。</summary>
public sealed class ScheduleOccurrence
{
    public required ScheduleItem Item { get; init; }

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
