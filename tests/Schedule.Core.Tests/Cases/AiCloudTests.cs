using Schedule.Core.Ai;
using Schedule.Core.Api;
using Schedule.Core.Models;

namespace Schedule.Core.Tests.Cases;

/// <summary>
/// 联网（云端）模型支持：密钥处理、鉴权请求头、代理/完整端点地址、
/// 云端专属参数策略、错误提示，以及服务预设清单。
/// </summary>
public static class AiCloudTests
{
    public static void Run()
    {
        TestRunner.Section("AI 联网模型");
        Secrets();
        Endpoints();
        Presets();
        AuthHeaders();
        CloudPayload();
        Errors();
    }

    // ==================== 密钥 ====================

    private static void Secrets()
    {
        TestRunner.Equal(string.Empty, AiSecrets.Resolve(null).Key, "空密钥安全");
        TestRunner.Equal("sk-abc", AiSecrets.Resolve("  sk-abc  ").Key, "普通密钥去掉首尾空格");
        TestRunner.Check(AiSecrets.Resolve("sk-abc").Variable == null, "普通密钥没有环境变量名");

        const string variable = "SCHEDULEAPP_TEST_AI_KEY";
        Environment.SetEnvironmentVariable(variable, "sk-from-env");

        try
        {
            var (key, name) = AiSecrets.Resolve("env:" + variable);
            TestRunner.Equal("sk-from-env", key, "env: 前缀会去读环境变量");
            TestRunner.Equal(variable, name, "汇报环境变量名（便于出错时提示）");

            var (missing, missingName) = AiSecrets.Resolve("env:SCHEDULEAPP_NOT_DEFINED_KEY");
            TestRunner.Equal(string.Empty, missing, "环境变量不存在时密钥为空");
            TestRunner.Equal("SCHEDULEAPP_NOT_DEFINED_KEY", missingName, "仍然记得变量名");

            TestRunner.Check(AiSecrets.Describe("env:" + variable).Contains("sk-f", StringComparison.Ordinal), "界面提示带上打码后的密钥");
            TestRunner.Check(AiSecrets.Describe("env:SCHEDULEAPP_NOT_DEFINED_KEY").Contains("为空或不存在", StringComparison.Ordinal), "环境变量缺失时提示清楚");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        TestRunner.Equal("(未填写)", AiSecrets.Mask(""), "空密钥的展示");
        TestRunner.Equal("****", AiSecrets.Mask("abcd"), "短密钥全打码");
        TestRunner.Check(AiSecrets.Mask("sk-1234567890abcdef").StartsWith("sk-1", StringComparison.Ordinal), "长密钥保留头四位");
        TestRunner.Check(!AiSecrets.Mask("sk-1234567890abcdef").Contains("567890", StringComparison.Ordinal), "长密钥中间不外泄");
        TestRunner.Check(AiSecrets.Describe(string.Empty).Contains("未填写", StringComparison.Ordinal), "未填密钥的提示");
    }

    // ==================== 地址判断 ====================

    private static void Endpoints()
    {
        foreach (var local in new[]
                 {
                     "http://127.0.0.1:11434/v1", "http://localhost:1234/v1", "127.0.0.1:8080",
                     "http://192.168.1.50:11434/v1", "http://10.0.0.7:11434", "http://172.16.3.4:11434",
                     "http://172.31.9.9:11434", "http://169.254.10.1:11434", "http://[::1]:11434",
                     "http://nas.local:11434", "",
                 })
        {
            TestRunner.Check(AiEndpoint.IsLocal(local), "本地地址：" + (local.Length == 0 ? "(空)" : local));
        }

        foreach (var cloud in new[]
                 {
                     "https://api.openai.com/v1", "https://api.deepseek.com/v1",
                     "https://dashscope.aliyuncs.com/compatible-mode/v1", "https://openrouter.ai/api/v1",
                     "http://172.32.0.1:11434",
                 })
        {
            TestRunner.Check(!AiEndpoint.IsLocal(cloud), "云端地址：" + cloud);
        }

        TestRunner.Check(AiEndpoint.LooksLikeReasoningModel("o3-mini"), "o3 是推理模型");
        TestRunner.Check(AiEndpoint.LooksLikeReasoningModel("gpt-5-mini"), "gpt-5 是推理模型");
        TestRunner.Check(AiEndpoint.LooksLikeReasoningModel("deepseek-reasoner"), "deepseek-reasoner 是推理模型");
        TestRunner.Check(AiEndpoint.LooksLikeReasoningModel("qwen3:8b"), "qwen3 系列带思考");
        TestRunner.Check(!AiEndpoint.LooksLikeReasoningModel("gpt-4o-mini"), "gpt-4o-mini 不是推理模型");
        TestRunner.Check(!AiEndpoint.LooksLikeReasoningModel("glm-4-flash"), "glm-4-flash 不是推理模型");
        TestRunner.Check(!AiEndpoint.LooksLikeReasoningModel("deepseek-chat"), "deepseek-chat 不是推理模型");
        TestRunner.Check(!AiEndpoint.LooksLikeReasoningModel(null), "空模型名安全");

        const string azure = "https://res.openai.azure.com/openai/deployments/dep/chat/completions?api-version=2024-06-01";
        TestRunner.Check(AiEndpoint.IsFullChatEndpoint(azure), "识别 Azure 这类完整端点");
        TestRunner.Check(!AiEndpoint.IsFullChatEndpoint("https://api.openai.com/v1"), "普通根地址不算完整端点");
        TestRunner.Equal("https://res.openai.azure.com/openai/deployments/dep", AiEndpoint.RootOfFullEndpoint(azure), "从完整端点推根地址");
        TestRunner.Equal("https://x/a", AiEndpoint.WithoutQuery("https://x/a?b=1"), "去掉查询串");
    }

    // ==================== 服务预设 ====================

    private static void Presets()
    {
        TestRunner.Check(AiProviderPresets.All.Count >= 8, "预设数量够用（本地 + 常见云端）");
        TestRunner.Equal(
            AiProviderPresets.All.Count,
            AiProviderPresets.All.Select(p => p.Name).Distinct().Count(),
            "预设名不重复");

        foreach (var preset in AiProviderPresets.All)
        {
            TestRunner.Check(preset.Name.Length > 0, "预设都有名字");
            TestRunner.Check(preset.Hint.Length > 5, preset.Name + " 有使用提示");

            if (preset.Name == AiProviderPresets.CustomName) continue;

            TestRunner.Check(preset.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase), preset.Name + " 地址是 http(s)");

            if (preset.IsLocal)
            {
                TestRunner.Check(AiEndpoint.IsLocal(preset.BaseUrl), preset.Name + " 地址应判定为本地");
                TestRunner.Check(!preset.NeedsKey, preset.Name + " 本地服务不需要密钥");
                TestRunner.Check(preset.BaseUrl.StartsWith("http://127.0.0.1", StringComparison.Ordinal), preset.Name + " 用回环地址");
            }
            else
            {
                TestRunner.Check(preset.BaseUrl.StartsWith("https://", StringComparison.Ordinal), preset.Name + " 云端必须走 https");
                TestRunner.Check(preset.NeedsKey, preset.Name + " 云端需要密钥");
                TestRunner.Check(preset.Model.Length > 0 || preset.Name.Contains("OpenRouter", StringComparison.Ordinal) || preset.Name.Contains("Azure", StringComparison.Ordinal),
                    preset.Name + " 通常应给出默认模型");
            }
        }

        var ollama = AiProviderPresets.Match("http://127.0.0.1:11434");
        TestRunner.Check(ollama != null && ollama.Name.Contains("Ollama", StringComparison.Ordinal), "能按地址匹配回 Ollama 预设");

        var deepseek = AiProviderPresets.Match("https://api.deepseek.com/v1/");
        TestRunner.Check(deepseek != null && deepseek.Name == "DeepSeek", "能按地址匹配回 DeepSeek 预设");

        TestRunner.Check(AiProviderPresets.Match("http://127.0.0.1:9999/v1") == null, "不认识的本地端口没有预设");
        TestRunner.Check(AiProviderPresets.IsCloud("https://api.openai.com/v1"), "云端判定");
        TestRunner.Check(!AiProviderPresets.IsCloud("http://127.0.0.1:11434/v1"), "本地判定");

        var settings = new AppSettings();
        var deepSeekPreset = AiProviderPresets.All.First(p => p.Name == "DeepSeek");
        AiProviderPresets.Apply(deepSeekPreset, settings);

        TestRunner.Equal("DeepSeek", settings.AiProvider, "套用预设记录服务名");
        TestRunner.Equal("https://api.deepseek.com/v1", settings.AiBaseUrl, "套用预设写入地址");
        TestRunner.Equal("deepseek-chat", settings.AiModel, "套用预设写入模型");
        TestRunner.Equal(AiAuthMode.Bearer, settings.AiAuthMode, "套用预设写入鉴权方式");
        TestRunner.Equal(AiThinkingLevel.Off, settings.AiThinking, "套用预设后思考仍是最低档");
    }

    // ==================== 鉴权请求头（真实 HTTP） ====================

    private sealed class HeaderRecorder
    {
        public string? Authorization { get; set; }

        public string? ApiKey { get; set; }

        public string? UserAgent { get; set; }

        public string? Path { get; set; }
    }

    private static void AuthHeaders()
    {
        var recorder = new HeaderRecorder();

        using var server = new MiniHttpServer(0, request =>
        {
            recorder.Authorization = request.Headers.TryGetValue("Authorization", out var auth) ? auth : null;
            recorder.ApiKey = request.Headers.TryGetValue("api-key", out var apiKey) ? apiKey : null;
            recorder.UserAgent = request.Headers.TryGetValue("User-Agent", out var agent) ? agent : null;
            recorder.Path = request.Path;

            // 只有标准 OpenAI 根地址才有模型列表；Azure 这类完整端点的同级路径会 404
            if (request.Path == "/v1/models")
            {
                return ApiResponse.Text("{\"data\":[{\"id\":\"gpt-4o-mini\"}]}", 200, "application/json");
            }

            if (request.Path.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                return ApiResponse.Text(
                    "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"好\"}}]}",
                    200, "application/json");
            }

            // Azure 形状的端点没有统一的模型列表接口，这里模拟 404
            return ApiResponse.Error(404, "not found");
        });

        if (!server.Start(out var error))
        {
            TestRunner.Check(false, "假云端服务启动失败：" + error);
            return;
        }

        var baseUrl = "http://127.0.0.1:" + server.Port + "/v1";
        var request = new AiChatRequest { Model = "gpt-4o-mini", Messages = new List<ChatMessage> { ChatMessage.User("你好") } };

        // 1) Bearer（默认）
        using (var client = new OpenAiCompatibleClient(baseUrl, "sk-test-123", authMode: AiAuthMode.Bearer))
        {
            client.CompleteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Equal("Bearer sk-test-123", recorder.Authorization, "Bearer 方式带上密钥");
            TestRunner.Check(recorder.ApiKey == null, "Bearer 方式不带 api-key 头");
            TestRunner.Check(recorder.UserAgent != null && recorder.UserAgent.StartsWith("ScheduleApp/", StringComparison.Ordinal), "带上 User-Agent");

            client.ListModelsAsync(CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Equal("Bearer sk-test-123", recorder.Authorization, "列出模型同样带密钥（云端必须）");
        }

        // 2) api-key 头（Azure）
        using (var client = new OpenAiCompatibleClient(baseUrl, "azure-key", authMode: AiAuthMode.ApiKeyHeader))
        {
            client.CompleteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Equal("azure-key", recorder.ApiKey, "api-key 方式带上密钥");
            TestRunner.Check(recorder.Authorization == null, "api-key 方式不带 Authorization");
        }

        // 3) 不带鉴权（本地）
        using (var client = new OpenAiCompatibleClient(baseUrl, "sk-ignored", authMode: AiAuthMode.None))
        {
            client.CompleteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Check(recorder.Authorization == null, "显式关闭鉴权时不带 Authorization");
            TestRunner.Check(recorder.ApiKey == null, "显式关闭鉴权时不带 api-key");
        }

        // 4) 没有填密钥
        using (var client = new OpenAiCompatibleClient(baseUrl, string.Empty))
        {
            client.CompleteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Check(recorder.Authorization == null, "密钥为空时不带 Authorization");
        }

        // 5) env: 间接引用
        const string variable = "SCHEDULEAPP_TEST_AI_KEY2";
        Environment.SetEnvironmentVariable(variable, "sk-env-abc");

        try
        {
            using var client = new OpenAiCompatibleClient(baseUrl, "env:" + variable);
            client.CompleteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Equal("Bearer sk-env-abc", recorder.Authorization, "env: 引用的密钥被真正使用");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        // 6) 完整端点地址（Azure 形状）：必须原样打过去，不能补成 /v1/chat/completions
        var full = "http://127.0.0.1:" + server.Port + "/openai/deployments/my-dep/chat/completions?api-version=2024-06-01";

        using (var client = new OpenAiCompatibleClient(full, "azure-key", authMode: AiAuthMode.ApiKeyHeader))
        {
            var reply = client.CompleteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Equal("好", reply.Content, "完整端点也能正常对话");
            TestRunner.Equal("/openai/deployments/my-dep/chat/completions", recorder.Path, "完整端点地址被原样使用");
            TestRunner.Check(client.FullChatEndpoint != null, "客户端记住这是完整端点");

            // 这种地址列不出模型时应给出人话
            recorder.Path = null;
            var problem = false;
            try
            {
                client.ListModelsAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (AiChatException ex)
            {
                problem = ex.Message.Contains("不支持列出模型", StringComparison.Ordinal);
            }

            TestRunner.Check(problem, "完整端点列不出模型时提示「手填模型名」");
        }
    }

    // ==================== 云端参数策略 ====================

    private static void CloudPayload()
    {
        var request = new AiChatRequest { Model = "gpt-4o-mini", Thinking = AiThinkingLevel.Off };

        using var cloud = new OpenAiCompatibleClient("https://api.openai.com/v1", "sk-x");
        using var local = new OpenAiCompatibleClient("http://127.0.0.1:11434/v1");

        TestRunner.Check(!cloud.IsLocal, "云端地址判定为云端");
        TestRunner.Check(local.IsLocal, "本地地址判定为本地");

        // Auto：云端 + 非推理模型 → 不发 reasoning_effort（避免不认识的字段被 400）
        TestRunner.Check(!cloud.ShouldSendReasoningEffort("gpt-4o-mini"), "Auto：云端普通模型不发 reasoning_effort");
        TestRunner.Check(cloud.BuildPayload(request)["reasoning_effort"] == null, "云端普通模型的请求体里没有 reasoning_effort");
        TestRunner.Check(cloud.BuildPayload(request)["think"] == null, "非 Ollama 地址不传 think");

        // Auto：云端 + 推理模型 → 发
        TestRunner.Check(cloud.ShouldSendReasoningEffort("o3-mini"), "Auto：云端推理模型才发");
        TestRunner.Equal("none", cloud.BuildPayload(new AiChatRequest { Model = "o3-mini", Thinking = AiThinkingLevel.Off })["reasoning_effort"]!.GetValue<string>(), "推理模型最低档 = none");

        // Auto：本地 → 一定发（实测能把思考压到 0 字）
        TestRunner.Check(local.ShouldSendReasoningEffort("qwen3:8b"), "Auto：本地一定发");
        TestRunner.Equal("none", local.BuildPayload(request)["reasoning_effort"]!.GetValue<string>(), "本地最低档 = none");

        // 手动覆盖
        using var always = new OpenAiCompatibleClient("https://api.openai.com/v1", "sk-x", reasoningEffort: AiReasoningEffortMode.Always);
        using var never = new OpenAiCompatibleClient("http://127.0.0.1:11434/v1", reasoningEffort: AiReasoningEffortMode.Never);

        TestRunner.Check(always.ShouldSendReasoningEffort("gpt-4o-mini"), "总是发送：云端普通模型也发");
        TestRunner.Check(!never.ShouldSendReasoningEffort("qwen3:8b"), "从不发送：本地也不发");
        TestRunner.Check(never.BuildPayload(request)["reasoning_effort"] == null, "从不发送时请求体里没有该字段");

        // 默认值：Bearer + Auto + 不使用自定义代理
        var settings = new AppSettings();
        TestRunner.Equal(AiAuthMode.Bearer, settings.AiAuthMode, "默认鉴权方式是 Bearer（填了密钥就能用云端）");
        TestRunner.Equal(AiReasoningEffortMode.Auto, settings.AiReasoningEffort, "默认自动决定是否发 reasoning_effort");
        TestRunner.Check(settings.AiUseSystemProxy, "默认跟随系统代理");
        TestRunner.Equal(string.Empty, settings.AiProxyUrl, "默认没有自定义代理");

        using var custom = new OpenAiCompatibleClient("https://api.openai.com/v1", "sk-x", useSystemProxy: false, proxyUrl: "http://127.0.0.1:7890");
        TestRunner.Check(custom.IsLocal == false, "自定义代理不影响地址判定");

        var clone = new AppSettings { AiProxyUrl = "http://127.0.0.1:7890", AiUseSystemProxy = false, AiAuthMode = AiAuthMode.ApiKeyHeader }.Clone();
        TestRunner.Equal("http://127.0.0.1:7890", clone.AiProxyUrl, "Clone 带上代理地址");
        TestRunner.Check(!clone.AiUseSystemProxy, "Clone 带上系统代理开关");
        TestRunner.Equal(AiAuthMode.ApiKeyHeader, clone.AiAuthMode, "Clone 带上鉴权方式");
    }

    // ==================== 错误提示 ====================

    private static void Errors()
    {
        using var cloud = new OpenAiCompatibleClient("https://api.openai.com/v1", "sk-x");
        using var local = new OpenAiCompatibleClient("http://127.0.0.1:11434/v1");

        TestRunner.Check(cloud.DescribeFailure(401, "{\"error\":{\"message\":\"Invalid API key\"}}").Contains("密钥", StringComparison.Ordinal), "401 提示检查密钥");
        TestRunner.Check(cloud.DescribeFailure(401, "{}").Contains("设置", StringComparison.Ordinal), "401 提示去设置里填密钥");
        TestRunner.Check(cloud.DescribeFailure(403, "{}").Contains("权限", StringComparison.Ordinal), "403 提示权限");
        TestRunner.Check(cloud.DescribeFailure(404, "{}").Contains("模型名", StringComparison.Ordinal), "404 提示检查地址与模型名");
        TestRunner.Check(cloud.DescribeFailure(429, "{}").Contains("限流", StringComparison.Ordinal), "429 提示限流");
        TestRunner.Check(cloud.DescribeFailure(503, "{}").Contains("稍后重试", StringComparison.Ordinal), "5xx 提示稍后重试");
        TestRunner.Check(cloud.DescribeFailure(401, "{\"error\":\"Incorrect API key provided\"}").Contains("Incorrect API key", StringComparison.Ordinal), "错误提示里带上服务端说明");
        TestRunner.Check(cloud.DescribeFailure(400, "{\"error\":{\"message\":\"unknown field\"}}").Contains("unknown field", StringComparison.Ordinal), "400 也带上服务端说明");

        var missing = new OpenAiCompatibleClient("https://api.openai.com/v1", "env:SCHEDULEAPP_NOT_DEFINED_KEY");
        TestRunner.Check(missing.DescribeFailure(401, "{}").Contains("环境变量", StringComparison.Ordinal), "env: 密钥缺失时提示环境变量");

        TestRunner.Check(cloud.DescribeNetworkFailure(new HttpRequestException("TLS 握手失败")).Contains("代理", StringComparison.Ordinal), "云端网络失败提示代理");
        TestRunner.Check(local.DescribeNetworkFailure(new HttpRequestException("拒绝连接")).Contains("ollama", StringComparison.OrdinalIgnoreCase), "本地网络失败提示启动 ollama");
        TestRunner.Check(cloud.DescribeTarget().StartsWith("云端", StringComparison.Ordinal), "云端目标描述");
        TestRunner.Check(local.DescribeTarget().StartsWith("本地", StringComparison.Ordinal), "本地目标描述");

        using var withLongKey = new OpenAiCompatibleClient("https://api.openai.com/v1", "sk-1234567890abcdef");
        TestRunner.Check(withLongKey.DescribeTarget().Contains("sk-1", StringComparison.Ordinal), "目标描述带上打码后的密钥头");
        TestRunner.Check(!withLongKey.DescribeTarget().Contains("567890", StringComparison.Ordinal), "目标描述不泄露密钥中段");
        TestRunner.Check(withLongKey.DescribeTarget().Contains("云端", StringComparison.Ordinal), "带密钥时仍标明是云端");
    }
}
