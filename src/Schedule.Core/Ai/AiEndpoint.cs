namespace Schedule.Core.Ai;

/// <summary>
/// 地址与模型的判断工具（纯函数，便于自测）：
/// 本地还是云端、模型是不是"会思考"的推理模型、用户是不是直接粘了完整端点地址。
/// </summary>
public static class AiEndpoint
{
    /// <summary>是不是本机/内网地址（决定要不要发 reasoning_effort 这类厂商专属参数）。</summary>
    public static bool IsLocal(string? baseUrl)
    {
        var host = Host(baseUrl);
        if (host.Length == 0) return true;

        if (host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "0.0.0.0") return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

        if (host.StartsWith("192.168.", StringComparison.Ordinal)) return true;
        if (host.StartsWith("10.", StringComparison.Ordinal)) return true;

        // 172.16.0.0 ~ 172.31.255.255
        if (host.StartsWith("172.", StringComparison.Ordinal))
        {
            var parts = host.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31) return true;
        }

        // 169.254.x.x（链路本地，常见于直连设备）
        if (host.StartsWith("169.254.", StringComparison.Ordinal)) return true;

        return false;
    }

    private static string Host(string? baseUrl)
    {
        var url = (baseUrl ?? string.Empty).Trim();
        if (url.Length == 0) return string.Empty;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
    }

    /// <summary>模型名看起来像"会思考"的推理模型（这类模型才认 reasoning_effort）。</summary>
    public static bool LooksLikeReasoningModel(string? model)
    {
        var name = (model ?? string.Empty).Trim().ToLowerInvariant();
        if (name.Length == 0) return false;

        string[] markers =
        {
            "o1", "o3", "o4", "gpt-5", "deepseek-reasoner", "deepseek-r1", "qwq", "glm-z1",
            "reasoning", "thinking", "qwen3", "magistral", "minimax-m1", "kimi-k2-thinking",
        };

        return markers.Any(marker => name.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// 用户是不是直接把"完整端点地址"粘了进来（例如 Azure OpenAI 或某个中转）：
    /// 形如 .../chat/completions，可能还带 ?api-version=...。
    /// 这种地址不能再去补 /v1，必须原样使用。
    /// </summary>
    public static bool IsFullChatEndpoint(string? baseUrl)
        => (baseUrl ?? string.Empty).Trim().Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);

    /// <summary>去掉查询串，便于从完整端点地址推出 /models 之类的兄弟地址。</summary>
    public static string WithoutQuery(string? url)
    {
        var text = (url ?? string.Empty).Trim();
        var index = text.IndexOf('?');
        return index < 0 ? text : text.Substring(0, index);
    }

    /// <summary>
    /// 把完整端点地址拆出"根地址"（用于展示与列出模型）：
    /// https://x/openai/deployments/d/chat/completions?api-version=1 → https://x/openai/deployments/d
    /// </summary>
    public static string RootOfFullEndpoint(string? url)
    {
        var text = WithoutQuery(url).TrimEnd('/');

        const string suffix = "/chat/completions";
        if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - suffix.Length).TrimEnd('/');
        }

        return text;
    }
}
