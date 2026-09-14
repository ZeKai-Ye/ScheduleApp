using System.Text;
using Schedule.App.Services;
using Schedule.Core.Ai;
using Schedule.Core.Logging;
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

            output.AppendLine("服务：" + OpenAiCompatibleClient.Normalize(settings.Current.AiBaseUrl)
                + "　协议：" + AiClientFactory.DescribeProtocol(settings.Current));
            output.AppendLine("共 " + models.Count + " 个模型：");

            foreach (var model in models)
            {
                output.AppendLine("  - " + model.Id + (string.IsNullOrWhiteSpace(model.Detail) ? string.Empty : "　" + model.Detail));
            }

            return 0;
        }
        catch (Exception ex)
        {
            output.AppendLine("读取模型列表失败：" + ex.Message);
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

            output.AppendLine("模型：" + settings.Current.AiModel
                + "　服务：" + OpenAiCompatibleClient.Normalize(settings.Current.AiBaseUrl)
                + "　协议：" + AiClientFactory.DescribeProtocol(settings.Current));

            var progress = new Progress<AiProgress>(p => output.AppendLine("  · " + p.Stage));
            var result = session.SendAsync(text, progress, CancellationToken.None).GetAwaiter().GetResult();

            foreach (var tool in result.Tools)
            {
                output.AppendLine("工具 " + tool.Name + "(" + tool.Arguments + ")");
                output.AppendLine("  结果：" + OpenAiCompatibleClient.Shorten(tool.Result, 400));
            }

            output.AppendLine();
            output.AppendLine("AI：" + result.Text);

            // 思考等级调到最低档时这里应该是 0 字；如果很长说明等级没生效（会明显更慢）
            output.AppendLine("思考过程：" + (result.Reasoning?.Length ?? 0) + " 字");
            output.AppendLine();
            output.AppendLine("用时 " + result.Elapsed.TotalSeconds.ToString("0.0") + " 秒"
                + "，工具 " + result.Tools.Count + " 次"
                + (result.PromptTokens.HasValue || result.CompletionTokens.HasValue
                    ? "，token " + (result.PromptTokens ?? 0) + "+" + (result.CompletionTokens ?? 0)
                    : string.Empty));

            AppLog.Info("命令行 AI 对话完成：" + result.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
            return 0;
        }
        catch (Exception ex)
        {
            output.AppendLine("AI 对话失败：" + ex.Message);
            return 2;
        }
    }
}
