using Schedule.Core.Models;

namespace Schedule.App.Services;

/// <summary>语音播报抽象（便于替换引擎与单元测试）。</summary>
public interface IVoiceService
{
    /// <summary>语音引擎是否可用。</summary>
    bool IsAvailable { get; }

    string? LastError { get; }

    IReadOnlyList<VoiceDescriptor> GetVoices();

    /// <summary>异步朗读（不阻塞界面）。</summary>
    void Speak(SpeechRequest request);

    /// <summary>同步朗读，直到播报结束（命令行测试用）。</summary>
    void SpeakAndWait(SpeechRequest request);

    void Stop();
}
