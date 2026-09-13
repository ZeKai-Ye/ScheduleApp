using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Schedule.App.Mvvm;
using Schedule.App.Services;
using Schedule.Core.Csv;
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
    private readonly ObservableCollection<ColumnMappingItem> _mappings = new();
    private string _filePath;
    private CsvPreview? _preview;
    private bool _busy;

    public CsvImportWindow(AppHost host, string filePath)
    {
        _host = host;
        _filePath = filePath;

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        FilePathBox.Text = filePath;
        MappingList.ItemsSource = _mappings;

        EncodingBox.Items.Add(new ComboBoxItem { Content = "自动识别", Tag = "auto" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "UTF-8", Tag = "utf-8" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "GBK / GB2312", Tag = "gbk" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "GB18030", Tag = "gb18030" });
        EncodingBox.Items.Add(new ComboBoxItem { Content = "UTF-16", Tag = "utf-16" });
        EncodingBox.SelectedIndex = EncodingIndex(host.Settings.CsvEncoding);

        HeaderBox.Items.Add(new ComboBoxItem { Content = "自动判断", Tag = "auto" });
        HeaderBox.Items.Add(new ComboBoxItem { Content = "第一行是表头", Tag = "yes" });
        HeaderBox.Items.Add(new ComboBoxItem { Content = "没有表头", Tag = "no" });
        HeaderBox.SelectedIndex = 0;

        ModeBox.Items.Add(new ComboBoxItem { Content = "追加为新日程", Tag = "append" });
        ModeBox.Items.Add(new ComboBoxItem { Content = "更新重复项（按标题 + 开始时间）", Tag = "upsert" });
        ModeBox.Items.Add(new ComboBoxItem { Content = "覆盖全部日程（先清空）", Tag = "replace" });
        ModeBox.SelectedIndex = 0;

        DefaultRemindBox.Text = host.Settings.DefaultRemindBeforeMinutes.ToString();
        DefaultCategoryBox.Text = host.Settings.CsvDefaultCategory;

        Loaded += (_, _) => Reload();
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
            Title = "选择要导入的 CSV 文件",
            Filter = "CSV 文件 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        _filePath = dialog.FileName;
        FilePathBox.Text = _filePath;
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
            DefaultReminderEnabled = _host.Settings.DefaultReminderEnabled,
            DefaultRemindBeforeMinutes = int.TryParse(DefaultRemindBox.Text, out var minutes)
                ? Math.Max(0, minutes)
                : _host.Settings.DefaultRemindBeforeMinutes,
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
            var options = BuildOptions(includeMap: false);
            var preview = _service.Preview(options);
            _preview = preview;

            EncodingText.Text = "识别编码：" + preview.EncodingName + "　分隔符：" + DescribeDelimiter(preview.Delimiter);
            WarningText.Text = preview.Warnings.Count == 0 ? string.Empty : string.Join("　", preview.Warnings);
            PreviewTitle.Text = "数据预览（共 " + preview.TotalDataRows + " 行数据，显示前 " + preview.SampleRows.Count + " 行" +
                                (preview.HasHeader ? "，已识别表头" : "，未检测到表头") + "）";

            BuildMappingUi(preview);
            PreviewBox.Text = BuildPreviewText(preview);
            ReportText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            ReportText.Text = "预览失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static string DescribeDelimiter(char delimiter) => delimiter switch
    {
        ',' => "逗号",
        ';' => "分号",
        '\t' => "制表符",
        '|' => "竖线",
        _ => delimiter.ToString(),
    };

    private void BuildMappingUi(CsvPreview preview)
    {
        var choices = new List<ColumnChoice> { new() { Index = -1, Text = "（忽略此列）" } };
        for (var i = 0; i < preview.Headers.Count; i++)
        {
            choices.Add(new ColumnChoice { Index = i, Text = "第 " + (i + 1) + " 列：" + preview.Headers[i] });
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
        if (preview.Headers.Count == 0) return "（没有可显示的数据）";

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
        if (!File.Exists(_filePath))
        {
            MessageBox.Show(this, "文件不存在：" + _filePath, "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedTag(ModeBox) == "replace")
        {
            var confirm = MessageBox.Show(this, "「覆盖全部日程」会先清空现有全部日程，确定继续吗？", "导入",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
        }

        try
        {
            var options = BuildOptions(includeMap: true);
            var items = _host.Repository.Snapshot();
            var report = _service.Import(options, items);

            _host.Repository.ReplaceAll(items);
            _host.Repository.Save(force: true);

            _host.Settings.CsvEncoding = options.EncodingName;
            _host.Settings.CsvDefaultCategory = options.DefaultCategory;
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
            text.AppendLine("导入完成：" + report.ToSummary());
            foreach (var warning in report.Warnings) text.AppendLine("[提示] " + warning);
            foreach (var error in report.Errors.Take(30)) text.AppendLine("[第 " + error.Line + " 行] " + error.Message);
            if (report.Errors.Count > 30) text.AppendLine("（还有 " + (report.Errors.Count - 30) + " 条错误未显示）");

            ReportText.Text = text.ToString();
            Reload();
        }
        catch (Exception ex)
        {
            ReportText.Text = "导入失败：" + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
