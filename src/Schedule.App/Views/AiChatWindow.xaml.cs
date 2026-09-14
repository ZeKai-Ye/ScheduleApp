using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Schedule.App.Services;
using Schedule.App.ViewModels;
using Schedule.Core.Ai;
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
            "你好，我是日程助手内置的 AI。可以直接用日常说话的方式让我干活，例如：\n"
            + "· 今天有什么日程？\n"
            + "· 明天下午三点加一个「客户沟通」，提前 30 分钟提醒我\n"
            + "· 把明天的例会改到 16:00\n"
            + "· 这周的周会都标成已完成\n"
            + "· 换成夜间主题\n\n"
            + "模型跑在本机（当前：" + settings.AiModel + " @ " + AiClientFactory.DescribeProtocol(settings) + "），"
            + "改动会立刻反映到主界面。"));
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
        StatusText.Text = stage + "（已等待 " + seconds.ToString("0") + " 秒）";
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
        SetStage("正在请求模型…");

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<AiProgress>(p => SetStage(p.Stage + "…"));

        try
        {
            var result = await _chat.SendAsync(text, progress, _cancellation.Token);

            foreach (var tool in result.Tools)
            {
                if (!_host.Settings.AiShowToolDetails)
                {
                    _bubbles.Add(AiChatBubble.Tool(tool.Name, tool.Summary ?? (tool.IsError ? "执行失败" : "完成"), string.Empty));
                    continue;
                }

                var details = "参数：" + tool.Arguments + "\n结果：" + tool.Result;
                _bubbles.Add(AiChatBubble.Tool(tool.Name, tool.Summary ?? (tool.IsError ? "执行失败" : "完成"), details));
            }

            var assistant = AiChatBubble.Assistant(string.IsNullOrWhiteSpace(result.Text) ? "（模型没有给出文字回复）" : result.Text);

            if (!string.IsNullOrWhiteSpace(result.Reasoning)) assistant.Reasoning = result.Reasoning;

            var meta = new List<string>
            {
                "用时 " + result.Elapsed.TotalSeconds.ToString("0.0") + " 秒",
                "工具 " + result.Tools.Count + " 次",
            };

            if (result.PromptTokens.HasValue || result.CompletionTokens.HasValue)
            {
                meta.Add("token " + (result.PromptTokens ?? 0) + "+" + (result.CompletionTokens ?? 0));
            }

            if (result.UsedTextFallback) meta.Add("（工具调用来自正文解析）");

            _bubbles.Add(assistant);
            SetStage(string.Join("　·　", meta));

            AppLog.Info("AI 回复完成：" + meta[0] + "，" + meta[1]);
        }
        catch (OperationCanceledException)
        {
            _bubbles.Add(AiChatBubble.Error("已停止本次生成。"));
            SetStage("已停止");
        }
        catch (AiChatException ex)
        {
            _bubbles.Add(AiChatBubble.Error(ex.Message));
            SetStage("请求失败");
        }
        catch (Exception ex)
        {
            _bubbles.Add(AiChatBubble.Error("出错了：" + ex.Message));
            SetStage("请求失败");
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
        SetStage("正在停止…");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _chat.Clear();
        _bubbles.Clear();
        Greet();
        SetStage("对话已清空");
    }

    // ================= 模型 =================

    private async void RefreshModels_Click(object sender, RoutedEventArgs e) => await RefreshModelsAsync(silent: false);

    /// <summary>从模型服务拉取模型列表。</summary>
    private async Task RefreshModelsAsync(bool silent)
    {
        if (_loadingModels) return;

        _loadingModels = true;
        if (!silent) SetStage("正在读取模型列表…");

        try
        {
            var models = await _chat.ListModelsAsync(CancellationToken.None);

            var current = _host.Settings.AiModel;
            ModelBox.ItemsSource = models;

            var match = models.FirstOrDefault(m => string.Equals(m.Id, current, StringComparison.OrdinalIgnoreCase));
            ModelBox.SelectedItem = match;
            if (match == null) ModelBox.Text = current;

            SetStage("已找到 " + models.Count + " 个模型：" + string.Join("、", models.Take(4).Select(m => m.Id))
                + (models.Count > 4 ? " …" : string.Empty));
        }
        catch (Exception ex)
        {
            if (!silent) SetStage("读取模型列表失败：" + ex.Message);
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
        SetStage("已切换到模型 " + model.Trim());
        AppLog.Info("AI 模型已切换为 " + model.Trim());
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_host, initialTab: SettingsWindow.AiTabIndex) { Owner = this };
        window.ShowDialog();

        UpdateEndpointText();
        SetStage("设置已更新：" + AiClientFactory.Describe(_host.Settings));
    }

    /// <summary>顶部一行只显示「地址 · 模型」，完整信息放 ToolTip（免得挤掉右边按钮）。</summary>
    private void UpdateEndpointText()
    {
        var settings = _host.Settings;
        EndpointText.Text = OpenAiCompatibleClient.Normalize(settings.AiBaseUrl) + " · " + settings.AiModel;
        EndpointText.ToolTip = AiClientFactory.Describe(settings)
            + Environment.NewLine + "模型来自本机服务，可在「AI 设置…」里切换或换成其它 OpenAI 兼容服务。";
    }

    private void ScrollToEnd()
    {
        Dispatcher.BeginInvoke(new Action(() => MessageScroll.ScrollToEnd()), DispatcherPriority.Background);
    }
}
