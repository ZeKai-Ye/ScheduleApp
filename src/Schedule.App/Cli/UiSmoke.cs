using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Schedule.App.Views;
using Schedule.Core.Csv;
using Schedule.Core.Ics;
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

        Try(
            "AI 对话窗口",
            () => new AiChatWindow(host),
            window =>
            {
                var chat = (AiChatWindow)window;
                return chat.BubbleCount > 0 ? null : "AI 对话窗口没有开场提示气泡";
            });

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

        // ---------- AI 自检：对话服务的会话复用与重建（不发任何网络请求） ----------
        foreach (var problem in AiServiceChecks(host))
        {
            failures++;
            report.AppendLine("FAIL  AI  " + problem);
            AppLog.Warn("AI 自检失败：" + problem);
        }

        // ---------- 导入自检：CSV 格式不匹配必须明确报错，ICS 能被识别 ----------
        foreach (var problem in ImportChecks(host))
        {
            failures++;
            report.AppendLine("FAIL  导入  " + problem);
            AppLog.Warn("导入自检失败：" + problem);
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

        // 列表选中态：夜间模式下这里曾经变成白底 + 浅字完全读不了，留一张有内容的截图便于复核
        void ShotSelectedList()
        {
            var demo = new ScheduleItem
            {
                Title = "选中态截图（客户沟通）",
                Start = DateTime.Now.AddHours(2),
                End = DateTime.Now.AddHours(3),
                Category = "工作",
                Priority = Priority.High,
                ReminderOffsets = new List<int> { 30, 5 },
                Notes = "用来看选中行的背景与文字对比",
            };

            host.Repository.Add(demo);

            try
            {
                var window = new MainWindow(host);
                window.ViewModel.ViewMode = ScheduleViewMode.List;
                window.Show();
                window.UpdateLayout();

                if (window.FindName("ScheduleList") is ListView list)
                {
                    list.UpdateLayout();

                    var row = FindDescendant<ListViewItem>(list);
                    if (row != null) row.IsSelected = true;

                    window.UpdateLayout();
                }

                Shot("11-list-selected", window);
            }
            finally
            {
                host.Repository.Remove(demo.Id);
                host.Repository.Save(force: true);
            }
        }

        Shot("01-main", new MainWindow(host));
        ShotSelectedList();
        ShotView("06-week", "WeekModeButton");
        ShotView("07-month", "MonthModeButton");
        Shot("02-settings", new SettingsWindow(host));
        ShotThemeSettings();

        // AI 助手设置页
        void ShotAiSettings()
        {
            var window = new SettingsWindow(host, SettingsWindow.AiTabIndex);
            Shot("10-settings-ai", window);
        }

        ShotAiSettings();
        Shot("03-editor", new ItemEditorWindow(host, sample, isNew: false));
        Shot("04-reminder", new ReminderWindow(host, due));
        Shot("09-ai-chat", new AiChatWindow(host));
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
    /// 导入自检：CSV 格式不匹配时必须给出原因并禁用导入（用户报过"静默报错"），
    /// 导入完成后报告不能被刷新清空，.ics 要被识别成 iCalendar。
    /// 全程用临时文件，结束后把导入进去的日程删掉。
    /// </summary>
    private static List<string> ImportChecks(AppHost host)
    {
        var problems = new List<string>();
        var dir = Path.Combine(AppPaths.DataDirectory, "import-selftest");
        var added = new List<Guid>();

        try
        {
            Directory.CreateDirectory(dir);

            // 1) 完全不相干的 CSV：必须明确报错 + 禁用「开始导入」
            var wrong = Path.Combine(dir, "wrong.csv");
            File.WriteAllText(wrong, "姓名,电话,地址\r\n张三,13800000000,北京\r\n", new UTF8Encoding(false));

            var wrongWindow = new CsvImportWindow(host, wrong);
            try
            {
                wrongWindow.Show();
                wrongWindow.UpdateLayout();

                if (string.IsNullOrWhiteSpace(wrongWindow.BlockReason))
                {
                    problems.Add("表头完全不匹配的 CSV 没有给出原因（这正是「静默报错」的场景）");
                }

                if (wrongWindow.FindName("ImportButton") is Button button && button.IsEnabled)
                {
                    problems.Add("格式不匹配时「开始导入」仍然可点");
                }

                if (wrongWindow.FindName("BlockPanel") is FrameworkElement panel && panel.Visibility != Visibility.Visible)
                {
                    problems.Add("格式不匹配时没有显示红色提示面板");
                }
            }
            finally
            {
                wrongWindow.Hide();
            }

            // 2) 统一格式模板：能导入，且导入报告不被刷新清掉
            var template = Path.Combine(dir, "template.csv");
            CsvSchema.WriteTemplate(template);

            var before = host.Repository.Snapshot().Select(i => i.Id).ToHashSet();
            var goodWindow = new CsvImportWindow(host, template);
            try
            {
                goodWindow.Show();
                goodWindow.UpdateLayout();

                if (!string.IsNullOrWhiteSpace(goodWindow.BlockReason))
                {
                    problems.Add("统一格式模板竟然被判成格式不匹配：" + goodWindow.BlockReason);
                }

                var report = goodWindow.RunImport();

                if (!report.Contains("导入完成", StringComparison.Ordinal))
                {
                    problems.Add("导入报告内容异常：" + report.Replace(Environment.NewLine, " / "));
                }

                if (string.IsNullOrWhiteSpace(goodWindow.ReportTextValue))
                {
                    problems.Add("导入完成后报告被刷新清空了（用户看不到任何结果）");
                }

                var imported = host.Repository.Snapshot().Where(i => !before.Contains(i.Id)).ToList();
                if (imported.Count == 0) problems.Add("统一格式模板一条都没导入");
                foreach (var item in imported) added.Add(item.Id);
            }
            finally
            {
                goodWindow.Hide();
            }

            // 3) .ics：要被识别成 iCalendar 并预览出事件
            var ics = Path.Combine(dir, "sample.ics");
            IcsWriter.WriteFile(ics, new[]
            {
                new ScheduleItem
                {
                    Title = "自检：ICS 日程",
                    Start = DateTime.Today.AddDays(1).AddHours(10),
                    End = DateTime.Today.AddDays(1).AddHours(11),
                    ReminderOffsets = new List<int> { 15 },
                },
            });

            var icsWindow = new CsvImportWindow(host, ics);
            try
            {
                icsWindow.Show();
                icsWindow.UpdateLayout();

                if (!icsWindow.IsIcsFile) problems.Add(".ics 文件没有被识别为 iCalendar");
                if (icsWindow.ImportableCount == 0) problems.Add("ICS 预览没有识别出可导入的事件");
                if (!string.IsNullOrWhiteSpace(icsWindow.BlockReason)) problems.Add("合法 ICS 被判成不能导入：" + icsWindow.BlockReason);
            }
            finally
            {
                icsWindow.Hide();
            }
        }
        catch (Exception ex)
        {
            problems.Add("导入自检异常：" + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            try
            {
                foreach (var id in added) host.Repository.Remove(id);
                host.Repository.Save(force: true);
            }
            catch (Exception ex)
            {
                problems.Add("清理导入自检数据失败：" + ex.Message);
            }
        }

        return problems;
    }

    /// <summary>
    /// 列表选中行配色自检：把主窗口渲染成位图，采样选中行内部的一个像素，
    /// 要求它等于调色板里的 Brush.RowSelected。
    /// 系统默认 ListViewItem 模板的选中色是硬编码浅色，夜间模式下会变成"白底 + 浅字"完全读不了。
    /// </summary>
    private static void CheckSelectedRowBackground(AppHost host, List<string> problems)
    {
        var item = new ScheduleItem
        {
            Title = "选中行配色自检",
            Start = DateTime.Now.AddHours(2),
            End = DateTime.Now.AddHours(3),
            Category = "自检",
        };

        host.Repository.Add(item);

        try
        {
            var window = new MainWindow(host);
            window.ViewModel.ViewMode = ScheduleViewMode.List;
            window.Show();
            window.UpdateLayout();

            if (window.FindName("ScheduleList") is not ListView list)
            {
                problems.Add("找不到 ScheduleList，无法校验选中行配色");
                return;
            }

            list.UpdateLayout();

            // 列表按天分组，Items 里是分组对象，因此直接在可视树里找第一行的 ListViewItem
            var row = FindDescendant<ListViewItem>(list);
            if (row == null)
            {
                problems.Add("列表里没有可见的日程行（自检日程应落在默认筛选范围内）");
                return;
            }

            row.IsSelected = true;
            window.UpdateLayout();
            row.UpdateLayout();

            var width = (int)Math.Ceiling(window.ActualWidth);
            var height = (int)Math.Ceiling(window.ActualHeight);
            if (width <= 0 || height <= 0)
            {
                problems.Add("主窗口尺寸异常，无法渲染");
                return;
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            // 采样点：行内「完成」列左侧的空白处，避开文字、色块与复选框
            var point = row.TransformToAncestor(window).Transform(new Point(16, 3));
            var x = (int)Math.Round(point.X);
            var y = (int)Math.Round(point.Y);

            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                problems.Add("列表行的采样点落在窗口外（" + x + "," + y + "）");
                return;
            }

            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);

            var sampled = Color.FromRgb(pixel[2], pixel[1], pixel[0]); // Pbgra32：B G R A

            if (Application.Current?.TryFindResource("Brush.RowSelected") is not SolidColorBrush expected)
            {
                problems.Add("拿不到 Brush.RowSelected");
                return;
            }

            var diff = Math.Abs(sampled.R - expected.Color.R)
                + Math.Abs(sampled.G - expected.Color.G)
                + Math.Abs(sampled.B - expected.Color.B);

            if (diff > 12)
            {
                problems.Add("选中行背景不是主题色：实测 #" + sampled.R.ToString("X2") + sampled.G.ToString("X2")
                    + sampled.B.ToString("X2") + "，期望 #" + expected.Color.R.ToString("X2")
                    + expected.Color.G.ToString("X2") + expected.Color.B.ToString("X2")
                    + "（系统默认模板会给出浅色高亮，夜间模式读不了）");
            }

            window.Hide();
        }
        catch (Exception ex)
        {
            problems.Add("选中行配色自检异常：" + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            host.Repository.Remove(item.Id);
            host.Repository.Save(force: true);
        }
    }

    /// <summary>在可视树里找第一个指定类型的后代。</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;

            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }

        return null;
    }

    /// <summary>
    /// AI 自检：只验证「会话按设置复用 / 重建」这条窗口用到的链路，不连接模型服务。
    /// </summary>
    private static List<string> AiServiceChecks(AppHost host)
    {
        var problems = new List<string>();
        var chat = host.Chat;

        if (chat == null)
        {
            problems.Add("AI 对话服务没有初始化");
            return problems;
        }

        if (!string.Equals(chat.Endpoint, Schedule.Core.Ai.OpenAiCompatibleClient.Normalize(host.Settings.AiBaseUrl), StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("AI 服务地址与设置不一致：" + chat.Endpoint);
        }

        var session = chat.Session;
        if (!ReferenceEquals(session, chat.Session)) problems.Add("相同设置下应复用同一个会话（否则历史会丢）");

        // 联网（云端）支持：预设能填进设置，且界面会判定为"云端"
        var savedSettings = new List<string>
        {
            host.Settings.AiProvider, host.Settings.AiBaseUrl, host.Settings.AiModel,
            host.Settings.AiAuthMode.ToString(), host.Settings.AiThinking.ToString(),
        };

        try
        {
            if (Schedule.Core.Ai.AiProviderPresets.All.Count < 8) problems.Add("模型服务预设太少");

            var cloudPreset = Schedule.Core.Ai.AiProviderPresets.All.First(p => !p.IsLocal && p.BaseUrl.StartsWith("https://", StringComparison.Ordinal));
            Schedule.Core.Ai.AiProviderPresets.Apply(cloudPreset, host.Settings);

            if (!chat.IsCloud) problems.Add("套用云端预设后没有判定为云端（" + cloudPreset.Name + "）");
            if (!chat.DescribeTarget().StartsWith("云端", StringComparison.Ordinal)) problems.Add("云端服务的描述应标注「云端」");
            if (!host.Settings.AiBaseUrl.StartsWith("https://", StringComparison.Ordinal)) problems.Add("云端预设地址必须是 https");
        }
        finally
        {
            host.Settings.AiProvider = savedSettings[0];
            host.Settings.AiBaseUrl = savedSettings[1];
            host.Settings.AiModel = savedSettings[2];
            host.Settings.AiAuthMode = Enum.Parse<AiAuthMode>(savedSettings[3]);
            host.Settings.AiThinking = Enum.Parse<AiThinkingLevel>(savedSettings[4]);
        }

        // 鉴权方式下拉必须与设置一一对应（枚举顺序曾经和界面顺序不一致，导致显示成 api-key）
        foreach (var (mode, expected) in new[]
                 {
                     (AiAuthMode.Bearer, "Bearer"),
                     (AiAuthMode.ApiKeyHeader, "api-key"),
                     (AiAuthMode.None, "不带"),
                 })
        {
            var savedAuth = host.Settings.AiAuthMode;
            host.Settings.AiAuthMode = mode;

            try
            {
                var probe = new SettingsWindow(host, SettingsWindow.AiTabIndex);
                var text = (probe.FindName("AiAuthModeBox") as ComboBox)?.SelectedItem is ComboBoxItem item
                    ? item.Content?.ToString() ?? string.Empty
                    : string.Empty;

                if (!text.Contains(expected, StringComparison.Ordinal))
                {
                    problems.Add("鉴权方式下拉没有反映 " + mode + "（显示为「" + text + "」）");
                }

                probe.Hide();
            }
            finally
            {
                host.Settings.AiAuthMode = savedAuth;
            }
        }

        var options = Schedule.Core.Ai.AiClientFactory.CreateOptions(host.Settings);
        if (options.Model != host.Settings.AiModel) problems.Add("会话选项里的模型与设置不一致");
        if (options.MaxHistoryMessages != host.Settings.AiMaxHistoryMessages) problems.Add("会话选项里的上下文条数与设置不一致");
        if (options.Thinking != host.Settings.AiThinking) problems.Add("会话选项里的思考等级与设置不一致");

        var original = host.Settings.AiModel;

        try
        {
            host.Settings.AiModel = original + "-selftest";

            if (ReferenceEquals(session, chat.Session)) problems.Add("改了模型之后应重建会话（否则还在用旧模型）");
            if (chat.Session.History.Count != session.History.Count) problems.Add("重建会话时历史应当保留");
        }
        finally
        {
            host.Settings.AiModel = original;
            chat.Clear();
        }

        return problems;
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
        report.AppendLine("AI 服务=" + Schedule.Core.Ai.AiClientFactory.Describe(host.Settings)
            + "　超时=" + host.Settings.AiTimeoutSeconds + "s"
            + "　工具=" + Schedule.Core.Ai.AiToolCatalog.Definitions.Count + " 个");

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

            // 1.5) 列表选中行的背景必须是主题色（夜间模式下系统默认高亮会让整行变白、文字读不了）
            CheckSelectedRowBackground(host, problems);

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

            if (window.FindName("AiProviderBox") is not ComboBox providerBox
                || providerBox.ItemsSource is not IEnumerable<Schedule.Core.Ai.AiProviderPreset> presets
                || presets.Count() < 8)
            {
                problems.Add("设置窗口的「服务预设」下拉没有装满预设（联网模型靠它一键填好地址与密钥方式）");
            }

            if (window.FindName("AiAuthModeBox") is not ComboBox authBox || authBox.Items.Count != 3)
            {
                problems.Add("设置窗口缺少「鉴权方式」下拉（Bearer / api-key / 不带）");
            }

            if (window.FindName("AiReasoningBox") is not ComboBox reasoningBox || reasoningBox.Items.Count != 3)
            {
                problems.Add("设置窗口缺少「推理强度参数」下拉");
            }

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

            // AI 标签页：内容里有长文本，若被撑到超出可视宽度，右侧按钮会被挤出窗外
            tabs.SelectedIndex = SettingsWindow.AiTabIndex;
            window.UpdateLayout();

            foreach (var name in new[] { "TestAiButton", "FetchAiModelsButton" })
            {
                if (window.FindName(name) is not FrameworkElement element)
                {
                    problems.Add("设置窗口找不到 " + name);
                    continue;
                }

                if (element.ActualWidth < 20)
                {
                    problems.Add(name + " 被压扁（宽 " + element.ActualWidth.ToString("0") + "）");
                }
                else if (RightEdge(window, element) > window.ActualWidth + 1)
                {
                    problems.Add(name + " 被挤出窗口右侧：" + RightEdge(window, element).ToString("0")
                        + " > " + window.ActualWidth.ToString("0"));
                }
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
