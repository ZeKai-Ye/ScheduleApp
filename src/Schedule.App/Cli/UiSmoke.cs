using System.Text;
using System.Windows;
using Schedule.App.Views;
using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Storage;

namespace Schedule.App;

/// <summary>
/// 界面冒烟自检：把每个窗口都构造一遍。
/// XAML 解析错误与代码里的初始化异常只有真正打开窗口时才会暴露，
/// 这个模式让它们在命令行 / CI 里就能被发现（结果写入日志目录 ui-smoke.log）。
/// </summary>
public static class UiSmoke
{
    public static int Run(AppHost host)
    {
        var report = new StringBuilder();
        var failures = 0;

        report.AppendLine("=== 界面冒烟自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

        void Try(string name, Func<object> factory, Func<object, string?>? verify = null)
        {
            try
            {
                var window = factory();
                var problem = verify?.Invoke(window);

                if (problem != null)
                {
                    failures++;
                    report.AppendLine("FAIL  " + name + "  " + problem);
                    AppLog.Warn("界面冒烟断言失败：" + name + " - " + problem);
                    return;
                }

                report.AppendLine("OK    " + name + "  (" + window.GetType().Name + ")");
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine("FAIL  " + name + "  " + ex.GetType().Name + ": " + ex.Message);
                AppLog.Error("界面冒烟失败：" + name, ex);
            }
        }

        var sample = new ScheduleItem
        {
            Title = "冒烟测试日程",
            Notes = "用于构造编辑窗口",
            Category = "测试",
            Start = DateTime.Now.AddHours(1),
            End = DateTime.Now.AddHours(2),
            Color = "#2563EB",
            Repeat = RepeatKind.Weekly,
            RepeatWeekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            RepeatCount = 6,
            ReminderOffsets = new List<int> { 30, 15, 0 },
            ReminderVoiceTexts = new Dictionary<int, string> { [30] = "还有 30 分钟", [0] = "现在开始" },
            VoiceText = "通用文案",
        };

        Try("主窗口", () => new MainWindow(host));
        Try("设置窗口", () => new SettingsWindow(host));
        Try("日程编辑窗口（新建）", () => new ItemEditorWindow(host, sample, isNew: true));
        Try("日程编辑窗口（编辑）", () => new ItemEditorWindow(host, sample, isNew: false));
        Try(
            "日程编辑窗口（档位明细已生成）",
            () => new ItemEditorWindow(host, sample, isNew: false),
            window =>
            {
                var editor = (ItemEditorWindow)window;
                var expected = sample.NormalizedOffsets().Count;
                return editor.OffsetDetailRowCount == expected
                    ? null
                    : "按档位自定义播报的明细行数 " + editor.OffsetDetailRowCount + "，期望 " + expected;
            });

        var sampleOccurrence = OccurrenceCalculator.Create(sample, sample.Start);
        Try("日程编辑窗口（仅本次范围）", () => new ItemEditorWindow(host, sample, isNew: false, sampleOccurrence));
        // 截图：把窗口渲染成 PNG，便于人工核对主题与布局（XAML 错误之外，样式问题也能看得出来）
        void Shot(string name, Window window)
        {
            try
            {
                window.Show();
                window.UpdateLayout();

                var width = (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : window.Width);
                var height = (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : window.Height);
                if (width <= 0 || height <= 0 || width > 4000 || height > 4000) return;

                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

                var file = Path.Combine(AppPaths.LogDirectory, "ui-" + name + ".png");
                using var stream = File.Create(file);
                encoder.Save(stream);

                report.AppendLine("SHOT  " + name + " -> " + file);
                window.Hide();
            }
            catch (Exception ex)
            {
                report.AppendLine("SHOT? " + name + " 截图失败：" + ex.Message);
            }
        }

        var sampleEnd = sample.End ?? sample.Start.AddHours(1);
        Try("改期窗口（系列）", () => new RescheduleWindow(host, sample, sample.Start, sampleEnd, true));
        Try("改期窗口（单次）", () => new RescheduleWindow(host, sample, sample.Start, sampleEnd, false));

        var occurrence = OccurrenceCalculator.Create(sample, sample.Start);
        var due = new DueReminder
        {
            Item = sample,
            Occurrence = occurrence,
            Plan = occurrence.Reminders.FirstOrDefault() ?? new ReminderPlan { OffsetMinutes = 0, FireAt = DateTime.Now },
        };

        Try("提醒弹窗", () => new ReminderWindow(host, due));

        var csv = Path.Combine(AppPaths.DataDirectory, "ui-smoke-sample.csv");
        try
        {
            File.WriteAllText(csv, "标题,开始时间,提前提醒,语音内容\r\n冒烟,2025-06-03 09:00,\"30,5\",\"30:甲|5:乙\"\r\n", new UTF8Encoding(false));
            Try("CSV 导入向导", () => new CsvImportWindow(host, csv));
        }
        catch (Exception ex)
        {
            failures++;
            report.AppendLine("FAIL  准备 CSV 样例  " + ex.Message);
        }

        // 主题自检：把关键画刷的解析结果与实际窗口背景都打出来（截图之外再用数据兜底）
        void DumpTheme(string tag)
        {
            string Color(string key)
                => Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush
                    ? brush.Color.ToString()
                    : "(未解析)";

            report.AppendLine("THEME " + tag
                + "  IsDark=" + Services.ThemeService.IsDark
                + "  Background=" + Color("Brush.Background")
                + "  Card=" + Color("Brush.Card")
                + "  Text=" + Color("Brush.Text")
                + "  Accent=" + Color("Brush.Accent"));
        }

        DumpTheme("应用前");

        var probe = new MainWindow(host);
        probe.Show();
        probe.UpdateLayout();
        report.AppendLine("THEME 窗口实际背景="
            + (probe.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString()
            + "  根面板背景="
            + ((probe.Content as System.Windows.Controls.Panel)?.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString());
        probe.Hide();
        DumpTheme("窗口创建后");

        // 关键窗口截图（主题 / 布局人工核对用）
        Shot("01-main", new MainWindow(host));
        Shot("02-settings", new SettingsWindow(host));
        Shot("03-editor", new ItemEditorWindow(host, sample, isNew: false));
        Shot("04-reminder", new ReminderWindow(host, due));
        Shot("05-reschedule", new RescheduleWindow(host, sample, sample.Start, sampleEnd, true));

        report.AppendLine(failures == 0 ? "结果：全部通过" : "结果：失败 " + failures + " 项");

        var text = report.ToString();

        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            File.WriteAllText(Path.Combine(AppPaths.LogDirectory, "ui-smoke.log"), text, new UTF8Encoding(true));
        }
        catch (Exception)
        {
            // 忽略
        }

        try
        {
            Console.Out.Write(text);
            Console.Out.Flush();
        }
        catch (Exception)
        {
            // 无控制台时忽略
        }

        return failures == 0 ? 0 : 1;
    }
}
