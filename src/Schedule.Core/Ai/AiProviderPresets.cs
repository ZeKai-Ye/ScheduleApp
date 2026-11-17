using Schedule.Core.Localization;
using Schedule.Core.Models;

namespace Schedule.Core.Ai;

/// <summary>一个模型服务预设：选中后自动填地址 / 模型 / 鉴权方式，并提示去哪拿密钥。</summary>
public sealed class AiProviderPreset
{
    /// <summary>预设名（内部键，也是写进 settings.json 的 aiProvider 值，不要翻译）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>界面显示用的英文名；留空表示直接用 <see cref="Name"/>。</summary>
    public string EnglishName { get; init; } = string.Empty;

    /// <summary>OpenAI 兼容根地址；本地服务是 http://127.0.0.1:…，云端是 https://…。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>建议的模型名（可留空让用户自己填）。</summary>
    public string Model { get; init; } = string.Empty;

    public AiAuthMode AuthMode { get; init; } = AiAuthMode.Bearer;

    /// <summary>是否需要密钥（本地服务不需要）。</summary>
    public bool NeedsKey { get; init; }

    /// <summary>是否本地运行（本地＝数据不出本机）。</summary>
    public bool IsLocal { get; init; }

    /// <summary>界面提示（中文）：去哪申请密钥 / 有什么注意点。</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>界面提示的英文版。</summary>
    public string HintEnglish { get; init; } = string.Empty;

    /// <summary>下拉里显示的名字（跟随界面语言）。</summary>
    public string DisplayName => string.IsNullOrEmpty(EnglishName) ? Name : Loc.T(Name, EnglishName);

    /// <summary>界面提示（跟随界面语言）。</summary>
    public string HintText => string.IsNullOrEmpty(HintEnglish) ? Hint : Loc.T(Hint, HintEnglish);

    /// <summary>下拉里显示的文本：名字 + 本地 / 需要密钥的标记。</summary>
    public string Display => DisplayName
        + (IsLocal
            ? Loc.T("（本地）", " (local)")
            : NeedsKey
                ? Loc.T("（需要密钥）", " (API key required)")
                : string.Empty);

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

    /// <summary>「自定义」在下拉里的显示名（跟随界面语言）。</summary>
    public static string CustomDisplay => Loc.T(CustomName, "Custom (OpenAI-compatible)");

    public static IReadOnlyList<AiProviderPreset> All { get; } = new List<AiProviderPreset>
    {
        new()
        {
            Name = "Ollama（本机）",
            EnglishName = "Ollama",
            BaseUrl = "http://127.0.0.1:11434/v1",
            Model = "qwen3:8b",
            IsLocal = true,
            AuthMode = AiAuthMode.None,
            Hint = "本地运行、不需要密钥、断网可用。安装 Ollama 后执行 ollama pull qwen3:8b。",
            HintEnglish = "Runs locally, needs no API key, works offline. Install Ollama, then run ollama pull qwen3:8b.",
        },
        new()
        {
            Name = "LM Studio（本机）",
            EnglishName = "LM Studio",
            BaseUrl = "http://127.0.0.1:1234/v1",
            Model = string.Empty,
            IsLocal = true,
            AuthMode = AiAuthMode.None,
            Hint = "LM Studio 里打开本地服务器（OpenAI Compatible Server），端口默认 1234。",
            HintEnglish = "Start the local server in LM Studio (OpenAI Compatible Server); the default port is 1234.",
        },
        new()
        {
            Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
            NeedsKey = true,
            Hint = "密钥在 platform.deepseek.com 申请；deepseek-chat 支持工具调用，deepseek-reasoner 偏推理、较慢。",
            HintEnglish = "Get a key at platform.deepseek.com. deepseek-chat supports tool calls; deepseek-reasoner is slower and reasoning-heavy.",
        },
        new()
        {
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini",
            NeedsKey = true,
            Hint = "密钥在 platform.openai.com 申请；gpt-4o-mini 便宜且支持工具调用。",
            HintEnglish = "Get a key at platform.openai.com. gpt-4o-mini is cheap and supports tool calls.",
        },
        new()
        {
            Name = "通义千问（阿里云 DashScope）",
            EnglishName = "Qwen (Alibaba Cloud DashScope)",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Model = "qwen-plus",
            NeedsKey = true,
            Hint = "密钥在阿里云百炼控制台申请；用「兼容模式」地址即可直接调用。",
            HintEnglish = "Get a key in the Alibaba Cloud Bailian console; the compatible-mode URL is ready to use.",
        },
        new()
        {
            Name = "智谱 GLM",
            EnglishName = "Zhipu GLM",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            Model = "glm-4-flash",
            NeedsKey = true,
            Hint = "密钥在 open.bigmodel.cn 申请；glm-4-flash 免费额度较大。",
            HintEnglish = "Get a key at open.bigmodel.cn. glm-4-flash has a generous free quota.",
        },
        new()
        {
            Name = "Moonshot Kimi",
            BaseUrl = "https://api.moonshot.cn/v1",
            Model = "moonshot-v1-8k",
            NeedsKey = true,
            Hint = "密钥在 platform.moonshot.cn 申请。",
            HintEnglish = "Get a key at platform.moonshot.cn.",
        },
        new()
        {
            Name = "OpenRouter（聚合）",
            EnglishName = "OpenRouter (aggregator)",
            BaseUrl = "https://openrouter.ai/api/v1",
            Model = string.Empty,
            NeedsKey = true,
            Hint = "密钥在 openrouter.ai 申请，一个密钥即可调用上百个模型；可在“获取模型列表”里挑。",
            HintEnglish = "Get a key at openrouter.ai: one key reaches hundreds of models. Pick one with Fetch models.",
        },
        new()
        {
            Name = "Azure OpenAI（完整端点）",
            EnglishName = "Azure OpenAI (full endpoint)",
            BaseUrl = "https://你的资源名.openai.azure.com/openai/deployments/你的部署名/chat/completions?api-version=2024-06-01",
            Model = string.Empty,
            NeedsKey = true,
            AuthMode = AiAuthMode.ApiKeyHeader,
            Hint = "整条端点地址照抄（含 ?api-version=）；密钥用 api-key 请求头。这种地址无法列出模型，请手填部署名。",
            HintEnglish = "Paste the whole endpoint URL (including ?api-version=); the key goes in the api-key header. Models cannot be listed here, so type the deployment name manually.",
        },
        new()
        {
            Name = CustomName,
            EnglishName = "Custom (OpenAI-compatible)",
            BaseUrl = string.Empty,
            Model = string.Empty,
            NeedsKey = false,
            AuthMode = AiAuthMode.Bearer,
            Hint = "任何 OpenAI 兼容服务都可以：填根地址（…/v1），需要密钥就填密钥。",
            HintEnglish = "Any OpenAI-compatible service works: enter the base URL (…/v1) and a key if it needs one.",
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
