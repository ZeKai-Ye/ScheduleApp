namespace Schedule.Core.Models;

/// <summary>一个已安装的语音（TTS）音色。</summary>
public sealed class VoiceDescriptor
{
    public string Id { get; set; } = string.Empty;

    /// <summary>友好名称，例如 "Microsoft Huihui Desktop - Chinese (Simplified)"。</summary>
    public string Name { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Gender { get; set; } = string.Empty;

    public string Age { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public override string ToString() => Name;
}

/// <summary>语音播报参数。</summary>
public sealed class SpeechRequest
{
    public string Text { get; set; } = string.Empty;

    /// <summary>音色 Id；为空使用系统默认音色。</summary>
    public string VoiceId { get; set; } = string.Empty;

    /// <summary>语速，-10 ~ 10，0 为正常。</summary>
    public int Rate { get; set; }

    /// <summary>音量，0 ~ 100。</summary>
    public int Volume { get; set; } = 100;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) || Text.Trim().Length == 0;
}
