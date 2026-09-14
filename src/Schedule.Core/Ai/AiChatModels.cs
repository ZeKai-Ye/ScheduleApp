using Schedule.Core.Models;

namespace Schedule.Core.Ai;

/// <summary>对话角色（与 OpenAI 兼容接口一致）。</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>模型请求调用的一次工具（函数调用）。</summary>
public sealed class AiToolCall
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>模型给出的参数（JSON 文本，可能不合法，由工具目录负责校验）。</summary>
    public string Arguments { get; set; } = "{}";

    public override string ToString() => Name + "(" + Arguments + ")";
}

/// <summary>一条对话消息。</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }

    public string Content { get; init; } = string.Empty;

    /// <summary>role = Tool 时，对应哪一次工具调用。</summary>
    public string? ToolCallId { get; init; }

    /// <summary>role = Tool 时的工具名（部分服务端需要）。</summary>
    public string? ToolName { get; init; }

    /// <summary>role = Assistant 时模型请求的工具调用。</summary>
    public List<AiToolCall> ToolCalls { get; init; } = new();

    /// <summary>思维链文本（只用于界面展示，不回传给模型）。</summary>
    public string? Reasoning { get; init; }

    /// <summary>界面展示用的角色名。</summary>
    public string RoleText => Role switch
    {
        ChatRole.System => "系统",
        ChatRole.User => "我",
        ChatRole.Tool => "工具",
        _ => "助手",
    };

    public static ChatMessage System(string content) => new() { Role = ChatRole.System, Content = content };

    public static ChatMessage User(string content) => new() { Role = ChatRole.User, Content = content };

    public static ChatMessage Assistant(string content, string? reasoning = null, List<AiToolCall>? toolCalls = null)
        => new()
        {
            Role = ChatRole.Assistant,
            Content = content,
            Reasoning = reasoning,
            ToolCalls = toolCalls ?? new List<AiToolCall>(),
        };

    public static ChatMessage ToolResult(AiToolCall call, string content)
        => new() { Role = ChatRole.Tool, Content = content, ToolCallId = call.Id, ToolName = call.Name };
}

/// <summary>提供给模型的一个工具定义。</summary>
public sealed class AiToolDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>参数的 JSON Schema 文本（形如 {"type":"object","properties":{...}}）。</summary>
    public string ParametersJson { get; init; } = "{\"type\":\"object\",\"properties\":{}}";
}

/// <summary>一次补全请求。</summary>
public sealed class AiChatRequest
{
    public string Model { get; set; } = string.Empty;

    public List<ChatMessage> Messages { get; set; } = new();

    public List<AiToolDefinition> Tools { get; set; } = new();

    /// <summary>0 ~ 2。</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>0 表示不限制（交给服务端默认）。</summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// 思考（思维链）等级。默认最低档（完全不思考）——Qwen3 这类"思考模型"会先想一大段，
    /// 既慢又占上下文。实现：Ollama 原生用 think（false / "low" / "medium" / "high"），
    /// OpenAI 兼容用 reasoning_effort（"none" / "low" / …），系统提示里再补 /no_think。
    /// </summary>
    public AiThinkingLevel Thinking { get; set; } = AiThinkingLevel.Off;

    public int TimeoutSeconds { get; set; } = 180;
}

/// <summary>一次补全结果。</summary>
public sealed class AiChatReply
{
    public string Content { get; set; } = string.Empty;

    /// <summary>思维链（部分服务端会返回 reasoning / reasoning_content / thinking）。</summary>
    public string? Reasoning { get; set; }

    public List<AiToolCall> ToolCalls { get; set; } = new();

    public string? FinishReason { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    /// <summary>耗时（由客户端填写，便于界面显示"本地模型跑了多久"）。</summary>
    public TimeSpan Elapsed { get; set; }
}

/// <summary>可用的模型。</summary>
public sealed class AiModelInfo
{
    public string Id { get; init; } = string.Empty;

    public string? OwnedBy { get; init; }

    /// <summary>Ollama 会额外给出参数量 / 量化信息，展示用。</summary>
    public string? Detail { get; init; }

    public override string ToString() => Id;
}

/// <summary>与模型服务通信失败（带面向用户的中文说明）。</summary>
public sealed class AiChatException : Exception
{
    public AiChatException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>对话模型客户端（本地 Ollama / LM Studio / 任意 OpenAI 兼容服务）。</summary>
public interface IAiChatClient
{
    /// <summary>服务地址（规范化后）。</summary>
    string BaseUrl { get; }

    Task<AiChatReply> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken);

    /// <summary>列出可用模型（GET /v1/models）。</summary>
    Task<List<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken);
}
