using Schedule.Core.Localization;
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
            errors.Add(Loc.T("标题不能为空。", "Title cannot be empty."));
        }
        else if (item.Title.Length > 200)
        {
            errors.Add(Loc.T("标题不能超过 200 个字符。", "Title cannot be longer than 200 characters."));
        }

        if (item.End.HasValue && item.End.Value < item.Start)
        {
            errors.Add(Loc.T("结束时间不能早于开始时间。", "End time cannot be earlier than the start time."));
        }

        foreach (var offset in item.ReminderOffsets)
        {
            if (offset < 0) errors.Add(Loc.T("提醒档位不能为负数。", "Reminder offsets cannot be negative."));
            else if (offset > 60 * 24 * 365) errors.Add(Loc.T("提醒档位过大（超过一年）。", "Reminder offset is too large (more than one year)."));
        }

        if (item.ReminderOffsets.Count == 0 && item.RemindBeforeMinutes < 0)
        {
            errors.Add(Loc.T("提前提醒分钟数不能为负数。", "Reminder minutes before cannot be negative."));
        }

        if (item.ReminderOffsets.Count == 0 && item.RemindBeforeMinutes > 60 * 24 * 365)
        {
            errors.Add(Loc.T("提前提醒时间过长（超过一年）。", "Reminder lead time is too long (more than one year)."));
        }

        if (item.Repeat < 0 || item.Repeat > RepeatKind.Yearly)
        {
            errors.Add(Loc.T("重复规则无效。", "Invalid repeat rule."));
        }

        if (item.RepeatInterval < 1)
        {
            errors.Add(Loc.T("重复间隔必须大于等于 1。", "Repeat interval must be at least 1."));
        }

        if (item.RepeatCount is <= 0)
        {
            errors.Add(Loc.T("重复次数必须大于等于 1。", "Repeat count must be at least 1."));
        }

        if (item.Repeat == RepeatKind.None && (item.RepeatUntil.HasValue || item.RepeatCount.HasValue))
        {
            errors.Add(Loc.T("未设置重复规则时不应指定重复截止或重复次数。", "A repeat end date or count should not be set when no repeat rule is set."));
        }

        if (item.RepeatUntil.HasValue && item.RepeatUntil.Value.Date < item.Start.Date)
        {
            errors.Add(Loc.T("重复截止日期不能早于开始日期。", "The repeat end date cannot be earlier than the start date."));
        }

        if (!string.IsNullOrWhiteSpace(item.Color) && !EventColors.IsValid(item.Color))
        {
            errors.Add(Loc.T("事件颜色格式不正确（应为 #RRGGBB）。", "Event color format is invalid (expected #RRGGBB)."));
        }

        if (!string.IsNullOrWhiteSpace(item.SoundFile) && !File.Exists(item.SoundFile))
        {
            errors.Add(Loc.F("指定的提醒音频文件不存在：{0}", "Reminder sound file not found: {0}", item.SoundFile));
        }

        if (item.OccurrenceOverrides.Count > 0 && item.Repeat == RepeatKind.None)
        {
            errors.Add(Loc.T("非重复日程不应带有「仅本次」例外。", "A non-repeating event should not carry any \"only this occurrence\" exception."));
        }

        foreach (var instance in item.OccurrenceOverrides)
        {
            if (instance.NewStart.HasValue && instance.NewEnd.HasValue && instance.NewEnd.Value < instance.NewStart.Value)
            {
                errors.Add(Loc.F("第 {0} 次的改期结束时间早于开始时间。", "The rescheduled occurrence {0} ends before it starts.", instance.OriginalStart.ToString("MM-dd HH:mm")));
            }

            foreach (var offset in instance.ReminderOffsets ?? new List<int>())
            {
                if (offset < 0) errors.Add(Loc.T("「仅本次」的提醒档位不能为负数。", "An \"only this occurrence\" reminder offset cannot be negative."));
            }
        }

        foreach (var pair in item.ReminderSoundFiles)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && !File.Exists(pair.Value))
            {
                errors.Add(Loc.F("档位 {0} 的提醒音频文件不存在：{1}", "Reminder sound file for offset {0} not found: {1}", pair.Key, pair.Value));
            }
        }

        var known = new HashSet<int>(item.NormalizedOffsets());
        foreach (var offset in item.ReminderVoiceTexts.Keys)
        {
            if (!known.Contains(offset)) errors.Add(Loc.F("档位 {0} 的自定义播报文案没有对应的提醒档位。", "Custom voice text for offset {0} has no matching reminder offset.", offset));
        }

        return errors;
    }

    /// <summary>
    /// 清掉非重复日程上残留的「仅本次」例外（旧版本把「仅这一次」改期写到了非重复日程上，
    /// 结果这条日程再打开编辑窗口保存时会被 <see cref="Validate"/> 拦住）。
    /// 返回是否真的清理过。
    /// </summary>
    public static bool DropOccurrenceEditsWhenNotRepeating(ScheduleItem item)
    {
        if (item.Repeat != RepeatKind.None || !item.HasOccurrenceEdits) return false;

        item.ExcludedOccurrences?.Clear();
        item.OccurrenceOverrides?.Clear();
        return true;
    }

    /// <summary>加载历史数据时修复明显不合法的字段，返回修复说明。</summary>
    public static List<string> Normalize(ScheduleItem item)
    {
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            item.Title = Loc.T("(无标题)", "(Untitled)");
            notes.Add(Loc.T("补齐了空标题", "filled in the empty title"));
        }

        if (item.End.HasValue && item.End.Value < item.Start)
        {
            item.End = null;
            notes.Add(Loc.T("清除了早于开始时间的结束时间", "removed an end time earlier than the start time"));
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
            notes.Add(Loc.T("清理了非法提醒档位", "removed invalid reminder offsets"));
            item.ReminderOffsets = sanitized;
        }

        // 档位文案 / 铃声：只保留仍然存在的档位，并丢弃空值
        var offsets = item.NormalizedOffsets();
        if (item.ReminderVoiceTexts.Count > 0)
        {
            var kept = item.ReminderVoiceTexts
                .Where(p => offsets.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                .ToDictionary(p => p.Key, p => p.Value);

            if (kept.Count != item.ReminderVoiceTexts.Count) notes.Add(Loc.T("清理了无对应档位的播报文案", "removed voice text without a matching reminder offset"));
            item.ReminderVoiceTexts = kept;
        }

        if (item.ReminderSoundFiles.Count > 0)
        {
            var kept = item.ReminderSoundFiles
                .Where(p => offsets.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                .ToDictionary(p => p.Key, p => p.Value);

            if (kept.Count != item.ReminderSoundFiles.Count) notes.Add(Loc.T("清理了无对应档位的铃声设置", "removed sound settings without a matching reminder offset"));
            item.ReminderSoundFiles = kept;
        }

        if (item.RemindBeforeMinutes < 0)
        {
            item.RemindBeforeMinutes = 0;
            notes.Add(Loc.T("负的提前提醒分钟数已归零", "reset a negative reminder lead time to zero"));
        }

        if (item.RepeatInterval < 1)
        {
            item.RepeatInterval = 1;
            notes.Add(Loc.T("重复间隔已修正为 1", "fixed the repeat interval to 1"));
        }

        if (item.RepeatCount is <= 0)
        {
            item.RepeatCount = null;
            notes.Add(Loc.T("清除了非法的重复次数", "removed an invalid repeat count"));
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
                notes.Add(Loc.T("清除了非法颜色值", "removed an invalid color value"));
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
                notes.Add(Loc.T("清理了重复的「删除本次」记录", "removed duplicate \"delete this occurrence\" records"));
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
                notes.Add(Loc.T("清理了无效的「仅本次」修改", "removed invalid \"only this occurrence\" changes"));
                item.OccurrenceOverrides = kept;
            }
        }

        if (DropOccurrenceEditsWhenNotRepeating(item))
        {
            notes.Add(Loc.T("清除了非重复日程上的「仅本次」例外", "removed \"only this occurrence\" exceptions from a non-repeating event"));
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
            notes.Add(Loc.T("迁移了旧的提醒触发记录", "migrated old reminder fire records"));
        }

        if (item.RemindAtOverride.HasValue && item.RemindAtOverride.Value.Year < 1900)
        {
            item.RemindAtOverride = null;
            notes.Add(Loc.T("清除了非法提醒时间", "removed an invalid reminder time"));
        }

        return notes;
    }
}
