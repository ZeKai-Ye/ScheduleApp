using System.Text;

namespace Schedule.Core.Csv;

/// <summary>文本规整工具：全角转半角、去除不可见字符。</summary>
public static class TextNormalizer
{
    /// <summary>把全角字符转换为半角（含全角空格）。</summary>
    public static char ToHalfWidthChar(char c)
    {
        if (c == '\u3000') return ' ';
        if (c >= '\uFF01' && c <= '\uFF5E') return (char)(c - 0xFEE0);
        return c;
    }

    public static string ToHalfWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var buffer = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            buffer[i] = ToHalfWidthChar(text[i]);
        }

        return new string(buffer);
    }

    /// <summary>清理 BOM、零宽字符、首尾空白，并压缩连续空白为单个空格。</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var raw in text)
        {
            var c = ToHalfWidthChar(raw);
            if (c == '\uFEFF' || c == '\u200B' || c == '\u00A0') continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    /// <summary>用于排序 / 比较的键：去空白、去标点、转小写。</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var raw in text)
        {
            var c = ToHalfWidthChar(raw);
            if (char.IsWhiteSpace(c)) continue;
            if (c is '_' or '-' or '(' or ')' or '[' or ']' or '*' or ':' or '/' or '\\' or '.' or ',' or ';') continue;
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
