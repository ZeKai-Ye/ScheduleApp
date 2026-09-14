using System.Text.Json;
using System.Text.RegularExpressions;

namespace Schedule.Core.Ai;

/// <summary>
/// 从模型正文里"抢救"出工具调用。
///
/// 首选原生 tool_calls（Ollama / Qwen3 都支持），但换别的本地推理框架时，
/// 模型常常把调用写成 JSON 代码块或 &lt;tool_call&gt; 标签，这里做兜底解析。
/// </summary>
public static class AiActionParser
{
    private static readonly Regex Fence = new(@"\`\`\`(?:json|JSON)?\s*([\s\S]*?)\`\`\`", RegexOptions.Compiled);
    private static readonly Regex Tagged = new(@"<tool_call>([\s\S]*?)</tool_call>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>尝试从文本里解析出工具调用（解析不出就返回空列表）。</summary>
    public static List<AiToolCall> Extract(string? content)
    {
        var calls = new List<AiToolCall>();
        if (string.IsNullOrWhiteSpace(content)) return calls;

        foreach (Match match in Tagged.Matches(content))
        {
            var call = ParseObject(match.Groups[1].Value);
            if (call != null) calls.Add(call);
        }

        if (calls.Count > 0) return calls;

        foreach (Match match in Fence.Matches(content))
        {
            foreach (var candidate in BalancedObjects(match.Groups[1].Value))
            {
                var call = ParseObject(candidate);
                if (call != null) calls.Add(call);
            }
        }

        if (calls.Count > 0) return calls;

        foreach (var candidate in BalancedObjects(content))
        {
            var call = ParseObject(candidate);
            if (call != null) calls.Add(call);
        }

        return calls;
    }

    /// <summary>把正文里的动作 JSON 去掉，避免把原始 JSON 直接展示给用户。</summary>
    public static string StripAction(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        var text = Tagged.Replace(content, string.Empty);
        text = Fence.Replace(text, string.Empty);

        foreach (var candidate in BalancedObjects(text))
        {
            if (ParseObject(candidate) != null) text = text.Replace(candidate, string.Empty);
        }

        return text.Trim();
    }

    /// <summary>把一个候选 JSON 解析成工具调用；不是「动作对象」就返回 null。</summary>
    public static AiToolCall? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var name = ReadName(root);
            if (string.IsNullOrWhiteSpace(name)) return null;

            var arguments = ReadArguments(root);
            return new AiToolCall { Id = "text_0", Name = name.Trim(), Arguments = arguments };
        }
    }

    private static string? ReadName(JsonElement root)
    {
        foreach (var key in new[] { "action", "tool", "name", "function", "tool_name" })
        {
            if (!root.TryGetProperty(key, out var value)) continue;

            if (value.ValueKind == JsonValueKind.String) return value.GetString();

            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("name", out var nested) && nested.ValueKind == JsonValueKind.String)
            {
                return nested.GetString();
            }
        }

        return null;
    }

    private static string ReadArguments(JsonElement root)
    {
        foreach (var key in new[] { "arguments", "args", "parameters", "params", "input" })
        {
            if (!root.TryGetProperty(key, out var value)) continue;

            return value.ValueKind switch
            {
                JsonValueKind.Object => value.GetRawText(),
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? "{}" : value.GetString()!,
                _ => "{}",
            };
        }

        // 允许把参数直接平铺在动作对象上：{"action":"list_items","date":"今天"}
        var flat = new System.Text.Json.Nodes.JsonObject();
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("action") || property.NameEquals("tool") || property.NameEquals("name")
                || property.NameEquals("function") || property.NameEquals("tool_name"))
            {
                continue;
            }

            flat[property.Name] = System.Text.Json.Nodes.JsonNode.Parse(property.Value.GetRawText());
        }

        return flat.Count == 0 ? "{}" : AiJson.Text(flat);
    }

    /// <summary>从文本里找出所有花括号配平的 JSON 对象（忽略字符串与转义）。</summary>
    public static List<string> BalancedObjects(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    if (depth == 0) start = i;
                    depth++;
                    break;
                case '}':
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0 && start >= 0)
                        {
                            result.Add(text.Substring(start, i - start + 1));
                            start = -1;
                        }
                    }

                    break;
            }
        }

        return result;
    }
}
