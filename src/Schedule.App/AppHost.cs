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

    /// <summary>本次启动的接口端口（重启时原样带上，避免重启后接口换了端口）。</summary>
    private int? _launchApiPort;

    /// <summary>本次启动是否禁用了接口。</summary>
    private bool _launchNoApi;

    public SettingsService SettingsService { get; private set; } = null!;

    public ScheduleRepository Repository { get; private set; } = null!;

    public ImportHistoryService ImportHistory { get; private set; } = null!;

    public IVoiceService Voice { get; private set; } = null!;

    public AudioPlayerService Audio { get; private set; } = null!;

    public ReminderHost Reminders { get; private set; } = null!;

    public TrayService? Tray { get; private set; }

    /// <summary>本地 JSON 接口（供 AI / 脚本访问）。</summary>
    public ApiHost? Api { get; private set; }

    /// <summary>是否以无界面模式运行（--serve）。</summary>
    public bool IsHeadless { get; private set; }

    /// <summary>启动时是否直接最小化到托盘（--minimized / --serve）。</summary>
    public bool StartMinimized { get; private set; }

    public CsvImportService CsvImporter { get; } = new();

    /// <summary>当前设置（只读引用，修改请走 SettingsService.Replace）。</summary>
    public AppSettings Settings => SettingsService.Current;

    public MainWindow? MainWindow => _mainWindow;

    /// <summary>用户主动要求退出（而非关闭窗口到托盘）。</summary>
    public bool IsExiting { get; private set; }

    /// <summary>设置发生变化的通知（界面据此刷新）。</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// 主题预览发生变化（设置页改选主题、尚未保存时）。
    /// 主题本身走 DynamicResource 会自动更新，但视图模型里已经算好的画刷要重新取色。
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>通知界面「调色板已换，请重建算好的画刷」。</summary>
    public void NotifyThemeChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

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

        // 主题要在创建窗口之前应用
        ThemeService.Apply(Settings.Theme, ThemeService.Parse(options.ThemeOverride));

        Api = new ApiHost(this);
        Api.Start(options.ApiPort, enabled: !options.NoApi);

        IsHeadless = options.Serve;
        StartMinimized = options.Minimized || options.Serve;
        _launchApiPort = options.ApiPort;
        _launchNoApi = options.NoApi;

        if (options.Serve)
        {
            AppLog.Info("以无界面模式启动（--serve）：仅运行本地接口与提醒调度，右键托盘图标可打开界面或退出");
        }
        else if (options.Minimized)
        {
            AppLog.Info("以最小化模式启动（--minimized）：已最小化到托盘，提醒照常触发");
        }
        else
        {
            _mainWindow = new MainWindow(this);
            _mainWindow.Show();
            _mainWindow.Activate();
        }

        SetupTray();
        Reminders.Start();

        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
        _autoSaveTimer.Tick += (_, _) => SaveAll();
        _autoSaveTimer.Start();

        SyncAutostartSetting();

        AppLog.Info((IsHeadless ? "无界面模式已启动，数据目录 " : "界面已启动，数据目录 ") + AppPaths.DataDirectory);
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
                onExit: () => ExitApp(),
                onToggleAutostart: ToggleAutostartFromTray,
                onCreateShortcut: CreateDesktopShortcutFromTray,
                isAutostartEnabled: () => Settings.StartWithWindows);

            OnRemindersUpdated(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘初始化失败", ex);
        }
    }

    private void ToggleAutostartFromTray()
    {
        var enable = !Settings.StartWithWindows;

        if (ApplyAutostart(enable, null, out var mode, out var error))
        {
            Tray?.ShowBalloon("开机启动", enable ? "已启用（" + mode + "）" : "已停用");
        }
        else
        {
            Tray?.ShowBalloon("开机启动设置失败", error ?? "未知原因");
        }
    }

    private void CreateDesktopShortcutFromTray()
    {
        if (SetShortcut("desktop", true, null, out var path, out var error))
        {
            Tray?.ShowBalloon("桌面快捷方式", "已创建：" + System.IO.Path.GetFileName(path ?? string.Empty));
        }
        else
        {
            Tray?.ShowBalloon("创建快捷方式失败", error ?? "未知原因");
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

    private void OnReminderDue(object? sender, DueReminder reminder)
    {
        try
        {
            var window = new ReminderWindow(this, reminder);
            window.Show();
            Tray?.ShowBalloon("日程提醒", reminder.Item.Title + "（" + reminder.Plan.OffsetText + "）");
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
        ThemeService.Apply(Settings.Theme);
        SyncAutostartSetting();
        Tray?.UpdateAutostart(Settings.StartWithWindows);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把「开机启动」设置与系统实际状态对齐（路径或参数变化时重写一次）。</summary>
    private void SyncAutostartSetting()
    {
        try
        {
            var (mode, command) = StartupService.Detect();
            var desired = StartupService.BuildCommand(BuildStartupArguments());

            if (!Settings.StartWithWindows)
            {
                if (mode != AutostartMode.Disabled) StartupService.Apply(false, null, out _, out _);
                return;
            }

            if (mode == AutostartMode.Registry && string.Equals(command, desired, StringComparison.OrdinalIgnoreCase)) return;

            if (StartupService.Apply(true, BuildStartupArguments(), out var applied, out var error))
            {
                AppLog.Info("开机启动已同步为 " + applied + "：" + desired);
            }
            else
            {
                AppLog.Warn("开机启动设置未生效：" + error);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("同步开机启动失败：" + ex.Message);
        }
    }

    /// <summary>按设置拼出开机启动参数。</summary>
    public string BuildStartupArguments()
    {
        var parts = new List<string>();

        if (Settings.StartMinimized) parts.Add("--minimized");
        if (!string.IsNullOrWhiteSpace(Settings.StartupArguments)) parts.Add(Settings.StartupArguments.Trim());

        return string.Join(" ", parts);
    }

    /// <summary>创建 / 删除快捷方式（界面、接口、命令行共用）。</summary>
    public bool SetShortcut(string target, bool create, string? directoryOverride, out string? path, out string? error)
    {
        var ok = create
            ? ShortcutService.TryCreate(target, null, directoryOverride, out path, out error)
            : ShortcutService.TryDelete(target, directoryOverride, out path, out error);

        if (ok) NotifySettingsChanged();

        return ok;
    }

    /// <summary>启用 / 停用开机启动，并同步设置。</summary>
    public bool ApplyAutostart(bool enabled, string? arguments, out string? mode, out string? error)
    {
        var effective = string.IsNullOrWhiteSpace(arguments) ? BuildStartupArguments() : arguments!.Trim();

        var ok = StartupService.Apply(enabled, effective, out mode, out error);

        if (ok)
        {
            Settings.StartWithWindows = enabled;
            Settings.StartMinimized = effective.Contains("--minimized", StringComparison.OrdinalIgnoreCase);
            Settings.StartupArguments = effective
                .Replace("--minimized", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            NotifySettingsChanged();
        }

        return ok;
    }

    /// <summary>记住提醒弹窗的位置（用户拖动后自动调用）。</summary>
    public void RememberReminderPosition(double left, double top)
    {
        Settings.ReminderPosition = ReminderPositionMode.Custom;
        Settings.ReminderPositionX = (int)Math.Round(left);
        Settings.ReminderPositionY = (int)Math.Round(top);

        SettingsService.MarkDirty();
        SettingsService.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>设置弹窗位置（设置页用）。</summary>
    public void SetReminderPosition(ReminderPositionMode mode, int x, int y)
    {
        Settings.ReminderPosition = mode;
        Settings.ReminderPositionX = x;
        Settings.ReminderPositionY = y;

        SettingsService.MarkDirty();
        SettingsService.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 拼出「重启后要执行的命令与参数」（不真的启动进程，自检与日志都用它）。
    /// 返回 null 表示拿不到可执行文件路径。
    /// </summary>
    public (string FileName, List<string> Arguments)? BuildRestartCommand()
    {
        var fileName = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var arguments = new List<string>();

        // 以框架依赖方式运行（dotnet ScheduleApp.dll）时 ProcessPath 是 dotnet.exe，
        // 需要把程序集自身作为第一个参数带上。
        if (string.Equals(Path.GetFileNameWithoutExtension(fileName), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entry)) return null;

            arguments.Add(entry);
        }

        // 本次启动的「模式」参数原样带上，其它（--data / --theme / --restart-of）由重启逻辑决定
        var extra = new List<string>();
        if (IsHeadless) extra.Add("--serve");
        if (_launchNoApi) extra.Add("--no-api");
        if (_launchApiPort.HasValue)
        {
            extra.Add("--port");
            extra.Add(_launchApiPort.Value.ToString());
        }

        arguments.AddRange(RestartPlan.BuildArguments(AppPaths.DataDirectory, Environment.ProcessId, extra));

        return (fileName, arguments);
    }

    /// <summary>
    /// 重启自身：先拉起新进程（它会等本进程退出后再抢单实例互斥体），再正常退出。
    /// 用于「主题切换后需要整个界面重新加载」这类场景。
    /// </summary>
    public bool Restart(out string? error)
    {
        error = null;

        try
        {
            var command = BuildRestartCommand();
            if (command == null)
            {
                error = "无法确定程序路径。";
                return false;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo(command.Value.FileName)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            foreach (var argument in command.Value.Arguments) startInfo.ArgumentList.Add(argument);

            AppLog.Info("准备重启：" + RestartPlan.ToCommandLine(startInfo.ArgumentList));

            // 新进程会用同一份数据：先把内存里的改动落盘，避免重启丢数据
            SaveAll();

            System.Diagnostics.Process.Start(startInfo);
            AppLog.Info("已拉起新实例，本进程即将退出");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("重启失败", ex);
            return false;
        }

        ExitApp();
        return true;
    }

    /// <summary>退出应用：保存数据 → 释放资源 → 关闭进程。</summary>
    public void ExitApp()
    {
        IsExiting = true;
        SaveAll();

        try
        {
            Reminders?.Dispose();
            Api?.Dispose();
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
            Api?.Dispose();
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
