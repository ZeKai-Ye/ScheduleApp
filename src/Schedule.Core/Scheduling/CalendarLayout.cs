using Schedule.Core.Models;

namespace Schedule.Core.Scheduling;

/// <summary>月视图中的一个日期格。</summary>
public sealed class MonthDayCell
{
    public DateOnly Date { get; init; }

    public bool IsCurrentMonth { get; init; }

    public bool IsToday { get; init; }

    /// <summary>当天的发生（全天在前，其后按开始时间升序）。</summary>
    public List<ScheduleOccurrence> Events { get; } = new();

    /// <summary>该格最多显示几条（由调用方按格子高度决定）。</summary>
    public int VisibleCount { get; set; }

    public int HiddenCount => Math.Max(0, Events.Count - VisibleCount);

    public string DayNumber => Date.Day.ToString();

    public bool HasEvents => Events.Count > 0;
}

/// <summary>周视图中的一个时间块。</summary>
public sealed class WeekEventBlock
{
    public required ScheduleOccurrence Occurrence { get; init; }

    /// <summary>距当天 0 点的分钟数。</summary>
    public required int StartMinutes { get; init; }

    public required int DurationMinutes { get; init; }

    /// <summary>同一时间重叠时的列号（0 起）。</summary>
    public required int Column { get; init; }

    /// <summary>该重叠簇的总列数。</summary>
    public required int ColumnCount { get; init; }

    public int EndMinutes => StartMinutes + DurationMinutes;
}

/// <summary>周视图中的一天。</summary>
public sealed class WeekDayColumn
{
    public DateOnly Date { get; init; }

    public bool IsToday { get; init; }

    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public List<ScheduleOccurrence> AllDayEvents { get; } = new();

    public List<WeekEventBlock> TimedBlocks { get; } = new();
}

/// <summary>
/// 日历布局引擎：把日程展开成周视图 / 月视图所需的网格数据。
/// 纯计算、不依赖任何 UI，便于测试。
/// </summary>
public static class CalendarLayout
{
    public const int MonthCellCount = 42;

    /// <summary>构建月视图网格（固定 6 周 × 7 天）。</summary>
    public static List<MonthDayCell> BuildMonth(
        IEnumerable<ScheduleItem> items,
        int year,
        int month,
        DateTime today,
        int cellCapacity = 3,
        bool weekStartsOnMonday = true)
    {
        var firstOfMonth = new DateTime(year, month, 1);
        var gridStart = DateHelpers.StartOfWeek(firstOfMonth, weekStartsOnMonday);
        var gridEnd = gridStart.AddDays(MonthCellCount);

        var cells = new List<MonthDayCell>(MonthCellCount);
        var index = new Dictionary<DateOnly, MonthDayCell>(MonthCellCount);

        for (var i = 0; i < MonthCellCount; i++)
        {
            var date = gridStart.AddDays(i);
            var cell = new MonthDayCell
            {
                Date = DateOnly.FromDateTime(date),
                IsCurrentMonth = date.Month == month && date.Year == year,
                IsToday = date.Date == today.Date,
            };

            cells.Add(cell);
            index[cell.Date] = cell;
        }

        var list = items as IList<ScheduleItem> ?? items.ToList();

        foreach (var occurrence in ExpandAll(list, gridStart, gridEnd))
        {
            var key = DateOnly.FromDateTime(occurrence.Start);
            if (!index.TryGetValue(key, out var cell)) continue;
            cell.Events.Add(occurrence);
        }

        foreach (var cell in cells)
        {
            cell.Events.Sort(CompareOccurrence);
            cell.VisibleCount = Math.Max(0, Math.Min(cellCapacity, cell.Events.Count));
        }

        return cells;
    }

    /// <summary>构建周视图（7 天，含全天事件与时间块重叠分列）。</summary>
    public static List<WeekDayColumn> BuildWeek(
        IEnumerable<ScheduleItem> items,
        DateTime weekStart,
        DateTime today,
        int minMinutes = 0,
        int maxMinutes = 24 * 60)
    {
        var start = weekStart.Date;
        var columns = new List<WeekDayColumn>(7);

        for (var i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            columns.Add(new WeekDayColumn
            {
                Date = DateOnly.FromDateTime(date),
                IsToday = date.Date == today.Date,
            });
        }

        var list = items as IList<ScheduleItem> ?? items.ToList();
        var occurrences = ExpandAll(list, start, start.AddDays(7));

        foreach (var occurrence in occurrences)
        {
            if (occurrence.Item.AllDay)
            {
                var day = DayIndex(start, occurrence.Start.Date);
                if (day >= 0 && day < 7) columns[day].AllDayEvents.Add(occurrence);
                continue;
            }

            var blockStart = occurrence.Start;
            var blockEnd = occurrence.End > occurrence.Start ? occurrence.End : occurrence.Start.AddMinutes(30);
            if (blockEnd > start.AddDays(7)) blockEnd = start.AddDays(7);

            // 跨天事件按天切成多块（结束时刻恰好为 0 点时属于前一天）
            var lastDay = blockEnd.AddTicks(-1).Date;
            for (var day = blockStart.Date; day <= lastDay; day = day.AddDays(1))
            {
                var dayStart = day;
                var dayEnd = day.AddDays(1);
                var from = blockStart > dayStart ? blockStart : dayStart;
                var to = blockEnd < dayEnd ? blockEnd : dayEnd;
                if (to <= from) continue;

                var columnIndex = DayIndex(start, day);
                if (columnIndex < 0 || columnIndex >= 7) continue;

                var startMinutes = (int)Math.Round((from - dayStart).TotalMinutes);
                var endMinutes = (int)Math.Round((to - dayStart).TotalMinutes);
                if (endMinutes <= minMinutes || startMinutes >= maxMinutes) continue;

                if (startMinutes < minMinutes) startMinutes = minMinutes;
                if (endMinutes > maxMinutes) endMinutes = maxMinutes;

                if (endMinutes - startMinutes < 15) endMinutes = startMinutes + 15;

                columns[columnIndex].TimedBlocks.Add(new WeekEventBlock
                {
                    Occurrence = occurrence,
                    StartMinutes = startMinutes,
                    DurationMinutes = endMinutes - startMinutes,
                    Column = 0,
                    ColumnCount = 1,
                });
            }
        }

        foreach (var column in columns)
        {
            column.AllDayEvents.Sort(CompareOccurrence);
            PackColumns(column.TimedBlocks);
        }

        return columns;
    }

    /// <summary>重叠分列：把同一时间段相互重叠的块分配到并排列上。</summary>
    private static void PackColumns(List<WeekEventBlock> blocks)
    {
        blocks.Sort(static (a, b) =>
        {
            var compare = a.StartMinutes.CompareTo(b.StartMinutes);
            return compare != 0 ? compare : b.DurationMinutes.CompareTo(a.DurationMinutes);
        });

        var clusterStart = 0;

        while (clusterStart < blocks.Count)
        {
            // 找出一个「重叠簇」：后续块只要与簇内任意块重叠就并入
            var clusterEnd = clusterStart;
            var clusterMaxEnd = blocks[clusterStart].EndMinutes;

            while (clusterEnd + 1 < blocks.Count && blocks[clusterEnd + 1].StartMinutes < clusterMaxEnd)
            {
                clusterEnd++;
                clusterMaxEnd = Math.Max(clusterMaxEnd, blocks[clusterEnd].EndMinutes);
            }

            var count = clusterEnd - clusterStart + 1;

            if (count == 1)
            {
                blocks[clusterStart] = Rebuild(blocks[clusterStart], 0, 1);
                clusterStart++;
                continue;
            }

            // 贪心分配列：每列记录当前结束时间
            var columnEnds = new List<int>();
            for (var i = clusterStart; i <= clusterEnd; i++)
            {
                var block = blocks[i];
                var placed = -1;

                for (var c = 0; c < columnEnds.Count; c++)
                {
                    if (columnEnds[c] <= block.StartMinutes)
                    {
                        placed = c;
                        columnEnds[c] = block.EndMinutes;
                        break;
                    }
                }

                if (placed < 0)
                {
                    columnEnds.Add(block.EndMinutes);
                    placed = columnEnds.Count - 1;
                }

                blocks[i] = Rebuild(block, placed, 0);
            }

            var total = columnEnds.Count;
            for (var i = clusterStart; i <= clusterEnd; i++)
            {
                blocks[i] = Rebuild(blocks[i], blocks[i].Column, total);
            }

            clusterStart = clusterEnd + 1;
        }
    }

    private static WeekEventBlock Rebuild(WeekEventBlock source, int column, int columnCount) => new()
    {
        Occurrence = source.Occurrence,
        StartMinutes = source.StartMinutes,
        DurationMinutes = source.DurationMinutes,
        Column = column,
        ColumnCount = columnCount,
    };

    private static int CompareOccurrence(ScheduleOccurrence a, ScheduleOccurrence b)
    {
        if (a.Item.AllDay != b.Item.AllDay) return a.Item.AllDay ? -1 : 1;

        var compare = a.Start.CompareTo(b.Start);
        return compare != 0 ? compare : string.CompareOrdinal(a.Item.Title, b.Item.Title);
    }

    private static int DayIndex(DateTime weekStart, DateTime date)
        => (int)(date.Date - weekStart.Date).TotalDays;

    private static IEnumerable<ScheduleOccurrence> ExpandAll(IList<ScheduleItem> items, DateTime from, DateTime to)
    {
        foreach (var item in items)
        {
            // 用 ExpandWithOverrides：被改期到本区间的「本次」也要画出来
            foreach (var occurrence in OccurrenceCalculator.ExpandWithOverrides(item, from, to))
            {
                yield return occurrence;
            }
        }
    }
}
