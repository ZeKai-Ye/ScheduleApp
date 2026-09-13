using System.Text;
using Schedule.Core.Models;

namespace Schedule.Core.Csv;

/// <summary>把日程导出为 CSV（UTF-8 with BOM，Excel 打开中文不乱码）。</summary>
public static class CsvExporter
{
    public static readonly string[] Headers =
    {
        "标题", "开始时间", "结束时间", "全天", "分类", "优先级", "重复", "提前提醒", "语音内容", "备注", "状态",
    };

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
                item.End.HasValue
                    ? (item.AllDay ? item.End.Value.ToString("yyyy-MM-dd") : item.End.Value.ToString("yyyy-MM-dd HH:mm"))
                    : string.Empty,
                item.AllDay ? "是" : "否",
                item.Category,
                PriorityText(item.Priority),
                RepeatText(item.Repeat),
                RemindText(item),
                item.VoiceText,
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

    public static string RepeatText(RepeatKind repeat) => repeat switch
    {
        RepeatKind.Daily => "每天",
        RepeatKind.Weekdays => "工作日",
        RepeatKind.Weekly => "每周",
        RepeatKind.Monthly => "每月",
        RepeatKind.Yearly => "每年",
        _ => "无",
    };

    public static string StatusText(ItemStatus status) => status switch
    {
        ItemStatus.Done => "已完成",
        ItemStatus.Canceled => "已取消",
        _ => "待办",
    };

    private static string RemindText(ScheduleItem item)
    {
        if (!item.ReminderEnabled) return "不提醒";
        if (item.RemindAtOverride.HasValue) return item.RemindAtOverride.Value.ToString("yyyy-MM-dd HH:mm");
        if (item.RemindBeforeMinutes <= 0) return "0";
        if (item.RemindBeforeMinutes % (60 * 24) == 0) return (item.RemindBeforeMinutes / (60 * 24)) + "天";
        if (item.RemindBeforeMinutes % 60 == 0) return (item.RemindBeforeMinutes / 60) + "小时";
        return item.RemindBeforeMinutes.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }
}
