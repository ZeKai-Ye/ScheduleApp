using System.Text;

namespace Schedule.Core.Storage;

/// <summary>
/// 原子写文件工具：先写临时文件并 flush 到磁盘，再整体替换目标文件，
/// 避免断电 / 崩溃导致数据文件损坏。同时负责滚动备份。
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content, bool createBackup = true, int backupKeepCount = 10)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var bytes = Utf8NoBom.GetBytes(content);

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            if (createBackup)
            {
                TryBackup(path, backupKeepCount);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        else
        {
            File.Move(tempPath, path, overwrite: true);
        }
    }

    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void TryBackup(string path, int keepCount)
    {
        try
        {
            if (keepCount <= 0) return;

            var backupDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", "backups");
            Directory.CreateDirectory(backupDir);

            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(backupDir, name + "-" + stamp + ext);

            var index = 1;
            while (File.Exists(target))
            {
                target = Path.Combine(backupDir, name + "-" + stamp + "-" + index++ + ext);
            }

            File.Copy(path, target, overwrite: false);
            Prune(backupDir, name, ext, keepCount);
        }
        catch
        {
            // 备份失败不应影响主流程
        }
    }

    private static void Prune(string backupDir, string namePrefix, string ext, int keepCount)
    {
        try
        {
            var files = new DirectoryInfo(backupDir)
                .GetFiles(namePrefix + "-*" + ext)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(keepCount)
                .ToList();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch
        {
            // 忽略
        }
    }
}
