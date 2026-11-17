using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Schedule.Core.Ai;

/// <summary>流式返回的一片内容：界面一边收一边显示。</summary>
public sealed class AiStreamChunk
{
    /// <summary>正文增量（可能为空）。</summary>
    public string? Text { get; init; }

    /// <summary>思维链增量（可能为空）。</summary>
    public string? Reasoning { get; init; }

    /// <summary>新一轮开始了（上一轮只调用了工具），界面可以把临时气泡清空。</summary>
    public bool IsNewRound { get; init; }

    /// <summary>是否结束；结束时 <see cref="Reply"/> 带着完整结果（正文 / 工具调用 / 用量）。</summary>
    public bool IsDone { get; init; }

    public AiChatReply? Reply { get; init; }
}

/// <summary>能流式返回的模型客户端（<see cref="OpenAiCompatibleClient"/> 实现了它）。</summary>
public interface IAiStreamingChatClient
{
    IAsyncEnumerable<AiStreamChunk> StreamAsync(AiChatRequest request, CancellationToken cancellationToken);
}

/// <summary>一行流式响应的处理结果。</summary>
public enum StreamLineResult
{
    /// <summary>与正文无关的行（注释、心跳、空行）。</summary>
    Ignored,

    /// <summary>拿到一片增量。</summary>
    Chunk,

    /// <summary>流结束。</summary>
    Ended,
}

/// <summary>
/// 把流式增量拼回一份完整的 <see cref="AiChatReply"/>。
/// 两种协议的差异（SSE / NDJSON、工具调用分片）都在这里消化，界面层不用关心。
/// </summary>
public sealed class AiStreamAccumulator
{
    private static readonly Regex ThinkBlock = new(
        @"<think(?:ing)?>([\s\S]*?)</think(?:ing)?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly SortedDictionary<int, ToolCallBuilder> _toolCalls = new();
    private List<AiToolCall>? _wholeToolCalls;

    private string? _finishReason;
    private int? _promptTokens;
    private int? _completionTokens;
    private bool _any;

    /// <summary>是否收到过任何正文 / 思维链 / 工具调用。</summary>
    public bool HasAny => _any;

    public void AppendText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        _content.Append(text);
        _any = true;
    }

    public void AppendReasoning(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        _reasoning.Append(text);
        _any = true;
    }

    public void SetFinishReason(string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason)) _finishReason = reason;
    }

    public void SetUsage(int? promptTokens, int? completionTokens)
    {
        if (promptTokens.HasValue) _promptTokens = promptTokens;
        if (completionTokens.HasValue) _completionTokens = completionTokens;
    }

    /// <summary>OpenAI 兼容：工具调用是分片来的，按 index 累积。</summary>
    public void AppendToolCall(int index, string? id, string? name, string? arguments)
    {
        if (!_toolCalls.TryGetValue(index, out var builder))
        {
            builder = new ToolCallBuilder();
            _toolCalls[index] = builder;
        }

        if (!string.IsNullOrEmpty(id)) builder.Id = id;
        if (!string.IsNullOrEmpty(name)) builder.Name = name;
        if (!string.IsNullOrEmpty(arguments)) builder.Arguments.Append(arguments);

        _any = true;
    }

    /// <summary>Ollama 原生：工具调用一次给全（不是分片）。</summary>
    public void SetWholeToolCalls(List<AiToolCall> calls)
    {
        if (calls.Count == 0) return;

        _wholeToolCalls = calls;
        _any = true;
    }

    public AiChatReply ToReply()
    {
        var content = _content.ToString();
        var reasoning = _reasoning.ToString();

        // 有些模板把思维链塞在正文的 think 块里：收完再拆开，界面上看到的是干净正文
        if (content.Contains("<think", StringComparison.OrdinalIgnoreCase))
        {
            var match = ThinkBlock.Match(content);
            if (match.Success)
            {
                if (reasoning.Length == 0) reasoning = match.Groups[1].Value;
                content = ThinkBlock.Replace(content, string.Empty);
            }
        }

        return new AiChatReply
        {
            Content = content.Trim(),
            Reasoning = string.IsNullOrWhiteSpace(reasoning) ? null : reasoning.Trim(),
            ToolCalls = BuildToolCalls(),
            FinishReason = _finishReason,
            PromptTokens = _promptTokens,
            CompletionTokens = _completionTokens,
        };
    }

    private List<AiToolCall> BuildToolCalls()
    {
        if (_wholeToolCalls != null) return _wholeToolCalls;

        var result = new List<AiToolCall>();

        foreach (var builder in _toolCalls.Values)
        {
            if (string.IsNullOrWhiteSpace(builder.Name)) continue;

            result.Add(new AiToolCall
            {
                Id = builder.Id,
                Name = builder.Name,
                Arguments = builder.Arguments.ToString(),
            });
        }

        return result;
    }

    private sealed class ToolCallBuilder
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public StringBuilder Arguments { get; } = new();
    }
}

/// <summary>
/// 逐行解析流式响应（纯函数，便于自测）：OpenAI 兼容走 SSE，Ollama 原生走 NDJSON。
/// </summary>
public static class AiStreamParser
{
    /// <summary>OpenAI 兼容：<c>data: {...}</c>，以 <c>data: [DONE]</c> 结束。</summary>
    public static StreamLineResult ReadOpenAiLine(string? line, AiStreamAccumulator accumulator, out AiStreamChunk? chunk)
    {
        chunk = null;

        if (string.IsNullOrWhiteSpace(line)) return StreamLineResult.Ignored;

        var text = line.Trim();
        if (!text.StartsWith("data:", StringComparison.Ordinal)) return StreamLineResult.Ignored;

        var json = text[5..].Trim();
        if (json.Length == 0) return StreamLineResult.Ignored;
        if (string.Equals(json, "[DONE]", StringComparison.Ordinal)) return StreamLineResult.Ended;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return StreamLineResult.Ignored;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return StreamLineResult.Ignored;

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                accumulator.SetUsage(ReadInt(usage, "prompt_tokens"), ReadInt(usage, "completion_tokens"));
            }

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return StreamLineResult.Ignored;
            }

            var choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            {
                accumulator.SetFinishReason(finish.GetString());
            }

            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            {
                return StreamLineResult.Ignored;
            }

            var content = ReadString(delta, "content");
            var reasoning = FirstNonEmpty(
                ReadString(delta, "reasoning_content"),
                ReadString(delta, "reasoning"),
                ReadString(delta, "thinking"));

            accumulator.AppendText(content);
            accumulator.AppendReasoning(reasoning);
            ReadToolCallDeltas(delta, accumulator);

            if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(reasoning)) return StreamLineResult.Ignored;

            chunk = new AiStreamChunk { Text = content, Reasoning = reasoning };
            return StreamLineResult.Chunk;
        }
    }

    /// <summary>Ollama 原生：每行一个 JSON，<c>done: true</c> 结束。</summary>
    public static StreamLineResult ReadOllamaLine(string? line, AiStreamAccumulator accumulator, out AiStreamChunk? chunk)
    {
        chunk = null;

        if (string.IsNullOrWhiteSpace(line)) return StreamLineResult.Ignored;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return StreamLineResult.Ignored;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return StreamLineResult.Ignored;

            if (root.TryGetProperty("error", out var error))
            {
                throw new AiChatException("模型服务报错：" + (error.GetString() ?? error.ToString()));
            }

            string? content = null;
            string? reasoning = null;

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                content = ReadString(message, "content");
                reasoning = FirstNonEmpty(ReadString(message, "thinking"), ReadString(message, "reasoning"));

                accumulator.AppendText(content);
                accumulator.AppendReasoning(reasoning);

                if (message.TryGetProperty("tool_calls", out var calls)
                    && calls.ValueKind == JsonValueKind.Array
                    && calls.GetArrayLength() > 0)
                {
                    accumulator.SetWholeToolCalls(ReadToolCalls(message));
                }
            }

            if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
            {
                accumulator.SetFinishReason(ReadString(root, "done_reason"));
                accumulator.SetUsage(ReadInt(root, "prompt_eval_count"), ReadInt(root, "eval_count"));
                return StreamLineResult.Ended;
            }

            if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(reasoning)) return StreamLineResult.Ignored;

            chunk = new AiStreamChunk { Text = content, Reasoning = reasoning };
            return StreamLineResult.Chunk;
        }
    }

    private static void ReadToolCallDeltas(JsonElement delta, AiStreamAccumulator accumulator)
    {
        if (!delta.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array) return;

        foreach (var item in calls.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var index = ReadInt(item, "index") ?? 0;
            var id = ReadString(item, "id");
            string? name = null;
            string? arguments = null;

            if (item.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
            {
                name = ReadString(function, "name");
                arguments = ReadString(function, "arguments");
            }

            accumulator.AppendToolCall(index, id, name, arguments);
        }
    }

    /// <summary>Ollama 的 tool_calls 是完整对象，arguments 可能是对象也可能是字符串。</summary>
    private static List<AiToolCall> ReadToolCalls(JsonElement message)
    {
        var result = new List<AiToolCall>();

        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in calls.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object) continue;

            var name = ReadString(function, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            string arguments = string.Empty;
            if (function.TryGetProperty("arguments", out var args))
            {
                arguments = args.ValueKind switch
                {
                    JsonValueKind.String => args.GetString() ?? string.Empty,
                    JsonValueKind.Object or JsonValueKind.Array => args.GetRawText(),
                    _ => string.Empty,
                };
            }

            result.Add(new AiToolCall
            {
                Id = ReadString(item, "id") ?? string.Empty,
                Name = name,
                Arguments = arguments,
            });
        }

        return result;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;
}
