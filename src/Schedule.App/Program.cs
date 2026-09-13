using System.Threading;
using System.Windows;
using Schedule.Core.Logging;
using Schedule.Core.Storage;

namespace Schedule.App;

/// <summary>
/// 应用程序入口。负责：命令行模式分流 → 单实例检查 → 创建 WPF 应用与组合根。
/// 之所以不用 App.xaml 的 StartupUri，是因为需要在启动前解析命令行并保证单实例。
/// </summary>
internal static class Program
{
    private const string ActivateEventName = @"Local\ScheduleApp.Activate.v1";

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
            return 1;
        }
    }

    /// <summary>把无法恢复的启动异常写到临时目录并弹窗提示，避免「闪退且无信息」。</summary>
    private static void ReportFatal(Exception ex)
    {
        var text = "日程助手启动失败" + Environment.NewLine
            + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + Environment.NewLine
            + ex;

        try
        {
            var path = Path.Combine(Path.GetTempPath(), "ScheduleApp-crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            File.WriteAllText(path, text, System.Text.Encoding.UTF8);
        }
        catch (Exception)
        {
            // 忽略
        }

        try
        {
            Console.Error.WriteLine(text);
        }
        catch (Exception)
        {
            // 忽略
        }

        try
        {
            MessageBox.Show(text, "日程助手 - 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private static int Run(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        AppPaths.Initialize(options.DataDirectory);
        AppLog.Initialize(AppPaths.LogDirectory);
        AppLog.Info("启动：" + string.Join(" ", args));

        if (!string.IsNullOrEmpty(AppPaths.LastFallbackReason))
        {
            AppLog.Warn(AppPaths.LastFallbackReason!);
        }

        // 1) 命令行模式（导入 / 导出 / 语音 / 信息），不启动界面
        if (options.HasCliAction)
        {
            return CliRunner.Run(options);
        }

        // 2) 单实例
        using var mutex = new Mutex(initiallyOwned: true, AppPaths.SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            TryActivateRunningInstance();
            return 0;
        }

        var app = new App();
        app.InitializeComponent();

        // 全局异常兜底：界面异常不应直接杀死进程
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("未处理异常", e.ExceptionObject as Exception);

        app.DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Error("界面线程异常", e.Exception);
            MessageBox.Show("发生了一个错误，程序已记录到日志：" + Environment.NewLine + e.Exception.Message,
                "日程助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };

        var host = new AppHost();
        App.Host = host;
        host.Start(options);

        StartActivationListener(host);

        var exitCode = app.Run();
        host.Dispose();
        AppLog.Info("退出，代码 " + exitCode);
        return exitCode;
    }

    private static void TryActivateRunningInstance()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
            handle.Set();
        }
        catch (Exception)
        {
            // 已有实例但无法激活（例如权限不同），静默退出
        }
    }

    private static void StartActivationListener(AppHost host)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                    Application.Current?.Dispatcher.BeginInvoke(new Action(host.ShowMainWindow));
                }
                catch (Exception)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "ScheduleApp.ActivationListener",
        };

        thread.Start();
    }
}
