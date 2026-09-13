using System.Diagnostics;
using Schedule.Core.Tests.Cases;

namespace Schedule.Core.Tests;

/// <summary>极简测试运行器（离线环境无法安装 xunit，自行实现断言与统计）。</summary>
public static class TestRunner
{
    private static readonly List<string> Failures = new();
    private static string _section = "(root)";
    private static int _passed;

    public static int RunAll()
    {
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("=== Schedule.Core 自测 ===");

        Run("日期时间解析", DateTimeParsingTests.Run);
        Run("CSV 解析与导入", CsvTests.Run);
        Run("重复规则", RecurrenceTests.Run);
        Run("周期事件进阶", AdvancedRecurrenceTests.Run);
        Run("提醒调度", ReminderTests.Run);
        Run("日历布局", CalendarLayoutTests.Run);
        Run("事件配色", ColorTests.Run);
        Run("CSV 新特性", CsvFeatureTests.Run);
        Run("档位文案", VoiceStageTests.Run);
        Run("本地接口", ApiTests.Run);
        Run("弹窗定位与系统", PositionAndSystemTests.Run);
        Run("语音模板", TemplateTests.Run);
        Run("存储与持久化", StorageTests.Run);

        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine("---------------------------------------------");
        if (Failures.Count == 0)
        {
            Console.WriteLine("全部通过：" + _passed + " 项断言，用时 " + stopwatch.ElapsedMilliseconds + " ms");
            return 0;
        }

        Console.WriteLine("失败 " + Failures.Count + " 项 / 通过 " + _passed + " 项，用时 " + stopwatch.ElapsedMilliseconds + " ms");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  [FAIL] " + failure);
        }

        return 1;
    }

    public static void Section(string name) => _section = name;

    public static void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
            return;
        }

        Failures.Add(_section + " / " + name);
    }

    public static void Equal<T>(T expected, T actual, string name)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            _passed++;
            return;
        }

        Failures.Add(_section + " / " + name + " -> 期望 [" + expected + "]，实际 [" + actual + "]");
    }

    public static void NotNull(object? value, string name)
    {
        if (value != null)
        {
            _passed++;
            return;
        }

        Failures.Add(_section + " / " + name + " -> 期望非 null");
    }

    public static void NoThrow(Action action, string name)
    {
        try
        {
            action();
            _passed++;
        }
        catch (Exception ex)
        {
            Failures.Add(_section + " / " + name + " -> 抛出异常 " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Run(string section, Action body)
    {
        _section = section;
        Console.WriteLine();
        Console.WriteLine("[" + section + "]");
        try
        {
            body();
            Console.WriteLine("  ok");
        }
        catch (Exception ex)
        {
            Failures.Add(section + " -> 用例执行崩溃 " + ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine("  崩溃: " + ex.Message);
        }
    }
}
