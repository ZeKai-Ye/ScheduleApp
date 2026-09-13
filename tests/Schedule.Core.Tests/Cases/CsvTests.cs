using System.Text;
using Schedule.Core.Csv;
using Schedule.Core.Models;

namespace Schedule.Core.Tests.Cases;

public static class CsvTests
{
    public static void Run()
    {
        Parsing();
        Encodings();
        Mapping();
        FieldValues();
        EndToEnd();
    }

    private static void Parsing()
    {
        TestRunner.Section("CSV 解析");

        var text = "标题,备注\r\n\"开会, 讨论\",\"第一行\n第二行\"\r\n";
        var rows = CsvReader.Parse(text, ',');
        TestRunner.Equal(2, rows.Count, "行数");
        TestRunner.Equal("标题", rows[0][0], "表头首列");
        TestRunner.Equal("开会, 讨论", rows[1][0], "引号内的逗号");
        TestRunner.Equal("第一行\n第二行", rows[1][1], "引号内的换行");

        TestRunner.Equal(';', CsvReader.DetectDelimiter("a;b;c\n1;2;3\n4;5;6"), "分号分隔符探测");
        TestRunner.Equal('\t', CsvReader.DetectDelimiter("a\tb\tc\n1\t2\t3"), "制表符探测");
        TestRunner.Equal(',', CsvReader.DetectDelimiter("a,b,c\n1,2,3"), "逗号探测");

        var escaped = CsvReader.Parse("a,\"say \"\"hi\"\"\",c", ',');
        TestRunner.Equal("say \"hi\"", escaped[0][1], "双引号转义");

        var blank = CsvReader.Parse("a,b\n\n\nc,d", ',');
        TestRunner.Equal(2, blank.Count, "忽略空行");
    }

    private static void Encodings()
    {
        TestRunner.Section("CSV 编码识别");

        var bomBody = Encoding.UTF8.GetBytes("标题,开始时间\r\n开会,2025-01-15 09:30");
        var utf8Bom = Encoding.UTF8.GetPreamble().Concat(bomBody).ToArray();
        var bomSource = CsvEncodingDetector.Decode(utf8Bom, "auto");
        TestRunner.Check(bomSource.HasBom, "识别 UTF-8 BOM");
        TestRunner.Check(bomSource.Text.StartsWith("标题", StringComparison.Ordinal), "BOM 文本正确");

        var utf8 = new UTF8Encoding(false).GetBytes("标题,开始时间\r\n开会,2025-01-15 09:30");
        var utf8Source = CsvEncodingDetector.Decode(utf8, "auto");
        TestRunner.Check(!utf8Source.HasBom, "无 BOM");
        TestRunner.Check(!utf8Source.IsFallback, "UTF-8 不是兜底");
        TestRunner.Check(utf8Source.Text.StartsWith("标题", StringComparison.Ordinal), "UTF-8 文本正确");

        var gbText = "标题,开始时间\r\n开会,2025-01-15 09:30";
        if (NativeEncoding.TryGetBytes(gbText, NativeEncoding.CodePageGbk, out var gbkBytes))
        {
            var gbkSource = CsvEncodingDetector.Decode(gbkBytes, "auto");
            TestRunner.Check(gbkSource.Text.Contains("标题", StringComparison.Ordinal), "GBK 自动识别文本");
            TestRunner.Check(gbkSource.EncodingName.Contains("GB", StringComparison.Ordinal), "GBK 编码名称");
            TestRunner.Check(gbkSource.IsGuessed, "GBK 标记为推测");

            // 显式指定编码
            var forced = CsvEncodingDetector.Decode(gbkBytes, "gbk");
            TestRunner.Check(forced.Text.Contains("开始时间", StringComparison.Ordinal), "显式 GBK 解码");
        }
        else
        {
            TestRunner.Check(false, "GBK 编码生成失败（WideCharToMultiByte 不可用）");
        }
    }

    private static void Mapping()
    {
        TestRunner.Section("CSV 列映射");

        TestRunner.Check(CsvImportService.LooksLikeHeader(new[] { "标题", "开始时间", "备注" }), "识别中文表头");
        TestRunner.Check(CsvImportService.LooksLikeHeader(new[] { "title", "start", "notes" }), "识别英文表头");
        TestRunner.Check(!CsvImportService.LooksLikeHeader(new[] { "开会", "2025-01-15 09:30", "x" }), "首行是数据");
        TestRunner.Check(!CsvImportService.LooksLikeHeader(new[] { "2025-01-15 09:30", "2025-01-15 10:30" }), "两列均为时间");

        var map = CsvColumnMap.AutoMap(new[] { "标题", "开始时间", "结束时间", "分类", "提前提醒", "备注", "重复" });
        TestRunner.Equal(0, map[CsvField.Title], "标题列");
        TestRunner.Equal(1, map[CsvField.Start], "开始时间列");
        TestRunner.Equal(2, map[CsvField.End], "结束时间列");
        TestRunner.Equal(3, map[CsvField.Category], "分类列");
        TestRunner.Equal(4, map[CsvField.Remind], "提醒列");
        TestRunner.Equal(5, map[CsvField.Notes], "备注列");
        TestRunner.Equal(6, map[CsvField.Repeat], "重复列");

        var englishMap = CsvColumnMap.AutoMap(new[] { "Subject", "Start Time", "Notes", "Tags" });
        TestRunner.Equal(0, englishMap[CsvField.Title], "英文标题列");
        TestRunner.Equal(1, englishMap[CsvField.Start], "英文开始列");
        TestRunner.Equal(2, englishMap[CsvField.Notes], "英文备注列");
        TestRunner.Equal(3, englishMap[CsvField.Category], "英文分类列");

        var positional = CsvColumnMap.DefaultPositionalMap(4);
        TestRunner.Equal(0, positional[CsvField.Title], "无表头默认映射标题");
        TestRunner.Equal(1, positional[CsvField.Start], "无表头默认映射开始");
    }

    private static void FieldValues()
    {
        TestRunner.Section("CSV 字段解析");

        var start = new DateTime(2025, 1, 15, 14, 0, 0);

        Remind("10", start, true, 10, null, "纯数字分钟");
        Remind("10分钟", start, true, 10, null, "中文分钟");
        Remind("提前30分钟", start, true, 30, null, "带提前前缀");
        Remind("1小时", start, true, 60, null, "小时换算");
        Remind("2天", start, true, 2880, null, "天换算");
        Remind("0", start, true, 0, null, "准点提醒");
        Remind("不提醒", start, false, 0, null, "关闭提醒");
        Remind("none", start, false, 0, null, "英文关闭提醒");
        Remind("08:00", start, true, 360, new DateTime(2025, 1, 15, 8, 0, 0), "当天时刻");
        Remind("2025-01-15 08:00", start, true, 360, new DateTime(2025, 1, 15, 8, 0, 0), "绝对时间");

        TestRunner.Check(!FieldParsers.TryParseRemind("随便写点什么", start, out _), "无法识别的提醒内容返回 false");

        FieldParsers.TryParsePriority("高", out var priority);
        TestRunner.Equal(Priority.High, priority, "高优先级");
        FieldParsers.TryParsePriority("low", out var low);
        TestRunner.Equal(Priority.Low, low, "低优先级");

        FieldParsers.TryParseRepeat("每天", out var daily);
        TestRunner.Equal(RepeatKind.Daily, daily, "每天");
        FieldParsers.TryParseRepeat("工作日", out var weekdays);
        TestRunner.Equal(RepeatKind.Weekdays, weekdays, "工作日");
        FieldParsers.TryParseRepeat("每周", out var weekly);
        TestRunner.Equal(RepeatKind.Weekly, weekly, "每周");
        FieldParsers.TryParseRepeat("无", out var none);
        TestRunner.Equal(RepeatKind.None, none, "不重复");

        FieldParsers.TryParseAllDay("是", out var allDay);
        TestRunner.Check(allDay, "全天是");
        FieldParsers.TryParseAllDay("no", out var notAllDay);
        TestRunner.Check(!notAllDay, "全天否");

        FieldParsers.TryParseStatus("已完成", out var done);
        TestRunner.Equal(ItemStatus.Done, done, "已完成状态");
    }

    private static void Remind(string text, DateTime start, bool enabled, int minutes, DateTime? absolute, string name)
    {
        if (!FieldParsers.TryParseRemind(text, start, out var spec))
        {
            TestRunner.Check(false, name + "（未识别）");
            return;
        }

        TestRunner.Equal(enabled, spec.Enabled, name + " 启用状态");
        TestRunner.Equal(minutes, spec.BeforeMinutes, name + " 分钟数");
        TestRunner.Equal(absolute, spec.Absolute, name + " 绝对时间");
    }

    private static void EndToEnd()
    {
        TestRunner.Section("CSV 端到端导入");

        const string content =
            "标题,开始时间,结束时间,分类,提前提醒,备注,重复\r\n" +
            "项目周会,2025-01-15 14:00,2025-01-15 15:00,工作,15,会议室A,无\r\n" +
            "晨会,2025-01-16 09:00,,工作,10,,每天\r\n" +
            ",2025-01-16 10:00,,,,\r\n" +
            "项目周会,2025-01-15 14:00,,,,,\r\n";

        var file = Path.Combine(TestPaths.NewTempDir(), "schedule.csv");
        File.WriteAllText(file, content, new UTF8Encoding(false));

        var service = new CsvImportService();
        var options = new CsvImportOptions { FilePath = file, Mode = ImportMode.Append };

        var preview = service.Preview(options);
        TestRunner.Equal(7, preview.Headers.Count, "预览列数");
        TestRunner.Equal(4, preview.TotalDataRows, "预览数据行数");
        TestRunner.Check(preview.HasHeader, "预览识别表头");
        TestRunner.Equal(CsvField.Title, CsvColumnMap.Suggest(preview.Headers[0]), "首列表头建议");
        TestRunner.Check(preview.SampleRows.Count == 4, "预览样本行数");

        var target = new List<ScheduleItem>();
        var report = service.Import(options, target);

        TestRunner.Equal(4, report.TotalRows, "导入总行数");
        TestRunner.Equal(2, report.Imported, "新增行数");
        TestRunner.Equal(1, report.Skipped, "跳过重复行数");
        TestRunner.Equal(1, report.Failed, "失败行数");
        TestRunner.Equal(2, target.Count, "目标集合大小");
        TestRunner.Check(report.HasErrors && report.Errors[0].Line == 4, "错误行号正确");

        var meeting = target.First(i => i.Title == "项目周会");
        TestRunner.Equal(15, meeting.RemindBeforeMinutes, "提前提醒分钟");
        TestRunner.Equal("工作", meeting.Category, "分类");
        TestRunner.Equal("会议室A", meeting.Notes, "备注");
        TestRunner.Equal(new DateTime(2025, 1, 15, 15, 0, 0), meeting.End!.Value, "结束时间");
        TestRunner.Equal(ItemOrigin.CsvImport, meeting.Origin, "来源标记");
        TestRunner.Check(meeting.Source == "schedule.csv", "来源文件名");

        var morning = target.First(i => i.Title == "晨会");
        TestRunner.Equal(RepeatKind.Daily, morning.Repeat, "重复规则");
        TestRunner.Check(!morning.AllDay, "非全天");

        // Upsert：重复导入应更新而不是新增
        // 同一文件里「项目周会」出现了两次，Upsert 模式下两次都会命中已有记录
        var upsertReport = service.Import(new CsvImportOptions { FilePath = file, Mode = ImportMode.Upsert }, target);
        TestRunner.Equal(3, upsertReport.Updated, "更新行数");
        TestRunner.Equal(1, upsertReport.Failed, "Upsert 仍会统计失败行");
        TestRunner.Equal(2, target.Count, "更新后集合大小");

        // 全天推断
        var allDayFile = Path.Combine(TestPaths.NewTempDir(), "allday.csv");
        File.WriteAllText(allDayFile, "标题,日期,备注\r\n年会,2025-02-01,全体\r\n", new UTF8Encoding(false));
        var allDayTarget = new List<ScheduleItem>();
        new CsvImportService().Import(new CsvImportOptions { FilePath = allDayFile }, allDayTarget);
        TestRunner.Equal(1, allDayTarget.Count, "全天日程导入");
        if (allDayTarget.Count == 1)
        {
            TestRunner.Check(allDayTarget[0].AllDay, "全天标记");
            TestRunner.Equal(new DateTime(2025, 2, 1), allDayTarget[0].Start, "全天日期");
        }

        // GBK 文件
        var gbkFile = Path.Combine(TestPaths.NewTempDir(), "gbk.csv");
        if (NativeEncoding.TryGetBytes(content, NativeEncoding.CodePageGbk, out var gbkBytes))
        {
            File.WriteAllBytes(gbkFile, gbkBytes);
            var gbkPreview = service.Preview(new CsvImportOptions { FilePath = gbkFile });
            TestRunner.Check(gbkPreview.EncodingName.Contains("GB", StringComparison.Ordinal), "GBK 文件编码识别");
            var gbkTarget = new List<ScheduleItem>();
            var gbkReport = service.Import(new CsvImportOptions { FilePath = gbkFile }, gbkTarget);
            TestRunner.Equal(2, gbkReport.Imported, "GBK 文件导入成功");
        }

        // 导出 → 再导入
        var exported = CsvExporter.ToCsv(target);
        var roundTripFile = Path.Combine(TestPaths.NewTempDir(), "roundtrip.csv");
        CsvExporter.WriteFile(roundTripFile, target);
        var roundTripTarget = new List<ScheduleItem>();
        var roundTripReport = service.Import(new CsvImportOptions { FilePath = roundTripFile }, roundTripTarget);
        TestRunner.Equal(2, roundTripReport.Imported, "导出后重新导入行数");
        TestRunner.Check(exported.Contains("项目周会", StringComparison.Ordinal), "导出内容包含标题");

        // 无表头文件 + 自动判定
        var noHeaderFile = Path.Combine(TestPaths.NewTempDir(), "noheader.csv");
        // 无表头时按位置约定：标题, 开始, 结束, 备注, 分类
        File.WriteAllText(noHeaderFile, "买牛奶,2025-03-01 08:30,,去超市,生活\r\n", new UTF8Encoding(false));
        var noHeaderPreview = service.Preview(new CsvImportOptions { FilePath = noHeaderFile });
        TestRunner.Check(!noHeaderPreview.HasHeader, "无表头自动判定");
        var noHeaderTarget = new List<ScheduleItem>();
        var noHeaderReport = service.Import(new CsvImportOptions { FilePath = noHeaderFile }, noHeaderTarget);
        TestRunner.Equal(1, noHeaderReport.Imported, "无表头导入");
        if (noHeaderTarget.Count == 1)
        {
            TestRunner.Equal("买牛奶", noHeaderTarget[0].Title, "无表头标题");
            TestRunner.Equal("去超市", noHeaderTarget[0].Notes, "无表头备注");
            TestRunner.Equal("生活", noHeaderTarget[0].Category, "无表头分类");
            TestRunner.Equal(10, noHeaderTarget[0].RemindBeforeMinutes, "无表头使用默认提醒");
        }
    }
}
