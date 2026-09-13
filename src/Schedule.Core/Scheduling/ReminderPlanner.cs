using Schedule.Core.Models;

namespace Schedule.Core.Scheduling;

/// <summary>
/// 提醒调度：判断在某个时间窗口内有哪些提醒应该被触发。
/// 采用「时间窗口 + 已触发标记」模型，程序重启或休眠唤醒后不会漏掉也不会重复提醒。
/// </summary>
public static class ReminderPlanner
{
    /// <summary>
    /// 收集 (fromExclusive, toInclusive] 内所有到点的提醒，按提醒时刻升序返回。
    /// </summary>
    public static List<ScheduleOccurrence> CollectDue(
        IEnumerable<ScheduleItem> items,
        DateTime fromExclusive,
        DateTime toInclusive)
    {
        var result = new List<ScheduleOccurrence>();
        if (toInclusive <= fromExclusive) return result;

        foreach (var item in items)
        {
            if (!item.ReminderEnabled || item.Status != ItemStatus.Pending) continue;

            // 提醒时刻 = 开始时间 - 提前量，因此展开区间必须按提前量向两端扩张
            var lead = Math.Max(0, item.RemindBeforeMinutes);
            var windowStart = fromExclusive.AddMinutes(-lead - 1);
            var windowEnd = toInclusive.AddMinutes(lead + 1);

            // 绝对提醒时间与开始时间解耦，需要把首次发生纳入区间
            if (item.RemindAtOverride.HasValue)
            {
                if (item.Start < windowStart) windowStart = item.Start;
                if (item.Start > windowEnd) windowEnd = item.Start;
            }

            foreach (var occurrence in OccurrenceCalculator.Expand(item, windowStart, windowEnd))
            {
                if (occurrence.ReminderAt <= fromExclusive || occurrence.ReminderAt > toInclusive) continue;
                if (AlreadyFired(item, occurrence)) continue;
                result.Add(occurrence);
            }
        }

        result.Sort(static (a, b) => a.ReminderAt.CompareTo(b.ReminderAt));
        return result;
    }

    /// <summary>取得下一次提醒（用于状态栏倒计时）。</summary>
    public static ScheduleOccurrence? NextUpcoming(
        IEnumerable<ScheduleItem> items,
        DateTime now,
        int searchDays = 400)
    {
        ScheduleOccurrence? best = null;

        foreach (var item in items)
        {
            if (!item.ReminderEnabled || item.Status != ItemStatus.Pending) continue;

            var lead = Math.Max(0, item.RemindBeforeMinutes);
            var from = now.AddMinutes(-lead - 1);
            var until = now.AddDays(Math.Max(1, searchDays));

            if (item.RemindAtOverride.HasValue)
            {
                if (item.Start < from) from = item.Start;
                if (item.Start > until) until = item.Start;
            }

            foreach (var occurrence in OccurrenceCalculator.Expand(item, from, until))
            {
                if (occurrence.ReminderAt <= now) continue;
                if (AlreadyFired(item, occurrence)) continue;

                if (best == null || occurrence.ReminderAt < best.ReminderAt) best = occurrence;
                break;
            }
        }

        return best;
    }

    /// <summary>标记某次发生已经提醒过，避免重复弹窗。</summary>
    public static void MarkFired(ScheduleItem item, ScheduleOccurrence occurrence)
    {
        if (!item.LastFiredOccurrence.HasValue || item.LastFiredOccurrence.Value < occurrence.Start)
        {
            item.LastFiredOccurrence = occurrence.Start;
        }
    }

    private static bool AlreadyFired(ScheduleItem item, ScheduleOccurrence occurrence)
        => item.LastFiredOccurrence.HasValue && item.LastFiredOccurrence.Value >= occurrence.Start;
}
