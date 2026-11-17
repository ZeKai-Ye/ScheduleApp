using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Schedule.App.Mvvm;
using Schedule.App.Services;
using Schedule.App.ViewModels;
using Schedule.Core.Ai;
using Schedule.Core.Localization;
using Schedule.Core.Logging;

namespace Schedule.App.Views;

/// <summary>
/// AI 对话窗口：与本地大模型（默认 Ollama + qwen3:8b）聊天，
/// 模型可以调用内置工具直接查看 / 新建 / 修改 / 删除日程。
/// </summary>
public partial class AiChatWindow : Window
{
    private readonly AppHost _host;
    private readonly AiChatService _chat;
    private readonly ObservableCollection<AiChatBubble> _bubbles = new();
    private readonly DispatcherTimer _elapsedTimer;
    private CancellationTokenSource? _cancellation;
    private DateTime _turnStarted;
    private bool _busy;
    private bool _loadingModels;

    public AiChatWindow(AppHost host)
    {
        _host = host;
        _chat = host.Chat ?? throw new InvalidOperationException("AI 对话服务尚未初始化。");

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        MessageList.ItemsSource = _bubbles;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (_, _) => UpdateBusyStatus();

        // 先把当前模型显示出来（模型列表是异步拉的），避免刚打开时下拉框看起来是空的
        ModelBox.Text = host.Settings.AiModel;
        UpdateEndpointText();

        Greet();

        Loaded += async (_, _) =>
        {
            InputBox.Focus();
            await RefreshModelsAsync(silent: true);
        };

        Closed += (_, _) =>
        {
            _cancellation?.Cancel();
            _elapsedTimer.Stop();
        };
    }

    /// <summary>当前建议的输入（自检 / 脚本可用）。</summary>
    public string SuggestedPrompt => "今天有什么日程？";

    /// <summary>对话气泡数量（自检用）。</summary>
    public int BubbleCount => _bubbles.Count;

    private void Greet()
    {
        var settings = _host.Settings;

        _bubbles.Add(AiChatBubble.Assistant(
            Loc.T("你好，我是日程助手内置的 AI。可以直接用日常说话的方式让我干活，例如：\n", "Hi, I am the AI built into this schedule app. Just tell me what you need in everyday words, for example:\n")
            + Loc.T("· 今天有什么日程？\n", "· What is on my schedule today?\n")
            + Loc.T("· 明天下午三点加一个「客户沟通」，提前 30 分钟提醒我\n", "· Add “Client call” tomorrow at 3 pm, and remind me 30 minutes before\n")
            + Loc.T("· 把明天的例会改到 16:00\n", "· Move tomorrow’s meeting to 16:00\n")
            + Loc.T("· 这周的周会都标成已完成\n", "· Mark all of this week’s weekly meetings as done\n")
            + Loc.T("· 换成夜间主题\n\n", "· Switch to the dark theme\n\n")
            + Loc.F("模型跑在本机（当前：{0} @ {1}），改动会立刻反映到主界面。", "The model runs on this machine (current: {0} @ {1}); changes show up in the main window right away.",
                settings.AiModel, AiClientFactory.DescribeProtocol(settings))));
    }

    /// <summary>把界面切到「正在生成」状态。</summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendButton.IsEnabled = !busy;
        InputBox.IsEnabled = !busy;
        ModelBox.IsEnabled = !busy;
        RefreshModelsButton.IsEnabled = !busy;
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            _turnStarted = DateTime.Now;
            _elapsedTimer.Start();
        }
        else
        {
            _elapsedTimer.Stop();
        }
    }

    private void UpdateBusyStatus()
    {
        if (!_busy) return;

        var seconds = (DateTime.Now - _turnStarted).TotalSeconds;
        var stage = _stage;
        StatusText.Text = Loc.F("{0}（已等待 {1} 秒）", "{0} (waiting {1}s)", stage, seconds.ToString("0"));
    }

    private string _stage = string.Empty;

    private void SetStage(string stage)
    {
        _stage = stage;
        StatusText.Text = stage;
    }

    // ================= 发送 =================

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

        e.Handled = true;
        await SendAsync();
    }

    private async Task SendAsync()
    {
        if (_busy) return;

        var text = (InputBox.Text ?? string.Empty).Trim();
        if (text.Length == 0) return;

        InputBox.Clear();
        _bubbles.Add(AiChatBubble.User(text));
        ScrollToEnd();

        SetBusy(true);
        SetStage(Loc.T("正在请求模型…", "Requesting the model…"));

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<AiProgress>(p => SetStage(p.Stage + "…"));

        try
        {
            var result = await _chat.SendAsync(text, progress, _cancellation.Token);

            foreach (var tool in result.Tools)
            {
                if (!_host.Settings.AiShowToolDetails)
                {
                    _bubbles.Add(AiChatBubble.Tool(tool.Name, tool.Summary ?? (tool.IsError ? Loc.T("执行失败", "Failed") : Loc.T("完成", "Done")), string.Empty));
                    continue;
                }

                var details = Loc.T("参数：", "Arguments: ") + tool.Arguments + Loc.T("\n结果：", "\nResult: ") + tool.Result;
                _bubbles.Add(AiChatBubble.Tool(tool.Name, tool.Summary ?? (tool.IsError ? Loc.T("执行失败", "Failed") : Loc.T("完成", "Done")), details));
            }

            var assistant = AiChatBubble.Assistant(string.IsNullOrWhiteSpace(result.Text) ? Loc.T("（模型没有给出文字回复）", "(the model returned no text)") : result.Text);

            if (!string.IsNullOrWhiteSpace(result.Reasoning)) assistant.Reasoning = result.Reasoning;

            var meta = new List<string>
            {
                Loc.F("用时 {0} 秒", "Took {0}s", result.Elapsed.TotalSeconds.ToString("0.0")),
                Loc.F("工具 {0} 次", "{0} tool calls", result.Tools.Count),
            };

            if (result.PromptTokens.HasValue || result.CompletionTokens.HasValue)
            {
                meta.Add("token " + (result.PromptTokens ?? 0) + "+" + (result.CompletionTokens ?? 0));
            }

            if (result.UsedTextFallback) meta.Add(Loc.T("（工具调用来自正文解析）", "(tool calls parsed from the message text)"));

            _bubbles.Add(assistant);
            SetStage(string.Join("　·　", meta));

            AppLog.Info("AI 回复完成：" + meta[0] + "，" + meta[1]);
        }
        catch (OperationCanceledException)
        {
            _bubbles.Add(AiChatBubble.Error(Loc.T("已停止本次生成。", "This reply was stopped.")));
            SetStage(Loc.T("已停止", "Stopped"));
        }
        catch (AiChatException ex)
        {
            _bubbles.Add(AiChatBubble.Error(ex.Message));
            SetStage(Loc.T("请求失败", "Request failed"));
        }
        catch (Exception ex)
        {
            _bubbles.Add(AiChatBubble.Error(Loc.T("出错了：", "Something went wrong: ") + ex.Message));
            SetStage(Loc.T("请求失败", "Request failed"));
            AppLog.Error("AI 对话异常", ex);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
            ScrollToEnd();
            InputBox.Focus();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        SetStage(Loc.T("正在停止…", "Stopping…"));
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _chat.Clear();
        _bubbles.Clear();
        Greet();
        SetStage(Loc.T("对话已清空", "Chat cleared"));
    }

    // ================= 模型 =================

    private async void RefreshModels_Click(object sender, RoutedEventArgs e) => await RefreshModelsAsync(silent: false);

    /// <summary>从模型服务拉取模型列表。</summary>
    private async Task RefreshModelsAsync(bool silent)
    {
        if (_loadingModels) return;

        _loadingModels = true;
        if (!silent) SetStage(Loc.T("正在读取模型列表…", "Loading the model list…"));

        try
        {
            var models = await _chat.ListModelsAsync(CancellationToken.None);

            var current = _host.Settings.AiModel;
            ModelBox.ItemsSource = models;

            var match = models.FirstOrDefault(m => string.Equals(m.Id, current, StringComparison.OrdinalIgnoreCase));
            ModelBox.SelectedItem = match;
            if (match == null) ModelBox.Text = current;

            SetStage(Loc.F("已找到 {0} 个模型：{1}", "Found {0} models: {1}", models.Count, string.Join(Loc.T("、", ", "), models.Take(4).Select(m => m.Id)))
                + (models.Count > 4 ? " …" : string.Empty));
        }
        catch (Exception ex)
        {
            if (!silent) SetStage(Loc.T("读取模型列表失败：", "Failed to load the model list: ") + ex.Message);
        }
        finally
        {
            _loadingModels = false;
        }
    }

    private void Model_Changed(object sender, SelectionChangedEventArgs e)
    {
        var model = (ModelBox.SelectedItem as AiModelInfo)?.Id ?? ModelBox.Text;

        if (string.IsNullOrWhiteSpace(model)) return;
        if (string.Equals(model, _host.Settings.AiModel, StringComparison.Ordinal)) return;

        _host.Settings.AiModel = model.Trim();
        _host.SettingsService.MarkDirty();
        _host.SettingsService.Save();

        UpdateEndpointText();
        SetStage(Loc.F("已切换到模型 {0}", "Switched to model {0}", model.Trim()));
        AppLog.Info("AI 模型已切换为 " + model.Trim());
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_host, initialTab: SettingsWindow.AiTabIndex) { Owner = this };
        window.ShowDialog();

        UpdateEndpointText();
        SetStage(Loc.T("设置已更新：", "Settings updated: ") + AiClientFactory.Describe(_host.Settings));
    }

    /// <summary>顶部一行只显示「地址 · 模型」与本地/云端徽标，完整信息放 ToolTip（免得挤掉右边按钮）。</summary>
    private void UpdateEndpointText()
    {
        var settings = _host.Settings;
        var cloud = _chat.IsCloud;

        EndpointText.Text = OpenAiCompatibleClient.Normalize(settings.AiBaseUrl) + " · " + settings.AiModel;
        EndpointText.ToolTip = _chat.DescribeTarget()
            + Environment.NewLine
            + (cloud
                ? Loc.T("云端模型：对话内容会发送给该服务商；密钥只存在本机。可在「AI 设置…」里换成本地模型。", "Cloud model: the conversation is sent to the provider; the key stays on this machine. You can switch to a local model in “AI settings…”.")
                : Loc.T("本地模型：数据不出本机，断网也能用。", "Local model: no data leaves this machine, and it works offline."));

        // 本地/云端一眼可见：云端是花钱 + 数据外发，本地是免费 + 不出本机
        CloudBadgeText.Text = cloud ? Loc.T("云端", "Cloud") : Loc.T("本地", "Local");
        CloudBadgeText.Foreground = cloud ? ThemeBrushes.Warning : ThemeBrushes.Success;
        CloudBadge.Background = ThemeBrushes.Hint;
        CloudBadge.BorderBrush = ThemeBrushes.HintBorder;
        CloudBadge.ToolTip = EndpointText.ToolTip;
    }

    private void ScrollToEnd()
    {
        Dispatcher.BeginInvoke(new Action(() => MessageScroll.ScrollToEnd()), DispatcherPriority.Background);
    }
}
