using Schedule.Core.Models;
using Schedule.Core.Scheduling;

namespace Schedule.Core.Tests.Cases;

/// <summary>周期事件的进阶能力：间隔、多星期、次数上限、截止日期。</summary>
public static class AdvancedRecurrenceTests
{
    public static void Run()
    {
        TestRunner.Section("周期事件进阶");

        // 每 2 天
        var everyTwoDays = new ScheduleItem
        {
            Title = "隔天跑步",
            Start = new DateTime(2025, 1, 1, 7, 0, 0),
            Repeat = RepeatKind.Daily,
            RepeatInterval = 2,
        };
        var twoDayOccurrences = OccurrenceCalculator.Expand(everyTwoDays, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31, 23, 59, 0));
        TestRunner.Equal(16, twoDayOccurrences.Count, "每 2 天：1 月共 16 次");
        TestRunner.Equal(new DateTime(2025, 1, 5, 7, 0, 0), twoDayOccurrences[2].Start, "每 2 天第三次");

        // 每 2 周
        var everyTwoWeeks = new ScheduleItem
        {
            Title = "双周会",
            Start = new DateTime(2025, 1, 6, 10, 0, 0),
            Repeat = RepeatKind.Weekly,
            RepeatInterval = 2,
        };
        var biweekly = OccurrenceCalculator.Expand(everyTwoWeeks, new DateTime(2025, 1, 1), new DateTime(2025, 2, 28, 23, 59, 0));
        TestRunner.Equal(4, biweekly.Count, "每 2 周：1/6、1/20、2/3、2/17");
        TestRunner.Equal(new DateTime(2025, 2, 17, 10, 0, 0), biweekly[3].Start, "每 2 周第四次");

        // 每周多选星期：周一 / 周三 / 周五
        var multiWeekday = new ScheduleItem
        {
            Title = "健身",
            Start = new DateTime(2025, 1, 6, 19, 0, 0),
            Repeat = RepeatKind.Weekly,
            RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
        };
        var week1 = OccurrenceCalculator.Expand(multiWeekday, new DateTime(2025, 1, 6), new DateTime(2025, 1, 12, 23, 59, 0));
        TestRunner.Equal(3, week1.Count, "一周三天");
        TestRunner.Equal(DayOfWeek.Monday, week1[0].Start.DayOfWeek, "第一个是周一");
        TestRunner.Equal(DayOfWeek.Wednesday, week1[1].Start.DayOfWeek, "第二个是周三");
        TestRunner.Equal(DayOfWeek.Friday, week1[2].Start.DayOfWeek, "第三个是周五");
        TestRunner.Equal(19, week1[1].Start.Hour, "继承开始时间");

        var twoWeeks = OccurrenceCalculator.Expand(multiWeekday, new DateTime(2025, 1, 6), new DateTime(2025, 1, 19, 23, 59, 0));
        TestRunner.Equal(6, twoWeeks.Count, "两周六天");

        // 每周多选星期 + 每 2 周
        var biweeklyMulti = new ScheduleItem
        {
            Title = "双周多天",
            Start = new DateTime(2025, 1, 6, 9, 0, 0),
            Repeat = RepeatKind.Weekly,
            RepeatInterval = 2,
            RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
        };
        var biweeklyMultiOccurrences = OccurrenceCalculator.Expand(biweeklyMulti, new DateTime(2025, 1, 1), new DateTime(2025, 2, 28, 23, 59, 0));
        TestRunner.Check(biweeklyMultiOccurrences.All(o => ((int)(o.Start.Date - new DateTime(2025, 1, 6)).TotalDays / 7) % 2 == 0), "双周多天落在偶数周");

        // 开始时间晚于当周部分星期时，不应产生「过去」的实例
        var startsFriday = new ScheduleItem
        {
            Title = "周五开始",
            Start = new DateTime(2025, 1, 10, 10, 0, 0),
            Repeat = RepeatKind.Weekly,
            RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday },
        };
        var afterStart = OccurrenceCalculator.Expand(startsFriday, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31, 23, 59, 0));
        TestRunner.Check(afterStart.All(o => o.Start >= startsFriday.Start), "不产生早于开始时间的实例");
        TestRunner.Equal(new DateTime(2025, 1, 13, 10, 0, 0), afterStart[0].Start, "首个实例是下周一");

        // 重复次数上限
        var limited = new ScheduleItem
        {
            Title = "共 3 次",
            Start = new DateTime(2025, 3, 1, 8, 0, 0),
            Repeat = RepeatKind.Daily,
            RepeatCount = 3,
        };
        var limitedOccurrences = OccurrenceCalculator.Expand(limited, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31, 23, 59, 0));
        TestRunner.Equal(3, limitedOccurrences.Count, "次数上限生效");
        TestRunner.Equal(new DateTime(2025, 3, 3, 8, 0, 0), limitedOccurrences[2].Start, "最后一次时间");

        // 次数上限 + 多星期
        var limitedWeekdays = new ScheduleItem
        {
            Title = "共 5 次",
            Start = new DateTime(2025, 1, 6, 9, 0, 0),
            Repeat = RepeatKind.Weekly,
            RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            RepeatCount = 5,
        };
        var limitedWeekdayOccurrences = OccurrenceCalculator.Expand(limitedWeekdays, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31, 23, 59, 0));
        TestRunner.Equal(5, limitedWeekdayOccurrences.Count, "多星期次数上限");
        TestRunner.Equal(new DateTime(2025, 1, 20, 9, 0, 0), limitedWeekdayOccurrences[4].Start, "第五次是 1/20 周一");

        // 次数上限 + 截止日期：谁先到用谁
        var countAndUntil = new ScheduleItem
        {
            Title = "双重限制",
            Start = new DateTime(2025, 1, 1, 8, 0, 0),
            Repeat = RepeatKind.Daily,
            RepeatCount = 10,
            RepeatUntil = new DateTime(2025, 1, 4),
        };
        var dual = OccurrenceCalculator.Expand(countAndUntil, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        TestRunner.Equal(4, dual.Count, "截止日期先到");

        // 每月间隔
        var quarterly = new ScheduleItem
        {
            Title = "季度复盘",
            Start = new DateTime(2025, 1, 15, 15, 0, 0),
            Repeat = RepeatKind.Monthly,
            RepeatInterval = 3,
        };
        var quarterlyOccurrences = OccurrenceCalculator.Expand(quarterly, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31, 23, 59, 0));
        TestRunner.Equal(4, quarterlyOccurrences.Count, "每季度一次");
        TestRunner.Equal(new DateTime(2025, 10, 15, 15, 0, 0), quarterlyOccurrences[3].Start, "第四个季度");

        // 长期 + 间隔仍需快速定位
        var ancientInterval = new ScheduleItem
        {
            Title = "很久以前每 3 天",
            Start = new DateTime(2000, 1, 1, 7, 0, 0),
            Repeat = RepeatKind.Daily,
            RepeatInterval = 3,
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var recent = OccurrenceCalculator.Expand(ancientInterval, DateTime.Today, DateTime.Today.AddDays(30));
        watch.Stop();
        TestRunner.Check(recent.Count >= 10 && recent.Count <= 11, "每 3 天 30 天内约 10-11 次（实际 " + recent.Count + "）");
        TestRunner.Check(watch.ElapsedMilliseconds < 1000, "长期间隔重复展开耗时（" + watch.ElapsedMilliseconds + " ms）");

        // 数据修复：非法的间隔与次数
        var broken = new ScheduleItem { Title = "脏数据", Repeat = RepeatKind.Daily, RepeatInterval = 0, RepeatCount = -3 };
        var notes = Schedule.Core.Services.ScheduleItemValidator.Normalize(broken);
        TestRunner.Equal(1, broken.RepeatInterval, "间隔修正为 1");
        TestRunner.Equal(null, broken.RepeatCount, "非法次数被清除");
        TestRunner.Check(notes.Count > 0, "记录了修复说明");
    }
}
