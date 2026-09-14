using Schedule.Core.Models;
using Schedule.Core.Services;

namespace Schedule.App.Services;

/// <summary>
/// AI 工具执行需要的最小应用上下文。
/// 界面宿主（AppHost）与命令行模式各给一份实现，这样 AI 那套逻辑在两种模式下都能跑、也都能自测。
/// </summary>
public interface IAiAppContext
{
    AppSettings Settings { get; }

    ScheduleRepository Repository { get; }

    SettingsService SettingsService { get; }

    /// <summary>设置改动后的落地动作（界面：刷新主题 / 托盘；命令行：只落盘）。</summary>
    void SaveSettings();

    string DataDirectory { get; }

    string Version { get; }
}

/// <summary>命令行模式用的轻量实现（没有 WPF 宿主）。</summary>
public sealed class SimpleAiContext : IAiAppContext
{
    public SimpleAiContext(SettingsService settings, ScheduleRepository repository)
    {
        SettingsService = settings;
        Repository = repository;
    }

    public AppSettings Settings => SettingsService.Current;

    public ScheduleRepository Repository { get; }

    public SettingsService SettingsService { get; }

    public string DataDirectory => Schedule.Core.Storage.AppPaths.DataDirectory;

    public string Version => typeof(SimpleAiContext).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public void SaveSettings()
    {
        SettingsService.MarkDirty();
        SettingsService.Save();
    }
}
