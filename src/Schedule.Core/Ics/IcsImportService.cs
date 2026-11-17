using System.Diagnostics;
using Schedule.Core.Localization;
using Schedule.Core.Models;

namespace Schedule.Core.Ics;

/// <summary>
/// ICS 导入服务：读文件 → 解析 → 映射 → 去重 / 更新 / 覆盖。
/// 与 CSV 导入保持同一套语义与同一份 <see cref="ImportReport"/>，界面上两条路一致。
/// </summary>
public sealed class IcsImportService
{
    private readonly Func<DateTime> _clock;

    public IcsImportService(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>解析但不改动数据（预览用）。</summary>
    public IcsPreview Preview(IcsImportOptions options, string filePath)
    {
        var preview = new IcsPreview();

        var source = ReadSource(filePath);
        preview.EncodingName = source.EncodingName;
        if (!string.IsNullOrEmpty(source.Note)) preview.Warnings.Add(source.Note!);

        var parsed = IcsReader.Parse(source.Text);
        preview.LooksLikeCalendar = parsed.LooksLikeCalendar;
        preview.EventCount = parsed.Events.Count;
        preview.RecurringCount = parsed.Events.Count(e => e.Find("RRULE") != null);
        preview.AlarmCount = parsed.Events.Sum(e => e.Alarms.Count);
        preview.ExcludedCount = parsed.Events.Sum(e => e.FindAll("EXDATE").Count());
        preview.OverrideCount = parsed.Events.Count(e => e.IsOverride);

        preview.Warnings.AddRange(parsed.Warnings);
        foreach (var (line, message) in parsed.Errors) preview.Warnings.Add(Loc.F("第 {0} 行：{1}", "Line {0}: {1}", line, message));

        if (!parsed.LooksLikeCalendar && parsed.Events.Count == 0)
        {
            preview.BlockReason = Loc.T("这个文件看起来不是 iCalendar：没有找到 BEGIN:VCALENDAR / BEGIN:VEVENT。\n请选择 Google 日历 / Outlook / Apple 日历导出的 .ics 文件，或改用 CSV 导入。", "This file does not look like iCalendar: no BEGIN:VCALENDAR / BEGIN:VEVENT was found.\nPlease pick an .ics file exported from Google Calendar / Outlook / Apple Calendar, or use the CSV import instead.");
        }
        else if (parsed.Events.Count == 0)
        {
            preview.BlockReason = Loc.T("文件里没有日程（VEVENT）。请确认导出时选择的是「日程」而不是「提醒事项」。", "The file contains no event (VEVENT). Please make sure you exported \"events\" instead of \"reminders\".");
        }

        // 试映射一遍，把"能认但支持不了"的问题提前暴露出来（不影响数据）
        if (preview.BlockReason == null)
        {
            var warnings = new List<string>();
            var errors = new List<ImportError>();
            preview.MappedCount = IcsMapper.Map(parsed, options, _clock(), preview.SourceTag, warnings, errors).Count;
            preview.Warnings.AddRange(warnings);
            preview.RowErrors.AddRange(errors);
        }

        return preview;
    }

    /// <summary>执行导入，target 会被就地修改。</summary>
    public ImportReport Import(IcsImportOptions options, string filePath, IList<ScheduleItem> target)
    {
        var stopwatch = Stopwatch.StartNew();
        var report = new ImportReport { FilePath = filePath };

        var source = ReadSource(filePath);
        report.EncodingName = source.EncodingName;
        if (!string.IsNullOrEmpty(source.Note)) report.Warnings.Add(source.Note!);

        var parsed = IcsReader.Parse(source.Text);
        report.Warnings.AddRange(parsed.Warnings);
        report.TotalRows = parsed.Events.Count;

        foreach (var (line, message) in parsed.Errors)
        {
            report.Errors.Add(new ImportError { Line = line, Message = message });
        }

        if (!parsed.LooksLikeCalendar && parsed.Events.Count == 0)
        {
            report.Blocked = true;
            report.BlockReason = Loc.T("这个文件看起来不是 iCalendar：没有找到 BEGIN:VCALENDAR / BEGIN:VEVENT。", "This file does not look like iCalendar: no BEGIN:VCALENDAR / BEGIN:VEVENT was found.");
            report.Warnings.Add(report.BlockReason);
            report.Elapsed = stopwatch.Elapsed;
            return report;
        }

        if (parsed.Events.Count == 0)
        {
            report.Blocked = true;
            report.BlockReason = Loc.T("文件里没有日程（VEVENT）。", "The file contains no event (VEVENT).");
            report.Warnings.Add(report.BlockReason);
            report.Elapsed = stopwatch.Elapsed;
            return report;
        }

        var mapped = IcsMapper.Map(parsed, options, _clock(), options.SourceTag, report.Warnings, report.Errors);
        report.TotalRows = parsed.Events.Count;
        report.Failed = report.Errors.Count;

        if (mapped.Count == 0)
        {
            report.Blocked = true;
            report.BlockReason = Loc.F("文件里的 {0} 个日程都没能转换成有效日程（详见下面的错误）。", "None of the {0} events in the file could be converted into a valid event (see the errors below).", parsed.Events.Count);
            report.Warnings.Add(report.BlockReason);
            report.Elapsed = stopwatch.Elapsed;
            return report;
        }

        if (options.Mode == ImportMode.Replace) target.Clear();

        var now = _clock();
        var index = new Dictionary<string, ScheduleItem>(StringComparer.OrdinalIgnoreCase);

        if (options.Mode != ImportMode.Replace)
        {
            foreach (var existing in target) index[Key(existing.Title, existing.Start)] = existing;
        }

        foreach (var item in mapped)
        {
            var key = Key(item.Title, item.Start);

            if (index.TryGetValue(key, out var existing))
            {
                if (options.Mode == ImportMode.Upsert && target.Contains(existing))
                {
                    CopyInto(existing, item, now);
                    report.Updated++;
                    continue;
                }

                if (options.SkipDuplicates && target.Contains(existing))
                {
                    report.Skipped++;
                    continue;
                }
            }

            if (options.SkipPastItems && item.EffectiveEnd < now)
            {
                report.Skipped++;
                continue;
            }

            target.Add(item);
            index[key] = item;
            report.Imported++;
        }

        report.Elapsed = stopwatch.Elapsed;
        return report;
    }

    /// <summary>读文件（复用 CSV 那套编码识别：ICS 规定 UTF-8，但 Outlook 有时带 BOM 或 GBK）。</summary>
    private static Csv.CsvTextSource ReadSource(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new Csv.CsvTextSource { Text = string.Empty, EncodingName = Loc.T("文件不存在", "File not found"), Note = Loc.F("找不到文件：{0}", "File not found: {0}", filePath) };
        }

        try
        {
            return Csv.CsvEncodingDetector.ReadFile(filePath, "auto");
        }
        catch (Exception ex)
        {
            return new Csv.CsvTextSource { Text = string.Empty, EncodingName = Loc.T("读取失败", "Read failed"), Note = Loc.F("读取文件失败：{0}", "Failed to read the file: {0}", ex.Message) };
        }
    }

    private static void CopyInto(ScheduleItem target, ScheduleItem source, DateTime now)
    {
        target.Title = source.Title;
        target.Notes = source.Notes;
        target.Category = source.Category;
        target.Start = source.Start;
        target.End = source.End;
        target.AllDay = source.AllDay;
        target.Color = source.Color;
        target.Priority = source.Priority;
        target.Status = source.Status;
        target.Repeat = source.Repeat;
        target.RepeatInterval = source.RepeatInterval;
        target.RepeatWeekdays = new List<DayOfWeek>(source.RepeatWeekdays);
        target.RepeatCount = source.RepeatCount;
        target.RepeatUntil = source.RepeatUntil;
        target.ExcludedOccurrences = new List<DateTime>(source.ExcludedOccurrences);
        target.OccurrenceOverrides = source.OccurrenceOverrides.Select(o => o.Clone()).ToList();
        target.ReminderEnabled = source.ReminderEnabled;
        target.ReminderOffsets = new List<int>(source.ReminderOffsets);
        target.RemindAtOverride = source.RemindAtOverride;
        target.RemindBeforeMinutes = source.RemindBeforeMinutes;
        target.ReminderVoiceTexts = new Dictionary<int, string>(source.ReminderVoiceTexts);
        target.ReminderSoundFiles = new Dictionary<int, string>(source.ReminderSoundFiles);
        target.VoiceText = source.VoiceText;
        target.SoundFile = source.SoundFile;
        target.Origin = source.Origin;
        target.Source = source.Source;
        target.UpdatedAt = now;
    }

    private static string Key(string title, DateTime start)
        => (title ?? string.Empty).Trim() + "|" + start.ToString("yyyyMMddHHmm");
}

/// <summary>ICS 预览信息。</summary>
public sealed class IcsPreview
{
    public string EncodingName { get; set; } = string.Empty;

    public bool LooksLikeCalendar { get; set; }

    public int EventCount { get; set; }

    public int MappedCount { get; set; }

    public int RecurringCount { get; set; }

    public int AlarmCount { get; set; }

    public int ExcludedCount { get; set; }

    public int OverrideCount { get; set; }

    public string SourceTag { get; set; } = string.Empty;

    public List<string> Warnings { get; } = new();

    public List<ImportError> RowErrors { get; } = new();

    public string? BlockReason { get; set; }

    public bool CanImport => BlockReason == null;

    public string ToSummary()
        => Loc.F("共 {0} 个事件：可导入 {1}", "{0} events: {1} importable", EventCount, MappedCount)
            + (RecurringCount > 0 ? Loc.F("，其中重复日程 {0}", ", {0} recurring", RecurringCount) : string.Empty)
            + (AlarmCount > 0 ? Loc.F("，带 {0} 个提醒", ", {0} reminder(s)", AlarmCount) : string.Empty)
            + (OverrideCount > 0 ? Loc.F("，含 {0} 处单独修改", ", {0} individual change(s)", OverrideCount) : string.Empty)
            + (ExcludedCount > 0 ? Loc.F("，{0} 条例外日期", ", {0} exception date(s)", ExcludedCount) : string.Empty);
}
