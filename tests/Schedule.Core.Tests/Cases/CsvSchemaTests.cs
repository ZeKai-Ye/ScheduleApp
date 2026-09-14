using System.Text;
using Schedule.Core.Csv;
using Schedule.Core.Models;

namespace Schedule.Core.Tests.Cases;

/// <summary>
/// CSV 统一格式：规范表头、模板、导出/导入往返、别名兼容，
/// 以及最关键的——**格式不匹配必须明确报错并阻止导入**（不再静默导入 0 条）。
/// </summary>
public static class CsvSchemaTests
{
    public static void Run()
    {
        TestRunner.Section("CSV 统一格式");
        Schema();
        Template();
        RoundTrip();
        Mismatch();
        Aliases();
    }

    private static void Schema()
    {
        TestRunner.Equal(17, CsvSchema.Headers.Length, "统一格式共 17 列");
        TestRunner.Equal(2, CsvSchema.RequiredColumns.Count, "只有标题与开始时间必填");
        TestRunner.Equal("标题", CsvSchema.HeaderOf(CsvField.Title), "标题列名");
        TestRunner.Equal("开始时间", CsvSchema.HeaderOf(CsvField.Start), "开始时间列名");
        TestRunner.Equal("重复截止", CsvSchema.HeaderOf(CsvField.RepeatUntil), "重复截止列名（新增，之前导出会丢）");
        TestRunner.Check(CsvSchema.IsRequired(CsvField.Title), "标题必填");
        TestRunner.Check(!CsvSchema.IsRequired(CsvField.Notes), "备注不必填");

        TestRunner.Check(CsvSchema.IsCanonical(CsvSchema.Headers), "规范表头判定为统一格式");
        TestRunner.Check(CsvSchema.IsCanonicalOrder(CsvSchema.Headers), "规范表头顺序一致");
        TestRunner.Check(CsvSchema.IsCanonical(CsvSchema.Headers.Reverse().ToList()), "列顺序不同也算统一格式（按列名识别）");
        TestRunner.Check(!CsvSchema.IsCanonicalOrder(CsvSchema.Headers.Reverse().ToList()), "顺序不同不算「顺序一致」");
        TestRunner.Check(!CsvSchema.IsCanonical(CsvSchema.Headers.Take(5).ToList()), "少列不是统一格式");
        TestRunner.Check(!CsvSchema.IsCanonical(CsvSchema.Headers.Append("额外列").ToList()), "多列不是统一格式");
        TestRunner.Check(CsvSchema.IsCanonical(CsvSchema.Headers.Select(h => " " + h + " ").ToList()), "列名前后的空格不影响判定");

        var description = CsvSchema.DescribeColumns();
        foreach (var header in CsvSchema.Headers)
        {
            TestRunner.Check(description.Contains(header, StringComparison.Ordinal), "格式说明里包含「" + header + "」");
        }

        TestRunner.Check(description.Contains("必填", StringComparison.Ordinal), "格式说明标注了必填");
        TestRunner.Check(description.Contains("iCalendar", StringComparison.Ordinal), "格式说明提到也支持 ICS");
        TestRunner.Check(description.Contains(CsvSchema.HeaderLine, StringComparison.Ordinal), "格式说明里给了完整表头");
    }

    private static void Template()
    {
        var template = CsvSchema.TemplateText();
        var rows = CsvReader.Parse(template, ',');

        TestRunner.Equal(3, rows.Count, "模板 = 表头 + 2 行示例");
        TestRunner.Equal(CsvSchema.Headers.Length, rows[0].Length, "模板表头列数正确");
        TestRunner.Check(CsvSchema.IsCanonicalOrder(rows[0]), "模板第一行就是统一格式");

        foreach (var row in rows.Skip(1))
        {
            TestRunner.Equal(CsvSchema.Headers.Length, row.Length, "示例行列数与表头一致");
        }

        TestRunner.Equal("客户沟通", rows[1][0], "示例 1 的标题");
        TestRunner.Equal("30,15,0", rows[1][12], "示例 1 的多档提醒");
        TestRunner.Equal("每周", rows[2][7], "示例 2 的重复");
        TestRunner.Equal("周一、周三、周五", rows[2][9], "示例 2 的星期");
        TestRunner.Equal("2026-12-31", rows[2][11], "示例 2 的重复截止");

        // 模板写盘 → 导入 → 两条日程
        var dir = TestPaths.NewTempDir();
        var file = Path.Combine(dir, "template.csv");
        CsvSchema.WriteTemplate(file);

        var bytes = File.ReadAllBytes(file);
        TestRunner.Equal((byte)0xEF, bytes[0], "模板带 UTF-8 BOM（Excel 打开不乱码）");

        var items = new List<ScheduleItem>();
        var report = new CsvImportService().Import(new CsvImportOptions { FilePath = file }, items);

        TestRunner.Check(!report.Blocked, "模板可以导入");
        TestRunner.Equal(2, report.Imported, "模板里的 2 条示例都导入了");
        TestRunner.Equal(0, report.Failed, "模板导入没有失败行");

        var first = items.First(i => i.Title == "客户沟通");
        TestRunner.Equal(new DateTime(2026, 9, 15, 15, 0, 0), first.Start, "示例 1 开始时间");
        TestRunner.Equal(new DateTime(2026, 9, 15, 16, 0, 0), first.End, "示例 1 结束时间");
        TestRunner.Equal("工作", first.Category, "示例 1 分类");
        TestRunner.Equal(Priority.High, first.Priority, "示例 1 优先级");
        TestRunner.Check(first.ReminderOffsets.SequenceEqual(new[] { 30, 15, 0 }), "示例 1 三档提醒");
        TestRunner.Equal("还有半小时", first.ReminderVoiceTexts[30], "示例 1 档位文案");
        TestRunner.Equal("现在开始", first.ReminderVoiceTexts[0], "示例 1 准点文案");
        TestRunner.Check(first.Notes.Contains("记得带材料", StringComparison.Ordinal), "示例 1 备注");

        var second = items.First(i => i.Title == "站会");
        TestRunner.Equal(RepeatKind.Weekly, second.Repeat, "示例 2 每周重复");
        TestRunner.Equal(3, second.RepeatWeekdays.Count, "示例 2 三个星期");
        TestRunner.Equal(6, second.RepeatCount, "示例 2 重复次数");
        TestRunner.Equal(new DateTime(2026, 12, 31), second.RepeatUntil, "示例 2 重复截止");
    }
    private static void RoundTrip()
    {
        var original = new List<ScheduleItem>
        {
            new()
            {
                Title = "季度复盘（含逗号,引号\"和换行）",
                Start = new DateTime(2026, 9, 21, 9, 30, 0),
                End = new DateTime(2026, 9, 21, 11, 0, 0),
                Category = "工作",
                Notes = "第一行\n第二行",
                Color = "#2563EB",
                Priority = Priority.High,
                Status = ItemStatus.Done,
                ReminderEnabled = true,
                ReminderOffsets = new List<int> { 60, 15, 0 },
                ReminderVoiceTexts = new Dictionary<int, string> { [60] = "一小时后开始", [0] = "现在开始" },
                VoiceText = "通用文案",
            },
            new()
            {
                Title = "每周站会",
                Start = new DateTime(2026, 9, 23, 10, 0, 0),
                End = new DateTime(2026, 9, 23, 10, 15, 0),
                Repeat = RepeatKind.Weekly,
                RepeatInterval = 2,
                RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
                RepeatCount = 8,
                RepeatUntil = new DateTime(2026, 12, 31),
                ReminderEnabled = true,
                ReminderOffsets = new List<int> { 10 },
            },
            new()
            {
                Title = "年会",
                Start = new DateTime(2026, 12, 31),
                End = new DateTime(2027, 1, 1),
                AllDay = true,
                Category = "活动",
                ReminderEnabled = false,
            },
        };

        var dir = TestPaths.NewTempDir();
        var file = Path.Combine(dir, "roundtrip.csv");
        CsvExporter.WriteFile(file, original);

        // 导出的第一行必须就是统一格式
        var rows = CsvReader.Parse(File.ReadAllText(file), ',');
        TestRunner.Check(CsvSchema.IsCanonicalOrder(rows[0]), "导出的表头就是统一格式（顺序也一致）");

        var imported = new List<ScheduleItem>();
        var report = new CsvImportService().Import(new CsvImportOptions { FilePath = file }, imported);

        TestRunner.Check(!report.Blocked, "导出的文件可以原样导入");
        TestRunner.Equal(3, report.Imported, "三条都回来了");
        TestRunner.Equal(0, report.Warnings.Count, "往返导入没有提示（说明格式完全对得上）");

        var first = imported.First(i => i.Title.StartsWith("季度复盘", StringComparison.Ordinal));
        TestRunner.Equal(original[0].Start, first.Start, "往返：开始时间");
        TestRunner.Equal(original[0].End, first.End, "往返：结束时间");
        TestRunner.Equal(original[0].Category, first.Category, "往返：分类");
        TestRunner.Equal(original[0].Notes, first.Notes, "往返：备注（含换行）");
        TestRunner.Equal(original[0].Color, first.Color, "往返：颜色");
        TestRunner.Equal(original[0].Priority, first.Priority, "往返：优先级");
        TestRunner.Equal(original[0].Status, first.Status, "往返：状态");
        TestRunner.Check(first.ReminderOffsets.SequenceEqual(original[0].ReminderOffsets), "往返：提醒档位");
        TestRunner.Equal("一小时后开始", first.ReminderVoiceTexts[60], "往返：档位文案");
        TestRunner.Equal("通用文案", first.VoiceText, "往返：通用文案");

        var second = imported.First(i => i.Title == "每周站会");
        TestRunner.Equal(RepeatKind.Weekly, second.Repeat, "往返：重复类型");
        TestRunner.Equal(2, second.RepeatInterval, "往返：重复间隔");
        TestRunner.Equal(2, second.RepeatWeekdays.Count, "往返：星期集合");
        TestRunner.Equal(8, second.RepeatCount, "往返：重复次数");
        TestRunner.Equal(new DateTime(2026, 12, 31), second.RepeatUntil, "往返：重复截止");

        var third = imported.First(i => i.Title == "年会");
        TestRunner.Check(third.AllDay, "往返：全天");
        TestRunner.Equal(new DateTime(2026, 12, 31), third.Start, "往返：全天开始");
        TestRunner.Equal(new DateTime(2027, 1, 1), third.End, "往返：全天结束");
        TestRunner.Check(!third.ReminderEnabled, "往返：关闭提醒");
    }

    private static void Mismatch()
    {
        var dir = TestPaths.NewTempDir();

        // 1) 完全不相干的表头：必须给出原因 + 阻止导入 + 不动数据
        var wrong = Path.Combine(dir, "wrong.csv");
        File.WriteAllText(wrong, "姓名,电话,地址\r\n张三,13800000000,北京\r\n", new UTF8Encoding(true));

        var service = new CsvImportService();

        var preview = service.Preview(new CsvImportOptions { FilePath = wrong });
        TestRunner.Check(preview.BlockReason != null, "表头完全不匹配：给出阻塞原因");
        TestRunner.Check(!preview.CanImport, "表头完全不匹配：不允许导入");
        TestRunner.Check(!preview.Diagnosis.IsCanonical, "诊断：不是统一格式");
        TestRunner.Check(preview.Diagnosis.MissingRequired.Count == 2, "诊断：标题与开始时间都缺");
        TestRunner.Check(preview.Diagnosis.Unknown.Count == 3, "诊断：三列都没认出来");
        TestRunner.Check(preview.BlockReason!.Contains("标题", StringComparison.Ordinal), "阻塞原因里点名缺「标题」");
        TestRunner.Check(preview.BlockReason!.Contains(CsvSchema.HeaderLine, StringComparison.Ordinal), "阻塞原因里给出标准表头");
        TestRunner.Equal("（一个都没认出来）", preview.Diagnosis.RecognizedFieldsText, "诊断：没有识别到任何字段");

        var target = new List<ScheduleItem>
        {
            new() { Title = "原有日程", Start = new DateTime(2026, 1, 1, 9, 0, 0) },
        };

        var report = service.Import(new CsvImportOptions { FilePath = wrong }, target);
        TestRunner.Check(report.Blocked, "导入被阻止");
        TestRunner.Equal(0, report.Imported, "没有导入任何东西");
        TestRunner.Equal(0, report.Failed, "也没有逐行失败（不再刷屏「缺少标题」）");
        TestRunner.Equal(1, report.Errors.Count, "只报告一条「缺少必需的列」");
        TestRunner.Check(report.Errors[0].Message.Contains("标题", StringComparison.Ordinal), "错误里点名缺的列");
        TestRunner.Check(report.BlockReason!.Contains(CsvSchema.HeaderLine, StringComparison.Ordinal), "报告里带上标准表头");
        TestRunner.Equal(1, target.Count, "原有一日程没有被改动");
        TestRunner.Equal("原有日程", target[0].Title, "原有数据完好");

        // 2) 只缺可选列：允许导入，但要有提示
        var partial = Path.Combine(dir, "partial.csv");
        File.WriteAllText(partial, "标题,开始时间,自定义列\r\n喝茶,2026-09-15 15:00,随便\r\n", new UTF8Encoding(true));

        var partialPreview = service.Preview(new CsvImportOptions { FilePath = partial });
        TestRunner.Check(partialPreview.CanImport, "只缺可选列仍然可以导入");
        TestRunner.Check(!partialPreview.Diagnosis.IsCanonical, "诊断：与统一格式有差异");
        TestRunner.Check(partialPreview.Warnings.Any(w => w.Contains("多余列", StringComparison.Ordinal)), "提示多余列会被忽略");
        TestRunner.Check(partialPreview.Warnings.Any(w => w.Contains("缺少可选列", StringComparison.Ordinal)), "提示缺少可选列");

        var partialTarget = new List<ScheduleItem>();
        var partialReport = service.Import(new CsvImportOptions { FilePath = partial }, partialTarget);
        TestRunner.Check(!partialReport.Blocked, "缺可选列不阻塞");
        TestRunner.Equal(1, partialReport.Imported, "多余列的文件照常导入");

        // 3) 空文件 / 只有表头
        var empty = Path.Combine(dir, "empty.csv");
        File.WriteAllText(empty, "标题,开始时间\r\n", new UTF8Encoding(true));

        var emptyPreview = service.Preview(new CsvImportOptions { FilePath = empty });
        TestRunner.Check(emptyPreview.CanImport, "只有表头不算格式错");
        TestRunner.Equal(0, emptyPreview.TotalDataRows, "只有表头时数据行为 0");
        TestRunner.Check(emptyPreview.Warnings.Any(w => w.Contains("只有表头", StringComparison.Ordinal)), "只有表头时给出提示");

        // 4) 无表头且列数不够
        var noHeader = Path.Combine(dir, "noheader.csv");
        File.WriteAllText(noHeader, "喝茶\r\n", new UTF8Encoding(true));

        var noHeaderPreview = service.Preview(new CsvImportOptions { FilePath = noHeader, Header = HeaderMode.Absent });
        TestRunner.Check(!noHeaderPreview.CanImport, "无表头且只有一列：不允许导入");
        TestRunner.Check(noHeaderPreview.BlockReason!.Contains(CsvSchema.HeaderLine, StringComparison.Ordinal), "无表头时也给出标准表头");
    }

    private static void Aliases()
    {
        var dir = TestPaths.NewTempDir();
        var legacy = Path.Combine(dir, "legacy.csv");
        File.WriteAllText(legacy, "主题,开始,结束,说明,类别,提醒\r\n开会,2026-09-15 15:00,2026-09-15 16:00,带上材料,工作,30\r\n", new UTF8Encoding(true));

        var service = new CsvImportService();
        var preview = service.Preview(new CsvImportOptions { FilePath = legacy });

        TestRunner.Check(preview.CanImport, "老写法的别名仍然能用");
        TestRunner.Check(!preview.Diagnosis.IsCanonical, "别名表头不是统一格式（会提示）");
        TestRunner.Check(preview.Diagnosis.MissingRequired.Count == 0, "别名里认出了标题与开始时间");
        TestRunner.Check(preview.Warnings.Any(w => w.Contains("表头与统一格式", StringComparison.Ordinal)), "别名文件给出「与统一格式有差异」的提示");

        var items = new List<ScheduleItem>();
        var report = service.Import(new CsvImportOptions { FilePath = legacy }, items);

        TestRunner.Equal(1, report.Imported, "别名文件导入成功");
        TestRunner.Equal("开会", items[0].Title, "标题来自「主题」列");
        TestRunner.Equal(new DateTime(2026, 9, 15, 15, 0, 0), items[0].Start, "开始时间来自「开始」列");
        TestRunner.Equal("工作", items[0].Category, "分类来自「类别」列");
        TestRunner.Check(items[0].ReminderOffsets.SequenceEqual(new[] { 30 }), "提醒来自「提醒」列");
    }
}
