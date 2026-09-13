using Schedule.Core.Models;

namespace Schedule.Core.Services;

/// <summary>日程数据校验（导入与手工编辑共用）。</summary>
public static class ScheduleItemValidator
{
    public static List<string> Validate(ScheduleItem item)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            errors.Add("标题不能为空。");
        }
        else if (item.Title.Length > 200)
        {
            errors.Add("标题不能超过 200 个字符。");
        }

        if (item.End.HasValue && item.End.Value < item.Start)
        {
            errors.Add("结束时间不能早于开始时间。");
        }

        if (item.RemindBeforeMinutes < 0)
        {
            errors.Add("提前提醒分钟数不能为负数。");
        }

        if (item.RemindBeforeMinutes > 60 * 24 * 365)
        {
            errors.Add("提前提醒时间过长（超过一年）。");
        }

        if (item.RepeatUntil.HasValue && item.RepeatUntil.Value.Date < item.Start.Date)
        {
            errors.Add("重复截止日期不能早于开始日期。");
        }

        if (item.Repeat == RepeatKind.None && item.RepeatUntil.HasValue)
        {
            errors.Add("未设置重复规则时不应指定重复截止日期。");
        }

        if (!string.IsNullOrWhiteSpace(item.SoundFile) && !File.Exists(item.SoundFile))
        {
            errors.Add("指定的提醒音频文件不存在：" + item.SoundFile);
        }

        return errors;
    }

    /// <summary>加载历史数据时修复明显不合法的字段，返回修复说明。</summary>
    public static List<string> Normalize(ScheduleItem item)
    {
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            item.Title = "(无标题)";
            notes.Add("补齐了空标题");
        }

        if (item.End.HasValue && item.End.Value < item.Start)
        {
            item.End = null;
            notes.Add("清除了早于开始时间的结束时间");
        }

        if (item.RemindBeforeMinutes < 0)
        {
            item.RemindBeforeMinutes = 0;
            notes.Add("负的提前提醒分钟数已归零");
        }

        if (item.Repeat == RepeatKind.None && item.RepeatUntil.HasValue)
        {
            item.RepeatUntil = null;
        }

        if (item.RemindAtOverride.HasValue && item.RemindAtOverride.Value.Year < 1900)
        {
            item.RemindAtOverride = null;
            notes.Add("清除了非法提醒时间");
        }

        return notes;
    }
}
