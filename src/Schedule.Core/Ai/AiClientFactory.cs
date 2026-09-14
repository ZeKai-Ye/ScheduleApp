using System.Text.Json;
using System.Text.Json.Nodes;
using Schedule.Core.Models;

namespace Schedule.Core.Ai;

/// <summary>按设置创建对话客户端（把 AppSettings 与 HTTP 细节隔离开）。</summary>
public static class AiClientFactory
{
    public static OpenAiCompatibleClient Create(AppSettings settings)
        => new(settings.AiBaseUrl, settings.AiApiKey, settings.AiTimeoutSeconds, settings.AiApiMode);

    /// <summary>把设置翻译成会话参数。</summary>
    public static AiSessionOptions CreateOptions(AppSettings settings) => new()
    {
        Model = settings.AiModel,
        Temperature = settings.AiTemperature,
        TimeoutSeconds = settings.AiTimeoutSeconds,
        Thinking = settings.AiThinking,
        MaxTokens = settings.AiMaxTokens,
        MaxHistoryMessages = settings.AiMaxHistoryMessages,
        SystemPrompt = settings.AiSystemPrompt,
    };

    /// <summary>实际使用的协议名（界面展示用）。</summary>
    public static string DescribeProtocol(AppSettings settings)
    {
        var normalized = OpenAiCompatibleClient.Normalize(settings.AiBaseUrl);
        return OpenAiCompatibleClient.ResolveMode(settings.AiApiMode, normalized) == AiApiMode.Ollama
            ? "Ollama 原生 /api/chat"
            : "OpenAI 兼容 /v1/chat/completions";
    }

    /// <summary>设置页「测试连接」用的一句话说明。</summary>
    public static string Describe(AppSettings settings)
        => OpenAiCompatibleClient.Normalize(settings.AiBaseUrl)
            + "　模型：" + (string.IsNullOrWhiteSpace(settings.AiModel) ? "(未选择)" : settings.AiModel)
            + "　协议：" + DescribeProtocol(settings);

    /// <summary>把工具结果里的内容压成一行，便于界面显示「AI 执行了什么」。</summary>
    public static string SummarizeToolResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";

        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("error", out var error)) return "错误：" + error;
                if (obj.TryGetPropertyValue("summary", out var summary)) return summary?.ToString() ?? "完成";
            }
        }
        catch (JsonException)
        {
            // 不是 JSON 就原样截断
        }

        return OpenAiCompatibleClient.Shorten(json, 120);
    }
}
