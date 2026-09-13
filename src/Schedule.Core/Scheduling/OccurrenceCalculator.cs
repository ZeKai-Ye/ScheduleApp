using Schedule.Core.Models;

namespace Schedule.Core.Scheduling;

/// <summary>
/// 把重复日程展开成具体发生（Occurrence）。
/// 支持：每天 / 工作日 / 每周（可多选星期）/ 每月 / 每年，
/// 以及「每 N 个单位」的间隔与「共 N 次」的次数上限。
/// </summary>
public static class OccurrenceCalculator
{
    /// <summary>单条日程在一次展开中的最大实例数，防止无限循环。</summary>
    public const int MaxOccurrencesPerItem = 5000;

    /// <summary>展开 [fromInclusive, toInclusive] 区间内的所有发生。</summary>
    public static List<ScheduleOccurrence> Expand(ScheduleItem item, DateTime fromInclusive, DateTime toInclusive)
    {
        var result = new List<ScheduleOccurrence>();
        if (toInclusive < fromInclusive) return result;
        if (item.Status == ItemStatus.Canceled) return result;

        foreach (var start in EnumerateStarts(item, fromInclusive, toInclusive))
        {
            result.Add(Create(item, start));
            if (result.Count >= MaxOccurrencesPerItem) break;
        }

        return result;
    }

    /// <summary>按日程的开始时间与结束时间生成一次具体发生（含全部提醒档位）。</summary>
    public static ScheduleOccurrence Create(ScheduleItem item, DateTime occurrenceStart)
    {
        var duration = item.EffectiveEnd - item.Start;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        var reminders = item.ComputeReminders(occurrenceStart);

        return new ScheduleOccurrence
        {
            Item = item,
            Start = occurrenceStart,
            End = occurrenceStart + duration,
            Reminders = reminders,
            ReminderAt = reminders.Count > 0 ? reminders[0].FireAt : occurrenceStart,
        };
    }

    /// <summary>取得 after 之后（含）的第一次发生。</summary>
    public static ScheduleOccurrence? Next(ScheduleItem item, DateTime after, int searchDays = 400)
    {
        if (item.Status == ItemStatus.Canceled) return null;

        var limit = after.AddDays(Math.Max(1, searchDays));
        foreach (var start in EnumerateStarts(item, after, limit))
        {
            return Create(item, start);
        }

        return null;
    }

    /// <summary>枚举区间内的所有开始时间（升序）。</summary>
    public static IEnumerable<DateTime> EnumerateStarts(ScheduleItem item, DateTime fromInclusive, DateTime toInclusive)
    {
        if (item.Repeat == RepeatKind.None)
        {
            if (item.Start >= fromInclusive && item.Start <= toInclusive) yield return item.Start;
            yield break;
        }

        var interval = Math.Max(1, item.RepeatInterval);
        var limitedByCount = item.RepeatCount is > 0;
        var produced = 0;
        var guard = 0;

        // 每周 + 多选星期：按「周」推进，一周内可产生多个实例
        var weekdays = item.Repeat == RepeatKind.Weekly && item.RepeatWeekdays is { Count: > 0 }
            ? item.RepeatWeekdays.Distinct().OrderBy(DateHelpers.MondayBasedIndex).ToList()
            : null;

        if (weekdays != null)
        {
            var anchorWeek = DateHelpers.StartOfWeek(item.Start);
            var weekIndex = 0;

            if (!limitedByCount && fromInclusive.Date > anchorWeek)
            {
                var elapsedWeeks = (int)((DateHelpers.StartOfWeek(fromInclusive).Date - anchorWeek).TotalDays / 7);
                weekIndex = Math.Max(0, (elapsedWeeks / interval) - 1);
            }

            while (guard++ < MaxOccurrencesPerItem)
            {
                var weekStart = anchorWeek.AddDays(7.0 * interval * weekIndex);

                foreach (var dayOfWeek in weekdays)
                {
                    var occurrence = weekStart.AddDays(DateHelpers.MondayBasedIndex(dayOfWeek)).Add(item.Start.TimeOfDay);
                    if (occurrence < item.Start) continue;

                    if (limitedByCount && ++produced > item.RepeatCount!.Value) yield break;
                    if (item.RepeatUntil.HasValue && occurrence.Date > item.RepeatUntil.Value.Date) yield break;
                    if (occurrence > toInclusive) yield break;

                    if (occurrence >= fromInclusive) yield return occurrence;
                }

                weekIndex++;
            }

            yield break;
        }

        var index = limitedByCount ? 0 : EstimateStartIndex(item, fromInclusive);

        while (guard++ < MaxOccurrencesPerItem)
        {
            var occurrence = OccurrenceAt(item, index);

            if (occurrence > toInclusive) yield break;
            if (item.RepeatUntil.HasValue && occurrence.Date > item.RepeatUntil.Value.Date) yield break;
            if (limitedByCount && ++produced > item.RepeatCount!.Value) yield break;

            if (occurrence >= fromInclusive) yield return occurrence;
            index++;
        }
    }

    /// <summary>第 index 次发生的开始时间（index 从 0 开始，即基准时间）。</summary>
    public static DateTime OccurrenceAt(ScheduleItem item, int index)
    {
        if (index <= 0) return item.Start;

        var interval = Math.Max(1, item.RepeatInterval);

        switch (item.Repeat)
        {
            case RepeatKind.Daily:
                return item.Start.AddDays((double)index * interval);

            case RepeatKind.Weekdays:
            {
                var cursor = item.Start;
                var remaining = index;
                var guard = 0;

                while (guard++ <= MaxOccurrencesPerItem * 2)
                {
                    if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    {
                        if (remaining == 0) return cursor;
                        remaining--;
                    }

                    cursor = cursor.AddDays(1);
                }

                return cursor;
            }

            case RepeatKind.Weekly:
                return item.Start.AddDays(7.0 * index * interval);

            case RepeatKind.Monthly:
                return item.Start.AddMonths(index * interval);

            case RepeatKind.Yearly:
                return item.Start.AddYears(index * interval);

            default:
                return item.Start;
        }
    }

    /// <summary>估算第一个 >= from 的实例下标，避免从很久以前开始空转。</summary>
    private static int EstimateStartIndex(ScheduleItem item, DateTime from)
    {
        if (from <= item.Start) return 0;

        var interval = Math.Max(1, item.RepeatInterval);
        var days = (from.Date - item.Start.Date).TotalDays;

        var estimate = item.Repeat switch
        {
            RepeatKind.Daily => (int)Math.Floor(days / interval),
            RepeatKind.Weekdays => (int)Math.Floor(days * 5.0 / 7.0) - 2,
            RepeatKind.Weekly => (int)Math.Floor(days / (7.0 * interval)) - 1,
            RepeatKind.Monthly => (MonthsBetween(item.Start, from) / interval) - 1,
            RepeatKind.Yearly => ((from.Year - item.Start.Year) / interval) - 1,
            _ => 0,
        };

        return Math.Max(0, estimate);
    }

    private static int MonthsBetween(DateTime from, DateTime to)
        => ((to.Year - from.Year) * 12) + (to.Month - from.Month);
}
