using System.Collections.ObjectModel;
using Schedule.App.Mvvm;
using Schedule.Core.Csv;
using Schedule.Core.Localization;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;
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

    public required string ColorHex { get; init; }

    /// <summary>该次发生在系列中的**原始**开始时间（改期后定位例外仍用它）。</summary>
    public DateTime SeriesStart { get; init; }

    /// <summary>是否属于重复系列。</summary>
    public bool IsSeries => Item.Repeat != RepeatKind.None;

    /// <summary>这一次是否已有「仅本次」例外。</summary>
    public bool HasOccurrenceEdit { get; init; }

    /// <summary>这一次是否被改期过。</summary>
    public bool IsRescheduled { get; init; }

    public System.Windows.Media.Brush ColorBrush => BrushCache.Get(ColorHex);

    public string TitleText => Item.Title;

    public Priority Priority => Item.Priority;

    public bool IsDone => Item.Status != ItemStatus.Pending;

    public bool IsAllDay => Item.AllDay;

    public override string ToString() => TimeText + " " + Item.Title;
}

/// <summary>主界面视图模型：列表 / 周 / 月三种视图，负责筛选、展开、统计与状态栏文本。</summary>
public sealed class MainViewModel : ObservableObject
{
    public static string AllCategories => Loc.T("全部分类", "All categories");

    private const int RowLimit = 2000;

    private readonly AppHost _host;

    private ScheduleViewMode _viewMode;
    private FilterOption _selectedFilter;
    private string _selectedCategory = AllCategories;
    private string _searchText = string.Empty;
    private string _countText = string.Empty;
    private string _nextReminderText = string.Empty;
    private string _periodLabel = string.Empty;
    private string _selectedDayLabel = string.Empty;
    private string _monthSummaryText = string.Empty;
    private bool _isEmpty = true;
    private int _totalCount;
    private int _pendingCount;
    private int _todayCount;
    private bool _capped;

    private DateTime _anchorDate = DateTime.Today;
    private DateOnly _selectedDay = DateOnly.FromDateTime(DateTime.Today);
    private double _weekColumnWidth;

    public MainViewModel(AppHost host)
    {
        _host = host;

        Filters = new ObservableCollection<FilterOption>
        {
            new() { Value = TimeFilter.Today, Name = Loc.T("今天", "Today") },
            new() { Value = TimeFilter.Tomorrow, Name = Loc.T("明天", "Tomorrow") },
            new() { Value = TimeFilter.ThisWeek, Name = Loc.T("本周", "This week") },
            new() { Value = TimeFilter.Next30Days, Name = Loc.T("未来 30 天", "Next 30 days") },
            new() { Value = TimeFilter.All, Name = Loc.T("全部待办", "All open") },
            new() { Value = TimeFilter.Completed, Name = Loc.T("已完成 / 已取消", "Done / canceled") },
        };

        _selectedFilter = Filters[3];
        _viewMode = host.Settings.DefaultView;
    }

    // ================= 集合 =================

    public ObservableCollection<ScheduleRow> Rows { get; } = new();

    public ObservableCollection<ScheduleRow> SelectedDayRows { get; } = new();

    public ObservableCollection<MonthCellViewModel> MonthCells { get; } = new();

    public ObservableCollection<WeekColumnViewModel> WeekColumns { get; } = new();

    public ObservableCollection<WeekHourViewModel> WeekHours { get; } = new();

    public ObservableCollection<string> WeekdayHeaders { get; } = new();

    public ObservableCollection<FilterOption> Filters { get; }

    public ObservableCollection<string> Categories { get; } = new();

    // ================= 状态 =================

    public string DataDirectoryText => AppPaths.DataDirectory;

    public ScheduleViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (!SetProperty(ref _viewMode, value)) return;

            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(IsWeekView));
            OnPropertyChanged(nameof(IsMonthView));
            OnPropertyChanged(nameof(IsCalendarView));
            Refresh();
        }
    }

    public bool IsListView => _viewMode == ScheduleViewMode.List;

    public bool IsWeekView => _viewMode == ScheduleViewMode.Week;

    public bool IsMonthView => _viewMode == ScheduleViewMode.Month;

    public bool IsCalendarView => _viewMode is ScheduleViewMode.Week or ScheduleViewMode.Month;

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

    public string PeriodLabel
    {
        get => _periodLabel;
        private set => SetProperty(ref _periodLabel, value);
    }

    public string SelectedDayLabel
    {
        get => _selectedDayLabel;
        private set => SetProperty(ref _selectedDayLabel, value);
    }

    public string MonthSummaryText
    {
        get => _monthSummaryText;
        private set => SetProperty(ref _monthSummaryText, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public double WeekGridHeight => 24 * HourHeight;

    public int HourHeight => Math.Clamp(_host.Settings.HourHeight, 32, 140);

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

    // ================= 刷新 =================

    public void Refresh()
    {
        var items = _host.Repository.Snapshot();
        var now = DateTime.Now;

        RefreshCategories(items);
        RefreshWeekdayHeaders();

        switch (_viewMode)
        {
            case ScheduleViewMode.Week:
                RefreshWeek(items, now);
                break;
            case ScheduleViewMode.Month:
                RefreshMonth(items, now);
                break;
            default:
                RefreshList(items, now);
                break;
        }

        UpdateNextReminder(items, now);
    }

    /// <summary>只刷新状态栏（提醒轮询调用，不重建视图）。</summary>
    public void RefreshStatus()
    {
        UpdateCountText();
        UpdateNextReminder(_host.Repository.Snapshot(), DateTime.Now);
    }

    // ================= 列表视图 =================

    private void RefreshList(List<ScheduleItem> items, DateTime now)
    {
        var today = now.Date;
        var lookAheadDays = Math.Clamp(_host.Settings.LookAheadDays, 1, 3650);
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
                rows.Add(CreateRow(OccurrenceCalculator.Create(item, item.Start), now));
                continue;
            }

            if (item.Status != ItemStatus.Pending && !_host.Settings.ShowCompleted) continue;

            if (item.Status != ItemStatus.Pending)
            {
                if (item.Start < from || item.Start > to) continue;
                rows.Add(CreateRow(OccurrenceCalculator.Create(item, item.Start), now));
                continue;
            }

            if (item.Repeat == RepeatKind.None)
            {
                if (item.Start < from || item.Start > to) continue;
                rows.Add(CreateRow(OccurrenceCalculator.Create(item, item.Start), now));
                if (item.Start >= today && item.Start < today.AddDays(1)) todayCount++;
                continue;
            }

            var windowFrom = from < today ? today : from;
            foreach (var occurrence in OccurrenceCalculator.ExpandWithOverrides(item, windowFrom, to))
            {
                // 改期到窗口外的那一次不该出现在这个筛选里
                if (occurrence.Start < windowFrom || occurrence.Start > to) continue;

                rows.Add(CreateRow(occurrence, now));
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

        PeriodLabel = SelectedFilter.Value == TimeFilter.Completed ? Loc.T("已完成 / 已取消", "Done / canceled") : SelectedFilter.Name;
        UpdateCountText();
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
                var start = DateHelpers.StartOfWeek(today);
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

    // ================= 周视图 =================

    /// <summary>窗口尺寸变化时更新周视图列宽（决定重叠块的像素布局）。</summary>
    public void UpdateWeekWidth(double availableWidth)
    {
        if (availableWidth <= 0) return;

        var columnWidth = Math.Max(70, (availableWidth / 7.0) - 2);
        if (Math.Abs(columnWidth - _weekColumnWidth) < 6) return;

        _weekColumnWidth = columnWidth;
        if (IsWeekView) RefreshWeek(_host.Repository.Snapshot(), DateTime.Now);
    }

    private void RefreshWeek(List<ScheduleItem> items, DateTime now)
    {
        var mondayFirst = _host.Settings.WeekStartsOnMonday;
        var weekStart = DateHelpers.StartOfWeek(_anchorDate, mondayFirst);
        var today = now.Date;
        var hourHeight = HourHeight;
        var columnWidth = _weekColumnWidth <= 0 ? 110 : _weekColumnWidth;

        EnsureWeekHours(hourHeight);

        var filtered = items.Where(i => MatchesCategory(i) && MatchesSearch(i)).ToList();
        var layout = CalendarLayout.BuildWeek(filtered, weekStart, today);

        WeekColumns.Clear();
        var total = 0;

        foreach (var column in layout)
        {
            var columnViewModel = new WeekColumnViewModel
            {
                Date = column.Date,
                HeaderText = VoiceTemplate.ShortWeekdayText(column.Date.DayOfWeek),
                DayNumberText = column.Date.Day.ToString(),
                IsToday = column.IsToday,
                IsWeekend = column.IsWeekend,
                CanvasHeight = WeekGridHeight,
            };

            foreach (var block in column.TimedBlocks)
            {
                var slice = columnWidth / Math.Max(1, block.ColumnCount);

                columnViewModel.Blocks.Add(new WeekBlockViewModel
                {
                    Item = block.Occurrence.Item,
                    Start = block.Occurrence.Start,
                    SeriesStart = block.Occurrence.SeriesStart,
                    IsSeries = block.Occurrence.Item.Repeat != RepeatKind.None,
                    HasOccurrenceEdit = block.Occurrence.IsOverride,
                    IsRescheduled = block.Occurrence.Override?.NewStart.HasValue ?? false,
                    End = block.Occurrence.End,
                    TitleText = block.Occurrence.Item.Title,
                    TimeText = BuildTimeText(block.Occurrence.Item, block.Occurrence.Start, block.Occurrence.End),
                    ColorHex = EventColors.Resolve(block.Occurrence.Item, _host.Settings.CategoryColors),
                    IsDone = block.Occurrence.Item.Status != ItemStatus.Pending,
                    Top = block.StartMinutes / 60.0 * hourHeight,
                    Height = Math.Max(18, block.DurationMinutes / 60.0 * hourHeight),
                    Left = (block.Column * slice) + 2,
                    Width = Math.Max(16, slice - 5),
                    ToolTipText = BuildToolTip(block.Occurrence)
                        + Environment.NewLine + Environment.NewLine
                        + Loc.T("提示：直接拖动可改期（默认只改这一次；按住 Shift 拖动 = 整个系列）", "Tip: drag to reschedule (this occurrence only by default; hold Shift to move the whole series)"),
                });

                total++;
            }

            foreach (var allDay in column.AllDayEvents)
            {
                columnViewModel.AllDayChips.Add(CreateChip(allDay, allDay.Item, allDay.Start, allDay.End));
                total++;
            }

            WeekColumns.Add(columnViewModel);
        }

        _totalCount = total;
        _pendingCount = filtered.Count(i => i.Status == ItemStatus.Pending);
        _todayCount = WeekColumns.Where(c => c.Date == DateOnly.FromDateTime(today))
            .Sum(c => c.Blocks.Count + c.AllDayChips.Count);
        _capped = false;
        IsEmpty = total == 0;

        var weekEnd = weekStart.AddDays(6);
        var isCurrentWeek = weekStart <= today && today <= weekEnd;
        PeriodLabel = FormatMonthDay(weekStart) + " - " + FormatMonthDay(weekEnd) + (isCurrentWeek ? Loc.T("（本周）", " (this week)") : string.Empty);

        UpdateCountText();
    }

    private void EnsureWeekHours(int hourHeight)
    {
        var expected = Enumerable.Range(0, 24)
            .Select(hour => new WeekHourViewModel
            {
                Label = hour.ToString("00") + ":00",
                Height = hourHeight,
                Hour = hour,
            })
            .ToList();

        if (WeekHours.Count == 24 && Math.Abs(WeekHours[0].Height - hourHeight) < 0.1) return;

        WeekHours.Clear();
        foreach (var hour in expected) WeekHours.Add(hour);
        OnPropertyChanged(nameof(WeekGridHeight));
    }

    // ================= 月视图 =================

    private void RefreshMonth(List<ScheduleItem> items, DateTime now)
    {
        var today = now.Date;
        var monthStart = new DateTime(_anchorDate.Year, _anchorDate.Month, 1);
        var capacity = Math.Clamp(_host.Settings.MonthCellCapacity, 1, 8);

        var filtered = items.Where(i => MatchesCategory(i) && MatchesSearch(i)).ToList();
        var cells = CalendarLayout.BuildMonth(
            filtered,
            monthStart.Year,
            monthStart.Month,
            today,
            capacity,
            _host.Settings.WeekStartsOnMonday);

        MonthCells.Clear();
        var monthTotal = 0;
        var todayTotal = 0;

        foreach (var cell in cells)
        {
            var viewModel = new MonthCellViewModel
            {
                Date = cell.Date,
                DayNumber = cell.DayNumber,
                IsCurrentMonth = cell.IsCurrentMonth,
                IsToday = cell.IsToday,
                IsWeekend = cell.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                EventCount = cell.Events.Count,
                MoreText = cell.HiddenCount > 0 ? "+" + cell.HiddenCount : string.Empty,
                Chips = cell.Events.Take(cell.VisibleCount)
                    .Select(o => CreateChip(o, o.Item, o.Start, o.End))
                    .ToList(),
                IsSelected = cell.Date == _selectedDay,
            };

            if (cell.IsCurrentMonth) monthTotal += cell.Events.Count;
            if (cell.IsToday) todayTotal = cell.Events.Count;

            MonthCells.Add(viewModel);
        }

        _totalCount = monthTotal;
        _pendingCount = filtered.Count(i => i.Status == ItemStatus.Pending);
        _todayCount = todayTotal;
        _capped = false;
        IsEmpty = monthTotal == 0;

        PeriodLabel = Loc.IsEnglish ? monthStart.ToString("MMMM yyyy", Loc.Culture) : monthStart.ToString("yyyy年M月");
        MonthSummaryText = Loc.F("本月 {0} 项 · 今天 {1} 项", "{0} items this month · {1} today", monthTotal, todayTotal);

        RefreshSelectedDay(filtered, now);
        UpdateCountText();
    }

    /// <summary>选中月视图中的某一天。</summary>
    public void SelectDay(DateOnly day)
    {
        _selectedDay = day;

        foreach (var cell in MonthCells)
        {
            cell.IsSelected = cell.Date == day;
        }

        RefreshSelectedDay(_host.Repository.Snapshot().Where(i => MatchesCategory(i) && MatchesSearch(i)).ToList(), DateTime.Now);
    }

    public DateOnly SelectedDay => _selectedDay;

    private void RefreshSelectedDay(List<ScheduleItem> items, DateTime now)
    {
        var from = _selectedDay.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);

        var rows = new List<ScheduleRow>();

        foreach (var item in items)
        {
            if (item.Status != ItemStatus.Pending && !_host.Settings.ShowCompleted) continue;

            if (item.Repeat == RepeatKind.None)
            {
                if (item.Start >= from && item.Start < to) rows.Add(CreateRow(OccurrenceCalculator.Create(item, item.Start), now));
                continue;
            }

            foreach (var occurrence in OccurrenceCalculator.ExpandWithOverrides(item, from, to))
            {
                if (occurrence.Start < from || occurrence.Start >= to) continue;
                rows.Add(CreateRow(occurrence, now));
            }
        }

        rows.Sort(static (a, b) => a.OccurrenceStart.CompareTo(b.OccurrenceStart));

        SelectedDayRows.Clear();
        foreach (var row in rows) SelectedDayRows.Add(row);

        SelectedDayLabel = FormatMonthDay(from) + " " + VoiceTemplate.WeekdayText(from.DayOfWeek)
            + Loc.F(" · {0} 项", " · {0} items", rows.Count);
    }

    private void RefreshWeekdayHeaders()
    {
        var mondayFirst = _host.Settings.WeekStartsOnMonday;
        var headers = Enumerable.Range(0, 7)
            .Select(i => VoiceTemplate.ShortWeekdayText((DayOfWeek)(((mondayFirst ? 1 : 0) + i) % 7)))
            .ToList();

        if (WeekdayHeaders.SequenceEqual(headers)) return;

        WeekdayHeaders.Clear();
        foreach (var header in headers) WeekdayHeaders.Add(header);
    }

    // ================= 导航 =================

    public void GoPrevious() => Shift(-1);

    public void GoNext() => Shift(1);

    public void GoToday()
    {
        _anchorDate = DateTime.Today;
        _selectedDay = DateOnly.FromDateTime(DateTime.Today);
        Refresh();
    }

    private void Shift(int direction)
    {
        switch (_viewMode)
        {
            case ScheduleViewMode.Month:
                _anchorDate = new DateTime(_anchorDate.Year, _anchorDate.Month, 1).AddMonths(direction);
                break;
            case ScheduleViewMode.Week:
                _anchorDate = _anchorDate.AddDays(7 * direction);
                break;
            default:
                SelectedFilter = Filters[0];
                return;
        }

        Refresh();
    }

    // ================= 行构造 =================

    private string ResolveColor(ScheduleItem item) => EventColors.Resolve(item, _host.Settings.CategoryColors);

    private MonthChipViewModel CreateChip(ScheduleOccurrence occurrence, ScheduleItem item, DateTime start, DateTime end)
    {
        var color = ResolveColor(item);

        return new MonthChipViewModel
        {
            Item = item,
            Start = start,
            SeriesStart = occurrence.SeriesStart,
            IsSeries = item.Repeat != RepeatKind.None,
            HasOccurrenceEdit = occurrence.IsOverride,
            IsRescheduled = occurrence.Override?.NewStart.HasValue ?? false,
            TimeText = item.AllDay ? Loc.T("全天", "All day") : start.ToString("HH:mm"),
            TitleText = item.Title,
            ColorHex = color,
            IsDone = item.Status != ItemStatus.Pending,
            ToolTipText = BuildToolTip(occurrence),
        };
    }

    /// <summary>按一次具体发生构造列表行（改期后的时间与内容都取自 occurrence.Item）。</summary>
    private ScheduleRow CreateRow(ScheduleOccurrence occurrence, DateTime now)
    {
        var item = occurrence.Item;
        var start = occurrence.Start;
        var end = occurrence.End;

        return new ScheduleRow
        {
            SeriesStart = occurrence.SeriesStart,
            HasOccurrenceEdit = occurrence.IsOverride,
            IsRescheduled = occurrence.Override?.NewStart.HasValue ?? false,
            Item = item,
            OccurrenceStart = start,
            OccurrenceEnd = end,
            DayLabel = BuildDayLabel(start, now),
            TimeText = BuildTimeText(item, start, end),
            CategoryText = string.IsNullOrWhiteSpace(item.Category) ? Loc.T("未分类", "Uncategorized") : item.Category,
            ReminderText = BuildReminderText(item, start),
            RepeatText = BuildRepeatText(item),
            PriorityText = CsvExporter.PriorityText(item.Priority),
            NotesPreview = BuildNotesPreview(item.Notes),
            SourceText = item.Origin == ItemOrigin.CsvImport
                ? (string.IsNullOrWhiteSpace(item.Source) ? Loc.T("CSV 导入", "From CSV") : Loc.T("导入：", "Imported: ") + item.Source)
                : Loc.T("手动创建", "Created manually"),
            ColorHex = ResolveColor(item),
        };
    }

    private static string BuildToolTip(ScheduleOccurrence occurrence)
    {
        var item = occurrence.Item;
        var lines = new List<string>
        {
            item.Title,
            MainViewModelStatic.TimeRange(item, occurrence.Start, occurrence.End),
        };

        if (!string.IsNullOrWhiteSpace(item.Category)) lines.Add(Loc.T("分类：", "Category: ") + item.Category);
        lines.Add(Loc.T("重复：", "Repeat: ") + BuildRepeatText(item));
        lines.Add(Loc.T("提醒：", "Reminder: ") + BuildReminderText(item, occurrence.Start));
        if (!string.IsNullOrWhiteSpace(item.Notes)) lines.Add(Loc.T("备注：", "Notes: ") + VoiceTemplate.SingleLine(item.Notes));

        return string.Join(Environment.NewLine, lines);
    }

    // ================= 通用工具 =================

    /// <summary>只删除系列中的某一次发生。</summary>
    public bool DeleteOccurrence(ScheduleItem series, DateTime occurrenceStart)
    {
        var item = _host.Repository.Find(series.Id);
        if (item == null) return false;

        item.ExcludeOccurrence(occurrenceStart);
        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
        return true;
    }

    /// <summary>只修改系列中的某一次发生（含改期）。</summary>
    public bool ApplyOccurrenceEdit(ScheduleItem series, DateTime occurrenceStart, ScheduleItem edited)
    {
        var item = _host.Repository.Find(series.Id);
        if (item == null) return false;

        // 非重复日程没有「某一次」的概念：直接整体覆盖，避免写出无意义的例外
        if (item.Repeat == RepeatKind.None)
        {
            edited.Id = item.Id;
            edited.UpdatedAt = DateTime.Now;
            _host.Repository.Update(edited);
            _host.Repository.Save();
            Refresh();
            return true;
        }

        item.UpsertOverride(OccurrenceOverride.FromItem(occurrenceStart, edited, item));
        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
        return true;
    }

    /// <summary>撤销某一次发生的「仅本次」例外。</summary>
    public bool RestoreOccurrence(ScheduleItem series, DateTime occurrenceStart)
    {
        var item = _host.Repository.Find(series.Id);
        if (item == null) return false;

        if (!item.ClearOccurrenceEdit(occurrenceStart)) return false;

        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
        return true;
    }

    /// <summary>改期：可以只改某一次，也可以整体平移整个系列。</summary>
    public bool Reschedule(ScheduleItem series, DateTime occurrenceStart, DateTime newStart, DateTime? newEnd, bool wholeSeries)
    {
        var item = _host.Repository.Find(series.Id);
        if (item == null) return false;

        // 非重复日程只有「整体」一种改法：写成「仅本次」例外会让这条日程带着
        // 无意义的例外数据，之后在编辑窗口里保存就会被校验拦下（"非重复日程不应带有「仅本次」例外"）。
        if (wholeSeries || item.Repeat == RepeatKind.None)
        {
            // 整体平移：按原开始时间算偏移量，例外键一起跟着走
            item.Shift(newStart - item.Start);

            if (newEnd.HasValue)
            {
                item.End = item.Start.Date == newEnd.Value.Date
                    ? newEnd
                    : item.Start + (newEnd.Value - newStart);
            }
        }
        else
        {
            // 基于「当前有效值」构造覆盖，避免丢掉已有的其它覆盖字段
            var effective = OccurrenceCalculator.Create(item, occurrenceStart).Item.Clone();
            effective.Start = newStart;
            effective.End = newEnd;

            item.UpsertOverride(OccurrenceOverride.FromItem(occurrenceStart, effective, item));
        }

        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
        return true;
    }

    /// <summary>整体平移整个系列（保持天数差）。</summary>
    public bool ShiftSeries(ScheduleItem series, TimeSpan delta)
    {
        var item = _host.Repository.Find(series.Id);
        if (item == null) return false;

        item.Shift(delta);
        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
        return true;
    }

    public void ToggleDone(ScheduleRow row) => ToggleDone(row.Item);

    /// <summary>切换整个系列的完成状态（日历上直接标记时用）。</summary>
    public void ToggleDone(ScheduleItem series)
    {
        var item = _host.Repository.Find(series.Id);
        if (item == null) return;

        item.Status = item.Status == ItemStatus.Pending ? ItemStatus.Done : ItemStatus.Pending;
        item.UpdatedAt = DateTime.Now;
        _host.Repository.Update(item);
        _host.Repository.Save();
        Refresh();
    }

    public bool Delete(ScheduleRow row) => Delete(row.Item);

    /// <summary>删除整条日程（含整个系列）。</summary>
    public bool Delete(ScheduleItem series)
    {
        var removed = _host.Repository.Remove(series.Id);
        if (removed)
        {
            _host.Repository.Save();
            Refresh();
        }

        return removed;
    }

    private void RefreshCategories(List<ScheduleItem> items)
    {
        var categories = items
            .Select(i => string.IsNullOrWhiteSpace(i.Category) ? Loc.T("未分类", "Uncategorized") : i.Category.Trim())
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

        var category = string.IsNullOrWhiteSpace(item.Category) ? Loc.T("未分类", "Uncategorized") : item.Category.Trim();
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

    private void UpdateCountText()
    {
        CountText = Loc.F("共 {0} 项 · 待办 {1} · 今天 {2}", "{0} items · {1} open · {2} today", _totalCount, _pendingCount, _todayCount)
            + (_capped ? Loc.F("（仅显示前 {0} 条）", " (showing the first {0})", RowLimit) : string.Empty);
    }

    private void UpdateNextReminder(List<ScheduleItem> items, DateTime now)
    {
        var next = ReminderPlanner.NextUpcoming(items, now);
        if (next == null)
        {
            NextReminderText = Loc.T("下一次提醒：无", "Next reminder: none");
            return;
        }

        NextReminderText = Loc.T("下一次提醒：", "Next reminder: ") + next.FireAt.ToString("MM-dd HH:mm") + " " + next.Plan.OffsetText
            + Loc.F(" 「{0}」（{1}开始）", " “{0}” ({1})", next.Item.Title, VoiceTemplate.DescribeCountdown(next.Occurrence.Start, now));
    }

    public static string BuildDayLabel(DateTime start, DateTime now)
    {
        var date = start.Date;
        var diff = (date - now.Date).Days;
        var prefix = diff switch
        {
            0 => Loc.T("今天 · ", "Today · "),
            1 => Loc.T("明天 · ", "Tomorrow · "),
            2 => Loc.T("后天 · ", "In 2 days · "),
            -1 => Loc.T("昨天 · ", "Yesterday · "),
            _ => string.Empty,
        };

        return prefix + FormatMonthDay(date) + " " + VoiceTemplate.WeekdayText(date.DayOfWeek);
    }

    private static string FormatMonthDay(DateTime date)
        => Loc.IsEnglish ? date.ToString("MMM d", Loc.Culture) : date.Month + "月" + date.Day + "日";

    public static string BuildTimeText(ScheduleItem item, DateTime start, DateTime end)
    {
        if (item.AllDay) return Loc.T("全天", "All day");

        var text = start.ToString("HH:mm");
        if (end <= start) return text;
        if (end.Date > start.Date) return text + Loc.T(" - 次日 ", " - next day ") + end.ToString("HH:mm");

        return text + " - " + end.ToString("HH:mm");
    }

    /// <summary>多档提醒的可读描述，例如「提前 30分钟 / 15分钟 / 准点」。</summary>
    public static string BuildReminderText(ScheduleItem item, DateTime start)
    {
        if (!item.ReminderEnabled) return Loc.T("不提醒", "No reminder");

        var parts = new List<string>();

        if (item.RemindAtOverride.HasValue && start == item.Start)
        {
            parts.Add(Loc.F("定时 {0}", "At {0}", item.RemindAtOverride.Value.ToString("MM-dd HH:mm")));
        }

        var offsets = item.NormalizedOffsets();
        if (offsets.Count > 0)
        {
            parts.Add(Loc.F("提前 {0}", "{0} before", string.Join(" / ", offsets.Select(ShortOffset))));
        }

        return parts.Count == 0 ? Loc.T("不提醒", "No reminder") : string.Join(" + ", parts);
    }

    private static string ShortOffset(int minutes)
    {
        if (minutes <= 0) return Loc.T("准点", "On time");
        return HumanizeMinutes(minutes).Replace(" ", string.Empty);
    }

    /// <summary>周期事件的可读描述，例如「每周（周一/三/五）· 共 8 次」。</summary>
    public static string BuildRepeatText(ScheduleItem item)
    {
        if (item.Repeat == RepeatKind.None) return Loc.T("无", "None");

        var text = item.Repeat switch
        {
            RepeatKind.Daily => item.RepeatInterval > 1 ? Loc.F("每 {0} 天", "Every {0} days", item.RepeatInterval) : Loc.T("每天", "Daily"),
            RepeatKind.Weekdays => Loc.T("工作日", "Weekdays"),
            RepeatKind.Weekly => item.RepeatInterval > 1 ? Loc.F("每 {0} 周", "Every {0} weeks", item.RepeatInterval) : Loc.T("每周", "Weekly"),
            RepeatKind.Monthly => item.RepeatInterval > 1 ? Loc.F("每 {0} 月", "Every {0} months", item.RepeatInterval) : Loc.T("每月", "Monthly"),
            RepeatKind.Yearly => item.RepeatInterval > 1 ? Loc.F("每 {0} 年", "Every {0} years", item.RepeatInterval) : Loc.T("每年", "Yearly"),
            _ => Loc.T("无", "None"),
        };

        if (item.Repeat == RepeatKind.Weekly && item.RepeatWeekdays.Count > 0)
        {
            var weekdays = item.RepeatWeekdays.Distinct().OrderBy(d => d).Select(VoiceTemplate.ShortWeekdayText);
            text += Loc.F("（{0}）", " ({0})", string.Join(Loc.T("、", ", "), weekdays));
        }

        if (item.RepeatCount.HasValue) text += Loc.F(" · 共 {0} 次", " · {0} times", item.RepeatCount.Value);
        else if (item.RepeatUntil.HasValue) text += Loc.F(" · 至 {0}", " · until {0}", item.RepeatUntil.Value.ToString("yyyy-MM-dd"));

        return text;
    }

    public static string HumanizeMinutes(int minutes)
    {
        if (minutes <= 0) return Loc.T("0 分钟", "0 min");
        if (minutes % (60 * 24) == 0) return Loc.F("{0} 天", "{0} d", minutes / (60 * 24));
        if (minutes % 60 == 0) return Loc.F("{0} 小时", "{0} h", minutes / 60);
        if (minutes > 60) return Loc.F("{0} 小时 {1} 分钟", "{0} h {1} min", minutes / 60, minutes % 60);
        return Loc.F("{0} 分钟", "{0} min", minutes);
    }

    private static string BuildNotesPreview(string? notes)
    {
        var text = VoiceTemplate.SingleLine(notes);
        if (text.Length == 0) return string.Empty;
        return text.Length <= 60 ? text : text.Substring(0, 57) + "...";
    }
}

/// <summary>供静态方法复用的小工具（避免实例/静态互相调用限制）。</summary>
internal static class MainViewModelStatic
{
    public static string TimeRange(ScheduleItem item, DateTime start, DateTime end)
    {
        if (item.AllDay) return start.ToString("yyyy-MM-dd") + Loc.T(" 全天", " All day");
        var text = start.ToString("yyyy-MM-dd HH:mm");
        if (end > start) text += " - " + end.ToString("HH:mm");
        return text;
    }
}
