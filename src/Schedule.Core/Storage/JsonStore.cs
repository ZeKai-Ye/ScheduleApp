using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schedule.Core.Storage;

/// <summary>通用 JSON 持久化（UTF-8 无 BOM、中文不转义、原子写入）。</summary>
public sealed class JsonStore<T> where T : class
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly int _backupKeepCount;

    public JsonStore(string path, int backupKeepCount = 10)
    {
        _path = path;
        _backupKeepCount = backupKeepCount;
    }

    public string Path => _path;

    public bool Exists => File.Exists(_path);

    /// <summary>读取；文件不存在或损坏时返回 null（损坏文件会重命名为 .corrupt）。</summary>
    public T? Load()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var json = AtomicFile.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            try
            {
                var corrupt = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(_path, corrupt, overwrite: true);
                Logging.AppLog.Error("数据文件解析失败，已备份为 " + System.IO.Path.GetFileName(corrupt), ex);
            }
            catch
            {
                // 忽略
            }

            return null;
        }
    }

    public void Save(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        AtomicFile.WriteAllText(_path, json, createBackup: true, backupKeepCount: _backupKeepCount);
    }
}
