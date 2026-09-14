using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Schedule.App.Views;
using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;
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

        // ---------- 布局自检：默认窗口尺寸下不能被横向裁掉，且要能左右拖动 ----------
        foreach (var problem in LayoutChecks(host, due))
        {
            failures++;
            report.AppendLine("FAIL  布局  " + problem);
            AppLog.Warn("布局自检失败：" + problem);
        }

        // ---------- 行为自检：只在 App 层才有的回归（非重复日程被写上「仅本次」例外） ----------
        foreach (var problem in BehaviorChecks(host))
        {
            failures++;
            report.AppendLine("FAIL  行为  " + problem);
            AppLog.Warn("行为自检失败：" + problem);
        }

        // ---------- 重启自检：主题改完要能一键重启，且新进程必须用同一份数据 ----------
        foreach (var problem in RestartChecks(host, report))
        {
            failures++;
            report.AppendLine("FAIL  重启  " + problem);
            AppLog.Warn("重启自检失败：" + problem);
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
        // 周 / 月视图：走真正的切换路径（点顶部按钮），便于核对日历配色与网格线
        void ShotView(string name, string buttonName)
        {
            var window = new MainWindow(host);

            if (window.FindName(buttonName) is System.Windows.Controls.Primitives.ButtonBase button)
            {
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }

            Shot(name, window);
        }

        // 外观页：新增的「切换主题后自动重启」与「立即重启软件」在这里
        void ShotThemeSettings()
        {
            var window = new SettingsWindow(host);
            if (window.FindName("Tabs") is TabControl tabs) tabs.SelectedIndex = 2;
            Shot("08-settings-theme", window);
        }

        Shot("01-main", new MainWindow(host));
        ShotView("06-week", "WeekModeButton");
        ShotView("07-month", "MonthModeButton");
        Shot("02-settings", new SettingsWindow(host));
        ShotThemeSettings();
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

    /// <summary>
    /// 重启自检：只检查「重启命令怎么拼」，绝不真的启动新进程（那会把自检自己顶掉）。
    /// </summary>
    private static List<string> RestartChecks(AppHost host, StringBuilder report)
    {
        var problems = new List<string>();
        var command = host.BuildRestartCommand();

        if (command == null)
        {
            problems.Add("拿不到重启命令（Environment.ProcessPath 为空？）");
            return problems;
        }

        var (fileName, arguments) = command.Value;
        report.AppendLine("RESTART 命令=" + fileName + " " + RestartPlan.ToCommandLine(arguments));

        if (!File.Exists(fileName)) problems.Add("重启用的可执行文件不存在：" + fileName);

        var dataIndex = arguments.IndexOf("--data");
        if (dataIndex < 0 || dataIndex + 1 >= arguments.Count)
        {
            problems.Add("重启命令没有带上 --data，新进程会另起一套数据");
        }
        else if (!string.Equals(arguments[dataIndex + 1], AppPaths.DataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("重启命令的数据目录与当前不一致：" + arguments[dataIndex + 1]
                + " ≠ " + AppPaths.DataDirectory);
        }

        if (!RestartPlan.TryReadWaitPid(arguments, out var waitPid))
        {
            problems.Add("重启命令没有带上 --restart-of，新进程会和本进程抢单实例互斥体");
        }
        else if (waitPid != Environment.ProcessId)
        {
            problems.Add("重启命令等待的 PID 不是本进程：" + waitPid + " ≠ " + Environment.ProcessId);
        }

        if (arguments.Any(a => a.StartsWith("--ui-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add("重启命令把 --ui-smoke 也透传了（自检会被带进正式实例）");
        }

        return problems;
    }

    /// <summary>
    /// 行为自检：直接调用界面层的操作方法。
    /// 「非重复长日程被写上『仅本次』例外后再也保存不了」这个 bug 只存在于 App 层
    /// （Core 只提供模型与校验），所以必须在这里用真实路径兜住。
    /// 会临时往仓库里加两条自检日程，结束前删除并强制落盘，不留残留。
    /// </summary>
    private static List<string> BehaviorChecks(AppHost host)
    {
        var problems = new List<string>();
        var added = new List<Guid>();

        try
        {
            var viewModel = new MainWindow(host).ViewModel;

            // 1) 非重复的长日程：周视图拖动 / 改期对话框的「仅这一次」都不能写例外
            var longEvent = new ScheduleItem
            {
                Title = "自检：8 小时长日程",
                Start = DateTime.Today.AddHours(9),
                End = DateTime.Today.AddHours(17),
                ReminderOffsets = new List<int> { 30 },
            };

            var longStart = longEvent.Start;
            var longEnd = longEvent.End!.Value;
            host.Repository.Add(longEvent);
            added.Add(longEvent.Id);

            viewModel.Reschedule(longEvent, longStart, longStart.AddMinutes(30), longEnd.AddMinutes(30), wholeSeries: false);

            var stored = host.Repository.Find(longEvent.Id);
            if (stored == null)
            {
                problems.Add("找不到刚加入的自检日程");
            }
            else
            {
                if (stored.HasOccurrenceEdits) problems.Add("非重复日程被写上了「仅本次」例外");
                if (stored.Start != longStart.AddMinutes(30))
                {
                    problems.Add("非重复日程整体改期未生效：" + stored.Start.ToString("MM-dd HH:mm"));
                }

                if (stored.End != longEnd.AddMinutes(30))
                {
                    problems.Add("非重复日程的结束时间没有跟着平移：" + (stored.End?.ToString("MM-dd HH:mm") ?? "(空)"));
                }

                var errors = ScheduleItemValidator.Validate(stored);
                if (errors.Count > 0) problems.Add("改期后校验不通过：" + string.Join("；", errors));
            }

            // 2) 重复日程的「仅这一次」改期仍然要写例外、且不能动整个系列
            var series = new ScheduleItem
            {
                Title = "自检：每周例会",
                Start = DateTime.Today.AddHours(10),
                End = DateTime.Today.AddHours(11),
                Repeat = RepeatKind.Weekly,
                RepeatWeekdays = new List<DayOfWeek> { DateTime.Today.DayOfWeek },
                ReminderOffsets = new List<int> { 10 },
            };

            var seriesStart = series.Start;
            host.Repository.Add(series);
            added.Add(series.Id);

            viewModel.Reschedule(series, seriesStart, seriesStart.AddHours(2), seriesStart.AddHours(3), wholeSeries: false);

            var storedSeries = host.Repository.Find(series.Id);
            if (storedSeries == null)
            {
                problems.Add("找不到刚加入的自检系列");
            }
            else
            {
                if (!storedSeries.HasOccurrenceEdits) problems.Add("重复日程的「仅这一次」改期没有写例外");
                if (storedSeries.Start != seriesStart) problems.Add("重复日程被整体挪走了（应只改这一次）");

                var occurrences = OccurrenceCalculator.ExpandWithOverrides(
                    storedSeries, DateTime.Today, DateTime.Today.AddDays(1));
                var moved = occurrences.Any(o => Math.Abs((o.Start - seriesStart.AddHours(2)).TotalMinutes) < 1);
                if (!moved) problems.Add("「仅这一次」改期后展开结果里找不到新时间");
            }
        }
        catch (Exception ex)
        {
            problems.Add("行为自检异常：" + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            try
            {
                foreach (var id in added) host.Repository.Remove(id);

                // 自检数据不能留在用户的数据目录里
                host.Repository.Save(force: true);
            }
            catch (Exception ex)
            {
                problems.Add("清理自检数据失败：" + ex.Message);
            }
        }

        return problems;
    }

    /// <summary>
    /// 主窗口列表的列宽 / 滚动能力自检。
    /// 用户反馈过「部分窗口横向显示不完全又无法左右拖动」：列宽合计一旦超过可视宽度，
    /// 最右两列（优先级 / 操作）就会被直接裁掉，而横向滚动又是 Disabled。
    /// </summary>
    private static List<string> LayoutChecks(AppHost host, DueReminder due)
    {
        var problems = new List<string>();

        var sample = new ScheduleItem
        {
            Title = "布局自检日程",
            Start = DateTime.Now.AddHours(1),
            End = DateTime.Now.AddHours(2),
            ReminderOffsets = new List<int> { 30 },
        };

        void Probe<T>(Func<T> factory, Action<T> check) where T : Window
        {
            var window = factory();
            try
            {
                window.Show();
                window.UpdateLayout();
                check(window);
            }
            catch (Exception ex)
            {
                problems.Add(window.GetType().Name + " 自检异常：" + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                window.Hide();
            }
        }

        static double RightEdge(Window window, FrameworkElement element)
            => element.TransformToAncestor(window).Transform(new Point(element.ActualWidth, 0)).X;

        Probe(() => new MainWindow(host), window =>
        {
            if (window.FindName("ScheduleList") is not ListView list)
            {
                problems.Add("主窗口找不到 ScheduleList");
                return;
            }

            var total = (list.View as GridView)?.Columns.Sum(c => c.ActualWidth) ?? 0;
            if (total > list.ActualWidth + 1)
            {
                problems.Add("列表列宽合计 " + total.ToString("0") + " 超过可视宽度 "
                    + list.ActualWidth.ToString("0") + "，最右列会被裁掉");
            }

            if (ScrollViewer.GetHorizontalScrollBarVisibility(list) == ScrollBarVisibility.Disabled)
            {
                problems.Add("列表未开启横向滚动：窗口变窄后无法左右拖动");
            }
        });

        Probe(() => new ItemEditorWindow(host, sample, isNew: false), window =>
        {
            if (window.FindName("EditorScroll") is ScrollViewer scroll
                && ScrollViewer.GetHorizontalScrollBarVisibility(scroll) == ScrollBarVisibility.Disabled)
            {
                problems.Add("日程编辑窗口未开启横向滚动");
            }

            if (window.FindName("CustomColorBox") is not TextBox box)
            {
                problems.Add("日程编辑窗口找不到 CustomColorBox");
                return;
            }

            if (box.ActualWidth < 60)
            {
                problems.Add("自定义颜色输入框被压扁（宽 " + box.ActualWidth.ToString("0") + "）");
            }
            else if (RightEdge(window, box) > window.ActualWidth + 1)
            {
                problems.Add("自定义颜色输入框右边缘 " + RightEdge(window, box).ToString("0")
                    + " 超出窗口宽度 " + window.ActualWidth.ToString("0"));
            }
        });

        Probe(() => new SettingsWindow(host), window =>
        {
            if (window.FindName("Tabs") is not TabControl tabs)
            {
                problems.Add("设置窗口找不到 Tabs");
                return;
            }

            tabs.SelectedIndex = 2; // 外观与默认值：最后一列是最容易被裁掉的位置
            window.UpdateLayout();

            if (window.FindName("AutoRestartThemeCheck") is not CheckBox autoRestart)
            {
                problems.Add("设置窗口找不到「切换主题后自动重启」复选框");
            }
            else if (autoRestart.IsChecked != host.Settings.AutoRestartOnThemeChange)
            {
                problems.Add("「切换主题后自动重启」复选框没有反映当前设置");
            }

            if (window.FindName("DefaultOffsetsBox") is not TextBox box)
            {
                problems.Add("设置窗口找不到 DefaultOffsetsBox");
                return;
            }

            if (box.ActualWidth < 60)
            {
                problems.Add("默认提醒档位输入框被压扁（宽 " + box.ActualWidth.ToString("0") + "）");
            }
            else if (RightEdge(window, box) > window.ActualWidth + 1)
            {
                problems.Add("设置窗口最右列超出窗口宽度：" + RightEdge(window, box).ToString("0")
                    + " > " + window.ActualWidth.ToString("0"));
            }
        });

        // 「拖动弹窗记住坐标」端到端：写进设置的自定义坐标必须真的生效
        var saved = (host.Settings.ReminderPosition, host.Settings.ReminderPositionX, host.Settings.ReminderPositionY);

        try
        {
            host.Settings.ReminderPosition = ReminderPositionMode.Custom;
            host.Settings.ReminderPositionX = 240;
            host.Settings.ReminderPositionY = 180;

            Probe(() => new ReminderWindow(host, due), window =>
            {
                if (Math.Abs(window.Left - 240) > 2 || Math.Abs(window.Top - 180) > 2)
                {
                    problems.Add("自定义弹窗位置未生效：期望 (240, 180)，实际 ("
                        + window.Left.ToString("0") + ", " + window.Top.ToString("0") + ")");
                }
            });
        }
        finally
        {
            host.Settings.ReminderPosition = saved.ReminderPosition;
            host.Settings.ReminderPositionX = saved.ReminderPositionX;
            host.Settings.ReminderPositionY = saved.ReminderPositionY;
        }

        return problems;
    }
}
