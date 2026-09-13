using System.Windows;
using System.Windows.Threading;
using Schedule.Core.Csv;
using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;
using Schedule.Core.Storage;
using Schedule.App.Services;
using Schedule.App.Views;

namespace Schedule.App;

/// <summary>
/// 应用组合根：装配仓储、设置、语音、提示音、提醒调度、托盘与主窗口。
/// 所有单例式服务都在这里创建与释放，界面只通过它访问。
/// </summary>
public sealed class AppHost : IDisposable
{
    private DispatcherTimer? _autoSaveTimer;
    private MainWindow? _mainWindow;
    private bool _disposed;

    public SettingsService SettingsService { get; private set; } = null!;

    public ScheduleRepository Repository { get; private set; } = null!;

    public ImportHistoryService ImportHistory { get; private set; } = null!;

    public IVoiceService Voice { get; private set; } = null!;

    public AudioPlayerService Audio { get; private set; } = null!;

    public ReminderHost Reminders { get; private set; } = null!;

    public TrayService? Tray { get; private set; }

    public CsvImportService CsvImporter { get; } = new();

    /// <summary>当前设置（只读引用，修改请走 SettingsService.Replace）。</summary>
    public AppSettings Settings => SettingsService.Current;

    public MainWindow? MainWindow => _mainWindow;

    /// <summary>用户主动要求退出（而非关闭窗口到托盘）。</summary>
    public bool IsExiting { get; private set; }

    /// <summary>设置发生变化的通知（界面据此刷新）。</summary>
    public event EventHandler? SettingsChanged;

    public void Start(CommandLineOptions options)
    {
        SettingsService = new SettingsService();
        SettingsService.Load();

        Repository = new ScheduleRepository(backupKeepCount: Settings.BackupKeepCount);
        Repository.Load();

        ImportHistory = new ImportHistoryService();
        ImportHistory.Load();

        Voice = new SapiVoiceService();
        Audio = new AudioPlayerService();

        Reminders = new ReminderHost(this);
        Reminders.ReminderDue += OnReminderDue;
        Reminders.Updated += OnRemindersUpdated;

        _mainWindow = new MainWindow(this);
        _mainWindow.Show();
        _mainWindow.Activate();

        SetupTray();
        Reminders.Start();

        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
        _autoSaveTimer.Tick += (_, _) => SaveAll();
        _autoSaveTimer.Start();

        ApplyStartupSetting(Settings.StartWithWindows);

        AppLog.Info("界面已启动，数据目录 " + AppPaths.DataDirectory);
    }

    private void SetupTray()
    {
        try
        {
            Tray = new TrayService(
                onOpen: () => ShowMainWindow(),
                onNew: () =>
                {
                    ShowMainWindow();
                    _mainWindow?.BeginCreateItem();
                },
                onToday: () =>
                {
                    ShowMainWindow();
                    _mainWindow?.ShowToday();
                },
                onExit: () => ExitApp());

            OnRemindersUpdated(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘初始化失败", ex);
        }
    }

    public void ShowMainWindow()
    {
        try
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow(this);
                _mainWindow.Show();
            }

            if (!_mainWindow.IsVisible) _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }
        catch (Exception ex)
        {
            AppLog.Error("显示主窗口失败", ex);
        }
    }

    private void OnReminderDue(object? sender, ScheduleOccurrence occurrence)
    {
        try
        {
            var window = new ReminderWindow(this, occurrence);
            window.Show();
            Tray?.ShowBalloon("日程提醒", occurrence.Item.Title);
        }
        catch (Exception ex)
        {
            AppLog.Error("显示提醒窗口失败", ex);
        }
    }

    private void OnRemindersUpdated(object? sender, EventArgs e)
    {
        var next = ReminderPlanner.NextUpcoming(Repository.Snapshot(), DateTime.Now);
        Tray?.UpdateNextReminder(next);
    }

    /// <summary>保存所有脏数据。</summary>
    public void SaveAll()
    {
        try
        {
            Repository.Save();
            SettingsService.Save();
        }
        catch (Exception ex)
        {
            AppLog.Error("自动保存失败", ex);
        }
    }

    /// <summary>应用设置变更（主题 / 开机自启 / 调度间隔等）。</summary>
    public void NotifySettingsChanged()
    {
        SettingsService.MarkDirty();
        SettingsService.Save();
        ApplyStartupSetting(Settings.StartWithWindows);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyStartupSetting(bool enabled)
    {
        if (StartupService.IsEnabled() == enabled) return;
        if (!StartupService.SetEnabled(enabled))
        {
            AppLog.Warn("开机自启设置未生效（可能被安全软件拦截）");
        }
    }

    /// <summary>退出应用：保存数据 → 释放资源 → 关闭进程。</summary>
    public void ExitApp()
    {
        IsExiting = true;
        SaveAll();

        try
        {
            Reminders?.Dispose();
            Tray?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("退出清理失败：" + ex.Message);
        }

        Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _autoSaveTimer?.Stop();
            Reminders?.Dispose();
            Tray?.Dispose();
            Audio?.Dispose();
            (Voice as IDisposable)?.Dispose();
            SaveAll();
        }
        catch (Exception ex)
        {
            AppLog.Error("释放资源失败", ex);
        }
    }
}
