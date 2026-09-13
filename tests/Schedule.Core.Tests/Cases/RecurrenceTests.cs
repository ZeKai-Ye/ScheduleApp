using Schedule.Core.Models;
using Schedule.Core.Scheduling;

namespace Schedule.Core.Tests.Cases;

public static class RecurrenceTests
{
    public static void Run()
    {
        TestRunner.Section("重复规则");

        var daily = new ScheduleItem { Title = "日报", Start = new DateTime(2025, 1, 1, 9, 0, 0), Repeat = RepeatKind.Daily };
        var dailyOccurrences = OccurrenceCalculator.Expand(daily, new DateTime(2025, 1, 1), new DateTime(2025, 1, 5, 23, 59, 0));
        TestRunner.Equal(5, dailyOccurrences.Count, "每天 5 天");
        TestRunner.Equal(new DateTime(2025, 1, 3, 9, 0, 0), dailyOccurrences[2].Start, "第三天时间");

        var weekly = new ScheduleItem { Title = "周会", Start = new DateTime(2025, 1, 6, 10, 0, 0), Repeat = RepeatKind.Weekly };
        var weeklyOccurrences = OccurrenceCalculator.Expand(weekly, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));
        TestRunner.Equal(4, weeklyOccurrences.Count, "每周次数");
        TestRunner.Equal(new DateTime(2025, 1, 27, 10, 0, 0), weeklyOccurrences[3].Start, "第四周时间");

        // 工作日：从周五开始，一周只应命中 5 次
        var weekdays = new ScheduleItem { Title = "站会", Start = new DateTime(2025, 1, 3, 9, 30, 0), Repeat = RepeatKind.Weekdays };
        var weekdayOccurrences = OccurrenceCalculator.Expand(weekdays, new DateTime(2025, 1, 3), new DateTime(2025, 1, 9, 23, 59, 0));
        TestRunner.Equal(5, weekdayOccurrences.Count, "工作日次数");
        TestRunner.Equal(DayOfWeek.Monday, weekdayOccurrences[1].Start.DayOfWeek, "跳过周末");
        TestRunner.Check(weekdayOccurrences.All(o => o.Start.DayOfWeek != DayOfWeek.Saturday && o.Start.DayOfWeek != DayOfWeek.Sunday), "工作日不含周末");

        // 月末：1 月 31 日按月重复应被压缩到 2 月 28 日
        var monthly = new ScheduleItem { Title = "月结", Start = new DateTime(2025, 1, 31, 17, 0, 0), Repeat = RepeatKind.Monthly };
        var monthlyOccurrences = OccurrenceCalculator.Expand(monthly, new DateTime(2025, 1, 1), new DateTime(2025, 4, 30, 23, 59, 0));
        TestRunner.Equal(4, monthlyOccurrences.Count, "每月次数");
        TestRunner.Equal(new DateTime(2025, 2, 28, 17, 0, 0), monthlyOccurrences[1].Start, "2 月末压缩");
        TestRunner.Equal(new DateTime(2025, 3, 31, 17, 0, 0), monthlyOccurrences[2].Start, "3 月回到 31 日");

        // 闰年 2 月 29 日按年重复
        var yearly = new ScheduleItem { Title = "纪念日", Start = new DateTime(2024, 2, 29, 8, 0, 0), Repeat = RepeatKind.Yearly };
        TestRunner.Equal(new DateTime(2025, 2, 28, 8, 0, 0), OccurrenceCalculator.OccurrenceAt(yearly, 1), "闰年压缩");

        // 重复截止
        var bounded = new ScheduleItem
        {
            Title = "限时活动",
            Start = new DateTime(2025, 1, 1, 12, 0, 0),
            Repeat = RepeatKind.Daily,
            RepeatUntil = new DateTime(2025, 1, 3),
        };
        var boundedOccurrences = OccurrenceCalculator.Expand(bounded, new DateTime(2025, 1, 1), new DateTime(2025, 2, 1));
        TestRunner.Equal(3, boundedOccurrences.Count, "重复截止次数");

        // 不重复
        var single = new ScheduleItem { Title = "一次性", Start = new DateTime(2025, 5, 1, 9, 0, 0) };
        TestRunner.Equal(1, OccurrenceCalculator.Expand(single, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)).Count, "不重复单次");
        TestRunner.Equal(0, OccurrenceCalculator.Expand(single, new DateTime(2025, 6, 1), new DateTime(2025, 12, 31)).Count, "不重复超出区间");

        // 已取消不产生实例
        var canceled = new ScheduleItem { Title = "取消", Start = new DateTime(2025, 1, 1, 9, 0, 0), Status = ItemStatus.Canceled };
        TestRunner.Equal(0, OccurrenceCalculator.Expand(canceled, new DateTime(2025, 1, 1), new DateTime(2025, 1, 2)).Count, "取消的日程不展开");

        // Next()
        var next = OccurrenceCalculator.Next(daily, new DateTime(2025, 6, 10, 12, 0, 0));
        TestRunner.NotNull(next, "Next 结果非空");
        TestRunner.Equal(new DateTime(2025, 6, 11, 9, 0, 0), next!.Start, "Next 时间");

        // 长期重复的展开性能（快速定位，不能线性空转）
        var ancient = new ScheduleItem { Title = "很久以前", Start = new DateTime(2000, 1, 1, 7, 0, 0), Repeat = RepeatKind.Daily };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var recent = OccurrenceCalculator.Expand(ancient, DateTime.Today, DateTime.Today.AddDays(7).AddHours(23));
        watch.Stop();
        TestRunner.Equal(8, recent.Count, "长期重复展开数量");
        TestRunner.Check(watch.ElapsedMilliseconds < 1000, "长期重复展开耗时（" + watch.ElapsedMilliseconds + " ms）");

        // 时长继承
        var withEnd = new ScheduleItem
        {
            Title = "会议",
            Start = new DateTime(2025, 1, 1, 9, 0, 0),
            End = new DateTime(2025, 1, 1, 10, 30, 0),
            Repeat = RepeatKind.Daily,
        };
        var withEndOccurrences = OccurrenceCalculator.Expand(withEnd, new DateTime(2025, 1, 1), new DateTime(2025, 1, 2, 23, 59, 0));
        TestRunner.Equal(new DateTime(2025, 1, 2, 10, 30, 0), withEndOccurrences[1].End, "重复实例继承时长");
    }
}
