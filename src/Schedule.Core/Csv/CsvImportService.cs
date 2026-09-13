using System.Diagnostics;
using Schedule.Core.Models;

namespace Schedule.Core.Csv;

/// <summary>
/// CSV 导入服务：编码识别 → 解析 → 表头 / 列自动映射 → 逐行构造日程 → 去重与更新。
/// 全过程不抛异常，所有问题都以 <see cref="ImportReport"/> 的形式返回。
/// </summary>
public sealed class CsvImportService
{
    private readonly Func<DateTime> _clock;

    public CsvImportService(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>读取并解析文件，返回预览信息（不修改任何数据）。</summary>
    public CsvPreview Preview(CsvImportOptions options)
    {
        var source = ReadText(options);
        var delimiter = options.Delimiter ?? CsvReader.DetectDelimiter(source.Text);
        var rows = CsvReader.Parse(source.Text, delimiter);

        var preview = new CsvPreview
        {
            EncodingName = source.EncodingName,
            Delimiter = delimiter,
        };

        if (!string.IsNullOrEmpty(source.Note)) preview.Warnings.Add(source.Note!);

        if (rows.Count == 0)
        {
            preview.Warnings.Add("文件中没有可解析的数据行。");
            return preview;
        }

        var hasHeader = ResolveHeaderMode(options.Header, rows[0]);
        var headers = BuildHeaders(rows, hasHeader);

        preview.HasHeader = hasHeader;
        preview.Headers = headers;
        preview.TotalDataRows = hasHeader ? rows.Count - 1 : rows.Count;
        preview.AutoMap = options.ColumnMap is { Count: > 0 }
            ? new Dictionary<CsvField, int>(options.ColumnMap)
            : hasHeader
                ? CsvColumnMap.AutoMap(headers)
                : CsvColumnMap.DefaultPositionalMap(headers.Count);
        preview.SampleRows = (hasHeader ? rows.Skip(1) : rows)
            .Take(30)
            .Select(r => PadRow(r, headers.Count))
            .ToList();

        if (!preview.AutoMap.ContainsKey(CsvField.Title))
        {
            preview.Warnings.Add("未识别到「标题」列，缺少标题的行会被跳过。");
        }

        if (!preview.AutoMap.ContainsKey(CsvField.Start) && !preview.AutoMap.ContainsKey(CsvField.Date))
        {
            preview.Warnings.Add("未识别到开始时间列，若无法解析时间将按默认时间处理。");
        }

        return preview;
    }

    /// <summary>执行导入。target 会被就地修改。</summary>
    public ImportReport Import(CsvImportOptions options, IList<ScheduleItem> target)
    {
        var stopwatch = Stopwatch.StartNew();
        var report = new ImportReport
        {
            FilePath = options.FilePath,
            EncodingName = string.Empty,
        };

        var source = ReadText(options);
        report.EncodingName = source.EncodingName;
        if (!string.IsNullOrEmpty(source.Note)) report.Warnings.Add(source.Note!);

        var delimiter = options.Delimiter ?? CsvReader.DetectDelimiter(source.Text);
        var rows = CsvReader.Parse(source.Text, delimiter);
        if (rows.Count == 0)
        {
            report.Warnings.Add("文件中没有可解析的数据行。");
            report.Elapsed = stopwatch.Elapsed;
            return report;
        }

        var hasHeader = ResolveHeaderMode(options.Header, rows[0]);
        var headers = BuildHeaders(rows, hasHeader);
        var dataRows = hasHeader ? rows.Skip(1).ToList() : rows;

        var map = options.ColumnMap is { Count: > 0 }
            ? new Dictionary<CsvField, int>(options.ColumnMap)
            : hasHeader
                ? CsvColumnMap.AutoMap(headers)
                : CsvColumnMap.DefaultPositionalMap(headers.Count);

        if (!map.ContainsKey(CsvField.Title))
        {
            report.Warnings.Add("未识别到「标题」列。");
        }

        var now = _clock();
        var sourceTag = string.IsNullOrWhiteSpace(options.SourceTag)
            ? Path.GetFileName(options.FilePath)
            : options.SourceTag;

        if (options.Mode == ImportMode.Replace)
        {
            target.Clear();
        }

        var index = new Dictionary<string, ScheduleItem>(StringComparer.OrdinalIgnoreCase);
        if (options.Mode != ImportMode.Replace)
        {
            foreach (var existing in target)
            {
                index[DuplicateKey(existing.Title, existing.Start)] = existing;
            }
        }

        var lineNumber = hasHeader ? 2 : 1;
        foreach (var row in dataRows)
        {
            report.TotalRows++;
            var raw = string.Join(delimiter.ToString(), row);

            var item = BuildItem(row, map, options, now, sourceTag, out var error);
            if (item == null)
            {
                report.Failed++;
                report.Errors.Add(new ImportError { Line = lineNumber, Message = error ?? "无法解析该行", Raw = Shorten(raw) });
                lineNumber++;
                continue;
            }

            var key = DuplicateKey(item.Title, item.Start);
            if (index.TryGetValue(key, out var existing))
            {
                if (options.Mode == ImportMode.Upsert
                    && !ReferenceEquals(existing, item)
                    && target.Contains(existing))
                {
                    ApplyTo(existing, item, now);
                    report.Updated++;
                    lineNumber++;
                    continue;
                }

                if (options.SkipDuplicates && target.Contains(existing))
                {
                    report.Skipped++;
                    lineNumber++;
                    continue;
                }
            }

            if (options.SkipPastItems && item.EffectiveEnd < now)
            {
                report.Skipped++;
                lineNumber++;
                continue;
            }

            target.Add(item);
            index[key] = item;
            report.Imported++;
            lineNumber++;
        }

        if (report.Updated > 0)
        {
            report.Warnings.Add("已按「标题 + 开始时间」更新 " + report.Updated + " 条重复日程。");
        }

        report.Elapsed = stopwatch.Elapsed;
        return report;
    }

    private static ScheduleItem? BuildItem(
        string[] row,
        Dictionary<CsvField, int> map,
        CsvImportOptions options,
        DateTime now,
        string sourceTag,
        out string? error)
    {
        error = null;

        string Get(CsvField field)
        {
            if (!map.TryGetValue(field, out var column)) return string.Empty;
            if (column < 0 || column >= row.Length) return string.Empty;
            return row[column].Trim();
        }

        var title = TextNormalizer.Clean(Get(CsvField.Title));
        if (title.Length == 0)
        {
            error = "缺少标题（标题列必填）";
            return null;
        }

        var dateText = Get(CsvField.Date);
        var startText = Get(CsvField.Start);
        var endText = Get(CsvField.End);

        DateTime? datePart = null;
        if (dateText.Length > 0 && FlexibleDateTime.TryParse(dateText, out var parsedDate, null, options.DayFirst))
        {
            datePart = parsedDate;
        }

        DateTime start;
        bool startHasTime;
        if (startText.Length > 0)
        {
            if (!FlexibleDateTime.TryParse(startText, out start, datePart, options.DayFirst))
            {
                error = "无法解析开始时间：" + startText;
                return null;
            }

            startHasTime = HasTimeComponent(startText);
        }
        else if (datePart.HasValue)
        {
            start = datePart.Value;
            startHasTime = false;
        }
        else
        {
            error = "缺少开始时间";
            return null;
        }

        var item = new ScheduleItem
        {
            Title = title,
            Start = start,
            Category = TextNormalizer.Clean(Get(CsvField.Category)),
            Notes = Get(CsvField.Notes).Trim(),
            Origin = ItemOrigin.CsvImport,
            Source = sourceTag,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (item.Category.Length == 0) item.Category = options.DefaultCategory;

        // 结束时间
        if (endText.Length > 0)
        {
            if (FlexibleDateTime.TryParse(endText, out var end, start.Date, options.DayFirst))
            {
                if (end < start) end = end.AddDays(1);
                item.End = end;
            }
        }

        // 全天
        if (FieldParsers.TryParseAllDay(Get(CsvField.AllDay), out var allDay))
        {
            item.AllDay = allDay;
        }
        else
        {
            var endHasTime = endText.Length > 0 && HasTimeComponent(endText);
            item.AllDay = !startHasTime && !endHasTime;
        }

        if (item.AllDay)
        {
            item.Start = start.Date;
            if (item.End.HasValue && item.End.Value.TimeOfDay == TimeSpan.Zero)
            {
                item.End = item.End.Value.Date.AddDays(1);
            }
        }

        FieldParsers.TryParsePriority(Get(CsvField.Priority), out var priority);
        item.Priority = priority;

        FieldParsers.TryParseRepeat(Get(CsvField.Repeat), out var repeat);
        item.Repeat = repeat;

        FieldParsers.TryParseStatus(Get(CsvField.Status), out var status);
        item.Status = status;

        // 提醒
        var remindText = Get(CsvField.Remind);
        if (FieldParsers.TryParseRemind(remindText, item.Start, out var spec))
        {
            item.ReminderEnabled = spec.Enabled;
            if (spec.Absolute.HasValue)
            {
                item.RemindAtOverride = spec.Absolute;
                var offset = (int)Math.Round((item.Start - spec.Absolute.Value).TotalMinutes);
                item.RemindBeforeMinutes = offset > 0 ? offset : 0;
            }
            else
            {
                item.RemindBeforeMinutes = spec.BeforeMinutes;
            }
        }
        else
        {
            item.ReminderEnabled = options.DefaultReminderEnabled;
            item.RemindBeforeMinutes = options.DefaultRemindBeforeMinutes;
        }

        item.VoiceText = Get(CsvField.Voice).Trim();
        item.SoundFile = Get(CsvField.Sound).Trim();

        return item;
    }

    private static void ApplyTo(ScheduleItem target, ScheduleItem source, DateTime now)
    {
        target.Title = source.Title;
        target.Notes = source.Notes;
        target.Category = source.Category;
        target.Start = source.Start;
        target.End = source.End;
        target.AllDay = source.AllDay;
        target.Priority = source.Priority;
        target.Repeat = source.Repeat;
        target.RepeatUntil = source.RepeatUntil;
        target.ReminderEnabled = source.ReminderEnabled;
        target.RemindBeforeMinutes = source.RemindBeforeMinutes;
        target.RemindAtOverride = source.RemindAtOverride;
        target.VoiceText = source.VoiceText;
        target.SoundFile = source.SoundFile;
        target.Status = source.Status;
        target.Origin = ItemOrigin.CsvImport;
        target.Source = source.Source;
        target.UpdatedAt = now;
        target.LastFiredOccurrence = null;
    }

    private CsvTextSource ReadText(CsvImportOptions options)
    {
        if (!File.Exists(options.FilePath))
        {
            return new CsvTextSource
            {
                Text = string.Empty,
                EncodingName = "文件不存在",
                Note = "找不到文件：" + options.FilePath,
            };
        }

        try
        {
            return CsvEncodingDetector.ReadFile(options.FilePath, options.EncodingName);
        }
        catch (Exception ex)
        {
            return new CsvTextSource
            {
                Text = string.Empty,
                EncodingName = "读取失败",
                Note = "读取文件失败：" + ex.Message,
            };
        }
    }

    private static bool ResolveHeaderMode(HeaderMode mode, string[] firstRow) => mode switch
    {
        HeaderMode.Present => true,
        HeaderMode.Absent => false,
        _ => LooksLikeHeader(firstRow),
    };

    /// <summary>启发式判断首行是否是表头：含已知字段名，且几乎没有可解析的日期时间。</summary>
    public static bool LooksLikeHeader(string[] row)
    {
        if (row.Length == 0) return false;

        var aliasHits = 0;
        var dateHits = 0;

        foreach (var cell in row)
        {
            if (string.IsNullOrWhiteSpace(cell)) continue;
            if (CsvColumnMap.Suggest(cell) != CsvField.Ignore) aliasHits++;
            if (FlexibleDateTime.TryParse(cell, out _)) dateHits++;
        }

        if (aliasHits == 0) return false;
        return dateHits == 0 || aliasHits >= dateHits;
    }

    public static List<string> BuildHeaders(List<string[]> rows, bool hasHeader)
    {
        if (hasHeader)
        {
            var first = rows[0];
            var headers = new List<string>(first.Length);
            for (var i = 0; i < first.Length; i++)
            {
                var name = TextNormalizer.Clean(first[i]);
                headers.Add(name.Length == 0 ? "列" + (i + 1) : name);
            }

            return headers;
        }

        var count = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var generated = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            generated.Add("列" + (i + 1));
        }

        return generated;
    }

    private static string[] PadRow(string[] row, int count)
    {
        if (row.Length == count) return row;
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = i < row.Length ? row[i] : string.Empty;
        }

        return result;
    }

    private static bool HasTimeComponent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains(':') || text.Contains('点') || text.Contains('时')
            || text.Contains("AM", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PM", StringComparison.OrdinalIgnoreCase);
    }

    public static string DuplicateKey(string title, DateTime start)
        => TextNormalizer.Key(title) + "|" + start.ToString("yyyyMMddHHmmss");

    private static string Shorten(string text)
        => text.Length <= 120 ? text : text.Substring(0, 117) + "...";
}
