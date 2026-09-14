using System.Diagnostics;
using System.Globalization;
using Schedule.Core.Logging;

namespace Schedule.Core.Ai;

/// <summary>本次对话的运行参数。</summary>
public sealed class AiSessionOptions
{
    public string Model { get; set; } = string.Empty;

    public double Temperature { get; set; } = 0.2;

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>让模型别输出思维链（明显更快，也更不容易把输出预算耗在思考上）。</summary>
    public bool DisableThinking { get; set; } = true;

    /// <summary>最多带多少条历史消息（不含系统提示）。</summary>
    public int MaxHistoryMessages { get; set; } = 16;

    /// <summary>一轮用户消息里最多允许几次工具调用往返。</summary>
    public int MaxToolRounds { get; set; } = 4;

    /// <summary>单次最大输出 token（0 = 交给服务端默认）。</summary>
    public int MaxTokens { get; set; }

    /// <summary>自定义系统提示；留空用内置的。</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>没带时间时用当前时间；自测可以固定。</summary>
    public DateTime? Now { get; set; }
}

/// <summary>一次工具执行（用于界面展示"AI 做了什么"）。</summary>
public sealed class AiToolExecution
{
    public string Name { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public bool IsError { get; init; }

    public string? Summary { get; init; }
}

/// <summary>一轮对话的结果。</summary>
public sealed class AiTurnResult
{
    /// <summary>要显示给用户的文本。</summary>
    public string Text { get; set; } = string.Empty;

    public string? Reasoning { get; set; }

    public List<AiToolExecution> Tools { get; } = new();

    /// <summary>工具调用达到上限（模型可能还没说完）。</summary>
    public bool HitToolRoundLimit { get; set; }

    /// <summary>模型没有走原生工具调用，是靠正文里的 JSON 兜底解析出来的。</summary>
    public bool UsedTextFallback { get; set; }

    public int Rounds { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public TimeSpan Elapsed { get; set; }
}

/// <summary>给界面用的进度回调。</summary>
public sealed class AiProgress
{
    public AiProgress(string stage, int round) { Stage = stage; Round = round; }

    public string Stage { get; }

    public int Round { get; }
}

/// <summary>
/// 对话会话：维护历史、驱动「模型 → 工具 → 模型」的循环、裁剪上下文。
/// 与界面完全解耦（模型客户端与工具执行都是注入的），因此可以完整自测。
/// </summary>
public sealed class AiSession
{
    private readonly IAiChatClient _client;
    private readonly AiToolCatalog _catalog;

    public AiSession(IAiChatClient client, IAiActions actions, AiSessionOptions? options = null)
    {
        _client = client;
        _catalog = new AiToolCatalog(actions);
        Options = options ?? new AiSessionOptions();
    }

    public AiSessionOptions Options { get; }

    /// <summary>历史消息（不含系统提示）。</summary>
    public List<ChatMessage> History { get; } = new();

    public void Clear() => History.Clear();

    public string BuildSystemPrompt()
    {
        if (!string.IsNullOrWhiteSpace(Options.SystemPrompt)) return Options.SystemPrompt;

        var now = Options.Now ?? DateTime.Now;
        var prompt = "你是「日程助手」内置的 AI 助手，帮用户查看和打理日程。"
            + "当前时间：" + now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            + "（" + WeekdayText(now.DayOfWeek) + "）。\n"
            + "工作要求：\n"
            + "1. 需要查看或修改日程时，必须调用提供的工具，不要凭记忆编造日程内容或编号。\n"
            + "2. 修改、删除、改期之前，先用 list_items 找到目标，再用它返回的 ref（形如 #1）操作；"
            + "要删除时先向用户确认一次。\n"
            + "3. 工具执行完后，用简短的中文回复用户（一般 1~3 句），不要重复粘贴 JSON。\n"
            + "4. 用户只是闲聊或问常识时直接回答，不要调用工具。\n"
            + "5. 时间用 24 小时制；信息不够（比如没说日期）就先问清楚。";

        // Qwen3 这类「思考模型」的软开关：模板看到 /no_think 就不再输出思维链
        if (Options.DisableThinking) prompt += "\n/no_think";

        return prompt;
    }

    private static string WeekdayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };

    /// <summary>发一条用户消息，跑完「模型 → 工具 → 模型」直到模型给出自然语言答复。</summary>
    public async Task<AiTurnResult> SendAsync(string userText, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        var result = new AiTurnResult();
        var watch = Stopwatch.StartNew();
        var rounds = Math.Clamp(Options.MaxToolRounds, 1, 10);

        History.Add(ChatMessage.User(userText ?? string.Empty));

        for (var round = 0; round < rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Rounds = round + 1;
            progress?.Report(new AiProgress(round == 0 ? "正在请求模型…" : "正在根据工具结果继续…", round));

            var request = new AiChatRequest
            {
                Model = Options.Model,
                Messages = BuildMessages(),
                Tools = AiToolCatalog.Definitions.ToList(),
                Temperature = Options.Temperature,
                DisableThinking = Options.DisableThinking,
                MaxTokens = Options.MaxTokens,
                TimeoutSeconds = Options.TimeoutSeconds,
            };

            var reply = await _client.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

            result.PromptTokens = reply.PromptTokens ?? result.PromptTokens;
            result.CompletionTokens = reply.CompletionTokens ?? result.CompletionTokens;
            if (!string.IsNullOrWhiteSpace(reply.Reasoning)) result.Reasoning = reply.Reasoning;

            var calls = reply.ToolCalls;
            var content = reply.Content;

            // 兜底：模型没走原生工具调用，而是把调用写进了正文
            if (calls.Count == 0)
            {
                var parsed = AiActionParser.Extract(content);
                if (parsed.Count > 0)
                {
                    calls = parsed;
                    content = AiActionParser.StripAction(content);
                    result.UsedTextFallback = true;
                    AppLog.Info("AI 未返回原生工具调用，已从正文解析出 " + parsed.Count + " 个动作：" + string.Join(", ", parsed.Select(p => p.Name)));
                }
            }

            History.Add(ChatMessage.Assistant(content, reply.Reasoning, calls));

            if (calls.Count == 0)
            {
                result.Text = content.Trim();
                break;
            }

            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new AiProgress("正在执行 " + call.Name + "…", round));

                var toolResult = _catalog.Execute(call);
                result.Tools.Add(new AiToolExecution
                {
                    Name = call.Name,
                    Arguments = call.Arguments,
                    Result = toolResult.Content,
                    IsError = toolResult.IsError,
                    Summary = toolResult.Summary,
                });

                History.Add(ChatMessage.ToolResult(call, toolResult.Content));
            }

            if (round == rounds - 1)
            {
                result.HitToolRoundLimit = true;
                result.Text = string.IsNullOrWhiteSpace(content)
                    ? "我连续调用工具的次数有点多，先停在这里。你可以再说得具体一点。"
                    : content.Trim();
            }
        }

        watch.Stop();
        result.Elapsed = watch.Elapsed;

        if (string.IsNullOrWhiteSpace(result.Text) && result.Tools.Count > 0)
        {
            result.Text = "已按你的要求执行：" + string.Join("；", result.Tools.Select(t => t.Summary ?? t.Name));
        }

        // 上下文别无限膨胀：超出上限时从头丢弃，但不能把工具结果和它的请求拆开
        TrimHistory();

        return result;
    }

    /// <summary>系统提示 + 裁剪后的历史。</summary>
    private List<ChatMessage> BuildMessages()
    {
        var messages = new List<ChatMessage> { ChatMessage.System(BuildSystemPrompt()) };
        messages.AddRange(History);
        return messages;
    }

    private void TrimHistory()
    {
        var max = Math.Clamp(Options.MaxHistoryMessages, 4, 200);
        if (History.Count <= max) return;

        var remove = History.Count - max;

        // 别让窗口从一条孤立的工具结果开始：那会让服务端报「tool 消息没有对应的 tool_calls」
        while (remove < History.Count && History[remove].Role == ChatRole.Tool) remove++;

        if (remove <= 0) return;

        History.RemoveRange(0, remove);
        AppLog.Info("AI 对话上下文已裁剪 " + remove + " 条（保留最近 " + History.Count + " 条）");
    }
}
