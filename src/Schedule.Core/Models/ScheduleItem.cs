namespace Schedule.Core.Models;

/// <summary>
/// 一条日程（领域实体）。所有时间均为本地时间（Kind = Unspecified/Local）。
/// </summary>
public sealed class ScheduleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>开始时间；全天日程时只取日期部分。</summary>
    public DateTime Start { get; set; } = DateTime.Now;

    /// <summary>结束时间，可为空。</summary>
    public DateTime? End { get; set; }

    public bool AllDay { get; set; }

    public Priority Priority { get; set; } = Priority.Normal;

    /// <summary>事件颜色（#RRGGBB）。为空表示按分类自动配色。</summary>
    public string Color { get; set; } = string.Empty;

    // ---------- 重复规则 ----------

    public RepeatKind Repeat { get; set; } = RepeatKind.None;

    /// <summary>重复间隔：每 N 天 / 周 / 月 / 年。</summary>
    public int RepeatInterval { get; set; } = 1;

    /// <summary>每周重复时选中的星期（可多选）；为空表示与开始时间同一星期。</summary>
    public List<DayOfWeek> RepeatWeekdays { get; set; } = new();

    /// <summary>重复次数上限（含首次）；为空表示不限次数。</summary>
    public int? RepeatCount { get; set; }

    /// <summary>重复截止日期（含当天），为空表示不限日期。</summary>
    public DateTime? RepeatUntil { get; set; }

    // ---------- 提醒 ----------

    /// <summary>是否启用提醒。</summary>
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>
    /// 多档提醒：相对开始时间的提前分钟数，可含多个档位（例如 30 / 15 / 5 / 0）。
    /// 为空时回退到旧字段 <see cref="RemindBeforeMinutes"/>。
    /// </summary>
    public List<int> ReminderOffsets { get; set; } = new();

    /// <summary>单档提醒（兼容旧数据与 CSV 默认值）。</summary>
    public int RemindBeforeMinutes { get; set; } = 10;

    /// <summary>绝对提醒时间；仅对首次发生生效。</summary>
    public DateTime? RemindAtOverride { get; set; }

    /// <summary>本日程的通用播报文本；为空则继续回退到全局档位文案 / 全局模板。</summary>
    public string VoiceText { get; set; } = string.Empty;

    /// <summary>
    /// 按提醒档位自定义播报文本（键 = 提前分钟数，0 = 准点）。
    /// 例如 { 30: "还有 30 分钟就要开会了", 5: "5 分钟后开始" }。
    /// </summary>
    public Dictionary<int, string> ReminderVoiceTexts { get; set; } = new();

    /// <summary>按提醒档位自定义提示音（键 = 提前分钟数）。</summary>
    public Dictionary<int, string> ReminderSoundFiles { get; set; } = new();

    /// <summary>自定义提醒音频文件（wav/mp3/wma/m4a）；为空则使用全局设置。</summary>
    public string SoundFile { get; set; } = string.Empty;

    public ItemStatus Status { get; set; } = ItemStatus.Pending;

    public ItemOrigin Origin { get; set; } = ItemOrigin.Manual;

    /// <summary>来源标识（例如导入的 CSV 文件名）。</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 被单独删除的那几次发生（系列事件「仅删除本次」）。
    /// 存的是它们的**原始**开始时间。
    /// </summary>
    public List<DateTime> ExcludedOccurrences { get; set; } = new();

    /// <summary>被单独修改过的那几次发生（改期 / 仅本次编辑）。</summary>
    public List<OccurrenceOverride> OccurrenceOverrides { get; set; } = new();

    /// <summary>已经触发过的提醒记录（按「发生时间 + 档位」去重）。</summary>
    public List<ReminderFireRecord> FiredReminders { get; set; } = new();

    /// <summary>旧字段：已触发过提醒的发生时间。仅用于迁移历史数据。</summary>
    public DateTime? LastFiredOccurrence { get; set; }

    // ---------- 计算属性 ----------

    /// <summary>实际结束时间：未填结束时间时，全天日程按当天结束，其余按 30 分钟。</summary>
    public DateTime EffectiveEnd
    {
        get
        {
            if (End.HasValue) return End.Value;
            return AllDay ? Start.Date.AddDays(1) : Start.AddMinutes(30);
        }
    }

    public TimeSpan Duration => EffectiveEnd - Start;

    public bool IsActive => Status == ItemStatus.Pending;

    // ---------- 系列事件的「本次」例外 ----------

    /// <summary>这次发生是否被单独删除。</summary>
    public bool IsExcluded(DateTime occurrenceStart) => ExcludedOccurrences.Contains(occurrenceStart);

    /// <summary>取这次发生的例外覆盖。</summary>
    public OccurrenceOverride? FindOverride(DateTime occurrenceStart)
    {
        foreach (var item in OccurrenceOverrides)
        {
            if (item.OriginalStart == occurrenceStart) return item;
        }

        return null;
    }

    /// <summary>是否有任何「本次」例外。</summary>
    public bool HasOccurrenceEdits => ExcludedOccurrences.Count > 0 || OccurrenceOverrides.Count > 0;

    /// <summary>单独删除某一次发生（会同时清掉它的覆盖）。</summary>
    public void ExcludeOccurrence(DateTime occurrenceStart)
    {
        OccurrenceOverrides.RemoveAll(o => o.OriginalStart == occurrenceStart);

        if (!ExcludedOccurrences.Contains(occurrenceStart)) ExcludedOccurrences.Add(occurrenceStart);
        ExcludedOccurrences.Sort();
    }

    /// <summary>写入 / 更新某一次发生的覆盖。</summary>
    public void UpsertOverride(OccurrenceOverride value)
    {
        ExcludedOccurrences.RemoveAll(e => e == value.OriginalStart);
        OccurrenceOverrides.RemoveAll(o => o.OriginalStart == value.OriginalStart);

        if (!value.IsEmpty) OccurrenceOverrides.Add(value);
    }

    /// <summary>
    /// 整体平移整个系列（改期整个系列时用）：
    /// 开始 / 结束 / 重复截止 / 绝对提醒都会跟着走，
    /// 已记录的「本次」例外键也一起偏移，否则改期后例外会全部对不上而失效。
    /// </summary>
    public void Shift(TimeSpan delta)
    {
        if (delta == TimeSpan.Zero) return;

        Start = Start.Add(delta);
        if (End.HasValue) End = End.Value.Add(delta);
        if (RepeatUntil.HasValue) RepeatUntil = RepeatUntil.Value.Add(delta);
        if (RemindAtOverride.HasValue) RemindAtOverride = RemindAtOverride.Value.Add(delta);

        for (var i = 0; i < ExcludedOccurrences.Count; i++)
        {
            ExcludedOccurrences[i] = ExcludedOccurrences[i].Add(delta);
        }

        foreach (var instance in OccurrenceOverrides)
        {
            instance.OriginalStart = instance.OriginalStart.Add(delta);
            if (instance.NewStart.HasValue) instance.NewStart = instance.NewStart.Value.Add(delta);
            if (instance.NewEnd.HasValue) instance.NewEnd = instance.NewEnd.Value.Add(delta);
        }

        ExcludedOccurrences.Sort();
        OccurrenceOverrides.Sort((a, b) => a.OriginalStart.CompareTo(b.OriginalStart));
    }

    /// <summary>撤销某一次发生的全部例外。</summary>
    public bool ClearOccurrenceEdit(DateTime occurrenceStart)
    {
        var removed = ExcludedOccurrences.RemoveAll(e => e == occurrenceStart);
        removed += OccurrenceOverrides.RemoveAll(o => o.OriginalStart == occurrenceStart);
        return removed > 0;
    }

    /// <summary>归一化后的提醒档位（降序：提前多的在前），已去重并过滤非法值。</summary>
    public List<int> NormalizedOffsets()
    {
        // 只配置了「绝对提醒时间」的日程不应再回退出一个相对档位
        if (ReminderOffsets is not { Count: > 0 } && RemindAtOverride.HasValue)
        {
            return new List<int>();
        }

        var source = ReminderOffsets is { Count: > 0 }
            ? ReminderOffsets
            : new List<int> { Math.Max(0, RemindBeforeMinutes) };

        return source
            .Where(o => o >= 0 && o <= 60 * 24 * 365)
            .Distinct()
            .OrderByDescending(o => o)
            .ToList();
    }

    /// <summary>最大提前量（分钟），用于提醒窗口扩张。</summary>
    public int MaxLeadMinutes()
    {
        if (!ReminderEnabled) return 0;
        var offsets = NormalizedOffsets();
        return offsets.Count == 0 ? 0 : offsets.Max();
    }

    /// <summary>是否存在多档提醒（超过一个档位）。</summary>
    public bool HasMultipleReminders => ReminderEnabled && NormalizedOffsets().Count > 1;

    /// <summary>计算某次发生的全部提醒时刻（按触发时间升序）。</summary>
    public List<ReminderPlan> ComputeReminders(DateTime occurrenceStart)
    {
        var result = new List<ReminderPlan>();
        if (!ReminderEnabled) return result;

        // 绝对提醒时间只针对首次发生
        if (RemindAtOverride.HasValue && occurrenceStart == Start)
        {
            var offset = (int)Math.Round((occurrenceStart - RemindAtOverride.Value).TotalMinutes);
            result.Add(new ReminderPlan
            {
                OffsetMinutes = offset,
                FireAt = RemindAtOverride.Value,
                IsAbsolute = true,
            });
        }

        foreach (var minutes in NormalizedOffsets())
        {
            var fireAt = occurrenceStart.AddMinutes(-minutes);
            if (result.Any(r => r.FireAt == fireAt)) continue;

            result.Add(new ReminderPlan
            {
                OffsetMinutes = minutes,
                FireAt = fireAt,
                IsAbsolute = false,
            });
        }

        result.Sort(static (a, b) => a.FireAt.CompareTo(b.FireAt));
        return result;
    }

    /// <summary>兼容旧接口：取最早的提醒时刻。</summary>
    public DateTime ComputeReminderAt(DateTime occurrenceStart)
    {
        var plans = ComputeReminders(occurrenceStart);
        return plans.Count > 0 ? plans[0].FireAt : occurrenceStart;
    }

    /// <summary>
    /// 解析某个提醒档位实际使用的播报模板。优先级（越具体越优先）：
    /// 1) 本日程 · 该档位文案  2) 本日程 · 通用文案
    /// 3) 全局 · 该档位默认文案  4) 全局播报模板
    /// 返回值可直接交给 VoiceTemplate.Render（仍支持全部占位符）。
    /// </summary>
    public string ResolveVoiceTemplate(int offsetMinutes, AppSettings settings)
    {
        if (ReminderVoiceTexts.Count > 0
            && ReminderVoiceTexts.TryGetValue(offsetMinutes, out var perOffset)
            && !string.IsNullOrWhiteSpace(perOffset))
        {
            return perOffset;
        }

        if (!string.IsNullOrWhiteSpace(VoiceText)) return VoiceText;

        if (settings.OffsetVoiceTemplates.Count > 0
            && settings.OffsetVoiceTemplates.TryGetValue(offsetMinutes, out var globalOffset)
            && !string.IsNullOrWhiteSpace(globalOffset))
        {
            return globalOffset;
        }

        return settings.VoiceTemplate;
    }

    /// <summary>解析某个提醒档位实际使用的提示音文件（空字符串表示用全局提示音）。</summary>
    public string ResolveSoundFile(int offsetMinutes, AppSettings settings)
    {
        if (ReminderSoundFiles.Count > 0
            && ReminderSoundFiles.TryGetValue(offsetMinutes, out var perOffset)
            && !string.IsNullOrWhiteSpace(perOffset))
        {
            return perOffset;
        }

        if (!string.IsNullOrWhiteSpace(SoundFile)) return SoundFile;
        return settings.DefaultSoundFile;
    }

    /// <summary>描述某个档位文案的来源，便于界面与接口说明「当前生效的是什么」。</summary>
    public string DescribeVoiceSource(int offsetMinutes, AppSettings settings)
    {
        if (ReminderVoiceTexts.Count > 0
            && ReminderVoiceTexts.TryGetValue(offsetMinutes, out var perOffset)
            && !string.IsNullOrWhiteSpace(perOffset))
        {
            return "本日程 · 该档位";
        }

        if (!string.IsNullOrWhiteSpace(VoiceText)) return "本日程 · 通用文案";

        if (settings.OffsetVoiceTemplates.TryGetValue(offsetMinutes, out var globalOffset)
            && !string.IsNullOrWhiteSpace(globalOffset))
        {
            return "全局 · 该档位文案";
        }

        return "全局模板";
    }

    /// <summary>提醒档位的可读描述，例如「提前 30 分钟 / 提前 5 分钟 / 准点」。</summary>
    public string DescribeReminders()
    {
        if (!ReminderEnabled) return "不提醒";

        var parts = new List<string>();
        if (RemindAtOverride.HasValue) parts.Add("指定 " + RemindAtOverride.Value.ToString("MM-dd HH:mm"));
        parts.AddRange(NormalizedOffsets().Select(ReminderPlan.DescribeOffset));
        return parts.Count == 0 ? "不提醒" : string.Join(" / ", parts);
    }

    /// <summary>
    /// 清理过期的触发记录，避免数据文件无限增长。
    /// 以「最近一次触发对应的发生时间」为基准而不是机器时钟——
    /// 否则当日程时间与系统时间相差较大时，刚写入的记录会被立刻删掉，导致重复提醒。
    /// </summary>
    public void PruneFiredReminders(int keepCount = 200, int keepDays = 90)
    {
        if (FiredReminders.Count == 0) return;

        var newest = FiredReminders.Max(r => r.OccurrenceStart);
        var threshold = newest.AddDays(-keepDays);
        FiredReminders.RemoveAll(r => r.OccurrenceStart < threshold);

        if (FiredReminders.Count > keepCount)
        {
            FiredReminders = FiredReminders
                .OrderByDescending(r => r.OccurrenceStart)
                .Take(keepCount)
                .ToList();
        }
    }

    public ScheduleItem Clone()
    {
        var copy = (ScheduleItem)MemberwiseClone();
        copy.ReminderOffsets = new List<int>(ReminderOffsets);
        copy.RepeatWeekdays = new List<DayOfWeek>(RepeatWeekdays);
        copy.ReminderVoiceTexts = new Dictionary<int, string>(ReminderVoiceTexts);
        copy.ReminderSoundFiles = new Dictionary<int, string>(ReminderSoundFiles);
        copy.FiredReminders = FiredReminders.Select(r => r.Clone()).ToList();
        copy.ExcludedOccurrences = new List<DateTime>(ExcludedOccurrences);
        copy.OccurrenceOverrides = OccurrenceOverrides.Select(o => o.Clone()).ToList();
        return copy;
    }
}
