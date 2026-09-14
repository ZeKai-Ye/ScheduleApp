using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Schedule.Core.Ai;

/// <summary>
/// 工具结果与请求体的 JSON 输出选项。
///
/// 关键点：**不转义非 ASCII**。默认的编码器会把每个汉字写成 \uXXXX（6 个字符），
/// 工具结果是要回传给模型当上下文用的，一条 20 字的日程标题就会白白多花上百个 token；
/// 本地 8B 模型的上下文本来就紧张，所以这里统一用宽松编码器。
/// </summary>
public static class AiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>按统一选项序列化。</summary>
    public static string Text(JsonNode? node) => node?.ToJsonString(Options) ?? "null";
}
