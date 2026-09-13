using System.Text;
using Schedule.Core.Models;
using Schedule.Core.Services;
using Schedule.Core.Storage;

namespace Schedule.Core.Tests.Cases;

public static class StorageTests
{
    public static void Run()
    {
        TestRunner.Section("存储与持久化");

        var dir = TestPaths.NewTempDir();
        AppPaths.Initialize(dir);
        TestRunner.Equal(Path.GetFullPath(dir), Path.GetFullPath(AppPaths.DataDirectory), "数据目录覆盖");
        TestRunner.Check(Directory.Exists(AppPaths.BackupDirectory), "备份目录已创建");

        var path = Path.Combine(dir, "schedules.json");
        var repository = new ScheduleRepository(path);
        repository.Load();
        TestRunner.Equal(0, repository.Count, "空仓储");

        repository.Add(new ScheduleItem
        {
            Title = "写文档",
            Start = new DateTime(2025, 7, 1, 9, 0, 0),
            Category = "工作",
            Notes = "包含, 逗号和\"引号\"",
        });
        repository.Add(new ScheduleItem { Title = "买牛奶", Start = new DateTime(2025, 7, 1, 18, 0, 0) });
        repository.Save();

        TestRunner.Check(File.Exists(path), "数据文件已生成");

        var reloaded = new ScheduleRepository(path);
        reloaded.Load();
        TestRunner.Equal(2, reloaded.Count, "重新加载条数");
        var doc = reloaded.Snapshot().First(i => i.Title == "写文档");
        TestRunner.Equal("包含, 逗号和\"引号\"", doc.Notes, "特殊字符往返");
        TestRunner.Equal(new DateTime(2025, 7, 1, 9, 0, 0), doc.Start, "时间往返");

        // 中文不转义
        var raw = File.ReadAllText(path, Encoding.UTF8);
        TestRunner.Check(raw.Contains("写文档", StringComparison.Ordinal), "JSON 中文未转义");

        // 再次保存会产生备份
        reloaded.Snapshot().First().Notes = "改过了";
        reloaded.MarkDirty();
        reloaded.Save();
        var backups = Directory.GetFiles(AppPaths.BackupDirectory, "*.json");
        TestRunner.Check(backups.Length >= 1, "生成了备份文件");

        // 损坏文件处理
        var brokenPath = Path.Combine(dir, "broken.json");
        File.WriteAllText(brokenPath, "{ 这不是 JSON", new UTF8Encoding(false));
        var brokenRepo = new ScheduleRepository(brokenPath);
        TestRunner.NoThrow(() => brokenRepo.Load(), "损坏文件不抛异常");
        TestRunner.Equal(0, brokenRepo.Count, "损坏文件返回空集合");
        TestRunner.Check(Directory.GetFiles(dir, "broken.json.corrupt-*").Length == 1, "损坏文件被隔离");

        // 数据修复
        var repairPath = Path.Combine(dir, "repair.json");
        File.WriteAllText(repairPath,
            "[{\"id\":\"" + Guid.NewGuid() + "\",\"title\":\"\",\"start\":\"2025-01-01T09:00:00\",\"remindBeforeMinutes\":-5}]",
            new UTF8Encoding(false));
        var repairRepo = new ScheduleRepository(repairPath);
        repairRepo.Load();
        TestRunner.Equal(1, repairRepo.Count, "修复后仍保留记录");
        var repaired = repairRepo.Snapshot()[0];
        TestRunner.Equal("(无标题)", repaired.Title, "空标题被修复");
        TestRunner.Equal(0, repaired.RemindBeforeMinutes, "负提醒分钟被修复");

        // 删除与统计
        TestRunner.Equal(0, reloaded.RemoveCompleted(), "无已完成时返回 0");
        var target = reloaded.Snapshot()[0];
        TestRunner.Check(reloaded.Remove(target.Id), "按 Id 删除");
        TestRunner.Equal(1, reloaded.Count, "删除后条数");

        // 设置读写
        var settingsPath = Path.Combine(dir, "settings.json");
        var settings = new SettingsService(settingsPath);
        settings.Load();
        TestRunner.Equal(10, settings.Current.DefaultRemindBeforeMinutes, "默认设置");
        settings.Current.VoiceTemplate = "{title} 到点了";
        settings.Current.VoiceRate = -2;
        settings.MarkDirty();
        settings.Save();

        var settings2 = new SettingsService(settingsPath);
        settings2.Load();
        TestRunner.Equal("{title} 到点了", settings2.Current.VoiceTemplate, "设置往返");
        TestRunner.Equal(-2, settings2.Current.VoiceRate, "语速往返");

        // 导入历史
        var history = new ImportHistoryService(Path.Combine(dir, "import-history.json"));
        history.Load();
        history.Add(new ImportRecord { FilePath = "a.csv", Imported = 3 });
        var history2 = new ImportHistoryService(Path.Combine(dir, "import-history.json"));
        history2.Load();
        TestRunner.Equal(1, history2.Records.Count, "导入历史持久化");

        // 日期时间格式化（用于导出，需保证往返一致）
        var item = new ScheduleItem { Title = "格式", Start = new DateTime(2025, 12, 31, 23, 59, 0) };
        TestRunner.Equal("2025-12-31 23:59", item.Start.ToString("yyyy-MM-dd HH:mm"), "导出时间格式");
    }
}
