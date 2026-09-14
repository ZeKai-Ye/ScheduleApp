using System.Text;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Templating;

namespace Schedule.Core.Csv;

/// <summary>把日程导出为 CSV（UTF-8 with BOM，Excel 打开中文不乱码）。</summary>
public static class CsvExporter
{
    /// <summary>
    /// 导出表头 = 统一格式（<see cref="CsvSchema"/>）的规范列，
    /// 因此"导出的文件一定能被原样导入"，不再各写一套。
    /// </summary>
    public static string[] Headers => CsvSchema.Headers;

    public static string ToCsv(IEnumerable<ScheduleItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Headers.Select(Escape))).Append("\r\n");

        foreach (var item in items.OrderBy(i => i.Start).ThenBy(i => i.Title, StringComparer.CurrentCulture))
        {
            var cells = new[]
            {
                item.Title,
                item.AllDay ? item.Start.ToString("yyyy-MM-dd") : item.Start.ToString("yyyy-MM-dd HH:mm"),
                // 全天日程的结束列写"最后一天"（我们的 End 是次日），这样 Excel 里写 12-31 → 12-31 就是一天
                item.End.HasValue
                    ? (item.AllDay ? AllDayEndText(item) : item.End.Value.ToString("yyyy-MM-dd HH:mm"))
                    : string.Empty,
                item.AllDay ? "是" : "否",
                item.Category,
                item.Color,
                PriorityText(item.Priority),
                RepeatText(item),
                RepeatIntervalText(item),
                WeekdaysText(item),
                item.RepeatCount.HasValue ? item.RepeatCount.Value.ToString() : string.Empty,
                UntilText(item),
                RemindText(item),
                FieldParsers.FormatOffsetTexts(item.ReminderVoiceTexts, item.VoiceText),
                FieldParsers.FormatOffsetTexts(item.ReminderSoundFiles, item.SoundFile),
                item.Notes,
                StatusText(item.Status),
            };

            sb.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");
        }

        return sb.ToString();
    }

    public static void WriteFile(string path, IEnumerable<ScheduleItem> items)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // UTF-8 BOM：保证 Excel 直接双击打开不乱码
        File.WriteAllText(path, ToCsv(items), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static string PriorityText(Priority priority) => priority switch
    {
        Priority.High => "高",
        Priority.Low => "低",
        _ => "中",
    };

    /// <summary>重复类型描述（间隔与星期由独立列承载）。</summary>
    public static string RepeatText(ScheduleItem item) => item.Repeat switch
    {
        RepeatKind.Daily => "每天",
        RepeatKind.Weekdays => "工作日",
        RepeatKind.Weekly => "每周",
        RepeatKind.Monthly => "每月",
        RepeatKind.Yearly => "每年",
        _ => "无",
    };

    public static string RepeatIntervalText(ScheduleItem item)
        => item.Repeat == RepeatKind.None ? string.Empty : Math.Max(1, item.RepeatInterval).ToString();

    /// <summary>星期描述：周一、周三、周五。</summary>
    public static string WeekdaysText(ScheduleItem item)
    {
        if (item.Repeat != RepeatKind.Weekly || item.RepeatWeekdays.Count == 0) return string.Empty;

        return string.Join("、", item.RepeatWeekdays
            .Distinct()
            .OrderBy(day => day)
            .Select(VoiceTemplate.ShortWeekdayText));
    }

    public static string StatusText(ItemStatus status) => status switch
    {
        ItemStatus.Done => "已完成",
        ItemStatus.Canceled => "已取消",
        _ => "待办",
    };

    /// <summary>全天日程的结束日期：End 是"次日"，导出成用户直觉上的"最后一天"。</summary>
    public static string AllDayEndText(ScheduleItem item)
    {
        if (!item.End.HasValue) return string.Empty;

        var lastDay = item.End.Value.Date;
        if (lastDay > item.Start.Date) lastDay = lastDay.AddDays(-1);

        return lastDay.ToString("yyyy-MM-dd");
    }

    /// <summary>重复截止日期（只写日期）。</summary>
    public static string UntilText(ScheduleItem item)
        => item.Repeat != RepeatKind.None && item.RepeatUntil.HasValue
            ? item.RepeatUntil.Value.ToString("yyyy-MM-dd")
            : string.Empty;

    /// <summary>提醒档位描述：30,15,5,0 或具体时间。</summary>
    public static string RemindText(ScheduleItem item)
    {
        if (!item.ReminderEnabled) return "不提醒";

        var parts = new List<string>();
        if (item.RemindAtOverride.HasValue) parts.Add(item.RemindAtOverride.Value.ToString("yyyy-MM-dd HH:mm"));
        parts.AddRange(item.NormalizedOffsets().Select(ReminderPlan.FormatOffset));

        return parts.Count == 0 ? "不提醒" : string.Join(",", parts);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }
}
