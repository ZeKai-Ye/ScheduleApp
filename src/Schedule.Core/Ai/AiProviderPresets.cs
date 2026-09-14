using Schedule.Core.Models;

namespace Schedule.Core.Ai;

/// <summary>一个模型服务预设：选中后自动填地址 / 模型 / 鉴权方式，并提示去哪拿密钥。</summary>
public sealed class AiProviderPreset
{
    public string Name { get; init; } = string.Empty;

    /// <summary>OpenAI 兼容根地址；本地服务是 http://127.0.0.1:…，云端是 https://…。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>建议的模型名（可留空让用户自己填）。</summary>
    public string Model { get; init; } = string.Empty;

    public AiAuthMode AuthMode { get; init; } = AiAuthMode.Bearer;

    /// <summary>是否需要密钥（本地服务不需要）。</summary>
    public bool NeedsKey { get; init; }

    /// <summary>是否本地运行（本地＝数据不出本机）。</summary>
    public bool IsLocal { get; init; }

    /// <summary>界面提示：去哪申请密钥 / 有什么注意点。</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>下拉里显示的文本。</summary>
    public string Display => Name + (IsLocal ? "（本地）" : NeedsKey ? "（需要密钥）" : string.Empty);

    public override string ToString() => Display;
}

/// <summary>
/// 常见模型服务的预设清单（纯数据，便于自测核对地址与"是否需要密钥"）。
/// 云端一律走 OpenAI 兼容接口；用户也可以选「自定义」自己填地址。
/// </summary>
public static class AiProviderPresets
{
    /// <summary>自定义（不对地址/模型做任何预设）。</summary>
    public const string CustomName = "自定义（OpenAI 兼容）";

    public static IReadOnlyList<AiProviderPreset> All { get; } = new List<AiProviderPreset>
    {
        new()
        {
            Name = "Ollama（本机）",
            BaseUrl = "http://127.0.0.1:11434/v1",
            Model = "qwen3:8b",
            IsLocal = true,
            AuthMode = AiAuthMode.None,
            Hint = "本地运行、不需要密钥、断网可用。安装 Ollama 后执行 ollama pull qwen3:8b。",
        },
        new()
        {
            Name = "LM Studio（本机）",
            BaseUrl = "http://127.0.0.1:1234/v1",
            Model = string.Empty,
            IsLocal = true,
            AuthMode = AiAuthMode.None,
            Hint = "LM Studio 里打开本地服务器（OpenAI Compatible Server），端口默认 1234。",
        },
        new()
        {
            Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
            NeedsKey = true,
            Hint = "密钥在 platform.deepseek.com 申请；deepseek-chat 支持工具调用，deepseek-reasoner 偏推理、较慢。",
        },
        new()
        {
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini",
            NeedsKey = true,
            Hint = "密钥在 platform.openai.com 申请；gpt-4o-mini 便宜且支持工具调用。",
        },
        new()
        {
            Name = "通义千问（阿里云 DashScope）",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Model = "qwen-plus",
            NeedsKey = true,
            Hint = "密钥在阿里云百炼控制台申请；用「兼容模式」地址即可直接调用。",
        },
        new()
        {
            Name = "智谱 GLM",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            Model = "glm-4-flash",
            NeedsKey = true,
            Hint = "密钥在 open.bigmodel.cn 申请；glm-4-flash 免费额度较大。",
        },
        new()
        {
            Name = "Moonshot Kimi",
            BaseUrl = "https://api.moonshot.cn/v1",
            Model = "moonshot-v1-8k",
            NeedsKey = true,
            Hint = "密钥在 platform.moonshot.cn 申请。",
        },
        new()
        {
            Name = "OpenRouter（聚合）",
            BaseUrl = "https://openrouter.ai/api/v1",
            Model = string.Empty,
            NeedsKey = true,
            Hint = "密钥在 openrouter.ai 申请，一个密钥即可调用上百个模型；可在“获取模型列表”里挑。",
        },
        new()
        {
            Name = "Azure OpenAI（完整端点）",
            BaseUrl = "https://你的资源名.openai.azure.com/openai/deployments/你的部署名/chat/completions?api-version=2024-06-01",
            Model = string.Empty,
            NeedsKey = true,
            AuthMode = AiAuthMode.ApiKeyHeader,
            Hint = "整条端点地址照抄（含 ?api-version=）；密钥用 api-key 请求头。这种地址无法列出模型，请手填部署名。",
        },
        new()
        {
            Name = CustomName,
            BaseUrl = string.Empty,
            Model = string.Empty,
            NeedsKey = false,
            AuthMode = AiAuthMode.Bearer,
            Hint = "任何 OpenAI 兼容服务都可以：填根地址（…/v1），需要密钥就填密钥。",
        },
    };

    /// <summary>按地址猜预设（设置页用来把"当前服务"高亮出来）。</summary>
    public static AiProviderPreset? Match(string? baseUrl)
    {
        var normalized = OpenAiCompatibleClient.Normalize(baseUrl);

        return All.FirstOrDefault(p =>
            p.BaseUrl.Length > 0
            && string.Equals(OpenAiCompatibleClient.Normalize(p.BaseUrl), normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>是否需要密钥的汇总判断（界面上给云端加提示用）。</summary>
    public static bool IsCloud(string? baseUrl) => !AiEndpoint.IsLocal(baseUrl);

    /// <summary>把预设应用到设置上。</summary>
    public static void Apply(AiProviderPreset preset, AppSettings settings)
    {
        settings.AiProvider = preset.Name;
        settings.AiBaseUrl = preset.BaseUrl;
        settings.AiModel = preset.Model;
        settings.AiAuthMode = preset.AuthMode;

        // 本地服务取消思考最快；云端保持最低档（不发厂商专属参数）
        settings.AiThinking = AiThinkingLevel.Off;
    }
}
