using Schedule.Core.Localization;
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
        ? Loc.T("（一个都没认出来）", "(none recognized)")
        : string.Join(ListSeparator, Recognized.Select(r => CsvColumnMap.DisplayName(r.Field)));

    public string UnknownText => Unknown.Count == 0 ? Loc.T("（无）", "(none)") : string.Join(ListSeparator, Unknown.Take(12)) + (Unknown.Count > 12 ? " …" : string.Empty);

    public string MissingRequiredText => MissingRequired.Count == 0
        ? Loc.T("（无）", "(none)")
        : string.Join(ListSeparator, MissingRequired.Select(CsvSchema.HeaderOf));

    /// <summary>列表分隔符：中文用顿号，英文用逗号。</summary>
    private static string ListSeparator => Loc.T("、", ", ");

    /// <summary>一行版说明（放进警告列表，不换行）。</summary>
    public string DescribeShort()
    {
        if (IsCanonical) return Loc.F("表头符合统一格式（{0} 列全部识别）。", "Header matches the standard format (all {0} columns recognized).", CsvSchema.Headers.Length);

        if (!CanImport) return Loc.F("表头不符合统一格式：缺少必填列 {0}。", "Header does not match the standard format: missing required column(s) {0}.", MissingRequiredText);

        var parts = new List<string>();
        if (Unknown.Count > 0) parts.Add(Loc.F("多余列 {0}（已忽略）", "extra column(s) {0} (ignored)", UnknownText));
        if (Missing.Count > 0) parts.Add(Loc.F("缺少可选列 {0}", "missing optional column(s) {0}", string.Join(ListSeparator, Missing.Select(CsvSchema.HeaderOf))));

        return Loc.F("表头与统一格式有差异：{0}。", "Header differs from the standard format: {0}.", string.Join(Loc.T("；", "; "), parts));
    }

    /// <summary>多行说明问题（界面上的红色提示用）。</summary>
    public string Describe()
    {
        if (IsCanonical) return Loc.F("表头符合统一格式（{0} 列全部识别）。", "Header matches the standard format (all {0} columns recognized).", CsvSchema.Headers.Length);

        if (!CanImport)
        {
            return Loc.F(
                "这个文件的表头不符合统一格式：缺少必填列 {0}。\n识别到的字段：{1}\n没认出来的列：{2}\n\n标准格式的第一行应为：\n{3}\n（也可以在下面的「列映射」里手工指定哪一列是标题 / 开始时间）",
                "The header of this file does not match the standard format: missing required column(s) {0}.\nRecognized fields: {1}\nUnrecognized columns: {2}\n\nThe first row of the standard format should be:\n{3}\n(You can also pick which column is the title / start time under \"Column mapping\" below.)",
                MissingRequiredText,
                RecognizedFieldsText,
                UnknownText,
                CsvSchema.HeaderLine);
        }

        return Loc.F(
            "表头与统一格式略有差异（缺少可选列：{0}；多余列：{1}），可以继续导入。",
            "Header differs slightly from the standard format (missing optional column(s): {0}; extra column(s): {1}), you can continue importing.",
            Missing.Count == 0 ? Loc.T("无", "none") : string.Join(ListSeparator, Missing.Select(CsvSchema.HeaderOf)),
            UnknownText);
    }
}
