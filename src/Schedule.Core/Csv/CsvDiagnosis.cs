using Schedule.Core.Models;

namespace Schedule.Core.Csv;

/// <summary>表头里某一列被识别成了哪个字段。</summary>
public sealed class CsvHeaderMatch
{
    public int Column { get; set; }

    public string Header { get; set; } = string.Empty;

    public CsvField Field { get; set; }

    /// <summary>该字段在统一格式里的标准列名（用于提示"应该叫什么"）。</summary>
    public string CanonicalHeader { get; set; } = string.Empty;
}

/// <summary>
/// 表头诊断结果：这是不是统一格式？哪些列没认出来？标准格式里还缺哪些？
/// 目的很直接——**格式不匹配时必须说人话**，不能默默导入 0 条。
/// </summary>
public sealed class CsvHeaderDiagnosis
{
    public List<string> Headers { get; set; } = new();

    public List<CsvHeaderMatch> Recognized { get; set; } = new();

    /// <summary>没认出来的列名（多余列，会被忽略）。</summary>
    public List<string> Unknown { get; set; } = new();

    /// <summary>标准格式里有、但这个文件里没有的字段。</summary>
    public List<CsvField> Missing { get; set; } = new();

    /// <summary>缺的必填字段（标题、开始时间）。</summary>
    public List<CsvField> MissingRequired { get; set; } = new();

    public bool IsCanonical { get; set; }

    /// <summary>能不能导：必填字段都在才让导。</summary>
    public bool CanImport => MissingRequired.Count == 0;

    /// <summary>识别出的字段名（大写逗号分隔，用于提示）。</summary>
    public string RecognizedFieldsText => Recognized.Count == 0
        ? "（一个都没认出来）"
        : string.Join("、", Recognized.Select(r => CsvColumnMap.DisplayName(r.Field)));

    public string UnknownText => Unknown.Count == 0 ? "（无）" : string.Join("、", Unknown.Take(12)) + (Unknown.Count > 12 ? " …" : string.Empty);

    public string MissingRequiredText => MissingRequired.Count == 0
        ? "（无）"
        : string.Join("、", MissingRequired.Select(CsvSchema.HeaderOf));

    /// <summary>一行版说明（放进警告列表，不换行）。</summary>
    public string DescribeShort()
    {
        if (IsCanonical) return "表头符合统一格式（" + CsvSchema.Headers.Length + " 列全部识别）。";

        if (!CanImport) return "表头不符合统一格式：缺少必填列 " + MissingRequiredText + "。";

        var parts = new List<string>();
        if (Unknown.Count > 0) parts.Add("多余列 " + UnknownText + "（已忽略）");
        if (Missing.Count > 0) parts.Add("缺少可选列 " + string.Join("、", Missing.Select(CsvSchema.HeaderOf)));

        return "表头与统一格式有差异：" + string.Join("；", parts) + "。";
    }

    /// <summary>多行说明问题（界面上的红色提示用）。</summary>
    public string Describe()
    {
        if (IsCanonical) return "表头符合统一格式（" + CsvSchema.Headers.Length + " 列全部识别）。";

        if (!CanImport)
        {
            return "这个文件的表头不符合统一格式：缺少必填列 " + MissingRequiredText
                + "。\n识别到的字段：" + RecognizedFieldsText
                + "\n没认出来的列：" + UnknownText
                + "\n\n标准格式的第一行应为：\n" + CsvSchema.HeaderLine
                + "\n（也可以在下面的「列映射」里手工指定哪一列是标题 / 开始时间）";
        }

        return "表头与统一格式略有差异（缺少可选列：" + (Missing.Count == 0 ? "无" : string.Join("、", Missing.Select(CsvSchema.HeaderOf)))
            + "；多余列：" + UnknownText + "），可以继续导入。";
    }
}
