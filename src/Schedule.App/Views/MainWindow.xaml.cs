using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Schedule.App.ViewModels;
using Schedule.Core.Csv;
using Schedule.Core.Ics;
using Schedule.Core.Localization;
using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Storage;

namespace Schedule.App.Views;

/// <summary>主窗口：列表 / 周 / 月三种视图。</summary>
public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private bool _trayHintShown;

    public MainWindow(AppHost host)
    {
        _host = host;

        InitializeComponent();

        ViewModel = new MainViewModel(host);
        DataContext = ViewModel;

        var icon = Services.AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        _host.Repository.Changed += OnRepositoryChanged;
        _host.Reminders.Updated += OnRemindersUpdated;
        _host.SettingsChanged += OnSettingsChanged;

        // 设置页「改选即预览」时调色板已经换了，但视图模型里算好的画刷（日历色块等）还是旧色，
        // 这里重建一次，避免看起来「要重启才生效」。
        _host.ThemeChanged += OnSettingsChanged;

        WeekBody.SizeChanged += (_, e) => ViewModel.UpdateWeekWidth(e.NewSize.Width);

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public MainViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DataDirText.Text = AppPaths.DataDirectory;
        SyncViewButtons();
        ViewModel.Refresh();

        if (ViewModel.IsWeekView && WeekBody.ActualWidth > 0)
        {
            ViewModel.UpdateWeekWidth(WeekBody.ActualWidth);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _host.SaveAll();

        if (_host.IsExiting || !_host.Settings.CloseToTray) return;

        e.Cancel = true;
        Hide();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _host.Tray?.ShowBalloon(Loc.T("日程助手仍在运行", "Schedule Assistant is still running"), Loc.T("程序已最小化到托盘，右键托盘图标可退出。", "The app is minimized to the tray; right-click the tray icon to exit."));
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _host.Settings.MinimizeToTray)
        {
            Hide();
        }
    }

    private void OnRepositoryChanged(object? sender, EventArgs e) => SafeRefresh();

    private void OnRemindersUpdated(object? sender, EventArgs e) => SafeRefreshStatus();

    private void OnSettingsChanged(object? sender, EventArgs e) => SafeRefresh();

    private void SafeRefresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(SafeRefresh));
            return;
        }

        SyncViewButtons();
        ViewModel.Refresh();
    }

    /// <summary>提醒轮询每 15 秒触发一次，只更新状态栏，避免打断用户操作。</summary>
    private void SafeRefreshStatus()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(SafeRefreshStatus));
            return;
        }

        ViewModel.RefreshStatus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            BeginCreateItem();
            e.Handled = true;
        }
        else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ImportCsv();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenAiChat();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            ViewModel.Refresh();
            e.Handled = true;
        }
        else if (e.Key is Key.D1 or Key.D2 or Key.D3 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ViewModel.ViewMode = e.Key == Key.D1 ? ScheduleViewMode.List
                : e.Key == Key.D2 ? ScheduleViewMode.Week
                : ScheduleViewMode.Month;
            SyncViewButtons();
            e.Handled = true;
        }
    }

    // ================= AI 对话 =================

    private void AiChat_Click(object sender, RoutedEventArgs e) => OpenAiChat();

    /// <summary>打开 AI 对话窗口（单例：重复点只是把已有窗口激活）。</summary>
    private void OpenAiChat()
    {
        if (_aiChatWindow is { IsLoaded: true })
        {
            _aiChatWindow.Activate();
            return;
        }

        _aiChatWindow = new AiChatWindow(_host) { Owner = this };
        _aiChatWindow.Closed += (_, _) => _aiChatWindow = null;
        _aiChatWindow.Show();
    }

    // ================= 视图切换 =================

    private void ViewList_Click(object sender, RoutedEventArgs e) => SwitchView(ScheduleViewMode.List);

    private void ViewWeek_Click(object sender, RoutedEventArgs e) => SwitchView(ScheduleViewMode.Week);

    private void ViewMonth_Click(object sender, RoutedEventArgs e) => SwitchView(ScheduleViewMode.Month);

    private void SwitchView(ScheduleViewMode mode)
    {
        ViewModel.ViewMode = mode;
        SyncViewButtons();

        if (mode == ScheduleViewMode.Week && WeekBody.ActualWidth > 0)
        {
            ViewModel.UpdateWeekWidth(WeekBody.ActualWidth);
        }
    }

    private void SyncViewButtons()
    {
        ListModeButton.IsChecked = ViewModel.IsListView;
        WeekModeButton.IsChecked = ViewModel.IsWeekView;
        MonthModeButton.IsChecked = ViewModel.IsMonthView;
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => ViewModel.GoPrevious();

    private void Next_Click(object sender, RoutedEventArgs e) => ViewModel.GoNext();

    private void Today_Click(object sender, RoutedEventArgs e) => ShowToday();

    /// <summary>切换到「今天」（托盘菜单也会调用）。</summary>
    public void ShowToday()
    {
        if (ViewModel.IsListView)
        {
            var today = ViewModel.Filters.FirstOrDefault(f => f.Value == TimeFilter.Today);
            if (today != null) ViewModel.SelectedFilter = today;
            return;
        }

        ViewModel.GoToday();
    }

    // ================= 日历交互 =================

    private void WeekSlot_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not Canvas canvas || canvas.DataContext is not WeekColumnViewModel column) return;

        var hourHeight = Math.Max(1, ViewModel.HourHeight);
        var y = e.GetPosition(canvas).Y;
        var minutes = (int)Math.Round(y / hourHeight * 60 / 15) * 15;
        minutes = Math.Clamp(minutes, 0, (24 * 60) - 15);

        BeginCreateItemAt(column.Date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes));
        e.Handled = true;
    }

    private void WeekBlock_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WeekBlockViewModel block })
        {
            EditItem(block.Item);
            e.Handled = true;
        }
    }

    private void MonthCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MonthCellViewModel cell }) return;

        ViewModel.SelectDay(cell.Date);

        if (e.ClickCount == 2)
        {
            BeginCreateItemAt(cell.Date.ToDateTime(new TimeOnly(9, 0)));
            e.Handled = true;
        }
    }

    private void MonthChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MonthChipViewModel chip })
        {
            EditItem(chip.Item);
            e.Handled = true;
        }
    }

    // ================= 列表交互 =================

    private void ScheduleList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(ScheduleList, (DependencyObject)e.OriginalSource) as ListViewItem;
        if (container != null) container.IsSelected = true;
    }

    private void ScheduleList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(ScheduleList, (DependencyObject)e.OriginalSource) as ListViewItem;
        if (container?.DataContext is ScheduleRow row) EditItem(row.Item);
    }

    private void NewItem_Click(object sender, RoutedEventArgs e) => BeginCreateItem();

    private void EditRow_Click(object sender, RoutedEventArgs e)
        => WithRow(sender, row => EditItem(row.Item, ToOccurrence(row)));

    private void RescheduleRow_Click(object sender, RoutedEventArgs e)
        => WithRow(sender, row => RescheduleItem(row.Item, row.SeriesStart, row.OccurrenceStart, row.OccurrenceEnd));

    private void DeleteOccurrenceRow_Click(object sender, RoutedEventArgs e)
        => WithRow(sender, row => RunScheduleAction("delete-once", row.Item, row.SeriesStart, row.OccurrenceStart, row.OccurrenceEnd));

    private void RestoreOccurrenceRow_Click(object sender, RoutedEventArgs e)
        => WithRow(sender, row => RunScheduleAction("restore", row.Item, row.SeriesStart, row.OccurrenceStart, row.OccurrenceEnd));

    // ================= 系列事件的「仅本次」操作 =================

    /// <summary>把列表行还原成一次发生（用于打开带范围的编辑窗口）。</summary>
    private static ScheduleOccurrence? ToOccurrence(ScheduleRow row)
        => row.IsSeries ? OccurrenceCalculator.Create(row.Item, row.SeriesStart) : null;

    /// <summary>把日历元素（周块 / 月色条 / 列表行）的上下文还原成可操作目标。</summary>
    private static (ScheduleItem Item, DateTime SeriesStart, DateTime Start, DateTime End)? ResolveTarget(object? context) => context switch
    {
        ScheduleRow row => (row.Item, row.SeriesStart, row.OccurrenceStart, row.OccurrenceEnd),
        WeekBlockViewModel block => (block.Item, block.SeriesStart, block.Start, block.End),
        MonthChipViewModel chip => (chip.Item, chip.SeriesStart, chip.Start, chip.Start.AddMinutes(30)),
        _ => null,
    };

    /// <summary>右键菜单统一入口（Tag 决定动作）。</summary>
    private void ScheduleContext_Action(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string action }) return;

        var target = ResolveTarget((sender as FrameworkElement)?.DataContext);
        if (target == null) return;

        RunScheduleAction(action, target.Value.Item, target.Value.SeriesStart, target.Value.Start, target.Value.End);
    }

    private void RunScheduleAction(string action, ScheduleItem item, DateTime seriesStart, DateTime start, DateTime end)
    {
        var isSeries = item.Repeat != RepeatKind.None;

        switch (action)
        {
            case "edit":
                EditItem(item, isSeries ? OccurrenceCalculator.Create(item, seriesStart) : null);
                break;

            case "reschedule":
                RescheduleItem(item, seriesStart, start, end);
                break;

            case "delete-once":
                if (!isSeries)
                {
                    ConfirmDeleteSeries(item);
                    break;
                }

                if (MessageBox.Show(this,
                        Loc.F("只删除 {0} 这一次？\n其它发生不受影响，之后仍可撤销。", "Delete only the {0} occurrence?\nThe other occurrences are unaffected, and you can undo this later.", start.ToString(Loc.IsEnglish ? "MMM d HH:mm" : "M月d日 HH:mm", Loc.Culture)),
                        Loc.T("删除本次", "Delete this occurrence"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    ViewModel.DeleteOccurrence(item, seriesStart);
                }

                break;

            case "delete-series":
                ConfirmDeleteSeries(item);
                break;

            case "restore":
                if (!isSeries || !item.HasOccurrenceEdits)
                {
                    MessageBox.Show(this, Loc.T("这一次没有单独的修改，无需恢复。", "This occurrence has no custom changes, so there is nothing to restore."), Loc.T("恢复本次默认", "Restore this occurrence"), MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }

                ViewModel.RestoreOccurrence(item, seriesStart);
                break;

            case "toggle":
                ViewModel.ToggleDone(item);
                break;
        }
    }

    private void ConfirmDeleteSeries(ScheduleItem item)
    {
        var message = item.Repeat == RepeatKind.None
            ? Loc.F("确定删除「{0}」吗？", "Delete “{0}”?", item.Title)
            : Loc.F("「{0}」是重复日程，将删除整个系列。确定继续吗？", "“{0}” repeats, so the whole series will be deleted. Continue?", item.Title);

        if (MessageBox.Show(this, message, Loc.T("删除日程", "Delete event"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        ViewModel.Delete(item);
    }

    /// <summary>改期对话框（仅这一次 / 整个系列）。</summary>
    private void RescheduleItem(ScheduleItem item, DateTime seriesStart, DateTime start, DateTime end)
    {
        var isSeries = item.Repeat != RepeatKind.None;
        var window = new RescheduleWindow(_host, item, start, end, isSeries) { Owner = this };
        if (window.ShowDialog() != true) return;

        ViewModel.Reschedule(item, seriesStart, window.NewStart, window.NewEnd, window.WholeSeries);
    }

    // ================= 周视图拖动改期 =================

    private Point _dragOrigin;
    private Border? _dragBlock;
    private bool _dragging;
    private AiChatWindow? _aiChatWindow;

    private void WeekBlock_DragStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border block) return;

        _dragBlock = block;
        _dragOrigin = e.GetPosition(this);
        _dragging = false;

        block.CaptureMouse();
        e.Handled = true;
    }

    private void WeekBlock_DragMove(object sender, MouseEventArgs e)
    {
        if (_dragBlock == null || e.LeftButton != MouseButtonState.Pressed) return;

        // 8 像素才认为是拖动：长日程的事件块很高，阈值太小会导致「点一下就被改期」
        var delta = e.GetPosition(this).Y - _dragOrigin.Y;
        if (!_dragging && Math.Abs(delta) < 8) return;

        _dragging = true;

        if (_dragBlock.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            _dragBlock.RenderTransform = transform;
        }

        transform.Y = delta;
        e.Handled = true;
    }

    private void WeekBlock_DragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragBlock == null) return;

        var block = _dragBlock;
        var delta = block.RenderTransform is TranslateTransform transform ? transform.Y : 0;

        _dragBlock = null;
        block.ReleaseMouseCapture();

        if (block.RenderTransform is TranslateTransform reset) reset.Y = 0;

        if (_dragging)
        {
            _dragging = false;
            ApplyDragReschedule(block, delta);
            e.Handled = true;
            return;
        }

        // 没拖动就是普通点击：打开编辑窗口
        if (block.DataContext is WeekBlockViewModel clicked)
        {
            EditItem(clicked.Item, clicked.IsSeries ? OccurrenceCalculator.Create(clicked.Item, clicked.SeriesStart) : null);
        }

        e.Handled = true;
    }

    /// <summary>把拖动距离换算成时间并改期（默认仅这一次；按住 Shift 表示整个系列）。</summary>
    private void ApplyDragReschedule(Border block, double deltaY)
    {
        if (block.DataContext is not WeekBlockViewModel target) return;

        var hourHeight = Math.Max(1, ViewModel.HourHeight);
        var minutes = (int)Math.Round(deltaY / hourHeight * 60 / 15) * 15;
        if (minutes == 0) return;

        var duration = target.End - target.Start;
        var newStart = target.Start.AddMinutes(minutes);

        // 非重复日程本来就只有「整体」一种改法：写成「仅本次」例外会污染数据，
        // 让这条日程之后在编辑窗口里再也保存不了。
        var wholeSeries = !target.IsSeries || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        ViewModel.Reschedule(target.Item, target.SeriesStart, newStart, newStart + duration, wholeSeries);
    }

    // ================= 月视图拖动改期 =================

    private Point _monthDragOrigin;
    private Border? _monthDragChip;
    private MonthCellViewModel? _monthDropCell;
    private bool _monthDragging;

    private void MonthChip_DragStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border chip || chip.DataContext is not MonthChipViewModel) return;

        _monthDragChip = chip;
        _monthDragOrigin = e.GetPosition(this);
        _monthDragging = false;

        chip.CaptureMouse();

        // 不置 Handled：单击仍要走到 MonthChip_Click 去选中当天
    }

    private void MonthChip_DragMove(object sender, MouseEventArgs e)
    {
        if (_monthDragChip == null || e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(this) - _monthDragOrigin;

        // 8 像素阈值：和编辑窗口里的手势保持一致，避免「点一下就被改期」
        if (!_monthDragging && Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;

        _monthDragging = true;

        if (_monthDragChip.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            _monthDragChip.RenderTransform = transform;
        }

        transform.X = delta.X;
        transform.Y = delta.Y;
        _monthDragChip.Opacity = 0.75;

        HighlightMonthDropTarget(FindMonthCell(e.GetPosition(MonthGrid)));
        e.Handled = true;
    }

    private void MonthChip_DragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_monthDragChip == null) return;

        var chip = _monthDragChip;
        var cell = FindMonthCell(e.GetPosition(MonthGrid));
        _monthDragChip = null;

        chip.ReleaseMouseCapture();

        if (chip.RenderTransform is TranslateTransform transform)
        {
            transform.X = 0;
            transform.Y = 0;
        }

        chip.Opacity = 1;
        HighlightMonthDropTarget(null);

        if (!_monthDragging) return; // 没拖动就是普通单击
        _monthDragging = false;

        if (cell != null && chip.DataContext is MonthChipViewModel dragged)
        {
            MoveChipToDate(dragged, cell.Date);
        }

        e.Handled = true;
    }

    /// <summary>拖动过程中高亮鼠标下面的日期格。</summary>
    private void HighlightMonthDropTarget(MonthCellViewModel? cell)
    {
        if (ReferenceEquals(_monthDropCell, cell)) return;

        if (_monthDropCell != null) _monthDropCell.IsDropTarget = false;
        _monthDropCell = cell;
        if (_monthDropCell != null) _monthDropCell.IsDropTarget = true;
    }

    /// <summary>命中测试：鼠标位置落在哪个日期格上。</summary>
    private MonthCellViewModel? FindMonthCell(Point point)
    {
        if (MonthGrid == null) return null;

        var hit = MonthGrid.InputHitTest(point) as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement { DataContext: MonthCellViewModel cell }) return cell;
            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    /// <summary>
    /// 把事件块拖到另一天：保留原来的时刻与时长。
    /// 与周视图一致，默认只改这一次发生；按住 Shift 表示整个系列一起平移。
    /// </summary>
    private void MoveChipToDate(MonthChipViewModel chip, DateOnly targetDate)
    {
        var dayDelta = targetDate.ToDateTime(TimeOnly.MinValue).Date - chip.Start.Date;
        if (dayDelta == TimeSpan.Zero) return;

        var duration = chip.End - chip.Start;
        var newStart = chip.Start + dayDelta;
        var wholeSeries = !chip.IsSeries || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        ViewModel.Reschedule(chip.Item, chip.SeriesStart, newStart, newStart + duration, wholeSeries);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e) => WithRow(sender, DeleteItem);

    private void ToggleDone_Click(object sender, RoutedEventArgs e) => WithRow(sender, ViewModel.ToggleDone);

    private void WithRow(object sender, Action<ScheduleRow> action)
    {
        var row = (sender as FrameworkElement)?.DataContext as ScheduleRow;
        if (row == null) return;
        action(row);
    }

    // ================= 新建 / 编辑 =================

    /// <summary>新建日程（托盘菜单也会调用）。</summary>
    public void BeginCreateItem() => BeginCreateItemAt(NextRoundFiveMinutes(DateTime.Now));

    /// <summary>在指定时间点新建日程（周 / 月视图双击）。</summary>
    public void BeginCreateItemAt(DateTime start)
    {
        var offsets = new List<int>(_host.Settings.DefaultReminderOffsets);
        if (offsets.Count == 0) offsets.Add(_host.Settings.DefaultRemindBeforeMinutes);

        var item = new ScheduleItem
        {
            Start = start,
            ReminderEnabled = _host.Settings.DefaultReminderEnabled,
            ReminderOffsets = offsets,
            RemindBeforeMinutes = offsets.Count > 0 ? offsets.Min() : _host.Settings.DefaultRemindBeforeMinutes,
            Color = Schedule.Core.Services.EventColors.Normalize(_host.Settings.DefaultEventColor),
            Category = ViewModel.SelectedCategory == MainViewModel.AllCategories ? string.Empty : ViewModel.SelectedCategory,
        };

        var editor = new ItemEditorWindow(_host, item, isNew: true) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result == null) return;

        _host.Repository.Add(editor.Result);
        _host.Repository.Save();
        ViewModel.Refresh();
    }

    /// <summary>
    /// 打开编辑窗口。occurrence 不为 null 时表示「从日历上点开的是系列中的某一次」，
    /// 此时窗口内可选择「仅这一次 / 整个系列」。
    /// </summary>
    private void EditItem(ScheduleItem source, ScheduleOccurrence? occurrence = null)
    {
        var current = _host.Repository.Find(source.Id);
        if (current == null) return;

        var editor = new ItemEditorWindow(_host, current, isNew: false, occurrence) { Owner = this };
        if (editor.ShowDialog() != true) return;

        var single = editor.Scope == ItemEditScope.SingleOccurrence && occurrence != null;

        // 恢复本次默认（撤销这一次的例外）
        if (editor.RestoreRequested)
        {
            if (occurrence == null)
            {
                MessageBox.Show(this, Loc.T("这一次没有单独的修改，无需恢复。", "This occurrence has no custom changes, so there is nothing to restore."), Loc.T("恢复本次默认", "Restore this occurrence"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ViewModel.RestoreOccurrence(current, occurrence.SeriesStart);
            return;
        }

        // 删除：按范围只删本次，或删除整个系列
        if (editor.DeleteRequested)
        {
            if (single)
            {
                ViewModel.DeleteOccurrence(current, occurrence!.SeriesStart);
            }
            else if (MessageBox.Show(this,
                         Loc.F("确定删除「{0}」整个系列吗？", "Delete the whole series “{0}”?", current.Title),
                         Loc.T("删除日程", "Delete event"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                _host.Repository.Remove(current.Id);
                _host.Repository.Save();
                ViewModel.Refresh();
            }

            return;
        }

        if (editor.Result == null) return;

        if (single)
        {
            ViewModel.ApplyOccurrenceEdit(current, occurrence!.SeriesStart, editor.Result);
            return;
        }

        _host.Repository.Update(editor.Result);
        _host.Repository.Save();
        ViewModel.Refresh();
    }

    private void DeleteItem(ScheduleRow row)
    {
        var message = row.Item.Repeat == RepeatKind.None
            ? Loc.F("确定删除「{0}」吗？", "Delete “{0}”?", row.Item.Title)
            : Loc.F("「{0}」是重复日程，将删除整个系列。确定继续吗？", "“{0}” repeats, so the whole series will be deleted. Continue?", row.Item.Title);

        var result = MessageBox.Show(this, message, Loc.T("删除日程", "Delete event"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        ViewModel.Delete(row);
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this,
            Loc.T("确定删除所有已完成 / 已取消的日程吗？", "Delete all done / canceled events?"),
            Loc.T("清理", "Clean up"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        var removed = _host.Repository.RemoveCompleted();
        _host.Repository.Save();
        ViewModel.Refresh();

        if (removed == 0)
        {
            MessageBox.Show(this, Loc.T("没有需要清理的日程。", "There is nothing to clean up."), Loc.T("清理", "Clean up"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    // ================= 导入导出 / 设置 =================

    private void ImportCsv_Click(object sender, RoutedEventArgs e) => ImportCsv();

    private void ImportCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("选择要导入的日程文件（CSV / iCalendar）", "Choose a schedule file to import (CSV / iCalendar)"),
            Filter = Loc.T("日程文件 (*.csv;*.ics;*.txt)|*.csv;*.ics;*.txt", "Event files (*.csv;*.ics;*.txt)|*.csv;*.ics;*.txt")
                + "|" + Loc.T("CSV 文件 (*.csv;*.txt)|*.csv;*.txt", "CSV files (*.csv;*.txt)|*.csv;*.txt")
                + "|" + Loc.T("iCalendar (*.ics)|*.ics", "iCalendar (*.ics)|*.ics")
                + "|" + Loc.T("所有文件 (*.*)|*.*", "All files (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(_host.Settings.LastCsvDirectory) && Directory.Exists(_host.Settings.LastCsvDirectory))
        {
            dialog.InitialDirectory = _host.Settings.LastCsvDirectory;
        }

        if (dialog.ShowDialog(this) != true) return;

        _host.Settings.LastCsvDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;

        var window = new CsvImportWindow(_host, dialog.FileName) { Owner = this };
        window.ShowDialog();
        ViewModel.Refresh();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var items = _host.Repository.Snapshot();
        if (items.Count == 0)
        {
            MessageBox.Show(this, Loc.T("还没有日程可以导出。", "There are no events to export."), Loc.T("导出", "Export"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("导出日程（CSV 用本程序的统一格式；.ics 可导入 Google 日历 / Outlook）", "Export events (CSV uses this app’s unified format; .ics works with Google Calendar / Outlook)"),
            Filter = Loc.T("CSV 文件 (*.csv)|*.csv|iCalendar (*.ics)|*.ics", "CSV file (*.csv)|*.csv|iCalendar (*.ics)|*.ics"),
            FilterIndex = string.Equals(_host.Settings.LastExportFormat, "ics", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            FileName = Loc.T("日程导出-", "Schedule export-") + DateTime.Now.ToString("yyyyMMdd-HHmm") + (string.Equals(_host.Settings.LastExportFormat, "ics", StringComparison.OrdinalIgnoreCase) ? ".ics" : ".csv"),
        };

        if (Directory.Exists(AppPaths.ExportDirectory))
        {
            dialog.InitialDirectory = AppPaths.ExportDirectory;
        }

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var asIcs = dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);

            if (asIcs)
            {
                IcsWriter.WriteFile(dialog.FileName, items);
                _host.Settings.LastExportFormat = "ics";
            }
            else
            {
                CsvExporter.WriteFile(dialog.FileName, items);
                _host.Settings.LastExportFormat = "csv";
            }

            _host.SaveAll();

            MessageBox.Show(this,
                Loc.F("已导出 {0} 条日程到：\n{1}", "Exported {0} items to:\n{1}", items.Count, dialog.FileName)
                + (asIcs
                    ? Loc.T("\n\n（iCalendar 格式，可直接导入 Google 日历 / Outlook）", "\n\n(iCalendar format; can be imported into Google Calendar / Outlook)")
                    : Loc.T("\n\n（CSV 为统一格式，可原样再导入本程序）", "\n\n(CSV uses the unified format and can be imported back into this app as-is)")),
                Loc.T("导出完成", "Export finished"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("导出失败", ex);
            MessageBox.Show(this, Loc.T("导出失败：", "Export failed: ") + ex.Message, Loc.T("导出", "Export"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_host) { Owner = this };
        window.ShowDialog();
        SyncViewButtons();
        ViewModel.Refresh();
    }

    /// <summary>把时间对齐到下一个 5 分钟。</summary>
    private static DateTime NextRoundFiveMinutes(DateTime now)
    {
        var add = 5 - (now.Minute % 5);
        if (add == 5) add = 0;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddMinutes(now.Minute + add);
    }
}
