using Schedule.Core.Ai;

namespace Schedule.Core.Tests.Cases;

/// <summary>流式输出的解析与拼接（不依赖网络，直接喂两种协议的原始行）。</summary>
public static class StreamingTests
{
    public static void Run()
    {
        TestRunner.Section("流式输出");

        OpenAiStream();
        OllamaStream();
        Accumulator();
    }

    private static void OpenAiStream()
    {
        var accumulator = new AiStreamAccumulator();

        var ignored = AiStreamParser.ReadOpenAiLine(": keep-alive", accumulator, out var none);
        TestRunner.Equal(StreamLineResult.Ignored, ignored, "SSE 心跳行忽略");
        TestRunner.Check(none == null, "忽略行不产生片段");

        var first = AiStreamParser.ReadOpenAiLine(
            "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}", accumulator, out var chunk1);
        TestRunner.Equal(StreamLineResult.Chunk, first, "SSE 正文片段");
        TestRunner.Equal("你好", chunk1?.Text, "SSE 正文内容");

        // 工具调用分两片来：name 一片、arguments 两片
        AiStreamParser.ReadOpenAiLine(
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"create_item\",\"arguments\":\"{\\\"title\\\":\"}}]}}]}",
            accumulator, out _);
        AiStreamParser.ReadOpenAiLine(
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"开会\\\"}\"}}]}}]}",
            accumulator, out _);

        var usage = AiStreamParser.ReadOpenAiLine(
            "data: {\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":22},\"choices\":[]}", accumulator, out _);
        TestRunner.Equal(StreamLineResult.Ignored, usage, "只有 usage 的片段不产生正文");

        var done = AiStreamParser.ReadOpenAiLine("data: [DONE]", accumulator, out _);
        TestRunner.Equal(StreamLineResult.Ended, done, "SSE 结束标记");

        var reply = accumulator.ToReply();
        TestRunner.Equal("你好", reply.Content, "拼接后的正文");
        TestRunner.Equal(1, reply.ToolCalls.Count, "工具调用合并成一条");
        TestRunner.Equal("create_item", reply.ToolCalls[0].Name, "工具名");
        TestRunner.Equal("call_1", reply.ToolCalls[0].Id, "工具调用 id");
        TestRunner.Equal("{\"title\":\"开会\"}", reply.ToolCalls[0].Arguments, "工具参数分片拼接");
        TestRunner.Equal(11, reply.PromptTokens, "prompt token 用量");
        TestRunner.Equal(22, reply.CompletionTokens, "completion token 用量");

        var bad = AiStreamParser.ReadOpenAiLine("hello world", new AiStreamAccumulator(), out _);
        TestRunner.Equal(StreamLineResult.Ignored, bad, "非 data: 行忽略");
    }

    private static void OllamaStream()
    {
        var accumulator = new AiStreamAccumulator();

        var thinking = AiStreamParser.ReadOllamaLine(
            "{\"message\":{\"content\":\"\",\"thinking\":\"先想想\"},\"done\":false}", accumulator, out var thinkChunk);
        TestRunner.Equal(StreamLineResult.Chunk, thinking, "Ollama 思维链片段");
        TestRunner.Equal("先想想", thinkChunk?.Reasoning, "思维链内容");

        var text = AiStreamParser.ReadOllamaLine(
            "{\"message\":{\"content\":\"下午三点开会\"},\"done\":false}", accumulator, out var textChunk);
        TestRunner.Equal("下午三点开会", textChunk?.Text, "Ollama 正文片段");

        // Ollama 的工具调用是一次给全的，arguments 是对象
        AiStreamParser.ReadOllamaLine(
            "{\"message\":{\"content\":\"\",\"tool_calls\":[{\"id\":\"c1\",\"function\":{\"name\":\"list_items\",\"arguments\":{\"range\":\"today\"}}}]},\"done\":false}",
            accumulator, out _);

        var done = AiStreamParser.ReadOllamaLine(
            "{\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":9,\"eval_count\":13}", accumulator, out _);
        TestRunner.Equal(StreamLineResult.Ended, done, "Ollama 结束行");

        var reply = accumulator.ToReply();
        TestRunner.Equal("下午三点开会", reply.Content, "Ollama 正文拼接");
        TestRunner.Equal("先想想", reply.Reasoning, "Ollama 思维链拼接");
        TestRunner.Equal("stop", reply.FinishReason, "Ollama 结束原因");
        TestRunner.Equal(9, reply.PromptTokens, "Ollama prompt 用量");
        TestRunner.Equal(13, reply.CompletionTokens, "Ollama completion 用量");
        TestRunner.Equal(1, reply.ToolCalls.Count, "Ollama 工具调用");
        TestRunner.Equal("list_items", reply.ToolCalls[0].Name, "Ollama 工具名");
        TestRunner.Check(reply.ToolCalls[0].Arguments.Contains("\"today\"", StringComparison.Ordinal), "Ollama 对象参数转成 JSON 文本");

        var broken = AiStreamParser.ReadOllamaLine("{not json", new AiStreamAccumulator(), out _);
        TestRunner.Equal(StreamLineResult.Ignored, broken, "坏行忽略");

        // 服务端报错要变成可读异常，而不是静默当成“没有内容”
        var threw = false;
        try
        {
            AiStreamParser.ReadOllamaLine("{\"error\":\"model not found\"}", new AiStreamAccumulator(), out _);
        }
        catch (AiChatException ex)
        {
            threw = ex.Message.Contains("model not found", StringComparison.Ordinal);
        }

        TestRunner.Check(threw, "Ollama 报错行抛出可读异常");
    }

    private static void Accumulator()
    {
        // 有些模板把思维链塞在正文里：收完要拆开，正文只留答复
        var accumulator = new AiStreamAccumulator();
        accumulator.AppendText("<think");
        accumulator.AppendText("ing>推理过程</think");
        accumulator.AppendText("ing>最终答复");

        var reply = accumulator.ToReply();
        TestRunner.Equal("最终答复", reply.Content, "正文里的 think 块被剥离");
        TestRunner.Equal("推理过程", reply.Reasoning, "think 块内容变成思维链");

        var empty = new AiStreamAccumulator();
        TestRunner.Check(!empty.HasAny, "空累积器 HasAny 为假");
        TestRunner.Equal(string.Empty, empty.ToReply().Content, "空累积器正文为空");
    }
}
