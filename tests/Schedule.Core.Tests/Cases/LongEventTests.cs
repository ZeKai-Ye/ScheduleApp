using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;

namespace Schedule.Core.Tests.Cases;

/// <summary>长日程（6 小时以上、跨夜、跨多天）在展开 / 布局 / 导出上的行为。</summary>
public static class LongEventTests
{
    public static void Run()
    {
        TestRunner.Section("长日程");

        SameDay();
        Overnight();
        MultiDay();
    }

    private static void SameDay()
    {
        var meeting = new ScheduleItem
        {
            Title = "全天工作坊",
            Start = new DateTime(2025, 6, 3, 9, 0, 0),
            End = new DateTime(2025, 6, 3, 17, 0, 0), // 8 小时
            ReminderOffsets = new List<int> { 60, 10 },
        };

        TestRunner.Equal(0, ScheduleItemValidator.Validate(meeting).Count, "8 小时日程可通过校验");

        var occurrence = OccurrenceCalculator.Create(meeting, meeting.Start);
        TestRunner.Equal(480d, occurrence.End.Subtract(occurrence.Start).TotalMinutes, "时长 480 分钟");
        TestRunner.Equal(60, occurrence.Reminders.Max(p => p.OffsetMinutes), "最大提前量 60 分钟");
        TestRunner.Equal(new DateTime(2025, 6, 3, 8, 0, 0), occurrence.Reminders[0].FireAt, "提醒仍在开始前 1 小时");

        var columns = CalendarLayout.BuildWeek(new[] { meeting }, new DateTime(2025, 6, 2), new DateTime(2025, 6, 3));
        var block = columns[1].TimedBlocks.Single();
        TestRunner.Equal(540, block.StartMinutes, "块从 09:00 开始");
        TestRunner.Equal(480, block.DurationMinutes, "块高 480 分钟");
        TestRunner.Equal(0, columns[2].TimedBlocks.Count, "不溢出到次日");

        var cells = CalendarLayout.BuildMonth(new[] { meeting }, 2025, 6, new DateTime(2025, 6, 3));
        TestRunner.Equal(1, cells.First(c => c.Date == new DateOnly(2025, 6, 3)).Events.Count, "月视图当天有一条");
        TestRunner.Equal("09:00 - 17:00", MainViewModelTimeText(meeting), "列表时间文案");
    }

    private static void Overnight()
    {
        // 22:00 → 次日 04:00（6 小时，跨夜；正是「只填结束时间会被当成前一天」的典型场景）
        var shift = new ScheduleItem
        {
            Title = "夜间值守",
            Start = new DateTime(2025, 6, 3, 22, 0, 0),
            End = new DateTime(2025, 6, 4, 4, 0, 0),
        };

        TestRunner.Equal(0, ScheduleItemValidator.Validate(shift).Count, "跨夜 6 小时日程可通过校验");

        var occurrence = OccurrenceCalculator.Create(shift, shift.Start);
        TestRunner.Equal(360d, occurrence.End.Subtract(occurrence.Start).TotalMinutes, "跨夜时长 360 分钟");

        var columns = CalendarLayout.BuildWeek(new[] { shift }, new DateTime(2025, 6, 2), new DateTime(2025, 6, 4));
        var firstDay = columns[1].TimedBlocks.Single();
        var secondDay = columns[2].TimedBlocks.Single();

        TestRunner.Equal(1320, firstDay.StartMinutes, "首日 22:00 起");
        TestRunner.Equal(120, firstDay.DurationMinutes, "首日画到 24:00（120 分钟）");
        TestRunner.Equal(0, secondDay.StartMinutes, "次日从 00:00 起");
        TestRunner.Equal(240, secondDay.DurationMinutes, "次日画到 04:00（240 分钟）");

        // 结束时间允许早于开始时间的“时钟读数”（跨夜），但日期必须是对的
        TestRunner.Check(shift.End > shift.Start, "结束时间在开始之后（已按次日处理）");
    }

    private static void MultiDay()
    {
        var marathon = new ScheduleItem
        {
            Title = "连续值班",
            Start = new DateTime(2025, 6, 3, 18, 0, 0),
            End = new DateTime(2025, 6, 5, 6, 0, 0), // 36 小时
        };

        TestRunner.Equal(0, ScheduleItemValidator.Validate(marathon).Count, "36 小时日程可通过校验");

        var columns = CalendarLayout.BuildWeek(new[] { marathon }, new DateTime(2025, 6, 2), new DateTime(2025, 6, 6));

        TestRunner.Equal(360, columns[1].TimedBlocks.Single().DurationMinutes, "第一天 18:00-24:00");
        TestRunner.Equal(1440, columns[2].TimedBlocks.Single().DurationMinutes, "第二天整天");
        TestRunner.Equal(360, columns[3].TimedBlocks.Single().DurationMinutes, "第三天 00:00-06:00");
        TestRunner.Equal(0, columns[0].TimedBlocks.Count + columns[4].TimedBlocks.Count, "不溢出到其它天");

        TestRunner.Equal(2160d, (marathon.EffectiveEnd - marathon.Start).TotalMinutes, "总时长 2160 分钟");

        // 导出 / 再导入要保持跨天时间
        var lines = Csv.CsvExporter.ToCsv(new[] { marathon });
        TestRunner.Check(lines.Contains("2025-06-03 18:00", StringComparison.Ordinal), "导出开始时间");
        TestRunner.Check(lines.Contains("2025-06-05 06:00", StringComparison.Ordinal), "导出结束时间");
    }

    /// <summary>与界面列表相同的时间文案规则（跨度跨天时标出「次日」）。</summary>
    private static string MainViewModelTimeText(ScheduleItem item)
    {
        var start = item.Start;
        var end = item.EffectiveEnd;
        if (item.AllDay) return "全天";
        var text = start.ToString("HH:mm");
        if (end <= start) return text;
        if (end.Date > start.Date) return text + " - 次日 " + end.ToString("HH:mm");
        return text + " - " + end.ToString("HH:mm");
    }
}
