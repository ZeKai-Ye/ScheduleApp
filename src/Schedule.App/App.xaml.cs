using System.Windows;

namespace Schedule.App;

/// <summary>
/// WPF 应用程序对象。真正的启动流程在 <see cref="Program"/>（支持单实例与命令行模式）。
/// 这里只负责承载全局资源字典与生命周期协调。
/// </summary>
public partial class App : Application
{
    /// <summary>应用组合根，在 <see cref="Program"/> 中创建。</summary>
    public static AppHost Host { get; set; } = null!;
}
