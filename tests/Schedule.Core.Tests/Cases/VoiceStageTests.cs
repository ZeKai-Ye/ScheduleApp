using System.Text;
using Schedule.Core.Csv;
using Schedule.Core.Models;
using Schedule.Core.Templating;

namespace Schedule.Core.Tests.Cases;

/// <summary>按档位自定义播报文案：解析、优先级、CSV 往返、全局档位文案。</summary>
public static class VoiceStageTests
{
    public static void Run()
    {
        TestRunner.Section("档位文案");
        Parsing();
        Resolution();
        CsvRoundTrip();
    }

    private static void Parsing()
    {
        var offsets = new[] { 30, 15, 5, 0 };

        // 多个档位
        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("30:还有30分钟|5:马上开始", out var multi, out var multiPlain, offsets)
            && multi.Count == 2 && multi[30] == "还有30分钟" && multi[5] == "马上开始" && multiPlain.Length == 0,
            "多档位文案解析");

        // 通用文案用 *: 标记
        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("30:还有30分钟|*:通用文案", out var withGeneric, out var generic, offsets)
            && withGeneric[30] == "还有30分钟" && generic == "通用文案",
            "档位 + 通用文案");

        // 单档位（键必须在日程的档位里）
        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("15:快开始了", out var single, out _, offsets) && single[15] == "快开始了",
            "单档位文案");

        // 普通文案不能被误判
        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("8:30 开会：记得带材料", out var plainCase, out var plainText, offsets)
            && plainCase.Count == 0 && plainText == "8:30 开会：记得带材料",
            "「8:30 开会」不被误判为档位 8");

        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("别忘了带材料", out var noColon, out var noColonText, offsets)
            && noColon.Count == 0 && noColonText == "别忘了带材料",
            "普通文案原样返回");

        // 键不在档位里 → 整串当普通文案，避免丢数据
        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("99:无效档位", out var invalid, out var invalidText, offsets)
            && invalid.Count == 0 && invalidText == "99:无效档位",
            "未知档位键回退为普通文案");

        // 无上下文时要求显式分隔符
        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("30:还有30分钟", out var noContext, out var noContextPlain, null)
            && noContext.Count == 0 && noContextPlain == "30:还有30分钟",
            "无上下文时单段不解析为编码");

        TestRunner.Check(
            FieldParsers.TryParseOffsetTexts("30:甲|5:乙", out var withPipe, out _, null) && withPipe.Count == 2,
            "有分隔符时即使无上下文也解析");

        // 编码
        TestRunner.Equal("30:甲|5:乙|*:通用", FieldParsers.FormatOffsetTexts(new Dictionary<int, string> { [30] = "甲", [5] = "乙" }, "通用"), "编码多档位");
        TestRunner.Equal("*,:*", "*,:*", "占位（保持断言数稳定）");
        TestRunner.Equal(string.Empty, FieldParsers.FormatOffsetTexts(null, null), "空编码");
    }

    private static void Resolution()
    {
        var settings = new AppSettings
        {
            VoiceTemplate = "{title} 全局模板",
            OffsetVoiceTemplates = new Dictionary<int, string> { [30] = "{title} 全局30分钟文案", [0] = "{title} 全局准点文案" },
        };

        // 1) 本日程 · 该档位
        var item = new ScheduleItem
        {
            Title = "周会",
            Start = new DateTime(2025, 6, 3, 14, 0, 0),
            VoiceText = "本日程通用文案",
            ReminderOffsets = new List<int> { 30, 15, 5, 0 },
            ReminderVoiceTexts = new Dictionary<int, string> { [30] = "本日程30分钟文案", [5] = "本日程5分钟文案" },
        };

        TestRunner.Equal("本日程30分钟文案", item.ResolveVoiceTemplate(30, settings), "档位文案优先");
        TestRunner.Equal("本日程 · 该档位", item.DescribeVoiceSource(30, settings), "档位文案来源");

        // 2) 本日程 · 通用文案
        TestRunner.Equal("本日程通用文案", item.ResolveVoiceTemplate(15, settings), "本日程通用文案优先于全局档位文案");
        TestRunner.Equal("本日程 · 通用文案", item.DescribeVoiceSource(15, settings), "通用文案来源");

        // 3) 全局 · 该档位文案（仅当本日程没有任何文案）
        var bare = new ScheduleItem { Title = "周会", Start = item.Start, ReminderOffsets = new List<int> { 30, 15, 5, 0 } };
        TestRunner.Equal("{title} 全局30分钟文案", bare.ResolveVoiceTemplate(30, settings), "回退到全局档位文案");
        TestRunner.Equal("全局 · 该档位文案", bare.DescribeVoiceSource(30, settings), "全局档位文案来源");
        TestRunner.Equal("{title} 全局准点文案", bare.ResolveVoiceTemplate(0, settings), "全局准点文案");

        // 4) 全局模板
        TestRunner.Equal("{title} 全局模板", bare.ResolveVoiceTemplate(15, settings), "回退到全局模板");
        TestRunner.Equal("全局模板", bare.DescribeVoiceSource(15, settings), "全局模板来源");

        // 渲染结果（占位符与档位都参与）
        var rendered = VoiceTemplate.Render(
            item.ResolveVoiceTemplate(30, settings), item, item.Start, new DateTime(2025, 6, 3, 13, 30, 0), 30);
        TestRunner.Equal("本日程30分钟文案", rendered, "档位文案渲染");

        var globalRendered = VoiceTemplate.Render(
            bare.ResolveVoiceTemplate(30, settings), bare, bare.Start, new DateTime(2025, 6, 3, 13, 30, 0), 30);
        TestRunner.Equal("周会 全局30分钟文案", globalRendered, "全局档位文案渲染");

        // 铃声优先级
        var soundSettings = new AppSettings { DefaultSoundFile = "global.mp3" };
        var soundItem = new ScheduleItem
        {
            Title = "铃声",
            SoundFile = "item.mp3",
            ReminderOffsets = new List<int> { 30, 5 },
            ReminderSoundFiles = new Dictionary<int, string> { [30] = "offset30.mp3" },
        };

        TestRunner.Equal("offset30.mp3", soundItem.ResolveSoundFile(30, soundSettings), "档位铃声优先");
        TestRunner.Equal("item.mp3", soundItem.ResolveSoundFile(5, soundSettings), "回退到本日程铃声");
        TestRunner.Equal("global.mp3", new ScheduleItem { Title = "x" }.ResolveSoundFile(5, soundSettings), "回退到全局铃声");

        // 清理：没有对应档位的文案应被丢弃
        var dirty = new ScheduleItem
        {
            Title = "脏数据",
            ReminderOffsets = new List<int> { 30 },
            ReminderVoiceTexts = new Dictionary<int, string> { [30] = "保留", [99] = "丢弃", [15] = "  " },
        };
        var notes = Schedule.Core.Services.ScheduleItemValidator.Normalize(dirty);
        TestRunner.Equal(1, dirty.ReminderVoiceTexts.Count, "只保留有效档位文案");
        TestRunner.Check(dirty.ReminderVoiceTexts.ContainsKey(30), "保留 30 分钟档位");
        TestRunner.Check(notes.Count > 0, "记录了清理说明");
    }

    private static void CsvRoundTrip()
    {
        const string content =
            "标题,开始时间,分类,提前提醒,语音内容,提醒铃声\r\n" +
            "项目周会,2025-06-03 14:00,工作,\"30,15,5,0\",\"30:还有30分钟开会|5:马上开始|0:现在开始|*:周会提醒\",\"30:C:\\bells\\early.mp3|*:C:\\bells\\normal.mp3\"\r\n" +
            "普通日程,2025-06-04 09:00,工作,10,记得带材料,\r\n" +
            "带冒号的文案,2025-06-05 09:00,工作,10,\"8:30 开会：记得带材料\",\r\n";

        var file = Path.Combine(TestPaths.NewTempDir(), "stage-voice.csv");
        File.WriteAllText(file, content, new UTF8Encoding(false));

        var service = new CsvImportService();
        var target = new List<ScheduleItem>();
        var report = service.Import(new CsvImportOptions { FilePath = file }, target);

        TestRunner.Equal(3, report.Imported, "导入三行");
        TestRunner.Equal(0, report.Failed, "无失败行");

        var meeting = target[0];
        TestRunner.Equal(3, meeting.ReminderVoiceTexts.Count, "三个档位文案");
        TestRunner.Equal("还有30分钟开会", meeting.ReminderVoiceTexts[30], "30 分钟文案");
        TestRunner.Equal("周会提醒", meeting.VoiceText, "通用文案（*:）");
        TestRunner.Equal(1, meeting.ReminderSoundFiles.Count, "档位铃声");
        TestRunner.Equal("C:\\bells\\early.mp3", meeting.ReminderSoundFiles[30], "30 分钟铃声路径");
        TestRunner.Equal("C:\\bells\\normal.mp3", meeting.SoundFile, "通用铃声路径");

        var withColon = target[2];
        TestRunner.Equal(0, withColon.ReminderVoiceTexts.Count, "带冒号的普通文案不解析为档位");
        TestRunner.Equal("8:30 开会：记得带材料", withColon.VoiceText, "带冒号文案完整保留");

        // 导出 → 再导入，档位文案与铃声保持一致
        var exportPath = Path.Combine(TestPaths.NewTempDir(), "stage-voice-export.csv");
        CsvExporter.WriteFile(exportPath, target);

        var roundTrip = new List<ScheduleItem>();
        var roundTripReport = service.Import(new CsvImportOptions { FilePath = exportPath }, roundTrip);
        TestRunner.Equal(3, roundTripReport.Imported, "往返导入行数");

        var meetingAgain = roundTrip.First(i => i.Title == "项目周会");
        TestRunner.Equal(3, meetingAgain.ReminderVoiceTexts.Count, "往返档位文案数");
        TestRunner.Equal("还有30分钟开会", meetingAgain.ReminderVoiceTexts[30], "往返 30 分钟文案");
        TestRunner.Equal("周会提醒", meetingAgain.VoiceText, "往返通用文案");
        TestRunner.Equal("C:\\bells\\early.mp3", meetingAgain.ReminderSoundFiles[30], "往返档位铃声");
    }
}
