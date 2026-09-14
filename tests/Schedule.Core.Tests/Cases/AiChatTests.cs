using Schedule.Core.Ai;
using Schedule.Core.Api;
using Schedule.Core.Models;

namespace Schedule.Core.Tests.Cases;

/// <summary>
/// AI 对话：地址与协议判定、请求拼装、响应解析、工具目录与参数校验、会话循环、引用编号表，
/// 以及用真实 HTTP（本地假模型服务）跑通的端到端往返。
/// </summary>
public static class AiChatTests
{
    public static void Run()
    {
        TestRunner.Section("AI 对话");
        UrlAndMode();
        Payloads();
        Replies();
        Models();
        Tools();
        Session();
        Refs();
        HttpRoundTrip();
        Settings();
    }

    // ==================== 地址与协议 ====================

    private static void UrlAndMode()
    {
        TestRunner.Equal("http://127.0.0.1:11434/v1", OpenAiCompatibleClient.Normalize("127.0.0.1:11434"), "裸主机端口自动补 http 与 /v1");
        TestRunner.Equal("http://127.0.0.1:11434/v1", OpenAiCompatibleClient.Normalize("http://127.0.0.1:11434/v1/"), "去掉末尾斜杠");
        TestRunner.Equal("http://127.0.0.1:11434/v1", OpenAiCompatibleClient.Normalize("http://127.0.0.1:11434/v1/chat/completions"), "粘贴完整路径也能回退到根");
        TestRunner.Equal("http://127.0.0.1:1234/v1", OpenAiCompatibleClient.Normalize("http://127.0.0.1:1234"), "LM Studio 默认端口");
        TestRunner.Equal(OpenAiCompatibleClient.DefaultBaseUrl, OpenAiCompatibleClient.Normalize("   "), "空地址用默认值");
        TestRunner.Equal("https://api.example.com/v1", OpenAiCompatibleClient.Normalize("https://api.example.com/v1/models"), "已含 /v1 的地址不再重复追加");

        TestRunner.Equal("http://127.0.0.1:11434", OpenAiCompatibleClient.OllamaRoot("http://127.0.0.1:11434/v1"), "Ollama 根地址去掉 /v1");
        TestRunner.Equal("http://127.0.0.1:11434", OpenAiCompatibleClient.OllamaRoot("http://127.0.0.1:11434/api"), "用户直接写 /api 也认");
        TestRunner.Equal("http://127.0.0.1:11434", OpenAiCompatibleClient.OllamaRoot("127.0.0.1:11434"), "裸地址也能得到根");

        TestRunner.Equal(AiApiMode.Ollama, OpenAiCompatibleClient.ResolveMode(AiApiMode.Auto, "http://127.0.0.1:11434/v1"), "自动识别 Ollama");
        TestRunner.Equal(AiApiMode.OpenAi, OpenAiCompatibleClient.ResolveMode(AiApiMode.Auto, "http://127.0.0.1:1234/v1"), "自动识别 OpenAI 兼容");
        TestRunner.Equal(AiApiMode.OpenAi, OpenAiCompatibleClient.ResolveMode(AiApiMode.OpenAi, "http://127.0.0.1:11434/v1"), "显式指定优先于自动");
        TestRunner.Equal(AiApiMode.Ollama, OpenAiCompatibleClient.ResolveMode(AiApiMode.Ollama, "http://127.0.0.1:9999/v1"), "显式 Ollama");

        using var client = new OpenAiCompatibleClient("http://127.0.0.1:11434", mode: AiApiMode.Auto);
        TestRunner.Check(client.IsOllama, "11434 默认走 Ollama 原生协议");
        TestRunner.Equal("http://127.0.0.1:11434", client.OllamaRootUrl, "客户端记下 Ollama 根地址");
    }

    // ==================== 请求体 ====================

    private static void Payloads()
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.System("你是助手 /no_think"),
            ChatMessage.User("今天有什么日程？"),
            ChatMessage.Assistant(string.Empty, null, new List<AiToolCall> { new() { Id = "call_1", Name = "list_items", Arguments = "{\"date\":\"今天\"}" } }),
            ChatMessage.ToolResult(new AiToolCall { Id = "call_1", Name = "list_items" }, "{\"count\":0}"),
        };

        var request = new AiChatRequest
        {
            Model = "qwen3:8b",
            Messages = messages,
            Tools = AiToolCatalog.Definitions.ToList(),
            Temperature = 0.2,
            MaxTokens = 512,
            DisableThinking = true,
        };

        using var openai = new OpenAiCompatibleClient("http://127.0.0.1:1234/v1");
        var openAiBody = openai.BuildPayload(request);

        TestRunner.Equal("qwen3:8b", openAiBody["model"]!.GetValue<string>(), "OpenAI 请求带 model");
        TestRunner.Equal(false, openAiBody["stream"]!.GetValue<bool>(), "OpenAI 请求 stream=false");
        TestRunner.Equal(512, openAiBody["max_tokens"]!.GetValue<int>(), "OpenAI 请求带 max_tokens");
        TestRunner.Equal(9, openAiBody["tools"]!.AsArray().Count, "OpenAI 请求带上全部工具");
        TestRunner.Equal("function", openAiBody["tools"]!.AsArray()[0]!["type"]!.GetValue<string>(), "工具用 function 形状");
        TestRunner.Check(openAiBody["tools"]!.AsArray()[0]!["function"]!["parameters"]!["properties"] != null, "工具带 JSON Schema 参数");
        TestRunner.Check(openAiBody["think"] == null, "OpenAI 服务端不传 think 字段");

        var wire = openAiBody["messages"]!.AsArray();
        TestRunner.Equal(4, wire.Count, "消息按顺序写入");
        TestRunner.Equal("system", wire[0]!["role"]!.GetValue<string>(), "第一条是 system");
        TestRunner.Equal("tool", wire[3]!["role"]!.GetValue<string>(), "工具结果用 tool 角色");
        TestRunner.Equal("call_1", wire[3]!["tool_call_id"]!.GetValue<string>(), "工具结果带 tool_call_id");
        TestRunner.Equal("call_1", wire[2]!["tool_calls"]!.AsArray()[0]!["id"]!.GetValue<string>(), "助手消息带 tool_calls");

        using var ollama = new OpenAiCompatibleClient("http://127.0.0.1:11434/v1");
        var native = ollama.BuildOllamaPayload(request);

        TestRunner.Equal(false, native["think"]!.GetValue<bool>(), "Ollama 原生请求带 think=false（关掉思维链）");
        TestRunner.Equal(0.2, native["options"]!["temperature"]!.GetValue<double>(), "Ollama 温度放在 options 里");
        TestRunner.Equal(512, native["options"]!["num_predict"]!.GetValue<int>(), "Ollama 输出上限用 num_predict");
        TestRunner.Equal(9, native["tools"]!.AsArray().Count, "Ollama 请求同样带工具");

        var nativeArguments = native["messages"]!.AsArray()[2]!["tool_calls"]!.AsArray()[0]!["function"]!["arguments"]!;
        TestRunner.Check(
            nativeArguments.GetValueKind() == System.Text.Json.JsonValueKind.Object,
            "Ollama 原生工具参数必须是对象（不能是字符串）");
        TestRunner.Equal("今天", nativeArguments["date"]!.GetValue<string>(), "原生参数内容保持");
        TestRunner.Equal("list_items", native["messages"]!.AsArray()[3]!["tool_name"]!.GetValue<string>(), "原生工具结果带 tool_name");

        var noThinking = new AiChatRequest { Model = "m", Messages = new List<ChatMessage>(), DisableThinking = false };
        TestRunner.Check(ollama.BuildOllamaPayload(noThinking)["think"] == null, "开启思考时不传 think（避免不支持思考的模型报错）");
    }
    // ==================== 响应解析 ====================

    private static void Replies()
    {
        const string openAiReply = """
        {"id":"x","choices":[{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":"",
        "reasoning_content":"先查一下","tool_calls":[{"id":"call_a","type":"function","function":{"name":"list_items","arguments":"{\"date\":\"今天\"}"}}]}}],
        "usage":{"prompt_tokens":11,"completion_tokens":7}}
        """;

        var parsed = OpenAiCompatibleClient.ParseReply(openAiReply);
        TestRunner.Equal(string.Empty, parsed.Content, "OpenAI 回复正文为空时保持空");
        TestRunner.Equal("先查一下", parsed.Reasoning, "reasoning_content 被识别为思考过程");
        TestRunner.Equal("tool_calls", parsed.FinishReason, "finish_reason 解析");
        TestRunner.Equal(1, parsed.ToolCalls.Count, "解析出 1 个工具调用");
        TestRunner.Equal("list_items", parsed.ToolCalls[0].Name, "工具名");
        TestRunner.Equal("call_a", parsed.ToolCalls[0].Id, "工具调用 id");
        TestRunner.Equal(11, parsed.PromptTokens, "prompt token");
        TestRunner.Equal(7, parsed.CompletionTokens, "completion token");

        var inline = OpenAiCompatibleClient.ParseReply("""
        {"choices":[{"message":{"content":"<think>想一下</think>好的，没问题。"}}]}
        """);
        TestRunner.Equal("好的，没问题。", inline.Content, "内联 think 块从正文里剥离");
        TestRunner.Equal("想一下", inline.Reasoning, "内联 think 块进思考过程");

        const string nativeReply = """
        {"model":"qwen3:8b","message":{"role":"assistant","content":"","thinking":"","tool_calls":[
        {"function":{"name":"list_items","arguments":{"date":"今天"}}}]},"done_reason":"stop","prompt_eval_count":9,"eval_count":3}
        """;

        var native = OpenAiCompatibleClient.ParseOllamaReply(nativeReply);
        TestRunner.Equal(1, native.ToolCalls.Count, "原生回复解析出工具调用");
        TestRunner.Equal("{\"date\":\"今天\"}", native.ToolCalls[0].Arguments, "原生参数对象被转成 JSON 文本");
        TestRunner.Equal("stop", native.FinishReason, "done_reason 解析");
        TestRunner.Equal(9, native.PromptTokens, "原生 prompt_eval_count");
        TestRunner.Equal(3, native.CompletionTokens, "原生 eval_count");
        TestRunner.Check(native.Reasoning == null, "空的 thinking 不产生思考过程");

        var nativeText = OpenAiCompatibleClient.ParseOllamaReply("""
        {"message":{"role":"assistant","content":"今天没有日程。"},"done_reason":"stop"}
        """);
        TestRunner.Equal("今天没有日程。", nativeText.Content, "原生正文解析");

        TestRunner.Check(Throws(() => OpenAiCompatibleClient.ParseReply("不是 JSON")), "非法 JSON 抛 AiChatException");
        TestRunner.Check(Throws(() => OpenAiCompatibleClient.ParseReply("{\"foo\":1}")), "缺少 choices 抛异常");
        TestRunner.Check(Throws(() => OpenAiCompatibleClient.ParseOllamaReply("{\"error\":\"model not found\"}")), "原生错误字段抛异常");

        TestRunner.Equal("模型不存在", OpenAiCompatibleClient.ExtractError("{\"error\":{\"message\":\"模型不存在\"}}"), "提取 OpenAI 风格错误");
        TestRunner.Equal("挂了", OpenAiCompatibleClient.ExtractError("{\"error\":\"挂了\"}"), "提取字符串错误");
        TestRunner.Check(OpenAiCompatibleClient.ExtractError("{}") == null, "没有错误时返回 null");
        TestRunner.Equal("abc", OpenAiCompatibleClient.Shorten("abc"), "短文本不截断");
        TestRunner.Check(OpenAiCompatibleClient.Shorten(new string('x', 400)).EndsWith("…", StringComparison.Ordinal), "长文本截断");
    }

    // ==================== 模型列表 ====================

    private static void Models()
    {
        var openAi = OpenAiCompatibleClient.ParseModels("""
        {"object":"list","data":[{"id":"qwen3:8b","owned_by":"library"},{"id":"llama3:8b"}]}
        """);
        TestRunner.Equal(2, openAi.Count, "OpenAI 模型列表条数");
        TestRunner.Equal("llama3:8b", openAi[0].Id, "模型列表按名字排序");
        TestRunner.Equal("library", openAi[1].OwnedBy, "带 owned_by");

        var native = OpenAiCompatibleClient.ParseModels("""
        {"models":[{"name":"qwen3:8b","size":5234567890,"details":{"parameter_size":"8.2B","quantization_level":"Q4_K_M"}}]}
        """);
        TestRunner.Equal(1, native.Count, "Ollama 模型列表条数");
        TestRunner.Equal("qwen3:8b", native[0].Id, "Ollama 用 name 作为 id");
        TestRunner.Equal("8.2B Q4_K_M", native[0].Detail, "Ollama 参数量 / 量化信息");

        TestRunner.Check(Throws(() => OpenAiCompatibleClient.ParseModels("{\"data\":[]}")), "空模型列表抛异常");

        var dedup = OpenAiCompatibleClient.ParseModels("{\"data\":[{\"id\":\"a\"},{\"id\":\"A\"}]}");
        TestRunner.Equal(1, dedup.Count, "忽略大小写去重");
    }

    // ==================== 工具目录 ====================

    private static void Tools()
    {
        TestRunner.Equal(9, AiToolCatalog.Definitions.Count, "内置工具数量");
        TestRunner.Equal(
            AiToolCatalog.Definitions.Count,
            AiToolCatalog.Definitions.Select(d => d.Name).Distinct().Count(),
            "工具名不重复");

        foreach (var tool in AiToolCatalog.Definitions)
        {
            TestRunner.Check(tool.Description.Length > 5, tool.Name + " 有说明（模型靠它决定何时调用）");

            using var doc = System.Text.Json.JsonDocument.Parse(tool.ParametersJson);
            var root = doc.RootElement;
            TestRunner.Equal("object", root.GetProperty("type").GetString(), tool.Name + " 的参数 schema 是 object");
            TestRunner.Check(
                root.TryGetProperty("properties", out var properties) && properties.ValueKind == System.Text.Json.JsonValueKind.Object,
                tool.Name + " 的参数 schema 有 properties");
        }

        var fake = new RecordingActions();

        var list = AiToolCatalog.Execute("list_items", "{\"date\":\"今天\",\"limit\":\"5\"}", fake);
        TestRunner.Check(!list.IsError, "list_items 正常执行");
        TestRunner.Equal("今天", fake.LastQuery!.Date, "date 参数传入");
        TestRunner.Equal(5, fake.LastQuery.Limit, "字符串数字也能解析");

        AiToolCatalog.Execute("list_items", "{\"date\":\"今天\"}", fake);
        TestRunner.Equal(20, fake.LastQuery!.Limit, "limit 缺省 20");

        AiToolCatalog.Execute("list_items", "{\"date\":\"今天\",\"limit\":999}", fake);
        TestRunner.Equal(100, fake.LastQuery!.Limit, "limit 上限被夹到 100（避免一次灌爆上下文）");

        var created = AiToolCatalog.Execute("create_item", """
        {"title":"客户沟通","start":"2026-09-15 15:00","reminderOffsets":[30,"5"],"allDay":"false"}
        """, fake);
        TestRunner.Check(!created.IsError, "create_item 正常执行");
        TestRunner.Equal("客户沟通", fake.LastDraft!.Title, "标题传入");
        TestRunner.Equal(2, fake.LastDraft.ReminderOffsets!.Count, "档位数组混合类型也能解析");
        TestRunner.Equal(false, fake.LastDraft.AllDay, "字符串布尔也能解析");
        TestRunner.Equal("2026-09-15 15:00", fake.LastDraft.Start, "开始时间原样传入（由宿主宽容解析）");

        TestRunner.Check(AiToolCatalog.Execute("create_item", "{\"start\":\"2026-09-15 15:00\"}", fake).IsError, "缺 title 报错");
        TestRunner.Check(AiToolCatalog.Execute("create_item", "{\"title\":\"x\"}", fake).IsError, "缺 start 报错");
        TestRunner.Check(AiToolCatalog.Execute("update_item", "{}", fake).IsError, "update_item 缺 id 报错");
        TestRunner.Check(AiToolCatalog.Execute("update_item", "{\"id\":\"#1\"}", fake).IsError, "update_item 没给字段报错");
        TestRunner.Check(AiToolCatalog.Execute("delete_item", "{}", fake).IsError, "delete_item 缺 id 报错");
        TestRunner.Check(AiToolCatalog.Execute("reschedule_item", "{\"id\":\"#1\"}", fake).IsError, "reschedule_item 缺新时间报错");
        TestRunner.Check(AiToolCatalog.Execute("set_theme", "{}", fake).IsError, "set_theme 缺参数报错");
        TestRunner.Check(AiToolCatalog.Execute("list_items", "{}", fake).IsError, "list_items 无查询条件报错");
        TestRunner.Check(AiToolCatalog.Execute("nope", "{}", fake).IsError, "未知工具报错");
        TestRunner.Check(AiToolCatalog.Execute("list_items", "不是 JSON", fake).IsError, "非法参数 JSON 报错");
        TestRunner.Check(AiToolCatalog.Execute("list_items", "[1,2]", fake).IsError, "参数不是对象报错");
        TestRunner.Check(AiToolCatalog.Execute("", "{}", fake).IsError, "空工具名报错");

        var unknown = AiToolCatalog.Execute("nope", "{}", fake);
        TestRunner.Check(unknown.Content.Contains("没有名为", StringComparison.Ordinal), "错误里说明工具不存在");
        TestRunner.Check(unknown.Content.Contains("list_items", StringComparison.Ordinal), "错误里列出可用工具");

        AiToolCatalog.Execute("set_status", "{\"id\":\"#1\",\"status\":\"done\"}", fake);
        TestRunner.Equal("#1", fake.LastReference, "引用原样传给宿主（编号由宿主解析）");
        TestRunner.Check(!AiToolCatalog.Execute("next_reminders", "{\"count\":1}", fake).IsError, "next_reminders 正常执行");
        TestRunner.Check(!AiToolCatalog.Execute("app_info", "{}", fake).IsError, "app_info 正常执行");

        // 参数读取的宽容度
        using var args = System.Text.Json.JsonDocument.Parse("""
        {"s":"x","n":3,"ns":"4","b":true,"bs":"true","bn":0,"arr":[1,"2"],"obj":{"a":1},"nul":null}
        """);
        var element = args.RootElement;

        TestRunner.Equal("x", AiToolCatalog.Text(element, "s"), "字符串读取");
        TestRunner.Equal("3", AiToolCatalog.Text(element, "n"), "数字当字符串读取");
        TestRunner.Check(AiToolCatalog.Text(element, "missing") == null, "缺字段返回 null");
        TestRunner.Check(AiToolCatalog.Text(element, "obj") == null, "对象不当字符串用");
        TestRunner.Equal(3, AiToolCatalog.Number(element, "n"), "数字读取");
        TestRunner.Equal(4, AiToolCatalog.Number(element, "ns"), "字符串数字读取");
        TestRunner.Check(AiToolCatalog.Number(element, "s") == null, "非数字返回 null");
        TestRunner.Equal(true, AiToolCatalog.Bool(element, "b"), "布尔读取");
        TestRunner.Equal(true, AiToolCatalog.Bool(element, "bs"), "字符串布尔读取");
        TestRunner.Equal(false, AiToolCatalog.Bool(element, "bn"), "数字 0 当 false");
        TestRunner.Equal(2, AiToolCatalog.IntList(element, "arr")!.Count, "数组读取");
        TestRunner.Check(AiToolCatalog.IntList(element, "s") == null, "非数组返回 null");
    }
    // ==================== 会话循环 ====================

    private static void Session()
    {
        // 1) 模型直接回答，不调工具
        var plain = new FakeClient(_ => Reply("今天没有日程。"));
        var session = new AiSession(plain, new RecordingActions());
        var turn = session.SendAsync("今天有什么日程？", null, CancellationToken.None).GetAwaiter().GetResult();

        TestRunner.Equal("今天没有日程。", turn.Text, "直接回答的文本");
        TestRunner.Equal(0, turn.Tools.Count, "没有工具调用");
        TestRunner.Equal(1, turn.Rounds, "一轮完成");
        TestRunner.Equal(2, session.History.Count, "历史里有用户与助手两条");
        TestRunner.Equal(9, plain.LastRequest!.Tools.Count, "请求带上全部工具");
        TestRunner.Equal(string.Empty, plain.LastRequest.Model, "模型名来自会话选项（未设置时为空由客户端兜底报错）");
        TestRunner.Check(plain.LastRequest.Messages[0].Role == ChatRole.System, "第一条是系统提示");
        TestRunner.Check(session.BuildSystemPrompt().Contains("/no_think", StringComparison.Ordinal), "默认提示带 /no_think（关思考）");

        var thinking = new AiSession(new FakeClient(_ => Reply("好")), new RecordingActions(),
            new AiSessionOptions { DisableThinking = false, Now = new DateTime(2026, 9, 14, 10, 0, 0) });
        TestRunner.Check(!thinking.BuildSystemPrompt().Contains("/no_think", StringComparison.Ordinal), "开启思考时不加 /no_think");
        TestRunner.Check(thinking.BuildSystemPrompt().Contains("2026-09-14", StringComparison.Ordinal), "系统提示带当前时间");
        TestRunner.Check(thinking.BuildSystemPrompt().Contains("星期一", StringComparison.Ordinal), "系统提示带星期");

        var custom = new AiSession(new FakeClient(_ => Reply("好")), new RecordingActions(),
            new AiSessionOptions { SystemPrompt = "自定义提示" });
        TestRunner.Equal("自定义提示", custom.BuildSystemPrompt(), "自定义系统提示优先生效");

        // 2) 工具往返：模型 → 工具 → 模型
        var actions = new RecordingActions();
        var queue = new Queue<AiChatReply>(new[]
        {
            ToolReply("list_items", "{\"date\":\"今天\"}"),
            Reply("今天有 1 条日程：客户沟通。"),
        });

        var tooled = new AiSession(new FakeClient(_ => queue.Dequeue()), actions);
        var toolTurn = tooled.SendAsync("今天有什么日程？", null, CancellationToken.None).GetAwaiter().GetResult();

        TestRunner.Equal("今天有 1 条日程：客户沟通。", toolTurn.Text, "工具执行后模型给出总结");
        TestRunner.Equal(1, toolTurn.Tools.Count, "记录了一次工具执行");
        TestRunner.Equal("list_items", toolTurn.Tools[0].Name, "工具名记录");
        TestRunner.Check(!toolTurn.Tools[0].IsError, "工具执行成功");
        TestRunner.Equal("查到 1 条日程", toolTurn.Tools[0].Summary, "工具结果摘要");
        TestRunner.Equal("今天", actions.LastQuery!.Date, "工具真的被调用并拿到参数");
        TestRunner.Equal(4, tooled.History.Count, "历史：用户 + 助手(工具) + 工具结果 + 助手");
        TestRunner.Equal(ChatRole.Tool, tooled.History[2].Role, "第三条是工具结果");
        TestRunner.Equal("call_0", tooled.History[2].ToolCallId, "工具结果对上调用 id");
        TestRunner.Equal(2, toolTurn.Rounds, "两轮完成");

        // 3) 正文兜底：模型没走原生工具调用，而是把调用写进 JSON
        var fallbackQueue = new Queue<AiChatReply>(new[]
        {
            Reply("我先查一下。\n```json\n{\"action\":\"list_items\",\"date\":\"今天\"}\n```"),
            Reply("今天有 1 条日程。"),
        });

        var fallback = new AiSession(new FakeClient(_ => fallbackQueue.Dequeue()), actions);
        var fallbackTurn = fallback.SendAsync("看看今天", null, CancellationToken.None).GetAwaiter().GetResult();

        TestRunner.Check(fallbackTurn.UsedTextFallback, "标记为正文兜底解析");
        TestRunner.Equal(1, fallbackTurn.Tools.Count, "兜底也能执行工具");
        TestRunner.Equal("今天有 1 条日程。", fallbackTurn.Text, "兜底后模型继续给出总结");

        // 4) 工具轮数上限：模型一直要调工具时必须能停下来
        var always = new FakeClient(_ => ToolReply("list_items", "{\"date\":\"今天\"}"));
        var limited = new AiSession(always, actions, new AiSessionOptions { MaxToolRounds = 3 });
        var limitedTurn = limited.SendAsync("查一下", null, CancellationToken.None).GetAwaiter().GetResult();

        TestRunner.Check(limitedTurn.HitToolRoundLimit, "达到工具轮数上限会停下");
        TestRunner.Equal(3, limitedTurn.Tools.Count, "三轮工具都执行了");
        TestRunner.Equal(3, always.CallCount, "不会无限循环");

        // 5) 上下文裁剪
        var trimmer = new AiSession(new FakeClient(_ => Reply("好")), actions, new AiSessionOptions { MaxHistoryMessages = 4 });
        for (var i = 0; i < 5; i++) trimmer.SendAsync("第" + i + "条", null, CancellationToken.None).GetAwaiter().GetResult();

        TestRunner.Check(trimmer.History.Count <= 4, "历史被裁剪到上限内");
        TestRunner.Check(trimmer.History[0].Role != ChatRole.Tool, "裁剪后不会以工具结果开头");

        // 6) 取消与异常向上传播
        TestRunner.Check(
            Throws(() => new AiSession(new ThrowingClient(new OperationCanceledException()), actions)
                .SendAsync("x", null, CancellationToken.None).GetAwaiter().GetResult()),
            "取消会向上抛出");
        TestRunner.Check(
            Throws(() => new AiSession(new ThrowingClient(new AiChatException("连不上模型服务")), actions)
                .SendAsync("x", null, CancellationToken.None).GetAwaiter().GetResult()),
            "模型异常向上抛出");

        // 7) 进度回调（工具往返时界面要能显示"在干什么"）
        var progress = new CollectingProgress();
        var progressQueue = new Queue<AiChatReply>(new[]
        {
            ToolReply("list_items", "{\"date\":\"今天\"}"),
            Reply("好的"),
        });

        var progressed = new AiSession(new FakeClient(_ => progressQueue.Dequeue()), actions);
        progressed.SendAsync("看看", progress, CancellationToken.None).GetAwaiter().GetResult();

        TestRunner.Check(progress.Stages.Count >= 3, "工具往返会上报多次进度（请求 / 执行工具 / 继续）");
        TestRunner.Check(progress.Stages.Any(s => s.Contains("list_items", StringComparison.Ordinal)), "进度里包含正在执行的工具名");

        // 8) 清空
        progressed.Clear();
        TestRunner.Equal(0, progressed.History.Count, "清空历史");
    }

    // ==================== 引用编号表 ====================

    private static void Refs()
    {
        var map = new AiItemReferences();
        var id = Guid.NewGuid().ToString();
        var occurrence = new DateTime(2026, 9, 15, 15, 0, 0);

        TestRunner.Equal("#1", map.Register(id, occurrence, "客户沟通"), "第一个目标是 #1");
        TestRunner.Equal("#1", map.Register(id, occurrence, "客户沟通"), "同一目标复用编号");
        TestRunner.Equal("#2", map.Register(id, null, "客户沟通"), "同一条日程的另一天是另一个编号");
        TestRunner.Equal(2, map.Count, "两个编号");

        var target = map.TryResolve("#1");
        TestRunner.Check(target != null && target.Id == id, "#1 解析回日程 id");
        TestRunner.Equal(occurrence, target!.OccurrenceStart, "带上了「这一次」的原始开始时间");
        TestRunner.Check(!map.TryResolve("#2")!.OccurrenceStart.HasValue, "整条日程的引用没有发生时间");
        TestRunner.Check(map.TryResolve("1")!.Id == id, "不带 # 也能解析");
        TestRunner.Check(map.TryResolve(id)!.Id == id, "直接给 GUID 也能解析");
        TestRunner.Check(map.TryResolve("\"#1\"")!.Id == id, "带引号也能解析（模型常这么写）");
        TestRunner.Check(map.TryResolve("#9") == null, "不存在的编号解析失败");
        TestRunner.Check(map.TryResolve("随便写的标题") == null, "非 GUID 文本解析失败");
        TestRunner.Check(map.TryResolve(null) == null, "空引用安全");

        map.Reset();
        TestRunner.Equal(0, map.Count, "Reset 清空");
        TestRunner.Check(map.DescribeUnknown("#1").Contains("list_items", StringComparison.Ordinal), "未知引用提示先查询");
    }
    // ==================== 真实 HTTP 端到端（本地假模型服务） ====================

    private static void HttpRoundTrip()
    {
        var requests = new List<string>();
        var chatCalls = 0;

        using var server = new MiniHttpServer(0, request =>
        {
            requests.Add(request.Path + " " + request.Body);

            if (request.Path.EndsWith("/api/tags", StringComparison.Ordinal))
            {
                return ApiResponse.Text(
                    "{\"models\":[{\"name\":\"qwen3:8b\",\"details\":{\"parameter_size\":\"8.2B\",\"quantization_level\":\"Q4_K_M\"}}]}",
                    200, "application/json");
            }

            if (request.Path.EndsWith("/api/chat", StringComparison.Ordinal))
            {
                chatCalls++;

                return ApiResponse.Text(chatCalls == 1
                        ? "{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"list_items\",\"arguments\":{\"date\":\"今天\"}}}]},\"done_reason\":\"stop\"}"
                        : "{\"message\":{\"role\":\"assistant\",\"content\":\"今天有一条日程：客户沟通。\"},\"done_reason\":\"stop\",\"prompt_eval_count\":12,\"eval_count\":8}",
                    200, "application/json");
            }

            if (request.Path.EndsWith("/v1/chat/completions", StringComparison.Ordinal))
            {
                return ApiResponse.Text(
                    "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"好的\"}}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":4}}",
                    200, "application/json");
            }

            return ApiResponse.Error(404, "not found");
        });

        if (!server.Start(out var error))
        {
            TestRunner.Check(false, "假模型服务启动失败：" + error);
            return;
        }

        var baseUrl = "http://127.0.0.1:" + server.Port;
        var actions = new RecordingActions();

        using (var client = new OpenAiCompatibleClient(baseUrl, mode: AiApiMode.Ollama))
        {
            var models = client.ListModelsAsync(CancellationToken.None).GetAwaiter().GetResult();
            TestRunner.Equal(1, models.Count, "端到端：模型列表");
            TestRunner.Equal("qwen3:8b", models[0].Id, "端到端：模型名");
            TestRunner.Equal("8.2B Q4_K_M", models[0].Detail, "端到端：模型规模信息");

            var session = new AiSession(client, actions, new AiSessionOptions { Model = "qwen3:8b" });
            var turn = session.SendAsync("今天有什么日程？", null, CancellationToken.None).GetAwaiter().GetResult();

            TestRunner.Equal("今天有一条日程：客户沟通。", turn.Text, "端到端：最终回复");
            TestRunner.Equal(1, turn.Tools.Count, "端到端：工具调用次数");
            TestRunner.Equal("今天", actions.LastQuery!.Date, "端到端：工具参数来自模型");
            TestRunner.Equal(12, turn.PromptTokens, "端到端：token 统计");
        }

        var chatBodies = requests.Where(r => r.Contains("/api/chat", StringComparison.Ordinal)).ToList();
        TestRunner.Equal(2, chatBodies.Count, "端到端：两次模型请求（先工具后总结）");
        TestRunner.Check(chatBodies[0].Contains("\"think\":false", StringComparison.Ordinal), "端到端：请求关掉思考");
        TestRunner.Check(chatBodies[0].Contains("\"tools\":[", StringComparison.Ordinal), "端到端：请求带工具定义");
        TestRunner.Check(chatBodies[0].Contains("今天有什么日程？", StringComparison.Ordinal), "端到端：中文用户消息按 UTF-8 发送");
        TestRunner.Check(chatBodies[1].Contains("\"date\":\"今天\"", StringComparison.Ordinal), "端到端：中文工具参数按 UTF-8 回传（没有被转义成 \\uXXXX 浪费 token）");
        TestRunner.Check(chatBodies[1].Contains("\"role\":\"tool\"", StringComparison.Ordinal), "端到端：第二轮把工具结果回传给模型");
        TestRunner.Check(chatBodies[1].Contains("list_items", StringComparison.Ordinal), "端到端：工具结果里带工具名");

        using (var client = new OpenAiCompatibleClient(baseUrl, mode: AiApiMode.OpenAi))
        {
            var reply = client.CompleteAsync(
                new AiChatRequest { Model = "m", Messages = new List<ChatMessage> { ChatMessage.User("hi") } },
                CancellationToken.None).GetAwaiter().GetResult();

            TestRunner.Equal("好的", reply.Content, "OpenAI 路径端到端回复");
            TestRunner.Equal(3, reply.PromptTokens, "OpenAI 路径 token");
        }

        TestRunner.Check(requests.Any(r => r.StartsWith("/v1/chat/completions", StringComparison.Ordinal)), "OpenAI 路径打到 /v1/chat/completions");

        using (var dead = new OpenAiCompatibleClient("http://127.0.0.1:1", mode: AiApiMode.Ollama))
        {
            TestRunner.Check(
                Throws(() => dead.CompleteAsync(
                    new AiChatRequest { Model = "m", Messages = new List<ChatMessage>() },
                    CancellationToken.None).GetAwaiter().GetResult()),
                "连不上模型服务时抛 AiChatException");
        }

        TestRunner.Check(
            Throws(() => new OpenAiCompatibleClient(baseUrl, mode: AiApiMode.Ollama)
                .CompleteAsync(new AiChatRequest { Model = string.Empty, Messages = new List<ChatMessage>() }, CancellationToken.None)
                .GetAwaiter().GetResult()),
            "没选模型时给出明确错误");
    }

    // ==================== 设置 ====================

    private static void Settings()
    {
        var settings = new AppSettings();

        TestRunner.Equal("http://127.0.0.1:11434/v1", settings.AiBaseUrl, "默认 AI 服务地址指向本机 Ollama");
        TestRunner.Equal("qwen3:8b", settings.AiModel, "默认模型 qwen3:8b");
        TestRunner.Equal(AiApiMode.Auto, settings.AiApiMode, "默认协议自动判断");
        TestRunner.Check(settings.AiDisableThinking, "默认关闭思考（本地模型更快）");
        TestRunner.Equal(0, settings.AiMaxTokens, "默认不限制输出 token");
        TestRunner.Equal(180, settings.AiTimeoutSeconds, "默认超时 180 秒");

        settings.AiModel = "qwen3:14b";
        settings.AiApiMode = AiApiMode.OpenAi;
        settings.AiTemperature = 0.7;
        settings.AiMaxTokens = 1024;
        settings.AiSystemPrompt = "自定义";
        settings.AiShowToolDetails = false;

        var clone = settings.Clone();
        TestRunner.Equal("qwen3:14b", clone.AiModel, "Clone 带上模型");
        TestRunner.Equal(AiApiMode.OpenAi, clone.AiApiMode, "Clone 带上协议");
        TestRunner.Equal(1024, clone.AiMaxTokens, "Clone 带上 token 上限");
        TestRunner.Equal("自定义", clone.AiSystemPrompt, "Clone 带上系统提示");
        TestRunner.Check(!clone.AiShowToolDetails, "Clone 带上工具详情开关");

        var target = new AppSettings();
        target.CopyFrom(settings);
        TestRunner.Equal("qwen3:14b", target.AiModel, "CopyFrom 复制模型");
        TestRunner.Equal(AiApiMode.OpenAi, target.AiApiMode, "CopyFrom 复制协议");
        TestRunner.Equal(0.7, target.AiTemperature, "CopyFrom 复制温度");

        var options = AiClientFactory.CreateOptions(settings);
        TestRunner.Equal("qwen3:14b", options.Model, "会话选项取模型");
        TestRunner.Equal(1024, options.MaxTokens, "会话选项取 token 上限");
        TestRunner.Check(!options.DisableThinking == false || true, "会话选项可读");

        TestRunner.Check(AiClientFactory.Describe(settings).Contains("qwen3:14b", StringComparison.Ordinal), "设置摘要带模型名");
        TestRunner.Check(AiClientFactory.DescribeProtocol(settings).Contains("OpenAI", StringComparison.Ordinal), "显式 OpenAI 协议描述");

        var ollamaSettings = new AppSettings { AiBaseUrl = "http://127.0.0.1:11434", AiApiMode = AiApiMode.Auto };
        TestRunner.Check(AiClientFactory.DescribeProtocol(ollamaSettings).Contains("Ollama", StringComparison.Ordinal), "自动识别描述为 Ollama 原生");
    }

    // ==================== 测试替身 ====================

    private sealed class RecordingActions : IAiActions
    {
        public AiListQuery? LastQuery { get; private set; }

        public AiItemDraft? LastDraft { get; private set; }

        public string LastReference { get; private set; } = string.Empty;

        public List<string> Calls { get; } = new();

        public AiToolResult ListItems(AiListQuery query)
        {
            LastQuery = query;
            Calls.Add("list_items");
            return AiToolResult.Ok("{\"count\":1,\"items\":[{\"ref\":\"#1\",\"title\":\"客户沟通\"}]}", "查到 1 条日程");
        }

        public AiToolResult CreateItem(AiItemDraft draft)
        {
            LastDraft = draft;
            Calls.Add("create_item");
            return AiToolResult.Ok("{\"ok\":true,\"ref\":\"#1\"}", "已新建日程：客户沟通");
        }

        public AiToolResult UpdateItem(string reference, AiItemDraft draft)
        {
            LastReference = reference;
            LastDraft = draft;
            Calls.Add("update_item");
            return AiToolResult.Ok("{\"ok\":true}", "已修改");
        }

        public AiToolResult RescheduleItem(string reference, string newStart, string? newEnd)
        {
            LastReference = reference;
            Calls.Add("reschedule_item");
            return AiToolResult.Ok("{\"ok\":true}", "已改期");
        }

        public AiToolResult DeleteItem(string reference)
        {
            LastReference = reference;
            Calls.Add("delete_item");
            return AiToolResult.Ok("{\"ok\":true}", "已删除");
        }

        public AiToolResult SetStatus(string reference, string status)
        {
            LastReference = reference;
            Calls.Add("set_status");
            return AiToolResult.Ok("{\"ok\":true}", "已标记为" + status);
        }

        public AiToolResult NextReminders(int count)
        {
            Calls.Add("next_reminders");
            return AiToolResult.Ok("{\"count\":0}", "接下来没有提醒");
        }

        public AiToolResult AppInfo()
        {
            Calls.Add("app_info");
            return AiToolResult.Ok("{\"itemCount\":3}", "共 3 条日程");
        }

        public AiToolResult SetTheme(string theme)
        {
            Calls.Add("set_theme");
            return AiToolResult.Ok("{\"ok\":true}", "主题已切换");
        }
    }

    private sealed class FakeClient : IAiChatClient
    {
        private readonly Func<AiChatRequest, AiChatReply> _handler;

        public FakeClient(Func<AiChatRequest, AiChatReply> handler) => _handler = handler;

        public string BaseUrl => "fake://local";

        public int CallCount { get; private set; }

        public AiChatRequest? LastRequest { get; private set; }

        public Task<AiChatReply> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }

        public Task<List<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new List<AiModelInfo> { new() { Id = "fake" } });
    }

    private sealed class ThrowingClient : IAiChatClient
    {
        private readonly Exception _error;

        public ThrowingClient(Exception error) => _error = error;

        public string BaseUrl => "fake://local";

        public Task<AiChatReply> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken)
            => Task.FromException<AiChatReply>(_error);

        public Task<List<AiModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromException<List<AiModelInfo>>(_error);
    }

    private sealed class CollectingProgress : IProgress<AiProgress>
    {
        public List<string> Stages { get; } = new();

        public void Report(AiProgress value) => Stages.Add(value.Stage);
    }

    private static AiChatReply Reply(string text) => new() { Content = text };

    private static AiChatReply ToolReply(string name, string arguments) => new()
    {
        Content = string.Empty,
        ToolCalls = new List<AiToolCall> { new() { Id = "call_0", Name = name, Arguments = arguments } },
    };

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
