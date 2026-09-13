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
        TestRunner.Equal(now, due[0].FireAt, "提醒时刻");

        // 标记后不再重复
        ReminderPlanner.MarkFired(item, due[0]);
        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { item }, now.AddSeconds(-30), now).Count, "已提醒不重复");
        TestRunner.Equal(1, item.FiredReminders.Count, "已生成触发记录");
        TestRunner.Equal(item.Start, item.FiredReminders[0].OccurrenceStart, "触发记录对应发生时间");
        TestRunner.Equal(10, item.FiredReminders[0].OffsetMinutes, "触发记录对应档位");

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
        TestRunner.Equal(now.AddHours(3).AddMinutes(-10), next!.FireAt, "下一次提醒时刻");

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

        MultiOffset();
    }

    /// <summary>多档提醒：30 / 15 / 5 分钟前各提醒一次，互不干扰。</summary>
    private static void MultiOffset()
    {
        TestRunner.Section("多档提醒");

        var now = new DateTime(2025, 6, 1, 12, 0, 0);
        var item = new ScheduleItem
        {
            Title = "多档提醒",
            Start = now.AddHours(1),
            ReminderOffsets = new List<int> { 30, 15, 5 },
        };

        var plans = item.ComputeReminders(item.Start);
        TestRunner.Equal(3, plans.Count, "三个档位");
        TestRunner.Equal(30, plans[0].OffsetMinutes, "第一档 30 分钟");
        TestRunner.Equal(now.AddMinutes(30), plans[0].FireAt, "第一档触发时刻");
        TestRunner.Equal(now.AddMinutes(45), plans[1].FireAt, "第二档触发时刻");
        TestRunner.Equal(now.AddMinutes(55), plans[2].FireAt, "第三档触发时刻");
        TestRunner.Equal("提前 30 分钟", plans[0].OffsetText, "档位文案");

        // 第一档触发后，第二档仍应正常触发
        var due30 = ReminderPlanner.CollectDue(new[] { item }, now.AddMinutes(29), now.AddMinutes(30));
        TestRunner.Equal(1, due30.Count, "第一档到点");
        TestRunner.Equal(30, due30[0].Plan.OffsetMinutes, "第一档档位值");
        ReminderPlanner.MarkFired(item, due30[0]);

        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { item }, now.AddMinutes(29), now.AddMinutes(30)).Count, "第一档不重复");

        var due15 = ReminderPlanner.CollectDue(new[] { item }, now.AddMinutes(44), now.AddMinutes(45));
        TestRunner.Equal(1, due15.Count, "第二档到点");
        TestRunner.Equal(15, due15[0].Plan.OffsetMinutes, "第二档档位值");
        ReminderPlanner.MarkFired(item, due15[0]);

        var due5 = ReminderPlanner.CollectDue(new[] { item }, now.AddMinutes(54), now.AddMinutes(55));
        TestRunner.Equal(1, due5.Count, "第三档到点");
        TestRunner.Equal(5, due5[0].Plan.OffsetMinutes, "第三档档位值");
        ReminderPlanner.MarkFired(item, due5[0]);

        TestRunner.Equal(0, ReminderPlanner.CollectDue(new[] { item }, now.AddMinutes(29), now.AddMinutes(60)).Count, "全部触发后不再提醒");
        TestRunner.Equal(3, item.FiredReminders.Count, "触发记录数");

        // 准点档位（0 分钟）
        var onTime = new ScheduleItem { Title = "准点", Start = now.AddMinutes(1), ReminderOffsets = new List<int> { 0 } };
        var onTimeDue = ReminderPlanner.CollectDue(new[] { onTime }, now, now.AddMinutes(1));
        TestRunner.Equal(1, onTimeDue.Count, "准点档位");
        TestRunner.Equal(0, onTimeDue[0].Plan.OffsetMinutes, "准点档位值");

        // 绝对时间 + 相对档位可以共存
        var mixed = new ScheduleItem
        {
            Title = "混合",
            Start = now.AddHours(2),
            ReminderOffsets = new List<int> { 10 },
            RemindAtOverride = now.AddMinutes(5),
        };
        var mixedPlans = mixed.ComputeReminders(mixed.Start);
        TestRunner.Equal(2, mixedPlans.Count, "绝对 + 相对共两个档位");
        TestRunner.Equal(now.AddMinutes(5), mixedPlans[0].FireAt, "绝对时间最先触发");

        // 只配置绝对时间时不应凭空多出一个相对档位
        var absoluteOnly = new ScheduleItem
        {
            Title = "仅绝对",
            Start = now.AddHours(2),
            RemindBeforeMinutes = 10,
            RemindAtOverride = now.AddMinutes(20),
        };

        absoluteOnly.ReminderOffsets.Clear();
        var absolutePlans = absoluteOnly.ComputeReminders(absoluteOnly.Start);
        TestRunner.Equal(1, absolutePlans.Count, "仅绝对时间只有一个档位");
        TestRunner.Equal(now.AddMinutes(20), absolutePlans[0].FireAt, "绝对时间触发时刻");
        TestRunner.Check(absolutePlans[0].IsAbsolute, "标记为绝对时间");
        TestRunner.Equal(0, absoluteOnly.NormalizedOffsets().Count, "绝对时间不产生相对档位");
    }
}
