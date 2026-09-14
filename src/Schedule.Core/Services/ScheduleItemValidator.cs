using Schedule.Core.Models;

namespace Schedule.Core.Services;

/// <summary>日程数据校验（导入、界面编辑与本地接口共用）。</summary>
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

        foreach (var offset in item.ReminderOffsets)
        {
            if (offset < 0) errors.Add("提醒档位不能为负数。");
            else if (offset > 60 * 24 * 365) errors.Add("提醒档位过大（超过一年）。");
        }

        if (item.ReminderOffsets.Count == 0 && item.RemindBeforeMinutes < 0)
        {
            errors.Add("提前提醒分钟数不能为负数。");
        }

        if (item.ReminderOffsets.Count == 0 && item.RemindBeforeMinutes > 60 * 24 * 365)
        {
            errors.Add("提前提醒时间过长（超过一年）。");
        }

        if (item.Repeat < 0 || item.Repeat > RepeatKind.Yearly)
        {
            errors.Add("重复规则无效。");
        }

        if (item.RepeatInterval < 1)
        {
            errors.Add("重复间隔必须大于等于 1。");
        }

        if (item.RepeatCount is <= 0)
        {
            errors.Add("重复次数必须大于等于 1。");
        }

        if (item.Repeat == RepeatKind.None && (item.RepeatUntil.HasValue || item.RepeatCount.HasValue))
        {
            errors.Add("未设置重复规则时不应指定重复截止或重复次数。");
        }

        if (item.RepeatUntil.HasValue && item.RepeatUntil.Value.Date < item.Start.Date)
        {
            errors.Add("重复截止日期不能早于开始日期。");
        }

        if (!string.IsNullOrWhiteSpace(item.Color) && !EventColors.IsValid(item.Color))
        {
            errors.Add("事件颜色格式不正确（应为 #RRGGBB）。");
        }

        if (!string.IsNullOrWhiteSpace(item.SoundFile) && !File.Exists(item.SoundFile))
        {
            errors.Add("指定的提醒音频文件不存在：" + item.SoundFile);
        }

        if (item.OccurrenceOverrides.Count > 0 && item.Repeat == RepeatKind.None)
        {
            errors.Add("非重复日程不应带有「仅本次」例外。");
        }

        foreach (var instance in item.OccurrenceOverrides)
        {
            if (instance.NewStart.HasValue && instance.NewEnd.HasValue && instance.NewEnd.Value < instance.NewStart.Value)
            {
                errors.Add("第 " + instance.OriginalStart.ToString("MM-dd HH:mm") + " 次的改期结束时间早于开始时间。");
            }

            foreach (var offset in instance.ReminderOffsets ?? new List<int>())
            {
                if (offset < 0) errors.Add("「仅本次」的提醒档位不能为负数。");
            }
        }

        foreach (var pair in item.ReminderSoundFiles)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && !File.Exists(pair.Value))
            {
                errors.Add("档位 " + pair.Key + " 的提醒音频文件不存在：" + pair.Value);
            }
        }

        var known = new HashSet<int>(item.NormalizedOffsets());
        foreach (var offset in item.ReminderVoiceTexts.Keys)
        {
            if (!known.Contains(offset)) errors.Add("档位 " + offset + " 的自定义播报文案没有对应的提醒档位。");
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

        if (item.ExcludedOccurrences == null) item.ExcludedOccurrences = new List<DateTime>();
        if (item.OccurrenceOverrides == null) item.OccurrenceOverrides = new List<OccurrenceOverride>();
        if (item.ReminderOffsets == null) item.ReminderOffsets = new List<int>();
        if (item.RepeatWeekdays == null) item.RepeatWeekdays = new List<DayOfWeek>();
        if (item.FiredReminders == null) item.FiredReminders = new List<ReminderFireRecord>();
        if (item.ReminderVoiceTexts == null) item.ReminderVoiceTexts = new Dictionary<int, string>();
        if (item.ReminderSoundFiles == null) item.ReminderSoundFiles = new Dictionary<int, string>();

        // 旧版单档提醒 → 档位列表
        if (item.ReminderOffsets.Count == 0 && item.ReminderEnabled && item.RemindBeforeMinutes >= 0 && item.RemindBeforeMinutes != 10)
        {
            item.ReminderOffsets = new List<int> { item.RemindBeforeMinutes };
        }

        var sanitized = item.ReminderOffsets
            .Where(o => o is >= 0 and <= 60 * 24 * 365)
            .Distinct()
            .OrderByDescending(o => o)
            .ToList();

        if (sanitized.Count != item.ReminderOffsets.Count)
        {
            notes.Add("清理了非法提醒档位");
            item.ReminderOffsets = sanitized;
        }

        // 档位文案 / 铃声：只保留仍然存在的档位，并丢弃空值
        var offsets = item.NormalizedOffsets();
        if (item.ReminderVoiceTexts.Count > 0)
        {
            var kept = item.ReminderVoiceTexts
                .Where(p => offsets.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                .ToDictionary(p => p.Key, p => p.Value);

            if (kept.Count != item.ReminderVoiceTexts.Count) notes.Add("清理了无对应档位的播报文案");
            item.ReminderVoiceTexts = kept;
        }

        if (item.ReminderSoundFiles.Count > 0)
        {
            var kept = item.ReminderSoundFiles
                .Where(p => offsets.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                .ToDictionary(p => p.Key, p => p.Value);

            if (kept.Count != item.ReminderSoundFiles.Count) notes.Add("清理了无对应档位的铃声设置");
            item.ReminderSoundFiles = kept;
        }

        if (item.RemindBeforeMinutes < 0)
        {
            item.RemindBeforeMinutes = 0;
            notes.Add("负的提前提醒分钟数已归零");
        }

        if (item.RepeatInterval < 1)
        {
            item.RepeatInterval = 1;
            notes.Add("重复间隔已修正为 1");
        }

        if (item.RepeatCount is <= 0)
        {
            item.RepeatCount = null;
            notes.Add("清除了非法的重复次数");
        }

        if (item.Repeat != RepeatKind.Weekly && item.RepeatWeekdays.Count > 0)
        {
            item.RepeatWeekdays.Clear();
        }

        if (item.Repeat == RepeatKind.None)
        {
            if (item.RepeatUntil.HasValue) item.RepeatUntil = null;
            if (item.RepeatCount.HasValue) item.RepeatCount = null;
            if (item.RepeatWeekdays.Count > 0) item.RepeatWeekdays.Clear();
        }

        if (!string.IsNullOrWhiteSpace(item.Color))
        {
            var normalized = EventColors.Normalize(item.Color);
            if (normalized.Length != 7)
            {
                item.Color = string.Empty;
                notes.Add("清除了非法颜色值");
            }
            else if (normalized != item.Color)
            {
                item.Color = normalized;
            }
        }

        // 「本次」例外：去重、排序、丢弃空覆盖
        if (item.ExcludedOccurrences.Count > 0)
        {
            var cleaned = item.ExcludedOccurrences.Distinct().OrderBy(v => v).ToList();
            if (cleaned.Count != item.ExcludedOccurrences.Count)
            {
                notes.Add("清理了重复的「删除本次」记录");
                item.ExcludedOccurrences = cleaned;
            }
        }

        if (item.OccurrenceOverrides.Count > 0)
        {
            var kept = new List<OccurrenceOverride>();
            var seen = new HashSet<DateTime>();

            foreach (var instance in item.OccurrenceOverrides.OrderBy(o => o.OriginalStart))
            {
                if (instance.IsEmpty || !seen.Add(instance.OriginalStart)) continue;
                if (instance.ReminderOffsets != null)
                {
                    instance.ReminderOffsets = instance.ReminderOffsets
                        .Where(o => o is >= 0 and <= 60 * 24 * 365)
                        .Distinct()
                        .OrderByDescending(o => o)
                        .ToList();
                }

                kept.Add(instance);
            }

            if (kept.Count != item.OccurrenceOverrides.Count)
            {
                notes.Add("清理了无效的「仅本次」修改");
                item.OccurrenceOverrides = kept;
            }
        }

        if (item.Repeat == RepeatKind.None && item.HasOccurrenceEdits)
        {
            item.ExcludedOccurrences.Clear();
            item.OccurrenceOverrides.Clear();
            notes.Add("清除了非重复日程上的「仅本次」例外");
        }

        // 旧字段迁移：LastFiredOccurrence → 每个档位各记一条，避免升级后重复弹窗
        if (item.LastFiredOccurrence.HasValue)
        {
            var occStart = item.LastFiredOccurrence.Value;
            var hasRecord = item.FiredReminders.Any(r => r.OccurrenceStart == occStart);

            if (!hasRecord)
            {
                foreach (var offset in item.NormalizedOffsets())
                {
                    item.FiredReminders.Add(new ReminderFireRecord
                    {
                        OccurrenceStart = occStart,
                        OffsetMinutes = offset,
                        FiredAt = occStart,
                    });
                }
            }

            item.LastFiredOccurrence = null;
            notes.Add("迁移了旧的提醒触发记录");
        }

        if (item.RemindAtOverride.HasValue && item.RemindAtOverride.Value.Year < 1900)
        {
            item.RemindAtOverride = null;
            notes.Add("清除了非法提醒时间");
        }

        return notes;
    }
}
