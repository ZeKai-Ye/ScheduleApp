namespace Schedule.Core.Models;

/// <summary>优先级。</summary>
public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>重复规则。</summary>
public enum RepeatKind
{
    /// <summary>不重复。</summary>
    None = 0,
    /// <summary>每天。</summary>
    Daily = 1,
    /// <summary>每个工作日（周一至周五）。</summary>
    Weekdays = 2,
    /// <summary>每周同一天。</summary>
    Weekly = 3,
    /// <summary>每月同一日。</summary>
    Monthly = 4,
    /// <summary>每年同一日。</summary>
    Yearly = 5,
}

/// <summary>日程状态。</summary>
public enum ItemStatus
{
    /// <summary>待办。</summary>
    Pending = 0,
    /// <summary>已完成。</summary>
    Done = 1,
    /// <summary>已取消。</summary>
    Canceled = 2,
}

/// <summary>日程来源。</summary>
public enum ItemOrigin
{
    Manual = 0,
    CsvImport = 1,
}
