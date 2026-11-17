using System.Text;
using Schedule.App.Services;
using Schedule.Core.Ai;
using Schedule.Core.Logging;
using Schedule.Core.Localization;
using Schedule.Core.Services;

namespace Schedule.App;

/// <summary>
/// 命令行里的 AI 对话：跟模型说一句话，打印回复与工具调用。
/// 主要用于无界面验证（也能给脚本 / AI 自己用），不启动 WPF。
/// </summary>
public static class AiCliRunner
{
    /// <summary>列出模型服务上的模型。</summary>
    public static int ListModels(SettingsService settings, StringBuilder output)
    {
        try
        {
            using var client = AiClientFactory.Create(settings.Current);
            var models = client.ListModelsAsync(CancellationToken.None).GetAwaiter().GetResult();

            output.AppendLine(Loc.T("服务：", "Endpoint: ") + OpenAiCompatibleClient.Normalize(settings.Current.AiBaseUrl)
                + Loc.T("　协议：", "  Protocol: ") + AiClientFactory.DescribeProtocol(settings.Current));
            output.AppendLine(Loc.F("共 {0} 个模型：", "{0} models:", models.Count));

            foreach (var model in models)
            {
                output.AppendLine("  - " + model.Id + (string.IsNullOrWhiteSpace(model.Detail) ? string.Empty : "　" + model.Detail));
            }

            return 0;
        }
        catch (Exception ex)
        {
            output.AppendLine(Loc.T("读取模型列表失败：", "Failed to load the model list: ") + ex.Message);
            return 2;
        }
    }

    /// <summary>跟 AI 说一句，打印回复。</summary>
    public static int Chat(SettingsService settings, ScheduleRepository repository, string text, StringBuilder output)
    {
        var context = new SimpleAiContext(settings, repository);
        var actions = new ScheduleAiActions(context);

        try
        {
            using var client = AiClientFactory.Create(settings.Current);
            var session = new AiSession(client, actions, AiClientFactory.CreateOptions(settings.Current));

            output.AppendLine(Loc.T("模型：", "Model: ") + settings.Current.AiModel
                + Loc.T("　服务：", "  Endpoint: ") + OpenAiCompatibleClient.Normalize(settings.Current.AiBaseUrl)
                + Loc.T("　协议：", "  Protocol: ") + AiClientFactory.DescribeProtocol(settings.Current));

            var progress = new Progress<AiProgress>(p => output.AppendLine("  · " + p.Stage));
            var result = session.SendAsync(text, progress, CancellationToken.None).GetAwaiter().GetResult();

            foreach (var tool in result.Tools)
            {
                output.AppendLine(Loc.F("工具 {0}({1})", "Tool {0}({1})", tool.Name, tool.Arguments));
                output.AppendLine(Loc.T("  结果：", "  Result: ") + OpenAiCompatibleClient.Shorten(tool.Result, 400));
            }

            output.AppendLine();
            output.AppendLine(Loc.T("AI：", "AI: ") + result.Text);

            // 思考等级调到最低档时这里应该是 0 字；如果很长说明等级没生效（会明显更慢）
            output.AppendLine(Loc.F("思考过程：{0} 字", "Reasoning: {0} chars", result.Reasoning?.Length ?? 0));
            output.AppendLine();
            output.AppendLine(Loc.F("用时 {0} 秒", "Took {0}s", result.Elapsed.TotalSeconds.ToString("0.0"))
                + Loc.F("，工具 {0} 次", ", {0} tool calls", result.Tools.Count)
                + (result.PromptTokens.HasValue || result.CompletionTokens.HasValue
                    ? Loc.F("，token {0}+{1}", ", tokens {0}+{1}", result.PromptTokens ?? 0, result.CompletionTokens ?? 0)
                    : string.Empty));

            AppLog.Info("命令行 AI 对话完成：" + result.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
            return 0;
        }
        catch (Exception ex)
        {
            output.AppendLine(Loc.T("AI 对话失败：", "AI chat failed: ") + ex.Message);
            return 2;
        }
    }
}
