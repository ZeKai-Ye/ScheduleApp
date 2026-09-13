using System.Text;
using Schedule.Core.Csv;
using Schedule.Core.Models;
using Schedule.Core.Services;
using Schedule.Core.Storage;
using Schedule.App.Services;

namespace Schedule.App;

/// <summary>无界面模式：导入 / 导出 / 语音 / 信息，返回值即进程退出码。</summary>
public static class CliRunner
{
    public static int Run(CommandLineOptions options)
    {
        var output = new StringBuilder();
        var exitCode = 0;

        try
        {
            if (options.Help)
            {
                output.AppendLine(CommandLineOptions.Usage);
            }
            else if (!string.IsNullOrWhiteSpace(options.ShortcutTarget))
            {
                exitCode = CreateShortcut(options, output);
            }
            else if (!string.IsNullOrWhiteSpace(options.RemoveShortcutTarget))
            {
                exitCode = RemoveShortcut(options, output);
            }
            else if (options.StartupEnabled.HasValue)
            {
                exitCode = SetStartup(options, output);
            }
            else if (options.ListVoices)
            {
                exitCode = ListVoices(output);
            }
            else if (!string.IsNullOrWhiteSpace(options.SpeakText))
            {
                exitCode = Speak(options, output);
            }
            else if (!string.IsNullOrWhiteSpace(options.ImportFile))
            {
                exitCode = Import(options, output);
            }
            else if (!string.IsNullOrWhiteSpace(options.ExportFile))
            {
                exitCode = Export(options, output);
            }
            else if (options.ShowInfo)
            {
                exitCode = Info(output);
            }
        }
        catch (Exception ex)
        {
            exitCode = 2;
            output.AppendLine("执行失败：" + ex.GetType().Name + ": " + ex.Message);
        }

        var text = output.ToString();
        try
        {
            Console.Out.Write(text);
            Console.Out.Flush();
        }
        catch (Exception)
        {
            // 无控制台（WinExe 且无重定向）时忽略
        }

        if (!string.IsNullOrWhiteSpace(options.OutFile))
        {
            try
            {
                var directory = Path.GetDirectoryName(options.OutFile);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(options.OutFile, text, new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("写入 --out 文件失败：" + ex.Message);
            }
        }

        return exitCode;
    }

    private static int CreateShortcut(CommandLineOptions options, StringBuilder output)
    {
        if (ShortcutService.TryCreate(options.ShortcutTarget!, null, options.ShortcutDirectory, out var path, out var error))
        {
            output.AppendLine("已创建快捷方式：" + path);
            return 0;
        }

        output.AppendLine("创建快捷方式失败：" + error);
        return 6;
    }

    private static int RemoveShortcut(CommandLineOptions options, StringBuilder output)
    {
        if (ShortcutService.TryDelete(options.RemoveShortcutTarget!, options.ShortcutDirectory, out var path, out var error))
        {
            output.AppendLine("已删除快捷方式：" + path);
            return 0;
        }

        output.AppendLine("删除快捷方式失败：" + error);
        return 6;
    }

    private static int SetStartup(CommandLineOptions options, StringBuilder output)
    {
        var enabled = options.StartupEnabled!.Value;
        var arguments = options.StartupArguments ?? "--minimized";

        if (!StartupService.Apply(enabled, arguments, out var mode, out var error))
        {
            output.AppendLine("设置开机启动失败：" + error);
            return 6;
        }

        if (enabled)
        {
            output.AppendLine("开机启动已启用（" + StartupService.Describe(mode == "StartupFolder" ? AutostartMode.StartupFolder : AutostartMode.Registry) + "）");
            output.AppendLine("执行命令：" + StartupService.BuildCommand(arguments));
        }
        else
        {
            output.AppendLine("开机启动已停用。");
        }

        if (!string.IsNullOrEmpty(error)) output.AppendLine("[提示] " + error);
        return 0;
    }

    private static int ListVoices(StringBuilder output)
    {
        var service = new SapiVoiceService();
        if (!service.IsAvailable)
        {
            output.AppendLine("未检测到可用的 SAPI 语音引擎：" + (service.LastError ?? "未知原因"));
            return 3;
        }

        var voices = service.GetVoices();
        output.AppendLine("已安装语音 " + voices.Count + " 个：");
        foreach (var voice in voices)
        {
            output.AppendLine("  " + voice.Name);
            output.AppendLine("      Id      : " + voice.Id);
            output.AppendLine("      语言/性别: " + voice.Language + " / " + voice.Gender);
        }

        if (voices.Count == 0)
        {
            output.AppendLine("（系统未安装任何语音包，可在「设置 → 时间和语言 → 语音」中添加）");
        }

        return 0;
    }

    private static int Speak(CommandLineOptions options, StringBuilder output)
    {
        var settings = LoadSettings();
        var service = new SapiVoiceService();
        if (!service.IsAvailable)
        {
            output.AppendLine("语音引擎不可用：" + (service.LastError ?? "未知原因"));
            return 3;
        }

        var voiceId = string.IsNullOrWhiteSpace(options.VoiceId) ? settings.VoiceId : options.VoiceId!;
        var name = service.GetVoices().FirstOrDefault(v => v.Id == voiceId)?.Name ?? "系统默认";

        output.AppendLine("音色：" + name);
        output.AppendLine("文本：" + options.SpeakText);

        service.SpeakAndWait(new SpeechRequest
        {
            Text = options.SpeakText!,
            VoiceId = voiceId,
            Rate = options.Rate,
            Volume = options.Volume,
        });

        if (!string.IsNullOrEmpty(service.LastError))
        {
            output.AppendLine("朗读失败：" + service.LastError);
            output.AppendLine("（常见原因：当前会话没有可用音频输出设备，或所选音色不可用）");
            return 3;
        }

        output.AppendLine("朗读完成。");
        return 0;
    }

    private static int Import(CommandLineOptions options, StringBuilder output)
    {
        var file = options.ImportFile!;
        if (!File.Exists(file))
        {
            output.AppendLine("文件不存在：" + file);
            return 4;
        }

        var settings = LoadSettings();
        var repository = new ScheduleRepository();
        repository.Load();

        var mode = options.ImportMode switch
        {
            "upsert" or "update" => ImportMode.Upsert,
            "replace" => ImportMode.Replace,
            _ => ImportMode.Append,
        };

        var importOptions = new CsvImportOptions
        {
            FilePath = file,
            EncodingName = string.IsNullOrWhiteSpace(options.Encoding) ? settings.CsvEncoding : options.Encoding,
            Mode = mode,
            SkipPastItems = options.SkipPast,
            SkipDuplicates = !options.NoSkipDuplicates,
            DefaultCategory = settings.CsvDefaultCategory,
            DefaultColor = settings.DefaultEventColor,
            DefaultReminderEnabled = settings.DefaultReminderEnabled,
            DefaultReminderOffsets = new List<int>(settings.DefaultReminderOffsets),
            DefaultRemindBeforeMinutes = settings.DefaultRemindBeforeMinutes,
        };

        // Snapshot 返回副本，导入服务就地修改该副本，再整体写回仓储
        var items = repository.Snapshot();
        var service = new CsvImportService();
        var report = service.Import(importOptions, items);

        repository.ReplaceAll(items);
        repository.Save(force: true);

        output.AppendLine("导入文件：" + file);
        output.AppendLine("编码：" + report.EncodingName);
        output.AppendLine(report.ToSummary());

        foreach (var warning in report.Warnings)
        {
            output.AppendLine("[提示] " + warning);
        }

        foreach (var error in report.Errors.Take(50))
        {
            output.AppendLine("[第 " + error.Line + " 行] " + error.Message);
        }

        if (report.Errors.Count > 50)
        {
            output.AppendLine("（还有 " + (report.Errors.Count - 50) + " 条错误未显示）");
        }

        return report.Failed > 0 && report.Imported == 0 && report.Updated == 0 ? 5 : 0;
    }

    private static int Export(CommandLineOptions options, StringBuilder output)
    {
        var repository = new ScheduleRepository();
        repository.Load();
        var items = repository.Snapshot();

        CsvExporter.WriteFile(options.ExportFile!, items);
        output.AppendLine("已导出 " + items.Count + " 条日程到：" + options.ExportFile);
        return 0;
    }

    private static int Info(StringBuilder output)
    {
        var repository = new ScheduleRepository();
        repository.Load();
        var settings = LoadSettings();
        var voices = new SapiVoiceService();

        output.AppendLine("数据目录：" + AppPaths.DataDirectory);
        output.AppendLine("便携模式：" + (AppPaths.IsPortable ? "是" : "否"));
        output.AppendLine("日程文件：" + repository.FilePath);
        output.AppendLine("日程数量：" + repository.Count);
        output.AppendLine("语音引擎：" + (voices.IsAvailable ? "可用（" + voices.GetVoices().Count + " 个音色）" : "不可用"));
        output.AppendLine("当前音色：" + (string.IsNullOrWhiteSpace(settings.VoiceName) ? "系统默认" : settings.VoiceName));
        output.AppendLine("语音模板：" + settings.VoiceTemplate);
        output.AppendLine("默认提醒档位：" + Schedule.Core.Models.ReminderPlan.FormatOffsets(settings.DefaultReminderOffsets));
        output.AppendLine("日志目录：" + AppPaths.LogDirectory);
        var (autostartMode, autostartCommand) = StartupService.Detect();
        output.AppendLine("开机启动：" + StartupService.Describe(autostartMode)
            + (autostartCommand.Length > 0 ? "（" + autostartCommand + "）" : string.Empty));
        output.AppendLine("桌面快捷方式：" + (ShortcutService.Exists("desktop") ? "已创建" : "未创建")
            + "；开始菜单：" + (ShortcutService.Exists("startmenu") ? "已创建" : "未创建"));
        output.AppendLine("提醒弹窗位置：" + Schedule.Core.Models.ReminderPositionMath.Describe(settings.ReminderPosition)
            + (settings.ReminderPosition == Schedule.Core.Models.ReminderPositionMode.Custom
                ? "（" + settings.ReminderPositionX + ", " + settings.ReminderPositionY + "）"
                : string.Empty));
        output.AppendLine("本地接口：" + (settings.ApiEnabled
            ? "已启用（端口 " + settings.ApiPort + "，令牌见 " + Path.Combine(AppPaths.DataDirectory, "api.json") + "）"
            : "已禁用（设置 apiEnabled=false）"));
        output.AppendLine("全局档位文案：" + (settings.OffsetVoiceTemplates.Count == 0
            ? "未配置"
            : string.Join(" / ", settings.OffsetVoiceTemplates.OrderByDescending(p => p.Key).Select(p => p.Key + " 分钟 → " + p.Value))));
        return 0;
    }

    private static AppSettings LoadSettings()
    {
        var service = new SettingsService();
        service.Load();
        return service.Current;
    }
}
