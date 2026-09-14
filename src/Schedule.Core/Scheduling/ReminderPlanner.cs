using Schedule.Core.Models;

namespace Schedule.Core.Scheduling;

/// <summary>
/// 提醒调度：在时间窗口内找出所有到点的「档位」提醒。
/// 一条日程可以配置多个档位（例如 30 / 15 / 5 分钟前 + 准点），
/// 每个档位独立去重，互不影响。
/// </summary>
public static class ReminderPlanner
{
    /// <summary>收集 (fromExclusive, toInclusive] 内所有到点的提醒，按触发时刻升序返回。</summary>
    public static List<DueReminder> CollectDue(
        IEnumerable<ScheduleItem> items,
        DateTime fromExclusive,
        DateTime toInclusive)
    {
        var result = new List<DueReminder>();
        if (toInclusive <= fromExclusive) return result;

        foreach (var item in items)
        {
            if (!item.ReminderEnabled || item.Status != ItemStatus.Pending) continue;

            var lead = item.MaxLeadMinutes();
            var windowStart = fromExclusive.AddMinutes(-lead - 1);
            var windowEnd = toInclusive.AddMinutes(lead + 1);

            // 绝对提醒时间与开始时间解耦，需要把首次发生纳入区间
            if (item.RemindAtOverride.HasValue)
            {
                if (item.Start < windowStart) windowStart = item.Start;
                if (item.Start > windowEnd) windowEnd = item.Start;
            }

            foreach (var occurrence in OccurrenceCalculator.ExpandWithOverrides(item, windowStart, windowEnd))
            {
                foreach (var plan in occurrence.Reminders)
                {
                    if (plan.FireAt <= fromExclusive || plan.FireAt > toInclusive) continue;
                    if (AlreadyFired(occurrence.Series, occurrence.Start, plan)) continue;

                    result.Add(new DueReminder
                    {
                        Item = occurrence.Item,
                        Occurrence = occurrence,
                        Plan = plan,
                    });
                }
            }
        }

        result.Sort(static (a, b) => a.FireAt.CompareTo(b.FireAt));
        return result;
    }

    /// <summary>取得下一次提醒（用于状态栏与托盘提示）。</summary>
    public static DueReminder? NextUpcoming(
        IEnumerable<ScheduleItem> items,
        DateTime now,
        int searchDays = 400)
    {
        DueReminder? best = null;

        foreach (var item in items)
        {
            if (!item.ReminderEnabled || item.Status != ItemStatus.Pending) continue;

            var lead = item.MaxLeadMinutes();
            var from = now.AddMinutes(-lead - 1);
            var until = now.AddDays(Math.Max(1, searchDays));

            if (item.RemindAtOverride.HasValue)
            {
                if (item.Start < from) from = item.Start;
                if (item.Start > until) until = item.Start;
            }

            foreach (var occurrence in OccurrenceCalculator.ExpandWithOverrides(item, from, until))
            {
                foreach (var plan in occurrence.Reminders)
                {
                    if (plan.FireAt <= now) continue;
                    if (AlreadyFired(occurrence.Series, occurrence.Start, plan)) continue;
                    if (best != null && plan.FireAt >= best.FireAt) continue;

                    best = new DueReminder
                    {
                        Item = occurrence.Item,
                        Occurrence = occurrence,
                        Plan = plan,
                    };
                }

                if (best != null && best.Occurrence.Start == occurrence.Start) break;
            }
        }

        return best;
    }

    /// <summary>当前时间之后最近的若干个提醒（用于「接下来」列表）。</summary>
    public static List<DueReminder> Upcoming(
        IEnumerable<ScheduleItem> items,
        DateTime now,
        int count = 5,
        int searchDays = 60)
    {
        var candidates = new List<DueReminder>();

        foreach (var item in items)
        {
            if (!item.ReminderEnabled || item.Status != ItemStatus.Pending) continue;

            var lead = item.MaxLeadMinutes();
            var from = now.AddMinutes(-lead - 1);
            var until = now.AddDays(Math.Max(1, searchDays));

            if (item.RemindAtOverride.HasValue && item.Start > until) until = item.Start;

            foreach (var occurrence in OccurrenceCalculator.ExpandWithOverrides(item, from, until))
            {
                foreach (var plan in occurrence.Reminders)
                {
                    if (plan.FireAt <= now) continue;
                    if (AlreadyFired(occurrence.Series, occurrence.Start, plan)) continue;

                    candidates.Add(new DueReminder { Item = occurrence.Item, Occurrence = occurrence, Plan = plan });
                }
            }
        }

        return candidates
            .OrderBy(d => d.FireAt)
            .Take(Math.Max(1, count))
            .ToList();
    }

    /// <summary>标记某个档位已经提醒过（写入的是系列本体，改期后的本次同样按新时间记录）。</summary>
    public static void MarkFired(ScheduleItem item, DueReminder due, DateTime? firedAt = null)
        => MarkFired(due.Occurrence.Series, due.Occurrence.Start, due.Plan, firedAt);

    public static void MarkFired(ScheduleItem item, DateTime occurrenceStart, ReminderPlan plan, DateTime? firedAt = null)
    {
        if (AlreadyFired(item, occurrenceStart, plan)) return;

        item.FiredReminders.Add(new ReminderFireRecord
        {
            OccurrenceStart = occurrenceStart,
            OffsetMinutes = plan.OffsetMinutes,
            FiredAt = firedAt ?? DateTime.Now,
        });

        item.PruneFiredReminders();
    }

    /// <summary>某个发生时间的某个档位是否已经提醒过。</summary>
    public static bool AlreadyFired(ScheduleItem item, DateTime occurrenceStart, ReminderPlan plan)
    {
        // 旧数据迁移前的兜底：LastFiredOccurrence 表示该次发生已提醒过
        if (item.LastFiredOccurrence.HasValue && item.LastFiredOccurrence.Value >= occurrenceStart) return true;

        foreach (var record in item.FiredReminders)
        {
            if (record.OccurrenceStart == occurrenceStart && record.OffsetMinutes == plan.OffsetMinutes) return true;
        }

        return false;
    }

    /// <summary>清除某次发生的全部提醒记录（例如用户改时间后重新提醒）。</summary>
    public static void ResetFired(ScheduleItem item)
    {
        item.FiredReminders.Clear();
        item.LastFiredOccurrence = null;
    }
}
