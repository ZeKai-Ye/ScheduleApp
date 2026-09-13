namespace Schedule.Core.Scheduling;

/// <summary>日期计算辅助。</summary>
public static class DateHelpers
{
    /// <summary>取所在周的周一（或周日）。</summary>
    public static DateTime StartOfWeek(DateTime date, bool mondayFirst = true)
    {
        var offset = mondayFirst
            ? ((int)date.DayOfWeek + 6) % 7
            : (int)date.DayOfWeek;

        return date.Date.AddDays(-offset);
    }

    /// <summary>周一起算的星期下标：周一 = 0 … 周日 = 6。</summary>
    public static int MondayBasedIndex(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>把分钟数格式化为 HH:mm（可超过 24 小时用 +1 表示）。</summary>
    public static string MinutesToClock(int minutes)
    {
        var normalized = ((minutes % 1440) + 1440) % 1440;
        return (normalized / 60).ToString("00") + ":" + (normalized % 60).ToString("00");
    }
}
