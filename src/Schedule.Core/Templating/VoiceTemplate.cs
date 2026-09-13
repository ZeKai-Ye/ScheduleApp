using System.Text;
using Schedule.Core.Models;

namespace Schedule.Core.Templating;

/// <summary>
/// 自定义语音播报模板引擎。
/// 支持占位符：{title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status}
/// </summary>
public static class VoiceTemplate
{
    public const string Default = "{time}，提醒您：{title}。{category}";

    public static readonly string[] Tokens =
    {
        "{title}", "{time}", "{date}", "{weekday}", "{category}", "{notes}", "{priority}", "{countdown}", "{status}",
    };

    public static string Describe(string token) => token switch
    {
        "{title}" => "日程标题",
        "{time}" => "开始时间 HH:mm",
        "{date}" => "日期 2025-01-01",
        "{weekday}" => "星期几",
        "{category}" => "分类",
        "{notes}" => "备注",
        "{priority}" => "优先级（高/中/低）",
        "{countdown}" => "还有多久（例如：还有 10 分钟）",
        "{status}" => "状态",
        _ => token,
    };

    /// <summary>渲染播报文本。未知占位符原样保留，空值占位符会被清理掉多余标点。</summary>
    public static string Render(string? template, ScheduleItem item, DateTime occurrenceStart, DateTime now)
    {
        var text = string.IsNullOrWhiteSpace(template) ? Default : template!;

        var countdown = DescribeCountdown(occurrenceStart, now);

        text = text
            .Replace("{title}", item.Title ?? string.Empty)
            .Replace("{time}", occurrenceStart.ToString("HH:mm"))
            .Replace("{date}", occurrenceStart.ToString("yyyy-MM-dd"))
            .Replace("{weekday}", WeekdayText(occurrenceStart.DayOfWeek))
            .Replace("{category}", item.Category ?? string.Empty)
            .Replace("{notes}", SingleLine(item.Notes))
            .Replace("{priority}", PriorityText(item.Priority))
            .Replace("{countdown}", countdown)
            .Replace("{status}", StatusText(item.Status));

        return Tidy(text);
    }

    /// <summary>用示例数据渲染，供设置界面实时预览。</summary>
    public static string Preview(string? template, DateTime now)
    {
        var item = new ScheduleItem
        {
            Title = "项目周会",
            Category = "工作",
            Notes = "会议室 A，带上周报",
            Priority = Priority.High,
            Start = now.Date.AddHours(14),
        };

        return Render(template, item, item.Start, now);
    }

    public static string DescribeCountdown(DateTime occurrenceStart, DateTime now)
    {
        var delta = occurrenceStart - now;
        if (delta.TotalSeconds >= -30 && delta.TotalSeconds <= 30) return "现在开始";

        if (delta < TimeSpan.Zero)
        {
            var overdue = -delta;
            if (overdue.TotalMinutes < 60) return "已经开始 " + (int)Math.Round(overdue.TotalMinutes) + " 分钟";
            if (overdue.TotalHours < 24) return "已经开始 " + (int)Math.Round(overdue.TotalHours) + " 小时";
            return "已经开始 " + (int)Math.Round(overdue.TotalDays) + " 天";
        }

        if (delta.TotalMinutes < 1) return "还有不到 1 分钟";
        if (delta.TotalMinutes < 60) return "还有 " + (int)Math.Round(delta.TotalMinutes) + " 分钟";
        if (delta.TotalHours < 24) return "还有 " + (int)Math.Round(delta.TotalHours) + " 小时";
        return "还有 " + (int)Math.Round(delta.TotalDays) + " 天";
    }

    public static string WeekdayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };

    public static string PriorityText(Priority priority) => priority switch
    {
        Priority.High => "高优先级",
        Priority.Low => "低优先级",
        _ => "普通优先级",
    };

    public static string StatusText(ItemStatus status) => status switch
    {
        ItemStatus.Done => "已完成",
        ItemStatus.Canceled => "已取消",
        _ => "待办",
    };

    public static string SingleLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    /// <summary>清理空占位符留下的重复标点。</summary>
    private static string Tidy(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        var result = sb.ToString().Trim();
        foreach (var pair in new[] { "，，", "。。", "；；", "，。", "。，", "，；", " ，", "。 " })
        {
            while (result.Contains(pair, StringComparison.Ordinal))
            {
                result = result.Replace(pair, pair.Substring(0, 1));
            }
        }

        result = result.Trim(' ', '，', '。', '；', '、', ',', '.', ';');
        return result.Length == 0 ? "日程提醒" : result;
    }
}
