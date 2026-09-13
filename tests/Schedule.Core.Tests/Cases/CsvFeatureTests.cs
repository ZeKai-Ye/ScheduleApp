using System.Text;
using Schedule.Core.Csv;
using Schedule.Core.Models;

namespace Schedule.Core.Tests.Cases;

/// <summary>CSV 新特性：多档提醒列、颜色列、周期列（间隔 / 星期 / 次数）。</summary>
public static class CsvFeatureTests
{
    public static void Run()
    {
        ReminderOffsets();
        RepeatDetails();
        EndToEnd();
    }

    private static void ReminderOffsets()
    {
        TestRunner.Section("CSV 多档提醒");

        var start = new DateTime(2025, 6, 3, 14, 0, 0);

        Offsets("30,15,5", start, new[] { 30, 15, 5 }, "逗号分隔");
        Offsets("30、15、5", start, new[] { 30, 15, 5 }, "顿号分隔");
        Offsets("提前30分钟、提前5分钟", start, new[] { 30, 5 }, "中文写法");
        Offsets("1小时;10", start, new[] { 60, 10 }, "分号分隔");
        Offsets("15", start, new[] { 15 }, "单档");
        Offsets("30|30|15", start, new[] { 30, 15 }, "去重");

        TestRunner.Check(FieldParsers.TryParseRemindOffsets("不提醒", start, out var disabled) && !disabled.Enabled, "关闭提醒");

        TestRunner.Check(
            FieldParsers.TryParseRemindOffsets("2025-06-03 08:00", start, out var absolute) && absolute.Absolute == new DateTime(2025, 6, 3, 8, 0, 0),
            "绝对时间");

        TestRunner.Check(
            FieldParsers.TryParseRemindOffsets("2025-06-03 08:00,10", start, out var mixed)
            && mixed.Absolute.HasValue && mixed.Offsets.Contains(10),
            "绝对时间与相对档位共存");

        TestRunner.Check(!FieldParsers.TryParseRemindOffsets("随便写点什么", start, out _), "无法识别返回 false");
        TestRunner.Check(!FieldParsers.TryParseRemindOffsets("", start, out _), "空值返回 false");
    }

    private static void Offsets(string text, DateTime start, int[] expected, string name)
    {
        if (!FieldParsers.TryParseRemindOffsets(text, start, out var spec))
        {
            TestRunner.Check(false, name + "（未识别）");
            return;
        }

        TestRunner.Equal(expected.Length, spec.Offsets.Count, name + " 档位数");
        for (var i = 0; i < expected.Length && i < spec.Offsets.Count; i++)
        {
            TestRunner.Equal(expected[i], spec.Offsets[i], name + " 第 " + (i + 1) + " 档");
        }
    }

    private static void RepeatDetails()
    {
        TestRunner.Section("CSV 周期规则");

        Repeat("每2周", RepeatKind.Weekly, 2, 0, null, "每 2 周");
        Repeat("每3天", RepeatKind.Daily, 3, 0, null, "每 3 天");
        Repeat("每隔2个月", RepeatKind.Monthly, 2, 0, null, "每 2 个月");
        Repeat("每两周", RepeatKind.Weekly, 2, 0, null, "中文数字「两」");
        Repeat("每天", RepeatKind.Daily, 1, 0, null, "每天");
        Repeat("工作日", RepeatKind.Weekdays, 1, 0, null, "工作日");
        Repeat("每周一、三、五", RepeatKind.Weekly, 1, 3, null, "每周多选星期");
        Repeat("每周二四", RepeatKind.Weekly, 1, 2, null, "每周二四");
        Repeat("每周共10次", RepeatKind.Weekly, 1, 0, 10, "重复次数");
        Repeat("每2周共6次", RepeatKind.Weekly, 2, 0, 6, "间隔 + 次数");
        Repeat("无", RepeatKind.None, 1, 0, null, "不重复");

        var withUntil = FieldParsers.TryParseRepeatDetail("每2天到2025-12-31", out var spec);
        TestRunner.Check(withUntil, "带截止日期的规则可解析");
        TestRunner.Equal(RepeatKind.Daily, spec.Kind, "截止规则类型");
        TestRunner.Equal(2, spec.Interval, "截止规则间隔");
        TestRunner.Check(spec.Until == new DateTime(2025, 12, 31), "截止日期解析");

        TestRunner.Check(FieldParsers.TryParseWeekdays("周一、三、五", out var weekdays) && weekdays.Count == 3, "星期列解析三种");
        TestRunner.Check(FieldParsers.TryParseWeekdays("周一、三、五", out var sorted)
            && sorted[0] == DayOfWeek.Monday && sorted[1] == DayOfWeek.Wednesday && sorted[2] == DayOfWeek.Friday, "星期顺序");
        TestRunner.Check(!FieldParsers.TryParseWeekdays("随便", out _), "无法识别的星期");
        FieldParsers.TryParseWeekdays("每周3次", out var none);
        TestRunner.Equal(0, none.Count, "「每周3次」不误判为周三");

        TestRunner.Check(!FieldParsers.TryParseRepeatDetail("随便写点什么", out _), "无法识别的重复规则");
    }

    private static void Repeat(string text, RepeatKind kind, int interval, int weekdayCount, int? count, string name)
    {
        if (!FieldParsers.TryParseRepeatDetail(text, out var spec))
        {
            TestRunner.Check(false, name + "（未识别）");
            return;
        }

        TestRunner.Equal(kind, spec.Kind, name + " 类型");
        TestRunner.Equal(interval, spec.Interval, name + " 间隔");
        TestRunner.Equal(weekdayCount, spec.Weekdays.Count, name + " 星期数");
        TestRunner.Equal(count, spec.Count, name + " 次数");
    }

    private static void EndToEnd()
    {
        TestRunner.Section("CSV 新特性端到端");

        const string content =
            "标题,开始时间,分类,颜色,重复,重复间隔,星期,重复次数,提前提醒\r\n" +
            "周会,2025-06-03 14:00,工作,红,每周,1,\"周一、三、五\",8,\"30,15,5\"\r\n" +
            "双周复盘,2025-06-05 10:00,工作,#7C3AED,每2周,,,15,\r\n" +
            "跑腿,2025-06-06,生活,灰色,不重复,,,,,\r\n";

        var file = Path.Combine(TestPaths.NewTempDir(), "features.csv");
        File.WriteAllText(file, content, new UTF8Encoding(false));

        var service = new CsvImportService();
        var target = new List<ScheduleItem>();
        var report = service.Import(new CsvImportOptions { FilePath = file }, target);

        TestRunner.Equal(3, report.Imported, "导入三行");
        TestRunner.Equal(0, report.Failed, "无失败行");

        var meeting = target[0];
        TestRunner.Equal("#DC2626", meeting.Color, "中文色名 → 十六进制");
        TestRunner.Equal(RepeatKind.Weekly, meeting.Repeat, "每周类型");
        TestRunner.Equal(1, meeting.RepeatInterval, "间隔 1");
        TestRunner.Equal(3, meeting.RepeatWeekdays.Count, "三个星期");
        TestRunner.Equal(8, meeting.RepeatCount!.Value, "重复 8 次");
        TestRunner.Equal(3, meeting.ReminderOffsets.Count, "三档提醒");
        TestRunner.Equal(30, meeting.ReminderOffsets[0], "第一档 30");
        TestRunner.Equal(5, meeting.ReminderOffsets[2], "第三档 5");
        TestRunner.Equal("30 / 15 / 5", string.Join(" / ", meeting.NormalizedOffsets().Select(Schedule.Core.Models.ReminderPlan.FormatOffset)).Replace("分钟", string.Empty), "档位归一化");

        var review = target[1];
        TestRunner.Equal(RepeatKind.Weekly, review.Repeat, "每 2 周类型");
        TestRunner.Equal(2, review.RepeatInterval, "每 2 周间隔");
        TestRunner.Equal("#7C3AED", review.Color, "十六进制颜色");
        TestRunner.Equal(1, review.ReminderOffsets.Count, "单档提醒");

        var errand = target[2];
        TestRunner.Equal(RepeatKind.None, errand.Repeat, "不重复");
        TestRunner.Equal("#64748B", errand.Color, "灰色");
        TestRunner.Equal(1, errand.ReminderOffsets.Count, "未填提醒时使用默认档位");
        TestRunner.Equal(10, errand.ReminderOffsets[0], "默认档位为 10 分钟");

        // 导出 → 重新导入，全部特性应保持一致
        var exportPath = Path.Combine(TestPaths.NewTempDir(), "features-export.csv");
        CsvExporter.WriteFile(exportPath, target);

        var roundTrip = new List<ScheduleItem>();
        var roundTripReport = service.Import(new CsvImportOptions { FilePath = exportPath }, roundTrip);
        TestRunner.Equal(3, roundTripReport.Imported, "往返导入行数");
        TestRunner.Equal(0, roundTripReport.Failed, "往返无失败");

        var roundTripMeeting = roundTrip.First(i => i.Title == "周会");
        TestRunner.Equal("#DC2626", roundTripMeeting.Color, "往返颜色");
        TestRunner.Equal(RepeatKind.Weekly, roundTripMeeting.Repeat, "往返重复类型");
        TestRunner.Equal(3, roundTripMeeting.RepeatWeekdays.Count, "往返星期数");
        TestRunner.Equal(8, roundTripMeeting.RepeatCount!.Value, "往返重复次数");
        TestRunner.Equal(3, roundTripMeeting.ReminderOffsets.Count, "往返提醒档位数");
        TestRunner.Equal(30, roundTripMeeting.ReminderOffsets[0], "往返第一档");

        var roundTripReview = roundTrip.First(i => i.Title == "双周复盘");
        TestRunner.Equal(2, roundTripReview.RepeatInterval, "往返每 2 周间隔");
        TestRunner.Equal("#7C3AED", roundTripReview.Color, "往返颜色 2");

        // 多档提醒应产生多个提醒时刻
        var plans = roundTripMeeting.ComputeReminders(roundTripMeeting.Start);
        TestRunner.Equal(3, plans.Count, "往返后仍是三档提醒");
        TestRunner.Equal(roundTripMeeting.Start.AddMinutes(-30), plans[0].FireAt, "第一档触发时刻");
    }
}
