using Schedule.Core.Models;
using Schedule.Core.Storage;

namespace Schedule.Core.Services;

/// <summary>一次 CSV 导入的历史记录。</summary>
public sealed class ImportRecord
{
    public DateTime Time { get; set; } = DateTime.Now;

    public string FilePath { get; set; } = string.Empty;

    public string EncodingName { get; set; } = string.Empty;

    public int Total { get; set; }

    public int Imported { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }
}

/// <summary>导入历史（import-history.json），最多保留 50 条。</summary>
public sealed class ImportHistoryService
{
    private const int MaxRecords = 50;
    private readonly JsonStore<List<ImportRecord>> _store;
    private List<ImportRecord> _records = new();

    public ImportHistoryService(string? filePath = null)
    {
        _store = new JsonStore<List<ImportRecord>>(filePath ?? AppPaths.ImportHistoryFile, backupKeepCount: 3);
    }

    public IReadOnlyList<ImportRecord> Records => _records;

    public void Load() => _records = _store.Load() ?? new List<ImportRecord>();

    public void Add(ImportRecord record)
    {
        _records.Insert(0, record);
        if (_records.Count > MaxRecords)
        {
            _records.RemoveRange(MaxRecords, _records.Count - MaxRecords);
        }

        _store.Save(_records);
    }
}
