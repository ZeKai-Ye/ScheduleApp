namespace Schedule.Core.Models;

/// <summary>
/// 系列事件中「某一次发生」的例外（RFC 5545 里 RECURRENCE-ID 的思路）：
/// 可以只改这一次的时间（改期）或内容，而不影响整个系列。
/// </summary>
public sealed class OccurrenceOverride
{
    /// <summary>被修改的那次发生的**原始**开始时间（定位用，不随改期变化）。</summary>
    public DateTime OriginalStart { get; set; }

    /// <summary>改期后的开始时间；为空表示时间不变。</summary>
    public DateTime? NewStart { get; set; }

    /// <summary>改期后的结束时间；为空则沿用原时长。</summary>
    public DateTime? NewEnd { get; set; }

    public string? Title { get; set; }

    public string? Notes { get; set; }

    public string? Category { get; set; }

    public string? Color { get; set; }

    public bool? AllDay { get; set; }

    public string? VoiceText { get; set; }

    public string? SoundFile { get; set; }

    public bool? ReminderEnabled { get; set; }

    public List<int>? ReminderOffsets { get; set; }

    public Dictionary<int, string>? ReminderVoiceTexts { get; set; }

    public ItemStatus? Status { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>是否只改了时间（纯改期）。</summary>
    public bool IsRescheduleOnly =>
        NewStart.HasValue && Title is null && Notes is null && Category is null && Color is null
        && AllDay is null && VoiceText is null && SoundFile is null && ReminderEnabled is null
        && ReminderOffsets is null && ReminderVoiceTexts is null && Status is null;

    /// <summary>是否没有任何实际修改。</summary>
    public bool IsEmpty => NewStart is null && NewEnd is null && Title is null && Notes is null && Category is null
        && Color is null && AllDay is null && VoiceText is null && SoundFile is null && ReminderEnabled is null
        && ReminderOffsets is null && ReminderVoiceTexts is null && Status is null;

    /// <summary>把覆盖写到一个日程副本上（不改 Start / End，时间由展开器负责）。</summary>
    public void ApplyTo(ScheduleItem item)
    {
        if (Title is not null) item.Title = Title;
        if (Notes is not null) item.Notes = Notes;
        if (Category is not null) item.Category = Category;
        if (Color is not null) item.Color = Color;
        if (AllDay.HasValue) item.AllDay = AllDay.Value;
        if (VoiceText is not null) item.VoiceText = VoiceText;
        if (SoundFile is not null) item.SoundFile = SoundFile;
        if (ReminderEnabled.HasValue) item.ReminderEnabled = ReminderEnabled.Value;
        if (ReminderOffsets is not null) item.ReminderOffsets = new List<int>(ReminderOffsets);
        if (ReminderVoiceTexts is not null) item.ReminderVoiceTexts = new Dictionary<int, string>(ReminderVoiceTexts);
        if (Status.HasValue) item.Status = Status.Value;
    }

    /// <summary>从「编辑后的日程」反推出覆盖内容（UI 保存「仅本次」时用）。</summary>
    public static OccurrenceOverride FromItem(DateTime originalStart, ScheduleItem edited, ScheduleItem series)
    {
        var result = new OccurrenceOverride
        {
            OriginalStart = originalStart,
            UpdatedAt = DateTime.Now,
        };

        if (edited.Start != originalStart) result.NewStart = edited.Start;
        if (edited.End != series.End) result.NewEnd = edited.End;
        if (!string.Equals(edited.Title, series.Title, StringComparison.Ordinal)) result.Title = edited.Title;
        if (!string.Equals(edited.Notes, series.Notes, StringComparison.Ordinal)) result.Notes = edited.Notes;
        if (!string.Equals(edited.Category, series.Category, StringComparison.Ordinal)) result.Category = edited.Category;
        if (!string.Equals(edited.Color, series.Color, StringComparison.Ordinal)) result.Color = edited.Color;
        if (edited.AllDay != series.AllDay) result.AllDay = edited.AllDay;
        if (!string.Equals(edited.VoiceText, series.VoiceText, StringComparison.Ordinal)) result.VoiceText = edited.VoiceText;
        if (!string.Equals(edited.SoundFile, series.SoundFile, StringComparison.Ordinal)) result.SoundFile = edited.SoundFile;
        if (edited.ReminderEnabled != series.ReminderEnabled) result.ReminderEnabled = edited.ReminderEnabled;
        if (edited.Status != series.Status) result.Status = edited.Status;

        if (!edited.NormalizedOffsets().SequenceEqual(series.NormalizedOffsets())) result.ReminderOffsets = edited.NormalizedOffsets();

        if (edited.ReminderVoiceTexts.Count != series.ReminderVoiceTexts.Count
            || edited.ReminderVoiceTexts.Any(p => !series.ReminderVoiceTexts.TryGetValue(p.Key, out var v) || v != p.Value))
        {
            result.ReminderVoiceTexts = new Dictionary<int, string>(edited.ReminderVoiceTexts);
        }

        return result;
    }

    public string Describe()
    {
        var parts = new List<string>();

        if (NewStart.HasValue) parts.Add("改期至 " + NewStart.Value.ToString("MM-dd HH:mm"));
        if (Title is not null) parts.Add("标题改为「" + Title + "」");
        if (Notes is not null) parts.Add("修改了备注");
        if (Category is not null) parts.Add("分类改为「" + Category + "」");
        if (Color is not null) parts.Add("修改了颜色");
        if (VoiceText is not null) parts.Add("修改了播报文案");
        if (ReminderOffsets is not null) parts.Add("修改了提醒档位");
        if (Status.HasValue) parts.Add("状态改为 " + Status.Value);
        if (parts.Count == 0) parts.Add("仅本次修改");

        return string.Join("、", parts);
    }

    public OccurrenceOverride Clone() => new()
    {
        OriginalStart = OriginalStart,
        NewStart = NewStart,
        NewEnd = NewEnd,
        Title = Title,
        Notes = Notes,
        Category = Category,
        Color = Color,
        AllDay = AllDay,
        VoiceText = VoiceText,
        SoundFile = SoundFile,
        ReminderEnabled = ReminderEnabled,
        ReminderOffsets = ReminderOffsets is null ? null : new List<int>(ReminderOffsets),
        ReminderVoiceTexts = ReminderVoiceTexts is null ? null : new Dictionary<int, string>(ReminderVoiceTexts),
        Status = Status,
        UpdatedAt = UpdatedAt,
    };
}
