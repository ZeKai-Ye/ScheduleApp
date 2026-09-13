namespace Schedule.Core.Models;

/// <summary>导入策略。</summary>
public enum ImportMode
{
    /// <summary>全部追加为新日程。</summary>
    Append = 0,
    /// <summary>按「标题 + 开始时间」匹配，命中则更新，否则新增。</summary>
    Upsert = 1,
    /// <summary>先清空现有日程再导入。</summary>
    Replace = 2,
}

/// <summary>表头处理方式。</summary>
public enum HeaderMode
{
    /// <summary>自动判断。</summary>
    Auto = 0,
    /// <summary>首行是表头。</summary>
    Present = 1,
    /// <summary>文件没有表头。</summary>
    Absent = 2,
}

/// <summary>CSV 中可识别并映射的字段。</summary>
public enum CsvField
{
    Ignore = 0,
    Title = 1,
    Start = 2,
    End = 3,
    Date = 4,
    Notes = 5,
    Category = 6,
    Priority = 7,
    Repeat = 8,
    AllDay = 9,
    Remind = 10,
    Voice = 11,
    Sound = 12,
    Status = 13,
    Color = 14,
    RepeatInterval = 15,
    RepeatCount = 16,
    Weekdays = 17,
}

/// <summary>CSV 导入选项。</summary>
public sealed class CsvImportOptions
{
    public string FilePath { get; set; } = string.Empty;

    /// <summary>auto / utf-8 / gbk / gb18030 / utf-16。</summary>
    public string EncodingName { get; set; } = "auto";

    /// <summary>分隔符；null 表示自动探测。</summary>
    public char? Delimiter { get; set; }

    /// <summary>表头处理方式。</summary>
    public HeaderMode Header { get; set; } = HeaderMode.Auto;

    /// <summary>列映射（字段 → 列下标）；为空表示自动识别。</summary>
    public Dictionary<CsvField, int>? ColumnMap { get; set; }

    public ImportMode Mode { get; set; } = ImportMode.Append;

    /// <summary>跳过开始时间早于当前时间的日程。</summary>
    public bool SkipPastItems { get; set; }

    /// <summary>按「标题 + 开始时间」跳过重复项。</summary>
    public bool SkipDuplicates { get; set; } = true;

    /// <summary>未指定分类时使用的分类。</summary>
    public string DefaultCategory { get; set; } = "导入";

    /// <summary>未指定颜色时使用的颜色（空 = 跟随分类自动配色）。</summary>
    public string DefaultColor { get; set; } = string.Empty;

    public bool DefaultReminderEnabled { get; set; } = true;

    /// <summary>CSV 未指定提醒时使用的默认档位。</summary>
    public List<int> DefaultReminderOffsets { get; set; } = new() { 10 };

    public int DefaultRemindBeforeMinutes { get; set; } = 10;

    /// <summary>对 03/04/2025 这类歧义日期按「日/月/年」解析。</summary>
    public bool DayFirst { get; set; }

    /// <summary>来源标记（默认取文件名）。</summary>
    public string SourceTag { get; set; } = string.Empty;
}

/// <summary>导入前的预览信息。</summary>
public sealed class CsvPreview
{
    public string EncodingName { get; set; } = string.Empty;

    public char Delimiter { get; set; } = ',';

    public bool HasHeader { get; set; }

    public List<string> Headers { get; set; } = new();

    public Dictionary<CsvField, int> AutoMap { get; set; } = new();

    public List<string[]> SampleRows { get; set; } = new();

    public int TotalDataRows { get; set; }

    public List<string> Warnings { get; set; } = new();
}

/// <summary>单行导入失败记录。</summary>
public sealed class ImportError
{
    public int Line { get; set; }

    public string Message { get; set; } = string.Empty;

    public string Raw { get; set; } = string.Empty;
}

/// <summary>导入结果报告。</summary>
public sealed class ImportReport
{
    public string FilePath { get; set; } = string.Empty;

    public string EncodingName { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int Imported { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public List<ImportError> Errors { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public TimeSpan Elapsed { get; set; }

    public bool HasErrors => Errors.Count > 0;

    public string ToSummary()
    {
        var text = "共 " + TotalRows + " 行：新增 " + Imported + "，更新 " + Updated
            + "，跳过 " + Skipped + "，失败 " + Failed
            + "（用时 " + Elapsed.TotalMilliseconds.ToString("F0") + " ms）";
        return text;
    }
}
