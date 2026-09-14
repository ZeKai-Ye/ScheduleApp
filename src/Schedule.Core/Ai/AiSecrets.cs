namespace Schedule.Core.Ai;

/// <summary>
/// 接口密钥的处理：支持 <c>env:NAME</c> 间接引用（密钥留在环境变量里，不写进 settings.json），
/// 以及界面上打码显示。
/// </summary>
public static class AiSecrets
{
    public const string EnvironmentPrefix = "env:";

    /// <summary>
    /// 解析密钥：<c>env:OPENAI_API_KEY</c> 会去读同名环境变量；其余的按原文使用。
    /// 返回 (实际使用的密钥, 环境变量名或 null)。
    /// </summary>
    public static (string Key, string? Variable) Resolve(string? configured)
    {
        var text = (configured ?? string.Empty).Trim();
        if (text.Length == 0) return (string.Empty, null);

        if (!text.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase)) return (text, null);

        var name = text.Substring(EnvironmentPrefix.Length).Trim();
        if (name.Length == 0) return (string.Empty, null);

        var value = Environment.GetEnvironmentVariable(name);
        return ((value ?? string.Empty).Trim(), name);
    }

    /// <summary>打码显示（只留头尾），日志与界面提示都用它，避免把密钥整串打出来。</summary>
    public static string Mask(string? key)
    {
        var text = (key ?? string.Empty).Trim();
        if (text.Length == 0) return "(未填写)";
        if (text.Length <= 8) return new string('*', text.Length);

        return text.Substring(0, 4) + "…" + text.Substring(text.Length - 4) + "（" + text.Length + " 位）";
    }

    /// <summary>给界面用的一句话：密钥从哪来、有没有配上。</summary>
    public static string Describe(string? configured)
    {
        var (key, variable) = Resolve(configured);

        if (variable != null)
        {
            return key.Length > 0
                ? "来自环境变量 " + variable + "：" + Mask(key)
                : "环境变量 " + variable + " 为空或不存在，请检查后重试";
        }

        return key.Length > 0 ? "已填写：" + Mask(key) : "未填写";
    }
}
