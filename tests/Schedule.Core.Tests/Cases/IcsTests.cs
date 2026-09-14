using System.Text;
using Schedule.Core.Ics;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;

namespace Schedule.Core.Tests.Cases;

/// <summary>
/// iCalendar（.ics）导入：Outlook / Google 常见写法、全天、DURATION、RRULE、
/// EXDATE、RECURRENCE-ID、VALARM、折行与转义，以及"不认识的东西必须变成提示"。
/// </summary>
public static class IcsTests
{
    public static void Run()
    {
        TestRunner.Section("ICS 导入");
        Primitives();
        Outlook();
        Google();
        AllDayAndDuration();
        Rules();
        Exceptions();
        Messy();
        ServiceImport();
        RoundTrip();
    }

    private static void Primitives()
    {
        TestRunner.Equal("第一行\n第二行", IcsText.Unescape("第一行\\n第二行"), "反转义换行");
        TestRunner.Equal("甲,乙;丙", IcsText.Unescape("甲\\,乙\\;丙"), "反转义逗号与分号");
        TestRunner.Equal("C:\\path", IcsText.Unescape("C:\\\\path"), "反转义反斜杠");
        TestRunner.Equal("甲\\,乙", IcsText.Escape("甲,乙"), "转义逗号");
        TestRunner.Equal("A\\nB", IcsText.Escape("A\nB"), "转义换行");
        TestRunner.Equal("第一行\n第二行", IcsText.Unescape(IcsText.Escape("第一行\n第二行")), "转义再反转义回到原文");

        var folded = IcsText.Fold(new string('x', 200));
        TestRunner.Check(folded.Contains("\r\n "), "长行会被折断且续行以空格开头");
        TestRunner.Equal(200, folded.Replace("\r\n ", string.Empty).Length, "折断不丢字符");

        TestRunner.Equal(TimeSpan.FromMinutes(30), IcsReader.ParseDuration("PT30M", out var negative), "解析 PT30M");
        TestRunner.Check(!negative, "PT30M 不带负号");
        TestRunner.Equal(TimeSpan.FromMinutes(-90), IcsReader.ParseDuration("-PT1H30M", out var negative2), "解析 -PT1H30M");
        TestRunner.Check(negative2, "负号被识别");
        TestRunner.Equal(TimeSpan.FromDays(1), IcsReader.ParseDuration("P1D", out _), "解析 P1D");
        TestRunner.Equal(TimeSpan.FromMinutes(90), IcsReader.ParseDuration("PT1H30M", out _), "解析 PT1H30M");
        TestRunner.Equal(TimeSpan.Zero, IcsReader.ParseDuration("随便写的", out _), "无法解析返回 Zero");

        var dateOnly = new IcsProperty { Name = "DTSTART", Value = "20260915" };
        TestRunner.Check(IcsReader.TryParseDate(dateOnly, out var date, out var isDate, out _), "解析纯日期");
        TestRunner.Check(isDate, "识别为全天日期");
        TestRunner.Equal(new DateTime(2026, 9, 15), date, "纯日期取值");

        var local = new IcsProperty { Name = "DTSTART", Value = "20260915T150000" };
        TestRunner.Check(IcsReader.TryParseDate(local, out var localValue, out var localIsDate, out var localIsUtc), "解析本地时间");
        TestRunner.Check(!localIsDate && !localIsUtc, "本地时间既不是纯日期也不是 UTC");
        TestRunner.Equal(new DateTime(2026, 9, 15, 15, 0, 0), localValue, "本地时间取值");

        var utc = new IcsProperty { Name = "DTSTART", Value = "20260915T070000Z" };
        TestRunner.Check(IcsReader.TryParseDate(utc, out var utcValue, out _, out var isUtc), "解析 UTC 时间");
        TestRunner.Check(isUtc, "识别为 UTC");
        TestRunner.Equal(
            DateTime.SpecifyKind(new DateTime(2026, 9, 15, 7, 0, 0), DateTimeKind.Utc).ToLocalTime(),
            IcsReader.ToLocal(utcValue, isUtc),
            "UTC 时间换算成本地时间");

        TestRunner.Check(!IcsReader.TryParseDate(new IcsProperty { Name = "DTSTART", Value = "???" }, out _, out _, out _), "无法解析的时间返回 false");
        TestRunner.Check(!IcsReader.TryParseDate(null, out _, out _, out _), "空属性返回 false");
    }

    private static void Outlook()
    {
        const string sample = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Microsoft Corporation//Outlook 16.0 MIMEDIR//EN
        BEGIN:VEVENT
        UID:040000008200E00074C5B7101A82E00800000000
        SUMMARY:项目启动会
        DESCRIPTION:讨论排期\n记得带笔记本
        LOCATION:三楼会议室
        DTSTART:20260915T070000Z
        DTEND:20260915T083000Z
        STATUS:CONFIRMED
        PRIORITY:2
        BEGIN:VALARM
        ACTION:DISPLAY
        DESCRIPTION:提醒
        TRIGGER:-PT30M
        END:VALARM
        BEGIN:VALARM
        ACTION:DISPLAY
        DESCRIPTION:提醒
        TRIGGER:-PT5M
        END:VALARM
        END:VEVENT
        END:VCALENDAR
        """;

        var parsed = IcsReader.Parse(sample);
        TestRunner.Check(parsed.LooksLikeCalendar, "识别为 iCalendar");
        TestRunner.Equal(1, parsed.Events.Count, "解析出 1 个事件");

        var items = Map(parsed, out var warnings, out var errors);
        TestRunner.Equal(1, items.Count, "映射出 1 条日程");
        TestRunner.Equal(0, errors.Count, "没有错误");
        TestRunner.Equal(0, warnings.Count, "没有警告");

        var item = items[0];
        TestRunner.Equal("项目启动会", item.Title, "标题");
        TestRunner.Equal(
            DateTime.SpecifyKind(new DateTime(2026, 9, 15, 7, 0, 0), DateTimeKind.Utc).ToLocalTime(),
            item.Start,
            "UTC 开始时间已换算成本地时间");
        TestRunner.Check(item.End.HasValue, "有结束时间");
        TestRunner.Equal(TimeSpan.FromMinutes(90), item.End!.Value - item.Start, "时长 90 分钟");
        TestRunner.Check(item.Notes.Contains("讨论排期", StringComparison.Ordinal), "备注含描述");
        TestRunner.Check(item.Notes.Contains("三楼会议室", StringComparison.Ordinal), "地点并入备注");
        TestRunner.Equal(Priority.High, item.Priority, "PRIORITY:2 → 高");
        TestRunner.Equal(ItemStatus.Pending, item.Status, "CONFIRMED → 待办");
        TestRunner.Check(item.ReminderOffsets.SequenceEqual(new[] { 30, 5 }), "两个 VALARM 变成两档提醒");
        TestRunner.Check(item.ReminderEnabled, "提醒已启用");
        TestRunner.Equal(ItemOrigin.IcsImport, item.Origin, "来源标记为 ICS");
    }

    private static void Google()
    {
        const string sample = """
        BEGIN:VCALENDAR
        PRODID:-//Google Inc//Google Calendar 70.9054//EN
        VERSION:2.0
        CALSCALE:GREGORIAN
        BEGIN:VEVENT
        DTSTART;TZID=Asia/Shanghai:20260916T093000
        DTEND;TZID=Asia/Shanghai:20260916T094500
        RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR;COUNT=9
        EXDATE;TZID=Asia/Shanghai:20260918T093000,20260923T093000
        SUMMARY:站会
        CATEGORIES:工作,例会
        STATUS:CONFIRMED
        END:VEVENT
        END:VCALENDAR
        """;

        var items = Map(IcsReader.Parse(sample), out var warnings, out var errors);
        TestRunner.Equal(1, items.Count, "映射出 1 条日程");
        TestRunner.Equal(0, errors.Count, "没有错误");

        var item = items[0];
        TestRunner.Equal(new DateTime(2026, 9, 16, 9, 30, 0), item.Start, "TZID 时间按本地墙上时间处理");
        TestRunner.Equal("工作", item.Category, "多个分类取第一个");
        TestRunner.Check(warnings.Any(w => w.Contains("多个分类", StringComparison.Ordinal)), "提示有多个分类");

        TestRunner.Equal(RepeatKind.Weekly, item.Repeat, "每周重复");
        TestRunner.Equal(2, item.RepeatInterval, "间隔 2 周");
        TestRunner.Equal(3, item.RepeatWeekdays.Count, "周一 / 周三 / 周五");
        TestRunner.Check(item.RepeatWeekdays.Contains(DayOfWeek.Monday), "含周一");
        TestRunner.Check(item.RepeatWeekdays.Contains(DayOfWeek.Friday), "含周五");
        TestRunner.Equal(9, item.RepeatCount, "重复 9 次");
        TestRunner.Equal(2, item.ExcludedOccurrences.Count, "两条 EXDATE 例外");
        TestRunner.Check(item.ExcludedOccurrences.Contains(new DateTime(2026, 9, 18, 9, 30, 0)), "例外日期内容正确");

        // 展开时例外与星期都生效
        var occurrences = OccurrenceCalculator.Expand(item, new DateTime(2026, 9, 1), new DateTime(2026, 10, 31));
        TestRunner.Check(occurrences.All(o => o.Start.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday), "展开只在周一三五");
        TestRunner.Check(!occurrences.Any(o => o.Start.Date == new DateTime(2026, 9, 18)), "9/18 被 EXDATE 排除");
    }
    private static void AllDayAndDuration()
    {
        const string sample = """
        BEGIN:VCALENDAR
        VERSION:2.0
        BEGIN:VEVENT
        UID:allday
        SUMMARY:年会
        DTSTART;VALUE=DATE:20261231
        DTEND;VALUE=DATE:20270101
        END:VEVENT
        BEGIN:VEVENT
        UID:duration
        SUMMARY:评审
        DTSTART:20260918T140000
        DURATION:PT2H
        END:VEVENT
        BEGIN:VEVENT
        UID:instant
        SUMMARY:打个电话
        DTSTART:20260919T100000
        END:VEVENT
        END:VCALENDAR
        """;

        var items = Map(IcsReader.Parse(sample), out _, out var errors);
        TestRunner.Equal(3, items.Count, "三个事件都映射成功");
        TestRunner.Equal(0, errors.Count, "没有错误");

        var allDay = items.First(i => i.Title == "年会");
        TestRunner.Check(allDay.AllDay, "全天日程被识别");
        TestRunner.Equal(new DateTime(2026, 12, 31), allDay.Start, "全天开始");
        TestRunner.Equal(new DateTime(2027, 1, 1), allDay.End, "全天结束（DTEND 是次日）");

        var duration = items.First(i => i.Title == "评审");
        TestRunner.Check(!duration.AllDay, "DURATION 事件不是全天");
        TestRunner.Equal(TimeSpan.FromHours(2), duration.End!.Value - duration.Start, "DURATION 生效");

        var instant = items.First(i => i.Title == "打个电话");
        TestRunner.Equal(TimeSpan.FromHours(1), instant.End!.Value - instant.Start, "没有 DTEND 时默认 1 小时");
    }

    private static void Rules()
    {
        // 1) 不支持的频率 → 警告 + 按单次
        var hourly = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:巡检
        DTSTART:20260915T090000
        RRULE:FREQ=HOURLY;COUNT=5
        END:VEVENT
        """)), out var warnings1, out _);

        TestRunner.Equal(RepeatKind.None, hourly[0].Repeat, "不支持的频率按单次导入");
        TestRunner.Check(warnings1.Any(w => w.Contains("HOURLY", StringComparison.Ordinal)), "提示不支持的频率");

        // 2) 每月第 N 个星期几 → 提示并降级成每周
        var ordinal = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:月度会
        DTSTART:20260901T100000
        RRULE:FREQ=MONTHLY;BYDAY=2MO
        END:VEVENT
        """)), out var warnings2, out _);

        TestRunner.Check(warnings2.Any(w => w.Contains("每月第 N 个星期几", StringComparison.Ordinal)), "提示「每月第 N 个星期几」不支持");
        TestRunner.Equal(RepeatKind.Weekly, ordinal[0].Repeat, "降级为每周");

        // 3) BYMONTHDAY 与开始日不一致 → 警告 + 单次
        var monthDay = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:账单
        DTSTART:20260905T100000
        RRULE:FREQ=MONTHLY;BYMONTHDAY=15
        END:VEVENT
        """)), out var warnings3, out _);

        TestRunner.Equal(RepeatKind.None, monthDay[0].Repeat, "月份日期不一致时按单次导入");
        TestRunner.Check(warnings3.Any(w => w.Contains("每月 15 日", StringComparison.Ordinal)), "提示月份日期不支持");

        // 4) INTERVAL + UNTIL
        var until = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:双周会
        DTSTART:20260915T140000
        RRULE:FREQ=WEEKLY;INTERVAL=2;UNTIL=20261231T235959
        END:VEVENT
        """)), out _, out _);

        TestRunner.Equal(RepeatKind.Weekly, until[0].Repeat, "每周");
        TestRunner.Equal(2, until[0].RepeatInterval, "间隔 2");
        TestRunner.Equal(new DateTime(2026, 12, 31, 23, 59, 59), until[0].RepeatUntil, "UNTIL 生效");

        // 5) 工作日
        var weekdays = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:工作日打卡
        DTSTART:20260915T090000
        RRULE:FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR
        END:VEVENT
        """)), out _, out _);

        TestRunner.Equal(RepeatKind.Weekly, weekdays[0].Repeat, "带 BYDAY 的按每周处理");
        TestRunner.Equal(5, weekdays[0].RepeatWeekdays.Count, "五个工作日");
    }

    private static void Exceptions()
    {
        const string sample = """
        BEGIN:VCALENDAR
        VERSION:2.0
        BEGIN:VEVENT
        UID:series-1
        SUMMARY:每周例会
        DTSTART:20260915T100000
        DTEND:20260915T110000
        RRULE:FREQ=WEEKLY;COUNT=10
        END:VEVENT
        BEGIN:VEVENT
        UID:series-1
        RECURRENCE-ID:20260922T100000
        SUMMARY:每周例会（改到下午）
        DTSTART:20260922T160000
        DTEND:20260922T170000
        END:VEVENT
        END:VCALENDAR
        """;

        var items = Map(IcsReader.Parse(sample), out _, out var errors);
        TestRunner.Equal(1, items.Count, "同一个 UID 合并成一条系列");
        TestRunner.Equal(0, errors.Count, "没有错误");

        var series = items[0];
        TestRunner.Equal(RepeatKind.Weekly, series.Repeat, "系列本身是每周");
        TestRunner.Equal(1, series.OccurrenceOverrides.Count, "有一条「仅这一次」的修改");

        var occurrences = OccurrenceCalculator.ExpandWithOverrides(series, new DateTime(2026, 9, 20), new DateTime(2026, 9, 24));
        var moved = occurrences.FirstOrDefault(o => o.Start.Date == new DateTime(2026, 9, 22));
        TestRunner.Check(moved != null, "9/22 那一次被改期后仍然能展开到");
        TestRunner.Equal(new DateTime(2026, 9, 22, 16, 0, 0), moved!.Start, "改期后的时间生效");

        // 绝对提醒（TRIGGER;VALUE=DATE-TIME）
        var absolute = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:面试
        DTSTART:20260915T150000
        BEGIN:VALARM
        ACTION:DISPLAY
        TRIGGER;VALUE=DATE-TIME:20260915T140000Z
        END:VALARM
        END:VEVENT
        """)), out _, out _);

        TestRunner.Check(absolute[0].RemindAtOverride.HasValue, "绝对提醒时间被记录");
        TestRunner.Equal(
            DateTime.SpecifyKind(new DateTime(2026, 9, 15, 14, 0, 0), DateTimeKind.Utc).ToLocalTime(),
            absolute[0].RemindAtOverride!.Value,
            "绝对提醒时间按 UTC 换算");

        // 正数 TRIGGER（开始之后提醒）→ 提示 + 归为"准点"
        var after = Map(IcsReader.Parse(Wrap("""
        BEGIN:VEVENT
        SUMMARY:喝水
        DTSTART:20260915T150000
        BEGIN:VALARM
        ACTION:DISPLAY
        TRIGGER:PT5M
        END:VALARM
        END:VEVENT
        """)), out var warnings, out _);

        TestRunner.Check(after[0].ReminderOffsets.SequenceEqual(new[] { 0 }), "开始之后的提醒归为准点");
        TestRunner.Check(warnings.Any(w => w.Contains("开始之后", StringComparison.Ordinal)), "提示提醒时间在开始之后");
    }
    private static void Messy()
    {
        // 折行 + 转义 + 缺 SUMMARY + 缺 DTSTART + 多个事件
        const string sample = "BEGIN:VCALENDAR\r\n"
            + "VERSION:2.0\r\n"
            + "BEGIN:VEVENT\r\n"
            + "SUMMARY:这是一个很长很长的标题，会被折成多行，用来验证折行处理是否正确无\r\n"
            + " 误地拼回去\r\n"
            + "DESCRIPTION:第一行\\n第二行，还有逗号\\,和分号\\;\r\n"
            + "DTSTART:20260915T150000\r\n"
            + "END:VEVENT\r\n"
            + "BEGIN:VEVENT\r\n"
            + "DTSTART:20260916T150000\r\n"
            + "END:VEVENT\r\n"
            + "BEGIN:VEVENT\r\n"
            + "SUMMARY:没有开始时间的事件\r\n"
            + "END:VEVENT\r\n"
            + "END:VCALENDAR\r\n";

        var parsed = IcsReader.Parse(sample);
        TestRunner.Equal(3, parsed.Events.Count, "三个 VEVENT");

        var items = Map(parsed, out var warnings, out var errors);

        TestRunner.Equal(2, items.Count, "两个能导入");
        TestRunner.Equal(1, errors.Count, "一个因为缺 DTSTART 报错");
        TestRunner.Check(errors[0].Message.Contains("DTSTART", StringComparison.Ordinal), "错误说明缺少 DTSTART");

        var folded = items.First(i => i.Title.StartsWith("这是一个很长", StringComparison.Ordinal));
        TestRunner.Check(folded.Title.EndsWith("拼回去", StringComparison.Ordinal), "折行被正确拼接");
        TestRunner.Check(!folded.Title.Contains("\r\n", StringComparison.Ordinal), "折行没有留下换行符");
        TestRunner.Check(folded.Notes.Contains("第一行\n第二行", StringComparison.Ordinal), "描述里的 \\n 被反转义成换行");
        TestRunner.Check(folded.Notes.Contains("还有逗号,和分号;", StringComparison.Ordinal), "描述里的逗号分号被反转义");

        var noTitle = items.First(i => i.Title == "(无标题)");
        TestRunner.Equal(new DateTime(2026, 9, 16, 15, 0, 0), noTitle.Start, "没有 SUMMARY 时按无标题导入");
        TestRunner.Check(warnings.Any(w => w.Contains("没有 SUMMARY", StringComparison.Ordinal)), "提示缺少标题");

        // 完全不是日历
        var notCalendar = IcsReader.Parse("标题,开始时间\r\n喝茶,2026-09-15 15:00\r\n");
        TestRunner.Check(!notCalendar.LooksLikeCalendar, "CSV 内容不会被当成 iCalendar");
        TestRunner.Equal(0, notCalendar.Events.Count, "解析不出事件");

        var empty = IcsReader.Parse("");
        TestRunner.Equal(0, empty.Events.Count, "空文本安全");
        TestRunner.Equal(0, IcsReader.Parse(null).Events.Count, "null 安全");

        // 缺冒号的行只提示不崩
        var broken = IcsReader.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY没有冒号\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        TestRunner.Equal(1, broken.Events.Count, "坏行不影响事件解析");
        TestRunner.Check(broken.Warnings.Any(w => w.Contains("缺少「:」", StringComparison.Ordinal)), "坏行给出提示");
    }

    private static void ServiceImport()
    {
        var dir = TestPaths.NewTempDir();
        var file = Path.Combine(dir, "sample.ics");
        File.WriteAllText(file, Wrap("""
        BEGIN:VEVENT
        UID:a
        SUMMARY:喝茶
        DTSTART:20260915T150000
        DTEND:20260915T160000
        END:VEVENT
        BEGIN:VEVENT
        UID:b
        SUMMARY:旧日程
        DTSTART:20200101T090000
        END:VEVENT
        """), new UTF8Encoding(true));

        var service = new IcsImportService(() => new DateTime(2026, 9, 1, 8, 0, 0));

        var preview = service.Preview(new IcsImportOptions(), file);
        TestRunner.Check(preview.LooksLikeCalendar, "预览识别为 iCalendar");
        TestRunner.Equal(2, preview.EventCount, "预览事件数");
        TestRunner.Equal(2, preview.MappedCount, "预览可导入数");
        TestRunner.Check(preview.CanImport, "可以导入");
        TestRunner.Check(preview.ToSummary().Contains("2 个事件", StringComparison.Ordinal), "预览摘要包含事件数");

        var target = new List<ScheduleItem>();
        var report = service.Import(new IcsImportOptions(), file, target);

        TestRunner.Check(!report.Blocked, "导入没有被阻止");
        TestRunner.Equal(2, report.Imported, "导入两条");
        TestRunner.Equal(0, report.Failed, "没有失败");

        // 再去重导入一次：跳过
        var again = service.Import(new IcsImportOptions { SkipDuplicates = true }, file, target);
        TestRunner.Equal(0, again.Imported, "重复导入不再新增");
        TestRunner.Equal(2, again.Skipped, "两条都被跳过");

        // upsert：更新已有
        var upsertTarget = new List<ScheduleItem>
        {
            new() { Title = "喝茶", Start = new DateTime(2026, 9, 15, 15, 0, 0), Notes = "旧备注" },
        };

        var upsert = service.Import(new IcsImportOptions { Mode = ImportMode.Upsert }, file, upsertTarget);
        TestRunner.Equal(1, upsert.Updated, "upsert 命中一条");
        TestRunner.Equal(1, upsert.Imported, "upsert 新增一条");

        // 跳过过去的日程
        var skipPastTarget = new List<ScheduleItem>();
        var skipPast = service.Import(new IcsImportOptions { SkipPastItems = true }, file, skipPastTarget);
        TestRunner.Equal(1, skipPast.Imported, "跳过过去后只导入一条");
        TestRunner.Equal(1, skipPast.Skipped, "过去的那条被跳过");

        // replace：先清空
        var replaceTarget = new List<ScheduleItem> { new() { Title = "要被清掉的" } };
        var replace = service.Import(new IcsImportOptions { Mode = ImportMode.Replace }, file, replaceTarget);
        TestRunner.Equal(2, replace.Imported, "replace 导入两条");
        TestRunner.Check(!replaceTarget.Any(i => i.Title == "要被清掉的"), "replace 清掉了旧数据");

        // 不是日历的文件 → 阻止 + 说明
        var badFile = Path.Combine(dir, "bad.ics");
        File.WriteAllText(badFile, "这不是日历文件", new UTF8Encoding(true));

        var badPreview = service.Preview(new IcsImportOptions(), badFile);
        TestRunner.Check(!badPreview.CanImport, "非日历文件不允许导入");
        TestRunner.Check(badPreview.BlockReason!.Contains("iCalendar", StringComparison.Ordinal), "给出「不是 iCalendar」的说明");

        var badReport = service.Import(new IcsImportOptions(), badFile, new List<ScheduleItem>());
        TestRunner.Check(badReport.Blocked, "非日历文件导入被阻止");
        TestRunner.Equal(0, badReport.Imported, "什么都没导入");

        // 文件不存在
        var missing = service.Preview(new IcsImportOptions(), Path.Combine(dir, "不存在.ics"));
        TestRunner.Check(missing.Warnings.Any(w => w.Contains("找不到文件", StringComparison.Ordinal)), "文件不存在时给出提示");
    }

    private static void RoundTrip()
    {
        var original = new List<ScheduleItem>
        {
            new()
            {
                Title = "周会",
                Start = new DateTime(2026, 9, 21, 10, 0, 0),
                End = new DateTime(2026, 9, 21, 11, 0, 0),
                Category = "工作",
                Notes = "第一行\n第二行",
                Priority = Priority.High,
                Repeat = RepeatKind.Weekly,
                RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday },
                RepeatCount = 6,
                ReminderEnabled = true,
                ReminderOffsets = new List<int> { 30, 0 },
            },
            new()
            {
                Title = "放假",
                Start = new DateTime(2026, 10, 1),
                End = new DateTime(2026, 10, 8),
                AllDay = true,
                Status = ItemStatus.Done,
            },
        };

        var dir = TestPaths.NewTempDir();
        var file = Path.Combine(dir, "roundtrip.ics");
        IcsWriter.WriteFile(file, original);

        var parsed = IcsReader.Parse(File.ReadAllText(file));
        TestRunner.Check(parsed.LooksLikeCalendar, "导出的文件是合法 iCalendar");
        TestRunner.Equal(2, parsed.Events.Count, "两个事件");

        var items = Map(parsed, out var warnings, out var errors);
        TestRunner.Equal(2, items.Count, "两条都回来了");
        TestRunner.Equal(0, errors.Count, "没有错误");
        TestRunner.Check(warnings.Count == 0, "往返没有提示（说明字段映射对称）：" + string.Join("；", warnings));

        var meeting = items.First(i => i.Title == "周会");
        TestRunner.Equal(original[0].Start, meeting.Start, "往返：开始");
        TestRunner.Equal(original[0].End, meeting.End, "往返：结束");
        TestRunner.Equal(original[0].Category, meeting.Category, "往返：分类");
        TestRunner.Equal(original[0].Notes, meeting.Notes, "往返：备注（含换行）");
        TestRunner.Equal(Priority.High, meeting.Priority, "往返：优先级");
        TestRunner.Equal(RepeatKind.Weekly, meeting.Repeat, "往返：重复");
        TestRunner.Equal(6, meeting.RepeatCount, "往返：重复次数");
        TestRunner.Check(meeting.ReminderOffsets.SequenceEqual(new[] { 30, 0 }), "往返：提醒档位");

        var holiday = items.First(i => i.Title == "放假");
        TestRunner.Check(holiday.AllDay, "往返：全天");
        TestRunner.Equal(original[1].Start, holiday.Start, "往返：全天开始");
        TestRunner.Equal(original[1].End, holiday.End, "往返：全天结束");
        TestRunner.Equal(ItemStatus.Done, holiday.Status, "往返：状态");
    }

    // ================= 小工具 =================

    /// <summary>把事件包进 VCALENDAR 外壳。</summary>
    private static string Wrap(string body)
        => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + body.Replace("\n", "\r\n").TrimEnd() + "\r\nEND:VCALENDAR\r\n";

    private static List<ScheduleItem> Map(IcsParseResult parsed, out List<string> warnings, out List<ImportError> errors)
    {
        warnings = new List<string>();
        errors = new List<ImportError>();

        return IcsMapper.Map(parsed, new IcsImportOptions(), new DateTime(2026, 1, 1, 0, 0, 0), "selftest.ics", warnings, errors);
    }
}
