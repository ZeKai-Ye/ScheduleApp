using Schedule.Core.Models;
using Schedule.Core.Scheduling;

namespace Schedule.Core.Tests.Cases;

/// <summary>日历布局引擎（周视图 / 月视图）。</summary>
public static class CalendarLayoutTests
{
    public static void Run()
    {
        MonthView();
        WeekView();
    }

    private static void MonthView()
    {
        TestRunner.Section("月视图布局");

        var today = new DateTime(2025, 6, 15, 9, 0, 0);
        var items = new List<ScheduleItem>
        {
            new() { Title = "6 月 15 日会议", Start = new DateTime(2025, 6, 15, 14, 0, 0), End = new DateTime(2025, 6, 15, 15, 0, 0), Category = "工作" },
            new() { Title = "6 月 15 日全天", Start = new DateTime(2025, 6, 15), AllDay = true, Category = "生活" },
            new() { Title = "上月末", Start = new DateTime(2025, 5, 28, 10, 0, 0) },
            new() { Title = "下月初", Start = new DateTime(2025, 7, 2, 10, 0, 0) },
            new() { Title = "每日站会", Start = new DateTime(2025, 5, 1, 9, 30, 0), Repeat = RepeatKind.Daily },
        };

        var cells = CalendarLayout.BuildMonth(items, 2025, 6, today, cellCapacity: 2);

        TestRunner.Equal(42, cells.Count, "固定 6 周 42 格");
        TestRunner.Equal(new DateOnly(2025, 5, 26), cells[0].Date, "网格从 5/26 周一开始");
        TestRunner.Equal(new DateOnly(2025, 7, 6), cells[41].Date, "最后一格是 7/6");
        TestRunner.Check(!cells[0].IsCurrentMonth, "5/26 不属于本月");
        TestRunner.Check(cells[6].IsCurrentMonth, "6/1 属于本月");
        TestRunner.Equal(new DateOnly(2025, 6, 1), cells[6].Date, "第 7 格是 6/1");
        TestRunner.Check(cells[20].IsToday, "6/15 标记为今天");
        TestRunner.Equal(new DateOnly(2025, 6, 15), cells[20].Date, "第 21 格是 6/15");

        var day15 = cells[20];
        TestRunner.Equal(3, day15.Events.Count, "6/15 有 3 个发生（全天 + 会议 + 每日站会）");
        TestRunner.Check(day15.Events[0].Item.AllDay, "全天事件排在最前");
        TestRunner.Equal("每日站会", day15.Events[1].Item.Title, "随后按开始时间排序（09:30）");
        TestRunner.Equal("6 月 15 日会议", day15.Events[2].Item.Title, "再是 14:00 的会议");
        TestRunner.Equal(2, day15.VisibleCount, "容量 2 只显示 2 条");
        TestRunner.Equal(1, day15.HiddenCount, "隐藏 1 条");

        var may28 = cells.First(c => c.Date == new DateOnly(2025, 5, 28));
        TestRunner.Check(may28.Events.Any(e => e.Item.Title == "上月末"), "上月事件落在正确格");
        TestRunner.Check(!may28.IsCurrentMonth, "上月格标记为非本月");

        var july2 = cells.First(c => c.Date == new DateOnly(2025, 7, 2));
        TestRunner.Check(july2.Events.Any(e => e.Item.Title == "下月初"), "下月事件落在正确格");

        // 重复日程应在月内每天都出现
        var repeatCells = cells.Count(c => c.Events.Any(e => e.Item.Title == "每日站会"));
        TestRunner.Equal(42, repeatCells, "每日重复填满 42 格");

        // 周日起始的日历
        var sundayFirst = CalendarLayout.BuildMonth(items, 2025, 6, today, 3, weekStartsOnMonday: false);
        TestRunner.Equal(new DateOnly(2025, 6, 1), sundayFirst[0].Date, "周日起始时首格是 6/1");
    }

    private static void WeekView()
    {
        TestRunner.Section("周视图布局");

        var today = new DateTime(2025, 6, 4, 12, 0, 0);
        var weekStart = new DateTime(2025, 6, 2); // 周一

        var items = new List<ScheduleItem>
        {
            new() { Title = "上午会议", Start = new DateTime(2025, 6, 3, 9, 0, 0), End = new DateTime(2025, 6, 3, 10, 30, 0), Category = "工作" },
            new() { Title = "重叠 A", Start = new DateTime(2025, 6, 4, 9, 0, 0), End = new DateTime(2025, 6, 4, 10, 0, 0) },
            new() { Title = "重叠 B", Start = new DateTime(2025, 6, 4, 9, 30, 0), End = new DateTime(2025, 6, 4, 10, 30, 0) },
            new() { Title = "不重叠 C", Start = new DateTime(2025, 6, 4, 10, 0, 0), End = new DateTime(2025, 6, 4, 11, 0, 0) },
            new() { Title = "全天事件", Start = new DateTime(2025, 6, 5), AllDay = true },
            new() { Title = "跨夜", Start = new DateTime(2025, 6, 6, 22, 0, 0), End = new DateTime(2025, 6, 7, 2, 0, 0) },
        };

        var columns = CalendarLayout.BuildWeek(items, weekStart, today, 0, 24 * 60);

        TestRunner.Equal(7, columns.Count, "一周 7 列");
        TestRunner.Equal(new DateOnly(2025, 6, 2), columns[0].Date, "首列是周一");
        TestRunner.Equal(new DateOnly(2025, 6, 8), columns[6].Date, "末列是周日");
        TestRunner.Check(columns[2].IsToday, "6/4 标记为今天");
        TestRunner.Check(columns[5].IsWeekend && columns[6].IsWeekend, "周六周日标记");

        var tuesday = columns[1];
        TestRunner.Equal(1, tuesday.TimedBlocks.Count, "周二 1 个时间块");
        TestRunner.Equal(540, tuesday.TimedBlocks[0].StartMinutes, "09:00 = 540 分钟");
        TestRunner.Equal(90, tuesday.TimedBlocks[0].DurationMinutes, "时长 90 分钟");

        var wednesday = columns[2];
        TestRunner.Equal(3, wednesday.TimedBlocks.Count, "周三 3 个时间块");

        var overlapA = wednesday.TimedBlocks.First(b => b.Occurrence.Item.Title == "重叠 A");
        var overlapB = wednesday.TimedBlocks.First(b => b.Occurrence.Item.Title == "重叠 B");
        var noOverlap = wednesday.TimedBlocks.First(b => b.Occurrence.Item.Title == "不重叠 C");

        TestRunner.Equal(2, overlapA.ColumnCount, "重叠块分两列");
        TestRunner.Equal(2, overlapB.ColumnCount, "重叠块分两列");
        TestRunner.Equal(0, overlapA.Column, "先到的排第 0 列");
        TestRunner.Equal(1, overlapB.Column, "后到的排第 1 列");
        // 传递性重叠簇（A-B-C 链）内共享列宽：C 与 B 重叠，因此仍按两列布版
        TestRunner.Equal(2, noOverlap.ColumnCount, "同簇内共享列宽");
        TestRunner.Equal(0, noOverlap.Column, "不重叠块回到第 0 列");

        var thursday = columns[3];
        TestRunner.Equal(1, thursday.AllDayEvents.Count, "全天事件单独归类");
        TestRunner.Equal(0, thursday.TimedBlocks.Count, "全天事件不进入时间块");

        // 跨夜事件按天切块
        var friday = columns[4];
        var saturday = columns[5];
        TestRunner.Equal(1, friday.TimedBlocks.Count, "跨夜事件在周五有块");
        TestRunner.Equal(1320, friday.TimedBlocks[0].StartMinutes, "周五 22:00 = 1320");
        TestRunner.Equal(120, friday.TimedBlocks[0].DurationMinutes, "周五块到 24:00");
        TestRunner.Equal(1, saturday.TimedBlocks.Count, "跨夜事件在周六有块");
        TestRunner.Equal(0, saturday.TimedBlocks[0].StartMinutes, "周六从 00:00 开始");
        TestRunner.Equal(120, saturday.TimedBlocks[0].DurationMinutes, "周六块到 02:00");

        // 可见时间范围过滤：只看 6:00 - 23:00
        var windowed = CalendarLayout.BuildWeek(items, weekStart, today, 6 * 60, 23 * 60);
        TestRunner.Equal(1, windowed[1].TimedBlocks.Count, "窗口内保留 09:00 会议");
        var windowedFriday = windowed[4];
        TestRunner.Check(windowedFriday.TimedBlocks.Count == 1 && windowedFriday.TimedBlocks[0].DurationMinutes == 60,
            "22:00 的块被裁剪到 23:00");

        // 已取消的日程不进入日历
        var canceled = new List<ScheduleItem>
        {
            new() { Title = "取消的会议", Start = new DateTime(2025, 6, 3, 9, 0, 0), Status = ItemStatus.Canceled },
        };
        var canceledColumns = CalendarLayout.BuildWeek(canceled, weekStart, today);
        TestRunner.Equal(0, canceledColumns.Sum(c => c.TimedBlocks.Count), "取消的日程不显示");
    }
}
