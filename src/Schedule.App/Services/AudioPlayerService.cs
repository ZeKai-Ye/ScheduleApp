using System.IO;
using System.Windows.Media;
using Schedule.Core.Logging;

namespace Schedule.App.Services;

/// <summary>
/// 提醒提示音播放：支持 wav / mp3 / wma / m4a（WPF MediaPlayer 内置解码），
/// 未指定文件时退回系统提示音。
/// </summary>
public sealed class AudioPlayerService : IDisposable
{
    private readonly object _gate = new();
    private MediaPlayer? _player;

    public string? LastError { get; private set; }

    public void Play(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            PlaySystemSound();
            return;
        }

        try
        {
            lock (_gate)
            {
                _player ??= new MediaPlayer();
                _player.Close();
                _player.Open(new Uri(Path.GetFullPath(filePath)));
                _player.Volume = 1.0;
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Warn("播放提示音失败：" + ex.Message);
            PlaySystemSound();
        }
    }

    public void PlaySystemSound()
    {
        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            AppLog.Warn("系统提示音播放失败：" + ex.Message);
        }
    }

    public void Stop()
    {
        try
        {
            lock (_gate)
            {
                _player?.Stop();
            }
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                _player?.Close();
                _player = null;
            }
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
