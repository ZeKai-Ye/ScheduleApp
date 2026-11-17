using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Schedule.App.Mvvm;
using Schedule.App.Services;
using Schedule.Core.Csv;
using Schedule.Core.Ics;
using Schedule.Core.Localization;
using Schedule.Core.Models;
using Schedule.Core.Services;

namespace Schedule.App.Views;

/// <summary>预览用的列选项。</summary>
public sealed class ColumnChoice
{
    public int Index { get; init; }

    public string Text { get; init; } = string.Empty;

    public override string ToString() => Text;
}

/// <summary>字段 → 列的映射行。</summary>
public sealed class ColumnMappingItem : ObservableObject
{
    private ColumnChoice? _selected;

    public CsvField Field { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Hint { get; init; } = string.Empty;

    public List<ColumnChoice> Choices { get; init; } = new();

    public ColumnChoice? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }
}

/// <summary>CSV 导入向导。</summary>
public partial class CsvImportWindow : Window
{
    private readonly AppHost _host;
    private readonly CsvImportService _service = new();
    private readonly IcsImportService _icsService = new();
    private readonly ObservableCollection<ColumnMappingItem> _mappings = new();
    private string _filePath;
    private CsvPreview? _preview;
    private IcsPreview? _icsPreview;
    private bool _isIcs;
    private bool _busy;

    /// <summary>当前文件是不是 iCalendar（自检用）。</summary>
    public bool IsIcsFile => _isIcs;

    /// <summary>格式不匹配等原因导致不能导入时的说明（自检用）。</summary>
    public string? BlockReason { get; private set; }

    /// <summary>报告区当前文本（自检用：导入后必须仍然可见，不能被刷新清掉）。</summary>
    public string ReportTextValue => ReportText.Text;

    /// <summary>可导入的日程条数（预览结果；自检用）。</summary>
    public int ImportableCount => _isIcs ? _icsPreview?.MappedCount ?? 0 : _preview?.TotalDataRows ?? 0;

    public CsvImportWindow(AppHost host, string filePath)
    {
        _host = host;
        _filePath = filePath;

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        FilePathBox.Text = filePath;
        MappingList.ItemsSource = _mappings;

        EncodingBox.Items.Add(new ComboBoxItem { Content = Loc.T("自动识别", "Auto-detect"), Tag = "auto" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "UTF-8", Tag = "utf-8" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "GBK / GB2312", Tag = "gbk" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "GB18030", Tag = "gb18030" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "UTF-16", Tag = "utf-16" });
        EncodingBox.SelectedIndex = EncodingIndex(host.Settings.CsvEncoding);

        HeaderBox.Items.Add(new ComboBoxItem { Content = Loc.T("自动判断", "Auto-detect"), Tag = "auto" });
        HeaderBox.Items.Add(new ComboBoxItem { Content = Loc.T("第一行是表头", "First row is the header"), Tag = "yes" });
        HeaderBox.Items.Add(new ComboBoxItem { Content = Loc.T("没有表头", "No header row"), Tag = "no" });
        HeaderBox.SelectedIndex = 0;

        ModeBox.Items.Add(new ComboBoxItem { Content = Loc.T("追加为新日程", "Append as new events"), Tag = "append" });
        ModeBox.Items.Add(new ComboBoxItem { Content = Loc.T("更新重复项（按标题 + 开始时间）", "Update duplicates (by title + start time)"), Tag = "upsert" });
        ModeBox.Items.Add(new ComboBoxItem { Content = Loc.T("覆盖全部日程（先清空）", "Replace all events (clear first)"), Tag = "replace" });
        ModeBox.SelectedIndex = 0;

        DefaultRemindBox.Text = host.Settings.DefaultReminderOffsets.Count > 0
            ? string.Join(",", host.Settings.DefaultReminderOffsets)
            : host.Settings.DefaultRemindBeforeMinutes.ToString();
        DefaultCategoryBox.Text = string.IsNullOrWhiteSpace(host.Settings.CsvDefaultCategory)
            ? Loc.T("导入", "Imported")
            : host.Settings.CsvDefaultCategory;

        Loaded += (_, _) => Reload();
    }

    /// <summary>解析「30,10,0」形式的默认档位。</summary>
    private static List<int> ParseOffsets(string? text)
    {
        var result = new List<int>();
        foreach (var part in (text ?? string.Empty).Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var minutes) && minutes >= 0) result.Add(minutes);
        }

        return result.Distinct().OrderByDescending(v => v).ToList();
    }

    private static int EncodingIndex(string encoding) => encoding?.ToLowerInvariant() switch
    {
        "utf-8" or "utf8" => 1,
        "gbk" or "gb2312" or "cp936" => 2,
        "gb18030" => 3,
        "utf-16" or "utf16" or "unicode" => 4,
        _ => 0,
    };

    private string SelectedTag(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("选择要导入的日程文件", "Choose the event file to import"),
            Filter = Loc.T("日程文件", "Event files") + " (*.csv;*.ics;*.txt)|*.csv;*.ics;*.txt"
                + "|" + Loc.T("CSV 文件", "CSV files") + " (*.csv;*.txt)|*.csv;*.txt"
                + "|iCalendar (*.ics)|*.ics"
                + "|" + Loc.T("所有文件", "All files") + " (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        _filePath = dialog.FileName;
        FilePathBox.Text = _filePath;
        ReportText.Text = string.Empty; // 换了文件，旧报告就作废
        Reload();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private CsvImportOptions BuildOptions(bool includeMap)
    {
        var header = SelectedTag(HeaderBox) switch
        {
            "yes" => HeaderMode.Present,
            "no" => HeaderMode.Absent,
            _ => HeaderMode.Auto,
        };

        var mode = SelectedTag(ModeBox) switch
        {
            "upsert" => ImportMode.Upsert,
            "replace" => ImportMode.Replace,
            _ => ImportMode.Append,
        };

        var options = new CsvImportOptions
        {
            FilePath = _filePath,
            EncodingName = SelectedTag(EncodingBox),
            Header = header,
            Mode = mode,
            SkipDuplicates = SkipDuplicatesCheck.IsChecked == true,
            SkipPastItems = SkipPastCheck.IsChecked == true,
            DefaultCategory = DefaultCategoryBox.Text.Trim(),
            DefaultColor = _host.Settings.DefaultEventColor,
            DefaultReminderEnabled = _host.Settings.DefaultReminderEnabled,
            DefaultReminderOffsets = ParseOffsets(DefaultRemindBox.Text),
            DefaultRemindBeforeMinutes = _host.Settings.DefaultRemindBeforeMinutes,
            SourceTag = Path.GetFileName(_filePath),
        };

        if (includeMap)
        {
            var map = new Dictionary<CsvField, int>();
            foreach (var mapping in _mappings)
            {
                if (mapping.Selected == null || mapping.Selected.Index < 0) continue;
                map[mapping.Field] = mapping.Selected.Index;
            }

            if (map.Count > 0) options.ColumnMap = map;
        }

        return options;
    }

    private void Reload()
    {
        if (_busy) return;
        _busy = true;

        try
        {
            _isIcs = DetectIcs();
            FormatHintText.Text = Loc.T("统一格式表头：", "Standard header row: ") + CsvSchema.HeaderLine;

            if (_isIcs) ReloadIcs();
            else ReloadCsv();
        }
        catch (Exception ex)
        {
            ReportText.Text = Loc.T("预览失败：", "Preview failed: ") + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>看扩展名，再看文件开头有没有 BEGIN:VCALENDAR。</summary>
    private bool DetectIcs()
    {
        if (_filePath.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(_filePath)) return false;

        try
        {
            using var reader = new StreamReader(_filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[2048];
            var read = reader.Read(buffer, 0, buffer.Length);

            return new string(buffer, 0, read).Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ReloadCsv()
    {
        FileKindText.Text = Loc.T("CSV 文件（统一格式）", "CSV file (standard format)");
        MappingCard.Visibility = Visibility.Visible;
        SetCsvOptionsEnabled(true);

        var options = BuildOptions(includeMap: false);
        var preview = _service.Preview(options);
        _preview = preview;
        _icsPreview = null;

        EncodingText.Text = Loc.T("识别编码：", "Detected encoding: ") + preview.EncodingName + Loc.T("　分隔符：", "  ·  Delimiter: ") + DescribeDelimiter(preview.Delimiter);
        WarningText.Text = preview.Warnings.Count == 0 ? string.Empty : string.Join("　", preview.Warnings);
        PreviewTitle.Text = Loc.F("数据预览（共 {0} 行数据，显示前 {1} 行{2}）", "Data preview ({0} rows of data, showing the first {1}{2})",
                            preview.TotalDataRows, preview.SampleRows.Count,
                            preview.HasHeader ? Loc.T("，已识别表头", ", header row detected") : Loc.T("，未检测到表头", ", no header row detected"));

        BuildMappingUi(preview);
        PreviewBox.Text = BuildPreviewText(preview);
        ApplyBlockState(preview.BlockReason);
    }

    private void ReloadIcs()
    {
        FileKindText.Text = Loc.T("iCalendar 文件（.ics）", "iCalendar file (.ics)");
        MappingCard.Visibility = Visibility.Collapsed;
        SetCsvOptionsEnabled(false);

        var options = new IcsImportOptions
        {
            Mode = SelectedTag(ModeBox) switch
            {
                "upsert" => ImportMode.Upsert,
                "replace" => ImportMode.Replace,
                _ => ImportMode.Append,
            },
            SkipDuplicates = SkipDuplicatesCheck.IsChecked == true,
            SkipPastItems = SkipPastCheck.IsChecked == true,
            SourceTag = Path.GetFileName(_filePath),
        };

        var preview = _icsService.Preview(options, _filePath);
        _icsPreview = preview;
        _preview = null;

        EncodingText.Text = Loc.T("识别编码：", "Detected encoding: ") + preview.EncodingName;
        PreviewTitle.Text = Loc.T("iCalendar 预览", "iCalendar preview");
        WarningText.Text = preview.Warnings.Count == 0
            ? string.Empty
            : string.Join(Loc.T("；", "; "), preview.Warnings.Take(3)) + (preview.Warnings.Count > 3 ? Loc.F("　…（共 {0} 条提示）", " … ({0} notes in total)", preview.Warnings.Count) : string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine(preview.LooksLikeCalendar ? Loc.T("已识别为 iCalendar 文件。", "Recognised as an iCalendar file.") : Loc.T("没有找到 BEGIN:VCALENDAR，可能不是标准 ICS。", "BEGIN:VCALENDAR was not found, so this may not be a standard ICS file."));
        sb.AppendLine(preview.ToSummary());

        if (preview.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("提示：", "Notes:"));
            foreach (var warning in preview.Warnings.Take(20)) sb.AppendLine("· " + warning);
            if (preview.Warnings.Count > 20) sb.AppendLine(Loc.F("（还有 {0} 条）", "({0} more)", preview.Warnings.Count - 20));
        }

        if (preview.RowErrors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("无法导入的事件：", "Events that cannot be imported:"));
            foreach (var error in preview.RowErrors.Take(20)) sb.AppendLine(Loc.F("· 第 {0} 行：{1}", "· Line {0}: {1}", error.Line, error.Message));
        }

        PreviewBox.Text = sb.ToString();
        ApplyBlockState(preview.BlockReason);
    }

    /// <summary>ICS 模式下编码 / 表头 / 默认值这些 CSV 专属选项没有意义。</summary>
    private void SetCsvOptionsEnabled(bool enabled)
    {
        EncodingBox.IsEnabled = enabled;
        HeaderBox.IsEnabled = enabled;
        DefaultRemindBox.IsEnabled = enabled;
        DefaultCategoryBox.IsEnabled = enabled;
    }

    /// <summary>格式不匹配时：红字说明 + 禁用「开始导入」，绝不让用户以为"导入成功了但没数据"。</summary>
    private void ApplyBlockState(string? blockReason)
    {
        BlockReason = blockReason;

        if (string.IsNullOrWhiteSpace(blockReason))
        {
            BlockPanel.Visibility = Visibility.Collapsed;
            BlockText.Text = string.Empty;
            ImportButton.IsEnabled = true;
            return;
        }

        BlockPanel.Visibility = Visibility.Visible;
        BlockText.Text = blockReason;
        ImportButton.IsEnabled = false;
    }

    private static string DescribeDelimiter(char delimiter) => delimiter switch
    {
        ',' => Loc.T("逗号", "comma"),
        ';' => Loc.T("分号", "semicolon"),
        '\t' => Loc.T("制表符", "tab"),
        '|' => Loc.T("竖线", "pipe"),
        _ => delimiter.ToString(),
    };

    private void BuildMappingUi(CsvPreview preview)
    {
        var choices = new List<ColumnChoice> { new() { Index = -1, Text = Loc.T("（忽略此列）", "(ignore this column)") } };
        for (var i = 0; i < preview.Headers.Count; i++)
        {
            choices.Add(new ColumnChoice { Index = i, Text = Loc.F("第 {0} 列：{1}", "Column {0}: {1}", i + 1, preview.Headers[i]) });
        }

        _mappings.Clear();
        foreach (var field in CsvColumnMap.AllFields)
        {
            var columnIndex = preview.AutoMap.TryGetValue(field, out var mapped) ? mapped : -1;

            _mappings.Add(new ColumnMappingItem
            {
                Field = field,
                Name = CsvColumnMap.DisplayName(field),
                Hint = CsvColumnMap.Hint(field),
                Choices = choices,
                Selected = choices.FirstOrDefault(c => c.Index == columnIndex) ?? choices[0],
            });
        }
    }

    private static string BuildPreviewText(CsvPreview preview)
    {
        if (preview.Headers.Count == 0) return Loc.T("（没有可显示的数据）", "(no data to display)");

        var widths = new int[preview.Headers.Count];
        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = DisplayWidth(preview.Headers[i]);
        }

        foreach (var row in preview.SampleRows)
        {
            for (var i = 0; i < widths.Length && i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], DisplayWidth(row[i]));
            }
        }

        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = Math.Min(widths[i] + 2, 32);
        }

        var sb = new StringBuilder();
        AppendRow(sb, preview.Headers.ToArray(), widths);
        sb.AppendLine(new string('-', Math.Min(widths.Sum(), 160)));

        foreach (var row in preview.SampleRows)
        {
            AppendRow(sb, row, widths);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string[] row, int[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            var cell = i < row.Length ? row[i] : string.Empty;
            if (cell.Length > widths[i]) cell = cell.Substring(0, Math.Max(0, widths[i] - 1)) + "…";
            sb.Append(cell);

            var padding = widths[i] - DisplayWidth(cell);
            if (padding > 0) sb.Append(' ', padding);
        }

        sb.AppendLine();
    }

    /// <summary>粗略计算显示宽度（中日韩字符按 2 计）。</summary>
    private static int DisplayWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var width = 0;
        foreach (var c in text)
        {
            width += c >= 0x1100 && (c <= 0x115F || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6)) ? 2 : 1;
        }

        return width;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTag(ModeBox) == "replace")
        {
            var confirm = MessageBox.Show(this, Loc.T("「覆盖全部日程」会先清空现有全部日程，确定继续吗？", "“Replace all events” clears every existing event first. Continue?"),
                Loc.T("导入", "Import"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
        }

        var text = RunImport();
        var finishedMark = Loc.T("导入完成", "Import finished");

        // 一条都没进来时必须弹窗说清楚原因，不能只把报告写在角落里
        // 「新增 0，更新 0」来自 Core 的 ImportReport.ToSummary()，跟随 Core 侧的语言实现
        if (text.Contains(finishedMark, StringComparison.Ordinal) && text.Contains("新增 0，更新 0", StringComparison.Ordinal))
        {
            MessageBox.Show(this, FirstLines(text, 12), Loc.T("没有任何日程被导入", "No events were imported"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 执行导入并把报告写进界面（返回报告文本）。
    /// 抽成公开方法既供按钮调用，也供冒烟自检直接跑一遍（保证报告不会被后续刷新清掉）。
    /// </summary>
    public string RunImport()
    {
        if (!File.Exists(_filePath))
        {
            ReportText.Text = Loc.F("导入失败：文件不存在（{0}）", "Import failed: file not found ({0})", _filePath);
            return ReportText.Text;
        }

        try
        {
            return _isIcs ? RunIcsImport() : RunCsvImport();
        }
        catch (Exception ex)
        {
            ReportText.Text = Loc.T("导入失败：", "Import failed: ") + ex.Message;
            return ReportText.Text;
        }
    }

    private string RunCsvImport()
    {
        var options = BuildOptions(includeMap: true);
        var items = _host.Repository.Snapshot();
        var report = _service.Import(options, items);

        if (report.Blocked)
        {
            ReportText.Text = Loc.T("已终止导入（未改动任何日程）：", "Import aborted (no events were changed): ") + Environment.NewLine + report.BlockReason;
            ApplyBlockState(report.BlockReason);
            return ReportText.Text;
        }

        _host.Repository.ReplaceAll(items);
        _host.Settings.CsvEncoding = options.EncodingName;
        _host.Settings.CsvDefaultCategory = options.DefaultCategory;

        return FinishImport(report, "CSV");
    }

    private string RunIcsImport()
    {
        var options = new IcsImportOptions
        {
            Mode = SelectedTag(ModeBox) switch
            {
                "upsert" => ImportMode.Upsert,
                "replace" => ImportMode.Replace,
                _ => ImportMode.Append,
            },
            SkipDuplicates = SkipDuplicatesCheck.IsChecked == true,
            SkipPastItems = SkipPastCheck.IsChecked == true,
            SourceTag = Path.GetFileName(_filePath),
        };

        var items = _host.Repository.Snapshot();
        var report = _icsService.Import(options, _filePath, items);

        if (report.Blocked)
        {
            ReportText.Text = Loc.T("已终止导入（未改动任何日程）：", "Import aborted (no events were changed): ") + Environment.NewLine + report.BlockReason;
            ApplyBlockState(report.BlockReason);
            return ReportText.Text;
        }

        _host.Repository.ReplaceAll(items);
        return FinishImport(report, "iCalendar");
    }

    /// <summary>落盘 + 记历史 + 写报告；报告写完**不刷新掉**（Reload 不再清空报告区）。</summary>
    private string FinishImport(ImportReport report, string kind)
    {
        _host.Repository.Save(force: true);

        _host.ImportHistory.Add(new ImportRecord
        {
            FilePath = _filePath,
            EncodingName = report.EncodingName,
            Total = report.TotalRows,
            Imported = report.Imported,
            Updated = report.Updated,
            Skipped = report.Skipped,
            Failed = report.Failed,
        });

        _host.SaveAll();

        var text = new StringBuilder();
        text.AppendLine(Loc.T("导入完成：", "Import finished: ") + report.ToSummary());
        text.AppendLine(Loc.T("来源：", "Source: ") + kind + Loc.T("　文件：", "  ·  File: ") + Path.GetFileName(_filePath));

        foreach (var warning in report.Warnings) text.AppendLine(Loc.T("[提示] ", "[Note] ") + warning);
        foreach (var error in report.Errors.Take(30)) text.AppendLine(Loc.F("[第 {0} 行] {1}", "[Line {0}] {1}", error.Line, error.Message));
        if (report.Errors.Count > 30) text.AppendLine(Loc.F("（还有 {0} 条错误未显示）", "({0} more errors not shown)", report.Errors.Count - 30));

        ReportText.Text = text.ToString();

        // 刷新预览，但保留上面的报告（之前这里会顺手把报告清空，用户什么都看不到）
        Reload();

        return text.ToString();
    }

    private static string FirstLines(string text, int count)
    {
        var lines = text.Split('\n');
        return string.Join(Environment.NewLine, lines.Take(count)).TrimEnd();
    }

    /// <summary>把统一格式的模板另存出去（照填即可导入成功）。</summary>
    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("保存 CSV 模板", "Save CSV template"),
            Filter = Loc.T("CSV 文件", "CSV files") + " (*.csv)|*.csv",
            FileName = "日程导入模板.csv",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            CsvSchema.WriteTemplate(dialog.FileName);
            ReportText.Text = Loc.T("模板已保存：", "Template saved: ") + dialog.FileName + Environment.NewLine
                + Loc.T("第一行是统一格式的表头，下面两行是示例，照着填就能导入。", "The first line is the standard header row and the next two lines are examples; fill them in the same way to import.") + Environment.NewLine
                + Environment.NewLine + CsvSchema.HeaderLine;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("保存模板失败：", "Failed to save the template: ") + ex.Message, Loc.T("导入", "Import"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>列出统一格式的每一列（含义 / 是否必填 / 示例）。</summary>
    private void ShowFormat_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(this, CsvSchema.DescribeColumns(), Loc.T("CSV 统一格式", "Standard CSV format"), MessageBoxButton.OK, MessageBoxImage.Information);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
