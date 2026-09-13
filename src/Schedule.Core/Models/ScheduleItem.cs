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

    public RepeatKind Repeat { get; set; } = RepeatKind.None;

    /// <summary>重复截止日期（含当天），为空表示无限重复。</summary>
    public DateTime? RepeatUntil { get; set; }

    /// <summary>是否启用提醒。</summary>
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>提前提醒的分钟数（0 = 准点提醒）。</summary>
    public int RemindBeforeMinutes { get; set; } = 10;

    /// <summary>绝对提醒时间；仅对首次发生生效（重复日程的后续发生使用 RemindBeforeMinutes）。</summary>
    public DateTime? RemindAtOverride { get; set; }

    /// <summary>自定义语音播报文本；为空则使用全局模板。</summary>
    public string VoiceText { get; set; } = string.Empty;

    /// <summary>自定义提醒音频文件（wav/mp3/wma/m4a）；为空则使用全局设置。</summary>
    public string SoundFile { get; set; } = string.Empty;

    public ItemStatus Status { get; set; } = ItemStatus.Pending;

    public ItemOrigin Origin { get; set; } = ItemOrigin.Manual;

    /// <summary>来源标识（例如导入的 CSV 文件名）。</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>已经触发过提醒的发生时间，避免重复提醒。</summary>
    public DateTime? LastFiredOccurrence { get; set; }

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

    /// <summary>计算某次发生的提醒时刻。</summary>
    public DateTime ComputeReminderAt(DateTime occurrenceStart)
    {
        // 绝对提醒时间只针对首次发生；重复发生的后续实例按提前量推算。
        if (RemindAtOverride.HasValue && occurrenceStart == Start)
        {
            return RemindAtOverride.Value;
        }

        return occurrenceStart.AddMinutes(-RemindBeforeMinutes);
    }

    public ScheduleItem Clone() => (ScheduleItem)MemberwiseClone();
}
