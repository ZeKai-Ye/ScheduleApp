using Schedule.Core.Models;
using Schedule.Core.Services;

namespace Schedule.Core.Tests.Cases;

/// <summary>自我重启的参数规划：非重复日程那类「只在 App 层」的坑之外，这部分是纯逻辑，必须自测。</summary>
public static class RestartPlanTests
{
    public static void Run()
    {
        TestRunner.Section("重启规划");
        BuildArguments();
        SanitizeArguments();
        ReadWaitPid();
        ShouldWait();
        SettingsRoundTrip();
    }

    private static void BuildArguments()
    {
        var args = RestartPlan.BuildArguments(@"E:\我的 数据\日程", 4321);

        TestRunner.Equal(4, args.Count, "重启参数个数");
        TestRunner.Equal("--data", args[0], "重启参数：先给数据目录开关");
        TestRunner.Equal(@"E:\我的 数据\日程", args[1], "数据目录含空格时必须是**一个**参数，不能被拆开");
        TestRunner.Equal("--restart-of", args[2], "重启参数：等待开关");
        TestRunner.Equal("4321", args[3], "重启参数：等待旧进程 PID");

        var noData = RestartPlan.BuildArguments(null, 9);
        TestRunner.Equal(2, noData.Count, "没有数据目录时不加 --data");
        TestRunner.Equal("--restart-of", noData[0], "没有数据目录时的第一个参数");
        TestRunner.Equal("9", noData[1], "没有数据目录时的 PID");

        var blank = RestartPlan.BuildArguments("   ", 9);
        TestRunner.Equal(2, blank.Count, "空白数据目录同样不加 --data");

        var withExtra = RestartPlan.BuildArguments(@"D:\data", 7, new[] { "--serve" });
        TestRunner.Equal("--serve", withExtra[^1], "额外参数追加在最后");

        // 透传进来的受管参数必须被丢掉，否则会把数据目录 / 主题覆盖回旧值
        var guarded = RestartPlan.BuildArguments(@"D:\data", 7, new[]
        {
            "--data", @"E:\old", "--restart-of", "1234", "--theme", "light", "--serve",
        });
        TestRunner.Equal(5, guarded.Count, "受管参数被过滤后只剩 --data / 目录 / --restart-of / PID / --serve");
        TestRunner.Equal(@"D:\data", guarded[1], "数据目录以本次为准，不被透传覆盖");
        TestRunner.Equal("--restart-of", guarded[2], "等待开关仍在");
        TestRunner.Equal("7", guarded[3], "等待的是本次 PID，而不是被透传进来的旧 PID");
        TestRunner.Equal("--serve", guarded[^1], "启动模式参数保留");
        TestRunner.Check(!guarded.Contains("light"), "临时主题覆盖不被继承（否则刚改的主题会被顶掉）");
    }

    private static void SanitizeArguments()
    {
        var kept = RestartPlan.Sanitize(new[] { "--minimized", "--no-api", "--port", "43199" });
        TestRunner.Equal(4, kept.Count, "普通参数原样保留");
        TestRunner.Equal("--port", kept[2], "端口开关保留");
        TestRunner.Equal("43199", kept[3], "端口取值保留");

        var inline = RestartPlan.Sanitize(new[] { @"--data=E:\x", "--theme=dark", "--serve" });
        TestRunner.Equal(1, inline.Count, "内联形式的受管参数整体丢弃");
        TestRunner.Equal("--serve", inline[0], "只留下普通参数");

        // --data 后面没有取值（紧跟另一个开关）时，不能把后面的开关也吃掉
        var dangling = RestartPlan.Sanitize(new[] { "--data", "--serve" });
        TestRunner.Equal(1, dangling.Count, "悬空的 --data 不会吃掉后面的开关");
        TestRunner.Equal("--serve", dangling[0], "后面的开关仍在");

        TestRunner.Equal(0, RestartPlan.Sanitize(null).Count, "空参数列表安全");
        TestRunner.Equal(0, RestartPlan.Sanitize(new[] { "  ", "" }).Count, "空白参数被忽略");
    }

    private static void ReadWaitPid()
    {
        TestRunner.Check(RestartPlan.TryReadWaitPid(new[] { "--restart-of", "1234" }, out var separated), "识别分开写法");
        TestRunner.Equal(1234, separated, "分开写法的 PID");

        TestRunner.Check(RestartPlan.TryReadWaitPid(new[] { "--restart-of=88" }, out var inline), "识别内联写法");
        TestRunner.Equal(88, inline, "内联写法的 PID");

        TestRunner.Check(RestartPlan.TryReadWaitPid(new[] { "--data", @"D:\x", "--restart-of", "5" }, out var mixed), "夹在其它参数中也能识别");
        TestRunner.Equal(5, mixed, "夹在中间时的 PID");

        TestRunner.Check(!RestartPlan.TryReadWaitPid(new[] { "--serve" }, out _), "没有该参数时返回 false");
        TestRunner.Check(!RestartPlan.TryReadWaitPid(new[] { "--restart-of", "abc" }, out _), "PID 非法时返回 false");
        TestRunner.Check(!RestartPlan.TryReadWaitPid(new[] { "--restart-of", "0" }, out _), "PID 为 0 时返回 false");
        TestRunner.Check(!RestartPlan.TryReadWaitPid(new[] { "--restart-of" }, out _), "缺少取值时返回 false");
        TestRunner.Check(!RestartPlan.TryReadWaitPid(null, out _), "空命令行安全");
    }

    private static void ShouldWait()
    {
        TestRunner.Check(RestartPlan.ShouldWaitFor(1234, 5678), "不同 PID 需要等待");
        TestRunner.Check(!RestartPlan.ShouldWaitFor(1234, 1234), "同一个 PID 不必等（自己）");
        TestRunner.Check(!RestartPlan.ShouldWaitFor(0, 1234), "PID 为 0 不必等");
        TestRunner.Check(!RestartPlan.ShouldWaitFor(-1, 1234), "负数 PID 不必等");
        TestRunner.Check(!RestartPlan.ShouldWaitFor(null, 1234), "没有 PID 不必等");

        TestRunner.Equal("--data \"E:\\a b\"", RestartPlan.ToCommandLine(new[] { "--data", "E:\\a b" }), "命令行日志会给含空格的取值加引号");
        TestRunner.Equal("--serve", RestartPlan.Quote("--serve"), "不含空格的参数不加引号");
        TestRunner.Equal(string.Empty, RestartPlan.Quote(null), "null 参数安全");
    }

    private static void SettingsRoundTrip()
    {
        TestRunner.Check(new AppSettings().AutoRestartOnThemeChange, "默认开启「切换主题后自动重启」");

        var settings = new AppSettings { Theme = AppTheme.Dark, AutoRestartOnThemeChange = false };
        var clone = settings.Clone();

        TestRunner.Check(!clone.AutoRestartOnThemeChange, "Clone 带上该字段");
        TestRunner.Equal(AppTheme.Dark, clone.Theme, "Clone 带上主题");

        var target = new AppSettings();
        target.CopyFrom(settings);
        TestRunner.Check(!target.AutoRestartOnThemeChange, "CopyFrom 带上该字段");
    }
}
