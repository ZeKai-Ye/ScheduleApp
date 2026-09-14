using System.Text;
using Schedule.Core.Models;

namespace Schedule.Core.Csv;

/// <summary>规范里的一列。</summary>
public sealed class CsvColumnSpec
{
    public CsvField Field { get; init; }

    /// <summary>规范表头（导出与模板都用它）。</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>必填列（标题、开始时间）。</summary>
    public bool Required { get; init; }

    /// <summary>取值说明。</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>示例值。</summary>
    public string Example { get; init; } = string.Empty;
}

/// <summary>
/// 日程 CSV 的**统一格式**（单一事实来源）。
///
/// 导出、模板、导入诊断都以这里为准；改了列就只改这一处。
/// 导入时仍然兼容各种历史写法（别名见 <see cref="CsvColumnMap"/>），
/// 但"标准格式"只有这一套：17 列、列名固定、顺序固定。
/// </summary>
public static class CsvSchema
{
    public const string TitleHeader = "标题";

    /// <summary>规范列（顺序即模板顺序）。</summary>
    public static IReadOnlyList<CsvColumnSpec> Columns { get; } = new List<CsvColumnSpec>
    {
        new() { Field = CsvField.Title, Header = "标题", Required = true, Hint = "日程名称", Example = "客户沟通" },
        new() { Field = CsvField.Start, Header = "开始时间", Required = true, Hint = "日期+时间；全天日程可只写日期", Example = "2026-09-15 15:00" },
        new() { Field = CsvField.End, Header = "结束时间", Hint = "留空 = 开始后 1 小时", Example = "2026-09-15 16:00" },
        new() { Field = CsvField.AllDay, Header = "全天", Hint = "是 / 否", Example = "否" },
        new() { Field = CsvField.Category, Header = "分类", Hint = "决定自动配色", Example = "工作" },
        new() { Field = CsvField.Color, Header = "颜色", Hint = "红 / 蓝 / #2563EB；留空跟随分类", Example = "蓝" },
        new() { Field = CsvField.Priority, Header = "优先级", Hint = "高 / 中 / 低", Example = "高" },
        new() { Field = CsvField.Repeat, Header = "重复", Hint = "无 / 每天 / 工作日 / 每周 / 每月 / 每年", Example = "每周" },
        new() { Field = CsvField.RepeatInterval, Header = "重复间隔", Hint = "每隔几个周期", Example = "1" },
        new() { Field = CsvField.Weekdays, Header = "星期", Hint = "每周重复的星期，顿号分隔", Example = "周一、周三" },
        new() { Field = CsvField.RepeatCount, Header = "重复次数", Hint = "共重复几次，留空 = 不限", Example = "6" },
        new() { Field = CsvField.RepeatUntil, Header = "重复截止", Hint = "重复到哪一天（含当天）", Example = "2026-12-31" },
        new() { Field = CsvField.Remind, Header = "提前提醒", Hint = "分钟（可多档，逗号分隔）；0 = 准点；也认 不提醒 / 具体时刻", Example = "30,15,0" },
        new() { Field = CsvField.Voice, Header = "语音内容", Hint = "播报文案；可用 档位:文案 逐档指定", Example = "30:还有半小时|0:现在开始" },
        new() { Field = CsvField.Sound, Header = "提醒铃声", Hint = "音频文件路径；可用 档位:路径 逐档指定", Example = "" },
        new() { Field = CsvField.Notes, Header = "备注", Hint = "详细说明", Example = "记得带材料" },
        new() { Field = CsvField.Status, Header = "状态", Hint = "待办 / 已完成 / 已取消", Example = "待办" },
    };

    /// <summary>规范表头（导出、模板、诊断提示都用它）。</summary>
    public static string[] Headers { get; } = Columns.Select(c => c.Header).ToArray();

    /// <summary>规范表头一行（逗号分隔，可直接粘进 Excel 第一行）。</summary>
    public static string HeaderLine => string.Join(",", Headers);

    /// <summary>必填列。</summary>
    public static IReadOnlyList<CsvColumnSpec> RequiredColumns { get; } = Columns.Where(c => c.Required).ToList();

    public static string HeaderOf(CsvField field)
        => Columns.FirstOrDefault(c => c.Field == field)?.Header ?? string.Empty;

    public static bool IsRequired(CsvField field)
        => Columns.Any(c => c.Field == field && c.Required);

    /// <summary>列说明（界面「格式说明」与文档共用）。</summary>
    public static string DescribeColumns()
    {
        var sb = new StringBuilder();
        sb.AppendLine("标准 CSV 表头（第一行照抄即可，列的先后顺序可以不同）：");
        sb.AppendLine();
        sb.AppendLine(HeaderLine);
        sb.AppendLine();
        sb.AppendLine("列说明：");

        foreach (var column in Columns)
        {
            sb.AppendLine("· " + column.Header + (column.Required ? "（必填）" : string.Empty)
                + "：" + column.Hint
                + (column.Example.Length > 0 ? "　例：" + column.Example : string.Empty));
        }

        sb.AppendLine();
        sb.AppendLine("其他说明：");
        sb.AppendLine("· 只有「标题」和「开始时间」是必填，其余留空即可；多余的列会被忽略并在导入时提示。");
        sb.AppendLine("· 时间支持 2026-09-15 15:00 / 2026/9/15 15:00 / 2026年9月15日 15:00 / 明天15:00 / 15:00（配合单独的日期列）。");
        sb.AppendLine("· 文件编码支持 UTF-8（推荐）/ GBK / GB18030 / UTF-16，通常自动识别即可。");
        sb.AppendLine("· 也可以直接导入 iCalendar（.ics）文件：Google 日历、Outlook 导出的都能用。");

        return sb.ToString();
    }

    /// <summary>模板内容：规范表头 + 两行示例。</summary>
    public static string TemplateText()
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Headers.Select(Escape))).Append("\r\n");

        // 示例 1：普通日程 + 多档提醒
        sb.Append(string.Join(",", new[]
        {
            "客户沟通", "2026-09-15 15:00", "2026-09-15 16:00", "否", "工作", "蓝", "高",
            "无", string.Empty, string.Empty, string.Empty, string.Empty,
            "30,15,0", "30:还有半小时|0:现在开始", string.Empty, "记得带材料", "待办",
        }.Select(Escape))).Append("\r\n");

        // 示例 2：周期日程（每周一三五，共 6 次，到 12 月底）
        sb.Append(string.Join(",", new[]
        {
            "站会", "2026-09-16 09:30", "2026-09-16 09:45", "否", "工作", string.Empty, "中",
            "每周", "1", "周一、周三、周五", "6", "2026-12-31",
            "10", "站会开始了", string.Empty, string.Empty, "待办",
        }.Select(Escape))).Append("\r\n");

        return sb.ToString();
    }

    /// <summary>写成模板文件（UTF-8 BOM，Excel 双击不乱码）。</summary>
    public static void WriteTemplate(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(path, TemplateText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>表头是否就是标准格式（列名与规范完全一致，允许顺序不同、允许大小写与空格差异）。</summary>
    public static bool IsCanonical(IReadOnlyList<string> headers)
    {
        if (headers.Count != Headers.Length) return false;

        var expected = Headers.Select(TextNormalizer.Key).ToHashSet(StringComparer.Ordinal);
        var actual = headers.Select(TextNormalizer.Key).ToHashSet(StringComparer.Ordinal);

        return expected.SetEquals(actual);
    }

    /// <summary>顺序也一致（导出后的文件应当满足）。</summary>
    public static bool IsCanonicalOrder(IReadOnlyList<string> headers)
        => headers.Count == Headers.Length
            && headers.Select(TextNormalizer.Key).SequenceEqual(Headers.Select(TextNormalizer.Key), StringComparer.Ordinal);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }
}
