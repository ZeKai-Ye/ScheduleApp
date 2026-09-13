using System.Collections.ObjectModel;
using Schedule.App.Mvvm;
using Schedule.Core.Csv;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Storage;
using Schedule.Core.Templating;

namespace Schedule.App.ViewModels;

/// <summary>时间范围筛选。</summary>
public enum TimeFilter
{
    Today = 0,
    Tomorrow = 1,
    ThisWeek = 2,
    Next30Days = 3,
    All = 4,
    Completed = 5,
}

public sealed class FilterOption
{
    public required TimeFilter Value { get; init; }

    public required string Name { get; init; }

    public override string ToString() => Name;
}

/// <summary>列表中的一行（日程的一次具体发生）。</summary>
public sealed class ScheduleRow
{
    public required ScheduleItem Item { get; init; }

    public required DateTime OccurrenceStart { get; init; }

    public required DateTime OccurrenceEnd { get; init; }

    public required string DayLabel { get; init; }

    public required string TimeText { get; init; }

    public required string CategoryText { get; init; }

    public required string ReminderText { get; init; }

    public required string RepeatText { get; init; }

    public required string PriorityText { get; init; }

    public required string NotesPreview { get; init; }

    public required string SourceText { get; init; }

    public string TitleText => Item.Title;

    public Priority Priority => Item.Priority;

    public bool IsDone => Item.Status != ItemStatus.Pending;

    public bool IsAllDay => Item.AllDay;

    public override string ToString() => TimeText + " " + Item.Title;
}

/// <summary>主界面视图模型：负责筛选、展开重复日程、统计与状态栏文本。</summary>
public sealed class MainViewModel : ObservableObject
{
    public const string AllCategories = "全部分类";

    private const int RowLimit = 2000;

    private readonly AppHost _host;

    private FilterOption _selectedFilter;
    private string _selectedCategory = AllCategories;
    private string _searchText = string.Empty;
    private string _countText = string.Empty;
    private string _nextReminderText = string.Empty;
    private bool _isEmpty = true;
    private int _totalCount;
    private int _pendingCount;
    private int _todayCount;
    private bool _capped;

    public MainViewModel(AppHost host)
    {
        _host = host;

        Filters = new ObservableCollection<FilterOption>
        {
            new() { Value = TimeFilter.Today, Name = "今天" },
            new() { Value = TimeFilter.Tomorrow, Name = "明天" },
            new() { Value = TimeFilter.ThisWeek, Name = "本周" },
            new() { Value = TimeFilter.Next30Days, Name = "未来 30 天" },
            new() { Value = TimeFilter.All, Name = "全部待办" },
            new() { Value = TimeFilter.Completed, Name = "已完成 / 已取消" },
        };

        _selectedFilter = Filters[3];
    }

    public ObservableCollection<ScheduleRow> Rows { get; } = new();

    public ObservableCollection<FilterOption> Filters { get; }

    public ObservableCollection<string> Categories { get; } = new();

    public string DataDirectoryText => AppPaths.DataDirectory;

    public FilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value == null) return;
            if (SetProperty(ref _selectedFilter, value)) Refresh();
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value == null) return;
            if (SetProperty(ref _selectedCategory, value)) Refresh();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty)) Refresh();
        }
    }

    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    public string NextReminderText
    {
        get => _nextReminderText;
        private set => SetProperty(ref _nextReminderText, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>是否显示已完成 / 已取消的日程（写回设置）。</summary>
    public bool ShowCompleted
    {
        get => _host.Settings.ShowCompleted;
        set
        {
            if (_host.Settings.ShowCompleted == value) return;

            _host.Settings.ShowCompleted = value;
            _host.SettingsService.MarkDirty();
            _host.SettingsService.Save();
            OnPropertyChanged();
            Refresh();
        }
    }

    /// <summary>重新计算列表内容。</summary>
    public void Refresh()
    {
        var items = _host.Repository.Snapshot();
        var now = DateTime.Now;
        var today = now.Date;
        var lookAheadDays = Math.Clamp(_host.Settings.LookAheadDays, 1, 3650);

        RefreshCategories(items);

        var (from, to) = ResolveWindow(SelectedFilter.Value, today, lookAheadDays);
        var rows = new List<ScheduleRow>();
        var pendingCount = 0;
        var todayCount = 0;

        foreach (var item in items)
        {
            if (!MatchesCategory(item) || !MatchesSearch(item)) continue;
            if (item.Status == ItemStatus.Pending) pendingCount++;

            if (SelectedFilter.Value == TimeFilter.Completed)
            {
                if (item.Status == ItemStatus.Pending) continue;
                rows.Add(CreateRow(item, item.Start, item.EffectiveEnd, now));
                continue;
            }

            if (item.Status != ItemStatus.Pending && !_host.Settings.ShowCompleted) continue;

            // 已完成 / 已取消的日程只展示自身那一次
            if (item.Status != ItemStatus.Pending)
            {
                if (item.Start < from || item.Start > to) continue;
                rows.Add(CreateRow(item, item.Start, item.EffectiveEnd, now));
                continue;
            }

            if (item.Repeat == RepeatKind.None)
            {
                if (item.Start < from || item.Start > to) continue;
                rows.Add(CreateRow(item, item.Start, item.EffectiveEnd, now));
                if (item.Start >= today && item.Start < today.AddDays(1)) todayCount++;
                continue;
            }

            // 重复日程不展示历史实例，避免「每天」类日程产生上千行
            var windowFrom = from < today ? today : from;
            foreach (var occurrence in OccurrenceCalculator.Expand(item, windowFrom, to))
            {
                rows.Add(CreateRow(item, occurrence.Start, occurrence.End, now));
                if (occurrence.Start >= today && occurrence.Start < today.AddDays(1)) todayCount++;
                if (rows.Count >= RowLimit) break;
            }

            if (rows.Count >= RowLimit) break;
        }

        rows.Sort(static (a, b) => a.OccurrenceStart.CompareTo(b.OccurrenceStart));

        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);

        _totalCount = Rows.Count;
        _pendingCount = pendingCount;
        _todayCount = todayCount;
        _capped = rows.Count >= RowLimit;
        IsEmpty = Rows.Count == 0;

        UpdateCountText();
        UpdateNextReminder(items, now);
    }

    /// <summary>只刷新状态栏（提醒轮询调用，不重建列表）。</summary>
    public void RefreshStatus()
    {
        UpdateCountText();
        UpdateNextReminder(_host.Repository.Snapshot(), DateTime.Now);
    }

    private void UpdateCountText()
    {
        CountText = "共 " + _totalCount + " 条 · 待办 " + _pendingCount + " · 今天 " + _todayCount
            + (_capped ? "（仅显示前 " + RowLimit + " 条）" : string.Empty);
    }

    private void UpdateNextReminder(List<ScheduleItem> items, DateTime now)
    {
        var next = ReminderPlanner.NextUpcoming(items, now);
        if (next == null)
        {
            NextReminderText = "下一次提醒：无";
            return;
        }

        NextReminderText = "下一次提醒：" + next.ReminderAt.ToString("MM-dd HH:mm") + " 「" + next.Item.Title + "」"
            + "（" + VoiceTemplate.DescribeCountdown(next.Start, now) + "）";
    }

    private static (DateTime From, DateTime To) ResolveWindow(TimeFilter filter, DateTime today, int lookAheadDays)
    {
        switch (filter)
        {
            case TimeFilter.Today:
                return (today, today.AddDays(1));
            case TimeFilter.Tomorrow:
                return (today.AddDays(1), today.AddDays(2));
            case TimeFilter.ThisWeek:
            {
                var start = StartOfWeek(today);
                return (start, start.AddDays(7));
            }
            case TimeFilter.Completed:
                return (today.AddYears(-5), today.AddYears(5));
            case TimeFilter.All:
                return (today.AddYears(-5), today.AddDays(lookAheadDays));
            default:
                return (today, today.AddDays(30));
        }
    }

    public static DateTime StartOfWeek(DateTime date)
    {
        var delta = ((int)date.DayOfWeek + 6) % 7; // 周一为一周开始
        return date.AddDays(-delta).Date;
    }

    private void RefreshCategories(List<ScheduleItem> items)
    {
        var categories = items
            .Select(i => string.IsNullOrWhiteSpace(i.Category) ? "未分类" : i.Category.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();

        var target = new List<string> { AllCategories };
        target.AddRange(categories);

        if (Categories.SequenceEqual(target)) return;

        Categories.Clear();
        foreach (var category in target) Categories.Add(category);

        if (!Categories.Contains(SelectedCategory))
        {
            _selectedCategory = AllCategories;
            OnPropertyChanged(nameof(SelectedCategory));
        }
    }

    private bool MatchesCategory(ScheduleItem item)
    {
        if (SelectedCategory == AllCategories) return true;

        var category = string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category.Trim();
        return string.Equals(category, SelectedCategory, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool MatchesSearch(ScheduleItem item)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var keyword = SearchText.Trim();
        return Contains(item.Title, keyword)
            || Contains(item.Notes, keyword)
            || Contains(item.Category, keyword)
            || Contains(item.VoiceText, keyword);

        static bool Contains(string? source, string keyword)
            => !string.IsNullOrEmpty(source) && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>切换完成状态。</summary>
    public void ToggleDone(ScheduleRow row)
    {
        var item = _host.Repository.Find(row.Item.Id);
        if (item == null) return;

        item.Status = item.Status == ItemStatus.Pending ? ItemStatus.Done : ItemStatus.Pending;
        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
    }

    /// <summary>删除日程（重复日程会删除整个系列）。</summary>
    public bool Delete(ScheduleRow row)
    {
        var removed = _host.Repository.Remove(row.Item.Id);
        if (removed)
        {
            _host.Repository.Save();
            Refresh();
        }

        return removed;
    }

    public static ScheduleRow CreateRow(ScheduleItem item, DateTime start, DateTime end, DateTime now)
    {
        return new ScheduleRow
        {
            Item = item,
            OccurrenceStart = start,
            OccurrenceEnd = end,
            DayLabel = BuildDayLabel(start, now),
            TimeText = BuildTimeText(item, start, end),
            CategoryText = string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category,
            ReminderText = BuildReminderText(item, start),
            RepeatText = CsvExporter.RepeatText(item.Repeat),
            PriorityText = CsvExporter.PriorityText(item.Priority),
            NotesPreview = BuildNotesPreview(item.Notes),
            SourceText = item.Origin == ItemOrigin.CsvImport
                ? (string.IsNullOrWhiteSpace(item.Source) ? "CSV 导入" : "导入：" + item.Source)
                : "手动创建",
        };
    }

    public static string BuildDayLabel(DateTime start, DateTime now)
    {
        var date = start.Date;
        var diff = (date - now.Date).Days;
        var prefix = diff switch
        {
            0 => "今天 · ",
            1 => "明天 · ",
            2 => "后天 · ",
            -1 => "昨天 · ",
            _ => string.Empty,
        };

        var label = date.Year == now.Year
            ? date.ToString("M月d日") + " " + VoiceTemplate.WeekdayText(date.DayOfWeek)
            : date.ToString("yyyy年M月d日") + " " + VoiceTemplate.WeekdayText(date.DayOfWeek);

        return prefix + label;
    }

    public static string BuildTimeText(ScheduleItem item, DateTime start, DateTime end)
    {
        if (item.AllDay) return "全天";

        var text = start.ToString("HH:mm");
        if (end <= start) return text;

        if (end.Date > start.Date)
        {
            return text + " - 次日 " + end.ToString("HH:mm");
        }

        return text + " - " + end.ToString("HH:mm");
    }

    public static string BuildReminderText(ScheduleItem item, DateTime start)
    {
        if (!item.ReminderEnabled) return "不提醒";

        if (item.RemindAtOverride.HasValue && start == item.Start)
        {
            return "定时 " + item.RemindAtOverride.Value.ToString("MM-dd HH:mm");
        }

        if (item.RemindBeforeMinutes <= 0) return "准点提醒";

        return "提前 " + HumanizeMinutes(item.RemindBeforeMinutes);
    }

    public static string HumanizeMinutes(int minutes)
    {
        if (minutes <= 0) return "0 分钟";
        if (minutes % (60 * 24) == 0) return (minutes / (60 * 24)) + " 天";
        if (minutes % 60 == 0) return (minutes / 60) + " 小时";
        if (minutes > 60) return (minutes / 60) + " 小时 " + (minutes % 60) + " 分钟";
        return minutes + " 分钟";
    }

    private static string BuildNotesPreview(string? notes)
    {
        var text = VoiceTemplate.SingleLine(notes);
        if (text.Length == 0) return string.Empty;
        return text.Length <= 60 ? text : text.Substring(0, 57) + "...";
    }
}
