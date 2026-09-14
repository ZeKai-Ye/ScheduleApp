using Schedule.Core.Ai;
using Schedule.Core.Logging;

namespace Schedule.App.Services;

/// <summary>
/// AI 对话宿主：按当前设置创建客户端与会话，并保持会话历史。
/// 设置里的地址 / 模型 / 温度等一改动就自动重建会话（历史保留，只换连接参数）。
/// </summary>
public sealed class AiChatService : IDisposable
{
    private readonly IAiAppContext _host;
    private OpenAiCompatibleClient? _client;
    private AiSession? _session;
    private string _signature = string.Empty;
    private bool _disposed;

    public AiChatService(IAiAppContext host)
    {
        _host = host;
        Actions = new ScheduleAiActions(host);
    }

    /// <summary>工具执行器（同时持有 #1 / #2 编号表）。</summary>
    public ScheduleAiActions Actions { get; }

    public bool IsBusy { get; private set; }

    public string Endpoint => OpenAiCompatibleClient.Normalize(_host.Settings.AiBaseUrl);

    public string Model => _host.Settings.AiModel;

    public int HistoryCount => _session?.History.Count ?? 0;

    /// <summary>当前会话（没有就按设置建一个）。</summary>
    public AiSession Session => EnsureSession();

    /// <summary>发一条消息，跑完「模型 → 工具 → 模型」的循环。</summary>
    public async Task<AiTurnResult> SendAsync(string text, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        var session = EnsureSession();
        IsBusy = true;

        try
        {
            return await session.SendAsync(text, progress, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清空对话（历史与 #1 编号表一起清）。</summary>
    public void Clear()
    {
        _session?.Clear();
        Actions.References.Reset();
        AppLog.Info("AI 对话已清空");
    }

    /// <summary>列出模型服务上的模型。</summary>
    public async Task<List<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var client = new OpenAiCompatibleClient(_host.Settings);
        return await client.ListModelsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>是不是云端服务（决定界面上要不要提示"数据会发出去"）。</summary>
    public bool IsCloud => !AiEndpoint.IsLocal(_host.Settings.AiBaseUrl);

    /// <summary>界面徽标文案：本地 / 云端 + 协议 + 密钥状态。</summary>
    public string DescribeTarget()
    {
        var settings = _host.Settings;

        return (IsCloud ? "云端" : "本地") + " · " + AiClientFactory.DescribeProtocol(settings)
            + " · 密钥：" + AiSecrets.Describe(settings.AiApiKey);
    }

    /// <summary>把「当前设置」与「上次建会话时的设置」比对，不一致就重建客户端与会话。</summary>
    private AiSession EnsureSession()
    {
        var settings = _host.Settings;
        var signature = string.Join("|",
            OpenAiCompatibleClient.Normalize(settings.AiBaseUrl),
            settings.AiApiKey,
            settings.AiAuthMode,
            settings.AiReasoningEffort,
            settings.AiUseSystemProxy,
            settings.AiProxyUrl,
            settings.AiModel,
            settings.AiTemperature.ToString("0.##"),
            settings.AiTimeoutSeconds,
            settings.AiThinking,
            settings.AiMaxHistoryMessages,
            settings.AiSystemPrompt);

        if (_session != null && signature == _signature) return _session;

        var history = _session != null ? new List<ChatMessage>(_session.History) : new List<ChatMessage>();

        _client?.Dispose();
        _client = AiClientFactory.Create(settings);
        _session = new AiSession(_client, Actions, AiClientFactory.CreateOptions(settings));

        // 换连接参数不该丢掉正在聊的内容
        _session.History.AddRange(history);

        _signature = signature;

        return _session;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
    }
}
