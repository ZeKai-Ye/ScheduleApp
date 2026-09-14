using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Schedule.App.ViewModels;
using Schedule.Core.Csv;
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
            _host.Tray?.ShowBalloon("日程助手仍在运行", "程序已最小化到托盘，右键托盘图标可退出。");
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
                        "只删除 " + start.ToString("M月d日 HH:mm") + " 这一次？\n其它发生不受影响，之后仍可撤销。",
                        "删除本次", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
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
                    MessageBox.Show(this, "这一次没有单独的修改，无需恢复。", "恢复本次默认", MessageBoxButton.OK, MessageBoxImage.Information);
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
            ? "确定删除「" + item.Title + "」吗？"
            : "「" + item.Title + "」是重复日程，将删除整个系列。确定继续吗？";

        if (MessageBox.Show(this, message, "删除日程", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

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
                MessageBox.Show(this, "这一次没有单独的修改，无需恢复。", "恢复本次默认", MessageBoxButton.OK, MessageBoxImage.Information);
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
                         "确定删除「" + current.Title + "」" + (current.Repeat == RepeatKind.None ? "吗？" : " 整个系列吗？"),
                         "删除日程", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
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
            ? "确定删除「" + row.Item.Title + "」吗？"
            : "「" + row.Item.Title + "」是重复日程，将删除整个系列。确定继续吗？";

        var result = MessageBox.Show(this, message, "删除日程", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        ViewModel.Delete(row);
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "确定删除所有已完成 / 已取消的日程吗？", "清理", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        var removed = _host.Repository.RemoveCompleted();
        _host.Repository.Save();
        ViewModel.Refresh();

        if (removed == 0)
        {
            MessageBox.Show(this, "没有需要清理的日程。", "清理", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    // ================= 导入导出 / 设置 =================

    private void ImportCsv_Click(object sender, RoutedEventArgs e) => ImportCsv();

    private void ImportCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要导入的 CSV 文件",
            Filter = "CSV 文件 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*",
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
            MessageBox.Show(this, "还没有日程可以导出。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出日程为 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = "日程导出-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv",
        };

        if (Directory.Exists(AppPaths.ExportDirectory))
        {
            dialog.InitialDirectory = AppPaths.ExportDirectory;
        }

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            CsvExporter.WriteFile(dialog.FileName, items);
            MessageBox.Show(this, "已导出 " + items.Count + " 条日程到：\n" + dialog.FileName, "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("导出失败", ex);
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Error);
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
