using System.Globalization;
using Schedule.Core.Localization;
using Schedule.Core.Logging;
using Schedule.Core.Models;

namespace Schedule.App.Services;

/// <summary>
/// 基于 Windows SAPI5 的语音服务（通过 COM 延迟绑定调用 SAPI.SpVoice）。
/// 不使用 System.Speech NuGet 包，保持零依赖与轻量。
/// </summary>
public sealed class SapiVoiceService : IVoiceService, IDisposable
{
    private const int SvsFlagsAsync = 1;
    private const int SvsFlagsPurgeBeforeSpeak = 2;

    private readonly object _gate = new();
    private object? _voice;
    private Type? _voiceType;
    private IReadOnlyList<VoiceDescriptor>? _voiceCache;

    public SapiVoiceService()
    {
        try
        {
            _voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (_voiceType == null)
            {
                LastError = Loc.T("系统中未注册 SAPI.SpVoice（缺少语音组件）",
                    "SAPI.SpVoice is not registered on this system (voice component missing)");
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public bool IsAvailable => _voiceType != null;

    public string? LastError { get; private set; }

    public IReadOnlyList<VoiceDescriptor> GetVoices()
    {
        if (_voiceCache != null) return _voiceCache;
        if (!IsAvailable) return Array.Empty<VoiceDescriptor>();

        var list = new List<VoiceDescriptor>();
        try
        {
            dynamic voice = EnsureVoice();
            dynamic tokens = voice.GetVoices();
            int count = tokens.Count;

            for (var i = 0; i < count; i++)
            {
                dynamic token = tokens.Item(i);
                var descriptor = new VoiceDescriptor
                {
                    Id = SafeString(() => (string)token.Id),
                    Name = DescribeToken(token),
                    Language = DescribeLanguage(Attribute(token, "Language")),
                    Gender = DescribeGender(Attribute(token, "Gender")),
                    Age = Attribute(token, "Age"),
                    IsDefault = i == 0,
                };

                if (descriptor.Name.Length == 0) descriptor.Name = Loc.F("语音 {0}", "Voice {0}", i + 1);
                list.Add(descriptor);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Warn("枚举语音失败：" + ex.Message);
        }

        _voiceCache = list;
        return list;
    }

    public void Speak(SpeechRequest request)
    {
        if (request.IsEmpty) return;
        if (!IsAvailable)
        {
            AppLog.Warn("语音引擎不可用，跳过播报");
            return;
        }

        try
        {
            lock (_gate)
            {
                dynamic voice = EnsureVoice();
                ApplyVoice(voice, request);
                voice.Speak(string.Empty, SvsFlagsPurgeBeforeSpeak);
                voice.Speak(request.Text, SvsFlagsAsync);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Warn("语音播报失败：" + ex.Message);
        }
    }

    public void SpeakAndWait(SpeechRequest request)
    {
        if (request.IsEmpty) return;
        if (!IsAvailable) return;

        try
        {
            lock (_gate)
            {
                dynamic voice = EnsureVoice();
                ApplyVoice(voice, request);
                voice.Speak(string.Empty, SvsFlagsPurgeBeforeSpeak);
                voice.Speak(request.Text, 0);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Warn("语音播报失败：" + ex.Message);
        }
    }

    public void Stop()
    {
        if (!IsAvailable) return;

        try
        {
            lock (_gate)
            {
                if (_voice == null) return;
                dynamic voice = _voice;
                voice.Speak(string.Empty, SvsFlagsPurgeBeforeSpeak);
            }
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private void ApplyVoice(dynamic voice, SpeechRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VoiceId))
        {
            try
            {
                dynamic tokens = voice.GetVoices(string.Empty, request.VoiceId);
                if ((int)tokens.Count > 0)
                {
                    voice.Voice = tokens.Item(0);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("切换音色失败，使用默认音色：" + ex.Message);
            }
        }

        voice.Rate = Math.Clamp(request.Rate, -10, 10);
        voice.Volume = Math.Clamp(request.Volume, 0, 100);
    }

    private dynamic EnsureVoice()
    {
        if (_voice != null) return _voice;

        var type = _voiceType ?? throw new InvalidOperationException("SAPI 不可用");
        _voice = Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法创建 SAPI.SpVoice 实例");
        return _voice;
    }

    private static string DescribeToken(dynamic token)
    {
        try
        {
            return (string)token.GetDescription(string.Empty);
        }
        catch (Exception)
        {
            // 某些语音包需要省略参数
        }

        try
        {
            return (string)token.GetDescription();
        }
        catch (Exception)
        {
            return SafeString(() => (string)token.Id);
        }
    }

    /// <summary>SAPI 的 Language 属性是十六进制 LANGID（例如 804 = zh-CN），转换成可读名称。</summary>
    private static string DescribeLanguage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var languageId))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(languageId);
                if (!string.IsNullOrWhiteSpace(culture.DisplayName)) return culture.DisplayName;
            }
            catch (Exception)
            {
                // 未知 LANGID，退回原始值
            }
        }

        return raw;
    }

    private static string DescribeGender(string raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "female" => Loc.T("女声", "Female"),
        "male" => Loc.T("男声", "Male"),
        null or "" => string.Empty,
        _ => raw,
    };

    private static string Attribute(dynamic token, string name)
    {
        try
        {
            var value = (string)token.GetAttribute(name);
            return value ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeString(Func<string> factory)
    {
        try
        {
            return factory() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_voice == null) return;

            try
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_voice);
            }
            catch (Exception)
            {
                // 忽略
            }

            _voice = null;
        }
    }

    /// <summary>把音色列表格式化为便于显示的一行文本。</summary>
    public static string FormatVoice(VoiceDescriptor voice)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(voice.Language)) parts.Add(voice.Language);
        if (!string.IsNullOrWhiteSpace(voice.Gender)) parts.Add(voice.Gender);
        if (!string.IsNullOrWhiteSpace(voice.Age)) parts.Add(voice.Age);

        var suffix = parts.Count == 0 ? string.Empty : "  (" + string.Join(" · ", parts) + ")";
        return voice.Name + suffix;
    }

    /// <summary>把音色 Id 里可读的语言信息提取出来（部分引擎的 Id 是 XML）。</summary>
    public static string FriendlyLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return string.Empty;
        return language.Split(';')[0].Trim();
    }

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
}
