namespace Schedule.Core.Csv;

/// <summary>解码后的 CSV 文本及其来源信息。</summary>
public sealed class CsvTextSource
{
    public string Text { get; init; } = string.Empty;

    /// <summary>人类可读的编码名称，例如 "UTF-8"、"GBK/GB18030（自动识别）"。</summary>
    public string EncodingName { get; init; } = string.Empty;

    public bool HasBom { get; init; }

    /// <summary>编码为推测结果（无 BOM 且非 UTF-8）。</summary>
    public bool IsGuessed { get; init; }

    /// <summary>是否为兜底解码（可能含替换字符）。</summary>
    public bool IsFallback { get; init; }

    public string? Note { get; init; }
}
