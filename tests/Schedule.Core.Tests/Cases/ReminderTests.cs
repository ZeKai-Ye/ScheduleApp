using Schedule.Core.Models;
using Schedule.Core.Scheduling;

namespace Schedule.Core.Tests.Cases;

public static class ReminderTests
{
    public static void Run()
    {
        TestRunner.Section("提醒调度");

        var now = new DateTime(2025, 6, 1, 12, 0, 0);

        var item = new ScheduleItem
        {
            Title = "吃药",
            Start = now.AddMinutes(10),
            RemindBeforeMinutes = 10,
            ReminderEnabled = true,
        };

        var due = ReminderPlanner.CollectDue(new[] { item }, now.AddSeconds(-30), now);
        TestRunner.Equal(1, due.Count, "到点提醒被收集");
        TestRunner.Equal(now, due[0].ReminderAt, "提醒时刻");

        // 标记后不再重复
        ReminderPlanner.MarkFired(item, due[0]);
        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { item }, now.AddSeconds(-30), now).Count, "已提醒不重复");
        TestRunner.Equal(item.Start, item.LastFiredOccurrence!.Value, "已触发标记");

        // 尚未到点
        var futureItem = new ScheduleItem { Title = "未来", Start = now.AddHours(3), RemindBeforeMinutes = 10 };
        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { futureItem }, now.AddSeconds(-30), now).Count, "未到点不提醒");

        // 关闭提醒
        var disabled = new ScheduleItem { Title = "静音", Start = now.AddMinutes(10), RemindBeforeMinutes = 10, ReminderEnabled = false };
        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { disabled }, now.AddSeconds(-30), now).Count, "关闭提醒不触发");

        // 已完成
        var done = new ScheduleItem { Title = "已完成", Start = now.AddMinutes(10), RemindBeforeMinutes = 10, Status = ItemStatus.Done };
        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { done }, now.AddSeconds(-30), now).Count, "已完成不触发");

        // 提醒早于开始：提前 1 天
        // 提前两天：提醒时刻应正好落在窗口内（验证窗口会按提前量扩张）
        var dayBefore = new ScheduleItem { Title = "提前两天", Start = now.AddDays(2), RemindBeforeMinutes = 60 * 48 };
        var dayBeforeDue = ReminderPlanner.CollectDue(new[] { dayBefore }, now.AddSeconds(-30), now);
        TestRunner.Equal(1, dayBeforeDue.Count, "提前两天也能命中");

        // 绝对提醒时间
        var absolute = new ScheduleItem
        {
            Title = "绝对时间",
            Start = now.Date.AddHours(20),
            RemindAtOverride = now,
            RemindBeforeMinutes = 0,
        };
        TestRunner.Equal(1, ReminderPlanner.CollectDue(new[] { absolute }, now.AddSeconds(-30), now).Count, "绝对提醒时间命中");

        // 重复日程：窗口内只在正确的日子提醒
        var repeating = new ScheduleItem
        {
            Title = "每日站会",
            Start = now.Date.AddDays(1).AddHours(9),
            Repeat = RepeatKind.Daily,
            RemindBeforeMinutes = 0,
        };
        var repeatDue = ReminderPlanner.CollectDue(
            new[] { repeating },
            now.Date.AddDays(1).AddHours(8).AddMinutes(59),
            now.Date.AddDays(1).AddHours(9));
        TestRunner.Equal(1, repeatDue.Count, "重复日程提醒一次");

        // 下一次提醒
        var next = ReminderPlanner.NextUpcoming(new[] { item, futureItem }, now);
        TestRunner.NotNull(next, "下一次提醒非空");
        TestRunner.Equal(now.AddHours(3).AddMinutes(-10), next!.ReminderAt, "下一次提醒时刻");

        // 大规模数据不卡顿
        var many = new List<ScheduleItem>();
        for (var i = 0; i < 2000; i++)
        {
            many.Add(new ScheduleItem
            {
                Title = "项目 " + i,
                Start = now.AddDays(i % 30).AddMinutes(i % 600),
                Repeat = i % 3 == 0 ? RepeatKind.Daily : RepeatKind.None,
                RemindBeforeMinutes = i % 30,
            });
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var manyDue = ReminderPlanner.CollectDue(many, now.AddSeconds(-30), now);
        watch.Stop();
        TestRunner.Check(watch.ElapsedMilliseconds < 2000, "2000 条日程调度耗时（" + watch.ElapsedMilliseconds + " ms）");
        TestRunner.Check(manyDue.Count >= 0 && many.Count == 2000, "大规模调度完成");
    }
}
