using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Storage;

namespace Schedule.Core.Services;

/// <summary>设置读写（settings.json）。</summary>
public sealed class SettingsService
{
    private readonly object _gate = new();
    private readonly JsonStore<AppSettings> _store;

    public SettingsService(string? filePath = null)
    {
        _store = new JsonStore<AppSettings>(filePath ?? AppPaths.SettingsFile, backupKeepCount: 5);
    }

    public AppSettings Current { get; private set; } = new();

    public bool IsDirty { get; private set; }

    public event EventHandler? Changed;

    public void Load()
    {
        var loaded = _store.Load();
        lock (_gate)
        {
            Current = loaded ?? new AppSettings();
            IsDirty = loaded == null;
        }

        AppLog.Info("已加载设置：" + _store.Path);
        RaiseChanged();
    }

    public void Save(bool force = false)
    {
        if (!force && !IsDirty) return;

        try
        {
            lock (_gate)
            {
                _store.Save(Current);
                IsDirty = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("保存设置失败", ex);
            throw;
        }
    }

    public void MarkDirty() => IsDirty = true;

    public void Replace(AppSettings settings)
    {
        lock (_gate)
        {
            Current = settings;
            IsDirty = true;
        }

        RaiseChanged();
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
