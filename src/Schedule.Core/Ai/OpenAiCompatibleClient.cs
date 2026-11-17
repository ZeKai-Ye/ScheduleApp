using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Schedule.Core.Logging;
using Schedule.Core.Models;

namespace Schedule.Core.Ai;

/// <summary>
/// 对话客户端，支持三类后端：
///
/// 1. **Ollama 原生**（/api/chat + /api/tags）：本地跑，能真正关掉思维链（Qwen3 关掉后从 30 秒降到 4 秒），
///    工具调用返回 message.tool_calls。地址 http://127.0.0.1:11434。
/// 2. **OpenAI 兼容**（/v1/chat/completions + /v1/models）：本地有 LM Studio（:1234/v1）、
///    llama.cpp（:8080/v1）、vLLM；**云端**有 OpenAI / DeepSeek / 通义千问 / 智谱 / Kimi / OpenRouter 等，
///    只要填地址 + 密钥即可。
/// 3. **完整端点地址**（含 /chat/completions 与 ?api-version=…）：Azure OpenAI 或各类中转，
///    原样使用，不再补 /v1。
///
/// 鉴权支持 `Authorization: Bearer`（默认，密钥为空则不带）与 `api-key` 头（Azure）；
/// 密钥还可以写成 `env:OPENAI_API_KEY` 从环境变量读取，不落盘。
/// 全程只用 BCL 的 HttpClient + System.Text.Json，不引入任何 NuGet 依赖。
/// </summary>
public sealed class OpenAiCompatibleClient : IAiChatClient, IAiStreamingChatClient, IDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:11434/v1";

    private static readonly Regex ThinkBlock = new(@"<think(?:ing)?>([\s\S]*?)</think(?:ing)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string? _apiKeyVariable;
    private bool _disposed;

    public OpenAiCompatibleClient(
        string? baseUrl,
        string? apiKey = null,
        int timeoutSeconds = 180,
        AiApiMode mode = AiApiMode.Auto,
        AiAuthMode authMode = AiAuthMode.Bearer,
        AiReasoningEffortMode reasoningEffort = AiReasoningEffortMode.Auto,
        bool useSystemProxy = true,
        string? proxyUrl = null)
    {
        RawBaseUrl = (baseUrl ?? string.Empty).Trim();
        BaseUrl = Normalize(baseUrl);
        OllamaRootUrl = OllamaRoot(baseUrl);
        FullChatEndpoint = AiEndpoint.IsFullChatEndpoint(baseUrl) ? AiEndpoint.WithoutQuery(baseUrl) : null;

        (_apiKey, _apiKeyVariable) = AiSecrets.Resolve(apiKey);
        AuthMode = authMode;
        ReasoningEffortMode = reasoningEffort;
        DefaultTimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600);
        RequestedMode = mode;
        Mode = ResolveMode(mode, BaseUrl);

        _http = new HttpClient(BuildHandler(useSystemProxy, proxyUrl)) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ScheduleApp/" + AppVersion);
    }

    /// <summary>按设置直接建客户端（界面与命令行都用它）。</summary>
    public OpenAiCompatibleClient(AppSettings settings)
        : this(
            settings.AiBaseUrl,
            settings.AiApiKey,
            settings.AiTimeoutSeconds,
            settings.AiApiMode,
            settings.AiAuthMode,
            settings.AiReasoningEffort,
            settings.AiUseSystemProxy,
            settings.AiProxyUrl)
    {
    }

    private static string AppVersion => typeof(OpenAiCompatibleClient).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>配置里原样写着的地址（可能是完整端点）。</summary>
    public string RawBaseUrl { get; }

    /// <summary>完整端点地址（Azure 等）；非 null 时聊天直接打这个地址。</summary>
    public string? FullChatEndpoint { get; }

    public AiAuthMode AuthMode { get; }

    public AiReasoningEffortMode ReasoningEffortMode { get; }

    /// <summary>是不是本地服务（本地不发云端专属参数、数据不出本机）。</summary>
    public bool IsLocal => AiEndpoint.IsLocal(RawBaseUrl);

    /// <summary>当前用的密钥（<c>env:</c> 已解析）；仅用于日志打码，不对外暴露明文。</summary>
    public string ApiKeyPreview => AiSecrets.Describe(_apiKeyVariable != null ? AiSecrets.EnvironmentPrefix + _apiKeyVariable : _apiKey);

    /// <summary>给界面用的一句话：本地 / 云端 + 协议 + 密钥状态。</summary>
    public string DescribeTarget()
        => (IsLocal ? "本地" : "云端") + "　" + (IsOllama ? "Ollama 原生" : FullChatEndpoint != null ? "自定义端点" : "OpenAI 兼容")
            + "　密钥：" + ApiKeyPreview;

    private static HttpClientHandler BuildHandler(bool useSystemProxy, string? proxyUrl)
    {
        var handler = new HttpClientHandler();

        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl.Trim());
            handler.UseProxy = true;
        }
        else
        {
            // 默认就是 true（走系统代理）；显式关掉给"直连"用
            handler.UseProxy = useSystemProxy;
        }

        return handler;
    }

    /// <summary>规范化后的 OpenAI 兼容地址（用于显示）。</summary>
    public string BaseUrl { get; }

    /// <summary>Ollama 原生地址（去掉 /v1）。</summary>
    public string OllamaRootUrl { get; }

    public AiApiMode RequestedMode { get; }

    /// <summary>实际使用的协议。</summary>
    public AiApiMode Mode { get; }

    public bool IsOllama => Mode == AiApiMode.Ollama;

    public int DefaultTimeoutSeconds { get; }

    private string ChatUri => IsOllama
        ? OllamaRootUrl + "/api/chat"
        : FullChatEndpoint ?? BaseUrl + "/chat/completions";

    private string ModelsUri => IsOllama
        ? OllamaRootUrl + "/api/tags"
        : (FullChatEndpoint != null ? AiEndpoint.RootOfFullEndpoint(FullChatEndpoint) : BaseUrl) + "/models";

    /// <summary>看起来是本地 Ollama（默认端口）。</summary>
    public static bool LooksLikeOllama(string? baseUrl)
        => (baseUrl ?? string.Empty).Contains(":11434", StringComparison.Ordinal);

    /// <summary>自动判断协议：Ollama 默认端口走原生接口，其余走 OpenAI 兼容。</summary>
    public static AiApiMode ResolveMode(AiApiMode requested, string normalizedBaseUrl) => requested switch
    {
        AiApiMode.Ollama => AiApiMode.Ollama,
        AiApiMode.OpenAi => AiApiMode.OpenAi,
        _ => LooksLikeOllama(normalizedBaseUrl) ? AiApiMode.Ollama : AiApiMode.OpenAi,
    };

    /// <summary>
    /// 规范化服务地址：允许直接粘贴 `.../v1/chat/completions`，也允许只写主机端口（自动补 /v1）。
    /// </summary>
    public static string Normalize(string? baseUrl)
    {
        var url = (baseUrl ?? string.Empty).Trim();
        if (url.Length == 0) return DefaultBaseUrl;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        url = url.TrimEnd('/');

        // 用户经常直接把文档里的完整路径粘进来，这里把这些尾巴都去掉
        foreach (var suffix in new[] { "/chat/completions", "/models", "/api/chat", "/api/tags", "/api" })
        {
            if (!url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            url = url.Substring(0, url.Length - suffix.Length).TrimEnd('/');
            break;
        }

        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) && !url.Contains("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            url += "/v1";
        }

        return url;
    }

    /// <summary>Ollama 原生根地址：去掉末尾的 /v1（也支持用户直接写 /api）。</summary>
    public static string OllamaRoot(string? baseUrl)
    {
        var url = Normalize(baseUrl).TrimEnd('/');

        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 3);
        if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 4);

        return url.TrimEnd('/');
    }

    // ================= 对话 =================

    /// <summary>
    /// 流式对话：一边收一边把增量交给调用方，结束时给出完整结果（正文 / 工具调用 / 用量）。
    /// OpenAI 兼容走 SSE，Ollama 原生走 NDJSON，两种协议都支持工具调用。
    /// </summary>
    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new AiChatException("还没有选择模型。请在「设置 → AI 助手」里选择或填写模型名。");
        }

        var payload = AiJson.Text(IsOllama
            ? BuildOllamaPayload(request, stream: true)
            : BuildPayload(request, stream: true));

        var timeout = Math.Clamp(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : DefaultTimeoutSeconds, 5, 3600);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(timeout));

        var watch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            using var content = new StringContent(payload, new UTF8Encoding(false), "application/json");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatUri) { Content = content };
            ApplyAuth(httpRequest);

            response = await _http
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiChatException(TimeoutMessage(timeout));
        }
        catch (HttpRequestException ex)
        {
            throw new AiChatException(DescribeNetworkFailure(ex), ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadBodyAsync(response, linked.Token).ConfigureAwait(false);
                throw new AiChatException(DescribeFailure((int)response.StatusCode, body));
            }

            var accumulator = new AiStreamAccumulator();

            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));

            while (true)
            {
                string? line;

                try
                {
                    line = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new AiChatException(TimeoutMessage(timeout));
                }

                if (line == null) break;

                AiStreamChunk? chunk;
                var state = IsOllama
                    ? AiStreamParser.ReadOllamaLine(line, accumulator, out chunk)
                    : AiStreamParser.ReadOpenAiLine(line, accumulator, out chunk);

                if (state == StreamLineResult.Chunk && chunk != null) yield return chunk;
                if (state == StreamLineResult.Ended) break;
            }

            watch.Stop();

            var reply = accumulator.ToReply();
            reply.Elapsed = watch.Elapsed;

            yield return new AiStreamChunk { IsDone = true, Reply = reply };
        }
    }

    /// <summary>超时提示（流式与非流式共用一套说法）。</summary>
    private string TimeoutMessage(int timeout)
        => "等待模型超过 " + timeout + " 秒仍未返回。"
            + (IsLocal
                ? "本地大模型首次加载会比较慢，可在「设置 → AI 助手」里调大超时时间；也可以在对话窗口点「停止」。"
                : "云端服务偶尔会排队或波动，可稍后重试、调大超时时间，或在对话窗口点「停止」。");

    public async Task<AiChatReply> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new AiChatException("还没有选择模型。请在「设置 → AI 助手」里选择或填写模型名。");
        }

        var payload = AiJson.Text(IsOllama ? BuildOllamaPayload(request) : BuildPayload(request));

        var timeout = Math.Clamp(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : DefaultTimeoutSeconds, 5, 3600);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(timeout));

        var watch = Stopwatch.StartNew();
        string body;

        try
        {
            using var content = new StringContent(payload, new UTF8Encoding(false), "application/json");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatUri) { Content = content };
            ApplyAuth(httpRequest);

            using var response = await _http.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);

            body = await ReadBodyAsync(response, linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new AiChatException(DescribeFailure((int)response.StatusCode, body));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiChatException("等待模型超过 " + timeout + " 秒仍未返回。"
                + (IsLocal
                    ? "本地大模型首次加载会比较慢，可在「设置 → AI 助手」里调大超时时间；也可以在对话窗口点「停止」。"
                    : "云端服务偶尔会排队或波动，可稍后重试、调大超时时间，或在对话窗口点「停止」。"));
        }
        catch (HttpRequestException ex)
        {
            throw new AiChatException(DescribeNetworkFailure(ex), ex);
        }

        watch.Stop();

        var reply = IsOllama ? ParseOllamaReply(body) : ParseReply(body);
        reply.Elapsed = watch.Elapsed;
        return reply;
    }

    public async Task<List<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Min(DefaultTimeoutSeconds, 30)));

        string body;
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, ModelsUri);
            ApplyAuth(httpRequest);

            using var response = await _http.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
            body = await ReadBodyAsync(response, linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;

                // Azure 这类完整端点没有统一的模型列表接口，给一句人话而不是甩 404
                if (FullChatEndpoint != null && status is 404 or 405 or 400)
                {
                    throw new AiChatException("这个服务地址（完整端点）不支持列出模型，请直接手填模型名。");
                }

                throw new AiChatException(DescribeFailure(status, body, "读取模型列表失败"));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiChatException("读取模型列表超时（" + ModelsUri + "）。");
        }
        catch (HttpRequestException ex)
        {
            throw new AiChatException(DescribeNetworkFailure(ex, ModelsUri), ex);
        }

        return ParseModels(body);
    }

    /// <summary>按鉴权方式给请求加头（密钥为空则不加，本地服务不受影响）。</summary>
    private void ApplyAuth(HttpRequestMessage request)
    {
        if (_apiKey.Length == 0 || AuthMode == AiAuthMode.None) return;

        switch (AuthMode)
        {
            case AiAuthMode.ApiKeyHeader:
                request.Headers.TryAddWithoutValidation("api-key", _apiKey);
                break;

            default:
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
                break;
        }
    }

    /// <summary>把 HTTP 状态码翻译成能照着做的中文提示。</summary>
    public string DescribeFailure(int status, string body, string prefix = "模型服务返回错误")
    {
        var detail = Shorten(ExtractError(body) ?? body, 240);
        var keyHint = _apiKeyVariable != null
            ? "环境变量 " + _apiKeyVariable + " 可能是空的。"
            : "请在「设置 → AI 助手」里填写接口密钥。";

        return status switch
        {
            401 => "鉴权失败（HTTP 401）：密钥未填或无效。" + keyHint + (detail.Length > 0 ? "　服务端说明：" + detail : string.Empty),
            403 => "没有权限（HTTP 403）：密钥可能未开通该模型，或所在地区受限。" + (detail.Length > 0 ? "　服务端说明：" + detail : string.Empty),
            404 => "找不到地址或模型（HTTP 404）：请检查服务地址与模型名。" + (detail.Length > 0 ? "　服务端说明：" + detail : string.Empty),
            429 => "请求过于频繁或被限流（HTTP 429）：稍后重试，或检查账号额度。" + (detail.Length > 0 ? "　服务端说明：" + detail : string.Empty),
            >= 500 => "模型服务内部错误（HTTP " + status + "）：通常是临时的，稍后重试。" + (detail.Length > 0 ? "　服务端说明：" + detail : string.Empty),
            _ => prefix + "（HTTP " + status + "）：" + detail,
        };
    }

    /// <summary>网络层失败（DNS / TLS / 代理）的提示。</summary>
    public string DescribeNetworkFailure(Exception ex, string? uri = null)
    {
        var target = uri ?? ChatUri;
        var hint = IsLocal
            ? "请确认本地模型服务已启动（例如 ollama serve）。"
            : "请检查网络、接口地址是否正确；若在公司网络或需要代理，请在「设置 → AI 助手」里配置代理。";

        return "连不上模型服务（" + target + "）：" + ex.Message + "。" + hint;
    }

    // ================= 请求体（OpenAI 兼容） =================

    /// <summary>拼出 OpenAI 兼容请求体（纯函数，便于自测核对协议字段）。</summary>
    public JsonObject BuildPayload(AiChatRequest request, bool stream = false)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages) messages.Add(ToWire(message));

        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["temperature"] = Math.Clamp(request.Temperature, 0, 2),
            ["stream"] = stream,
        };

        if (request.MaxTokens > 0) payload["max_tokens"] = request.MaxTokens;
        if (request.Tools.Count > 0) payload["tools"] = BuildTools(request.Tools);

        // Ollama 的 OpenAI 端点认 reasoning_effort（实测 none = 6 秒 / 0 字思维链，
        // 而只写 /no_think 要 19~30 秒且仍有几百字）。
        // 但云端服务端对不认识的字段可能直接 400，因此默认只"该发时才发"：
        //   Auto  → 本地服务一定发；云端仅当模型看起来是推理模型（o1/gpt-5/deepseek-reasoner…）
        //   Always→ always 发（明确知道服务端支持）
        //   Never → 从不发
        if (ShouldSendReasoningEffort(request.Model))
        {
            payload["reasoning_effort"] = ReasoningEffort(request.Thinking);
        }

        // 顺手带上 think（Ollama 会同时看它；其它服务端不认识会忽略）
        if (IsOllama) payload["think"] = request.Thinking == AiThinkingLevel.Off ? false : LevelName(request.Thinking);

        return payload;
    }

    /// <summary>
    /// 要不要给这个模型发 reasoning_effort。
    /// 云端服务端（尤其是中转）遇到不认识的字段常直接 400，所以默认只对"确定认这个参数"的场景发：
    /// 本地服务、或模型名看起来是推理模型；也可以在设置里强制 总是 / 从不。
    /// </summary>
    public bool ShouldSendReasoningEffort(string? model) => ReasoningEffortMode switch
    {
        AiReasoningEffortMode.Always => true,
        AiReasoningEffortMode.Never => false,
        _ => IsLocal || AiEndpoint.LooksLikeReasoningModel(model),
    };

    /// <summary>Ollama 原生 think 取值；返回 null 表示不传该字段。</summary>
    public static JsonNode? OllamaThinkValue(AiThinkingLevel level) => level switch
    {
        AiThinkingLevel.Off => JsonValue.Create(false),
        _ => JsonValue.Create(LevelName(level)),
    };

    /// <summary>OpenAI 兼容 reasoning_effort 取值。</summary>
    public static string ReasoningEffort(AiThinkingLevel level)
        => level == AiThinkingLevel.Off ? "none" : LevelName(level);

    private static string LevelName(AiThinkingLevel level) => level switch
    {
        AiThinkingLevel.Low => "low",
        AiThinkingLevel.Medium => "medium",
        AiThinkingLevel.High => "high",
        _ => "low",
    };

    // ================= 请求体（Ollama 原生） =================

    /// <summary>拼出 Ollama 原生 /api/chat 请求体。</summary>
    public JsonObject BuildOllamaPayload(AiChatRequest request, bool stream = false)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages) messages.Add(ToWireOllama(message));

        var options = new JsonObject { ["temperature"] = Math.Clamp(request.Temperature, 0, 2) };
        if (request.MaxTokens > 0) options["num_predict"] = request.MaxTokens;

        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["options"] = options,
        };

        // Ollama 认 think：false 关闭；"low"/"medium"/"high" 为分级（实测 low 依然会思考 500+ 字）。
        // 只在需要时显式传，避免不支持思考的模型被这个字段搞出错误。
        var think = OllamaThinkValue(request.Thinking);
        if (think != null) payload["think"] = think;

        if (request.Tools.Count > 0) payload["tools"] = BuildTools(request.Tools);

        return payload;
    }

    private static JsonArray BuildTools(List<AiToolDefinition> tools)
    {
        var array = new JsonArray();

        foreach (var tool in tools)
        {
            JsonNode? parameters;
            try
            {
                parameters = JsonNode.Parse(tool.ParametersJson);
            }
            catch (JsonException)
            {
                parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            }

            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = parameters,
                },
            });
        }

        return array;
    }

    private static JsonObject ToWire(ChatMessage message)
    {
        var node = new JsonObject
        {
            ["role"] = RoleName(message.Role),
        };

        if (message.Role == ChatRole.Tool)
        {
            node["content"] = message.Content;
            if (!string.IsNullOrEmpty(message.ToolCallId)) node["tool_call_id"] = message.ToolCallId;
            if (!string.IsNullOrEmpty(message.ToolName)) node["name"] = message.ToolName;
            return node;
        }

        node["content"] = message.Content;

        if (message.Role == ChatRole.Assistant && message.ToolCalls.Count > 0)
        {
            var calls = new JsonArray();
            foreach (var call in message.ToolCalls)
            {
                calls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments,
                    },
                });
            }

            node["tool_calls"] = calls;
        }

        return node;
    }

    /// <summary>Ollama 原生消息：工具参数是对象、工具结果用 tool_name。</summary>
    private static JsonObject ToWireOllama(ChatMessage message)
    {
        var node = new JsonObject { ["role"] = RoleName(message.Role) };

        if (message.Role == ChatRole.Tool)
        {
            node["content"] = message.Content;
            if (!string.IsNullOrEmpty(message.ToolName)) node["tool_name"] = message.ToolName;
            return node;
        }

        node["content"] = message.Content;

        if (message.Role == ChatRole.Assistant && message.ToolCalls.Count > 0)
        {
            var calls = new JsonArray();
            foreach (var call in message.ToolCalls)
            {
                JsonNode? arguments;
                try
                {
                    arguments = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
                }
                catch (JsonException)
                {
                    arguments = new JsonObject();
                }

                calls.Add(new JsonObject
                {
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = arguments,
                    },
                });
            }

            node["tool_calls"] = calls;
        }

        return node;
    }

    private static string RoleName(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Tool => "tool",
        _ => "assistant",
    };

    // ================= 响应解析 =================

    /// <summary>解析 OpenAI 兼容返回（纯函数，便于自测）。</summary>
    public static AiChatReply ParseReply(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AiChatException("模型返回的不是合法 JSON：" + Shorten(json), ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AiChatException("模型返回格式异常：" + Shorten(json));
            }

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw new AiChatException("模型返回里没有 choices：" + Shorten(ExtractError(json) ?? json));
            }

            var choice = choices[0];
            var reply = new AiChatReply();

            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            {
                reply.FinishReason = finish.GetString();
            }

            if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                ReadMessageInto(message, reply);
            }

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                reply.PromptTokens = ReadInt(usage, "prompt_tokens");
                reply.CompletionTokens = ReadInt(usage, "completion_tokens");
            }

            return reply;
        }
    }

    /// <summary>解析 Ollama 原生 /api/chat 返回（纯函数）。</summary>
    public static AiChatReply ParseOllamaReply(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AiChatException("模型返回的不是合法 JSON：" + Shorten(json), ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AiChatException("模型返回格式异常：" + Shorten(json));
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw new AiChatException("模型服务报错：" + Shorten(error.ToString()));
            }

            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                throw new AiChatException("模型返回里没有 message：" + Shorten(json));
            }

            var reply = new AiChatReply();
            ReadMessageInto(message, reply);

            if (root.TryGetProperty("done_reason", out var done) && done.ValueKind == JsonValueKind.String)
            {
                reply.FinishReason = done.GetString();
            }

            reply.PromptTokens = ReadInt(root, "prompt_eval_count");
            reply.CompletionTokens = ReadInt(root, "eval_count");

            return reply;
        }
    }

    /// <summary>两种协议的 message 结构基本一致，共用这段解析。</summary>
    private static void ReadMessageInto(JsonElement message, AiChatReply reply)
    {
        var content = ReadString(message, "content") ?? string.Empty;
        var reasoning = FirstNonEmpty(
            ReadString(message, "reasoning"),
            ReadString(message, "reasoning_content"),
            ReadString(message, "thinking"));

        // 有些模板把思维链直接塞进 content 的  thinking 块里
        if (content.Contains("<think", StringComparison.OrdinalIgnoreCase))
        {
            var match = ThinkBlock.Match(content);
            if (match.Success)
            {
                reasoning = string.IsNullOrWhiteSpace(reasoning) ? match.Groups[1].Value.Trim() : reasoning;
                content = ThinkBlock.Replace(content, string.Empty).Trim();
            }
        }

        reply.Content = content.Trim();
        reply.Reasoning = string.IsNullOrWhiteSpace(reasoning) ? null : reasoning.Trim();
        reply.ToolCalls = ReadToolCalls(message);
    }

    private static List<AiToolCall> ReadToolCalls(JsonElement message)
    {
        var calls = new List<AiToolCall>();

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in toolCalls.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                string? name = null;
                string? arguments = null;

                if (item.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                {
                    name = ReadString(function, "name");
                    arguments = ReadString(function, "arguments");
                }

                name ??= ReadString(item, "name");
                arguments ??= ReadString(item, "arguments");

                if (string.IsNullOrWhiteSpace(name)) continue;

                calls.Add(new AiToolCall
                {
                    Id = ReadString(item, "id") ?? ("call_" + index),
                    Name = name.Trim(),
                    Arguments = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments,
                });

                index++;
            }
        }

        // 老式 function_call 形状
        if (calls.Count == 0 && message.TryGetProperty("function_call", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            var name = ReadString(single, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                calls.Add(new AiToolCall
                {
                    Id = "call_0",
                    Name = name.Trim(),
                    Arguments = ReadString(single, "arguments") ?? "{}",
                });
            }
        }

        return calls;
    }

    // ================= 模型列表 =================

    /// <summary>解析模型列表：OpenAI 的 {"data":[...]} 与 Ollama 的 {"models":[...]} 都支持。</summary>
    public static List<AiModelInfo> ParseModels(string json)
    {
        var result = new List<AiModelInfo>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in data.EnumerateArray()) AddModel(result, model);
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in models.EnumerateArray()) AddModel(result, model, "name");
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in root.EnumerateArray()) AddModel(result, model);
            }
        }
        catch (JsonException ex)
        {
            throw new AiChatException("模型列表不是合法 JSON：" + Shorten(json), ex);
        }

        if (result.Count == 0) throw new AiChatException("模型服务没有返回任何模型：" + Shorten(json));

        return result
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddModel(List<AiModelInfo> result, JsonElement element, string idField = "id")
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text)) result.Add(new AiModelInfo { Id = text.Trim() });
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        var id = ReadString(element, idField) ?? ReadString(element, "id") ?? ReadString(element, "name") ?? ReadString(element, "model");
        if (string.IsNullOrWhiteSpace(id)) return;

        string? detail = null;

        if (element.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
        {
            var parameterSize = ReadString(details, "parameter_size");
            var quantization = ReadString(details, "quantization_level");
            detail = string.Join(" ", new[] { parameterSize, quantization }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        if (string.IsNullOrWhiteSpace(detail)
            && element.TryGetProperty("size", out var sizeElement)
            && sizeElement.ValueKind == JsonValueKind.Number)
        {
            detail = Math.Round(sizeElement.GetInt64() / 1024d / 1024d / 1024d, 2) + " GB";
        }

        result.Add(new AiModelInfo
        {
            Id = id.Trim(),
            OwnedBy = ReadString(element, "owned_by"),
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
        });
    }

    // ================= 杂项 =================

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // 服务端经常不带 charset（Ollama 就不带），统一按 UTF-8 解码，否则中文全变乱码
        return new UTF8Encoding(false).GetString(bytes);
    }

    /// <summary>取服务端返回的错误说明（OpenAI 形状 {"error":{"message":...}} 或 {"error":"..."}）。</summary>
    public static string? ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.ValueKind == JsonValueKind.Object)
                {
                    return ReadString(error, "message") ?? ReadString(error, "code") ?? error.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // 不是 JSON 就算了
        }

        return null;
    }

    public static string Shorten(string? text, int max = 300)
    {
        var value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value.Substring(0, max) + "…";
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,

            // 对象 / 数组：直接给紧凑 JSON 文本（Ollama 原生工具参数就是对象）
            _ => value.GetRawText(),
        };
    }

    private static int? ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
