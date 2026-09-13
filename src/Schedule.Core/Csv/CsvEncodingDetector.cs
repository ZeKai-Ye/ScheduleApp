using System.Text;

namespace Schedule.Core.Csv;

/// <summary>
/// CSV 编码识别：BOM 优先，其次严格 UTF-8 校验，最后回退到 GB18030（兼容 GBK）。
/// </summary>
public static class CsvEncodingDetector
{
    public const string Auto = "auto";
    public const string Utf8 = "utf-8";
    public const string Gbk = "gbk";
    public const string Gb18030 = "gb18030";
    public const string Utf16 = "utf-16";

    public static CsvTextSource ReadFile(string filePath, string preference = Auto)
    {
        var bytes = File.ReadAllBytes(filePath);
        return Decode(bytes, preference);
    }

    public static CsvTextSource Decode(byte[] bytes, string preference = Auto)
    {
        preference = Normalize(preference);

        // 1) BOM 是最高优先级的事实依据
        if (TryReadBom(bytes, out var bomText, out var bomName))
        {
            return new CsvTextSource
            {
                Text = bomText,
                EncodingName = bomName,
                HasBom = true,
                IsGuessed = false,
            };
        }

        // 2) 用户显式指定
        if (preference != Auto)
        {
            return DecodeExplicit(bytes, preference);
        }

        // 3) 严格 UTF-8 校验
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = strict.GetString(bytes);
            return new CsvTextSource
            {
                Text = text,
                EncodingName = "UTF-8（无 BOM）",
                HasBom = false,
                IsGuessed = false,
            };
        }
        catch (DecoderFallbackException)
        {
            // 落到传统代码页
        }

        // 4) 回退 GB18030（GBK 的超集，能无损解码 GBK 文本）
        if (NativeEncoding.TryGetString(bytes, NativeEncoding.CodePageGb18030, out var gb))
        {
            return new CsvTextSource
            {
                Text = gb,
                EncodingName = "GB18030/GBK（自动识别）",
                HasBom = false,
                IsGuessed = true,
                Note = "文件未使用 UTF-8 编码，已按简体中文代码页解码",
            };
        }

        // 5) 最后兜底：UTF-8 宽松解码
        var fallback = Encoding.UTF8.GetString(bytes);
        return new CsvTextSource
        {
            Text = fallback,
            EncodingName = "UTF-8（替换非法字节）",
            HasBom = false,
            IsGuessed = true,
            IsFallback = true,
            Note = "无法确定文件编码，部分字符可能显示为问号",
        };
    }

    private static CsvTextSource DecodeExplicit(byte[] bytes, string preference)
    {
        switch (preference)
        {
            case Gbk:
            case Gb18030:
            {
                var codePage = preference == Gbk ? NativeEncoding.CodePageGbk : NativeEncoding.CodePageGb18030;
                if (NativeEncoding.TryGetString(bytes, codePage, out var text))
                {
                    return new CsvTextSource
                    {
                        Text = text,
                        EncodingName = preference == Gbk ? "GBK（手动指定）" : "GB18030（手动指定）",
                        IsGuessed = false,
                    };
                }

                break;
            }

            case Utf16:
            {
                var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
                    ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
                    : Encoding.Unicode.GetString(bytes);
                return new CsvTextSource { Text = text, EncodingName = "UTF-16LE（手动指定）" };
            }

            case Utf8:
            {
                var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                var text = new UTF8Encoding(false, throwOnInvalidBytes: false)
                    .GetString(bytes, offset, bytes.Length - offset);
                return new CsvTextSource { Text = text, EncodingName = "UTF-8（手动指定）" };
            }
        }

        return Decode(bytes, Auto);
    }

    private static bool TryReadBom(byte[] bytes, out string text, out string name)
    {
        text = string.Empty;
        name = string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            name = "UTF-8 with BOM";
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            text = Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
            name = "UTF-32 LE with BOM";
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            name = "UTF-16 LE with BOM";
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            name = "UTF-16 BE with BOM";
            return true;
        }

        return false;
    }

    private static string Normalize(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference)) return Auto;
        return preference.Trim().ToLowerInvariant().Replace("_", "-") switch
        {
            "utf8" => Utf8,
            "utf-8" => Utf8,
            "gb2312" => Gbk,
            "gbk" => Gbk,
            "cp936" => Gbk,
            "gb18030" => Gb18030,
            "utf16" => Utf16,
            "utf-16" => Utf16,
            "unicode" => Utf16,
            _ => Auto,
        };
    }
}
