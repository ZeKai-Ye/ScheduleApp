using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Storage;

namespace Schedule.Core.Services;

/// <summary>
/// 日程仓储：内存集合 + JSON 文件持久化。
/// 所有写操作只置脏标记，由上层在合适时机调用 <see cref="Save"/>。
/// </summary>
public sealed class ScheduleRepository
{
    private readonly object _gate = new();
    private readonly JsonStore<List<ScheduleItem>> _store;
    private List<ScheduleItem> _items = new();

    public ScheduleRepository(string? filePath = null, int backupKeepCount = 10)
    {
        _store = new JsonStore<List<ScheduleItem>>(filePath ?? AppPaths.ItemsFile, backupKeepCount);
    }

    public string FilePath => _store.Path;

    public bool IsDirty { get; private set; }

    public event EventHandler? Changed;

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>取得集合快照（调用方可安全遍历）。</summary>
    public List<ScheduleItem> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToList();
        }
    }

    public void Load()
    {
        var loaded = _store.Load() ?? new List<ScheduleItem>();
        var repaired = 0;

        foreach (var item in loaded)
        {
            if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
            repaired += ScheduleItemValidator.Normalize(item).Count;
        }

        var seen = new HashSet<Guid>();
        var deduped = new List<ScheduleItem>(loaded.Count);
        foreach (var item in loaded)
        {
            if (seen.Add(item.Id)) deduped.Add(item);
        }

        lock (_gate)
        {
            _items = deduped;
            IsDirty = repaired > 0;
        }

        if (repaired > 0)
        {
            AppLog.Warn("加载时修复了 " + repaired + " 处数据问题");
        }

        AppLog.Info("已加载日程 " + deduped.Count + " 条：" + _store.Path);
        RaiseChanged();
    }

    /// <summary>落盘（默认只在有改动时写入）。</summary>
    public void Save(bool force = false)
    {
        if (!force && !IsDirty) return;

        try
        {
            lock (_gate)
            {
                _store.Save(_items);
                IsDirty = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("保存日程失败", ex);
            throw;
        }
    }

    public void MarkDirty()
    {
        IsDirty = true;
        RaiseChanged();
    }

    public ScheduleItem Add(ScheduleItem item)
    {
        if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
        item.UpdatedAt = DateTime.Now;

        lock (_gate)
        {
            _items.Add(item);
            IsDirty = true;
        }

        RaiseChanged();
        return item;
    }

    public bool Update(ScheduleItem item)
    {
        item.UpdatedAt = DateTime.Now;

        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == item.Id);
            if (index < 0) return false;
            _items[index] = item;
            IsDirty = true;
        }

        RaiseChanged();
        return true;
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index < 0) return false;
            _items.RemoveAt(index);
            IsDirty = true;
        }

        RaiseChanged();
        return true;
    }

    public int RemoveByIds(IEnumerable<Guid> ids)
    {
        var set = new HashSet<Guid>(ids);
        int removed;

        lock (_gate)
        {
            removed = _items.RemoveAll(i => set.Contains(i.Id));
            if (removed > 0) IsDirty = true;
        }

        if (removed > 0) RaiseChanged();
        return removed;
    }

    public int RemoveCompleted()
    {
        int removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(i => i.Status != ItemStatus.Pending);
            if (removed > 0) IsDirty = true;
        }

        if (removed > 0) RaiseChanged();
        return removed;
    }

    public ScheduleItem? Find(Guid id)
    {
        lock (_gate)
        {
            return _items.FirstOrDefault(i => i.Id == id);
        }
    }

    public void ReplaceAll(IEnumerable<ScheduleItem> items)
    {
        lock (_gate)
        {
            _items = items.ToList();
            IsDirty = true;
        }

        RaiseChanged();
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
