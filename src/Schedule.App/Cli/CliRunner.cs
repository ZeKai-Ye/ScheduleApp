using System.Text;
using Schedule.Core.Csv;
using Schedule.Core.Ics;
using Schedule.Core.Localization;
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
            else if (!string.IsNullOrWhiteSpace(options.CsvTemplateFile))
            {
                exitCode = WriteCsvTemplate(options.CsvTemplateFile!, output);
            }
            else if (options.AiModels)
            {
                exitCode = AiModels(output);
            }
            else if (!string.IsNullOrWhiteSpace(options.AiChatText))
            {
                exitCode = AiChat(options, output);
            }
        }
        catch (Exception ex)
        {
            exitCode = 2;
            output.AppendLine(Loc.T("执行失败：", "Failed: ") + ex.GetType().Name + ": " + ex.Message);
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
                Console.Error.WriteLine(Loc.T("写入 --out 文件失败：", "Failed to write the --out file: ") + ex.Message);
            }
        }

        return exitCode;
    }

    private static int CreateShortcut(CommandLineOptions options, StringBuilder output)
    {
        if (ShortcutService.TryCreate(options.ShortcutTarget!, null, options.ShortcutDirectory, out var path, out var error))
        {
            output.AppendLine(Loc.T("已创建快捷方式：", "Shortcut created: ") + path);
            return 0;
        }

        output.AppendLine(Loc.T("创建快捷方式失败：", "Failed to create the shortcut: ") + error);
        return 6;
    }

    private static int RemoveShortcut(CommandLineOptions options, StringBuilder output)
    {
        if (ShortcutService.TryDelete(options.RemoveShortcutTarget!, options.ShortcutDirectory, out var path, out var error))
        {
            output.AppendLine(Loc.T("已删除快捷方式：", "Shortcut removed: ") + path);
            return 0;
        }

        output.AppendLine(Loc.T("删除快捷方式失败：", "Failed to delete the shortcut: ") + error);
        return 6;
    }

    private static int SetStartup(CommandLineOptions options, StringBuilder output)
    {
        var enabled = options.StartupEnabled!.Value;
        var arguments = options.StartupArguments ?? "--minimized";

        if (!StartupService.Apply(enabled, arguments, out var mode, out var error))
        {
            output.AppendLine(Loc.T("设置开机启动失败：", "Failed to update Start with Windows: ") + error);
            return 6;
        }

        if (enabled)
        {
            output.AppendLine(Loc.F("开机启动已启用（{0}）", "Start with Windows enabled ({0})", StartupService.Describe(mode == "StartupFolder" ? AutostartMode.StartupFolder : AutostartMode.Registry)));
            output.AppendLine(Loc.T("执行命令：", "Command: ") + StartupService.BuildCommand(arguments));
        }
        else
        {
            output.AppendLine(Loc.T("开机启动已停用。", "Start with Windows disabled."));
        }

        if (!string.IsNullOrEmpty(error)) output.AppendLine(Loc.T("[提示] ", "[Notice] ") + error);
        return 0;
    }

    private static int ListVoices(StringBuilder output)
    {
        var service = new SapiVoiceService();
        if (!service.IsAvailable)
        {
            output.AppendLine(Loc.T("未检测到可用的 SAPI 语音引擎：", "No usable SAPI voice engine found: ") + (service.LastError ?? Loc.T("未知原因", "Unknown reason")));
            return 3;
        }

        var voices = service.GetVoices();
        output.AppendLine(Loc.F("已安装语音 {0} 个：", "{0} voices installed:", voices.Count));
        foreach (var voice in voices)
        {
            output.AppendLine("  " + voice.Name);
            output.AppendLine(Loc.T("      Id      : ", "      Id             : ") + voice.Id);
            output.AppendLine(Loc.T("      语言/性别: ", "      Language/Gender: ") + voice.Language + " / " + voice.Gender);
        }

        if (voices.Count == 0)
        {
            output.AppendLine(Loc.T("（系统未安装任何语音包，可在「设置 → 时间和语言 → 语音」中添加）", "(No voice packs installed — add one in Settings → Time & Language → Speech)"));
        }

        return 0;
    }

    private static int Speak(CommandLineOptions options, StringBuilder output)
    {
        var settings = LoadSettings();
        var service = new SapiVoiceService();
        if (!service.IsAvailable)
        {
            output.AppendLine(Loc.T("语音引擎不可用：", "Voice engine not available: ") + (service.LastError ?? Loc.T("未知原因", "Unknown reason")));
            return 3;
        }

        var voiceId = string.IsNullOrWhiteSpace(options.VoiceId) ? settings.VoiceId : options.VoiceId!;
        var name = service.GetVoices().FirstOrDefault(v => v.Id == voiceId)?.Name ?? Loc.T("系统默认", "System default");

        output.AppendLine(Loc.T("音色：", "Voice: ") + name);
        output.AppendLine(Loc.T("文本：", "Text: ") + options.SpeakText);

        service.SpeakAndWait(new SpeechRequest
        {
            Text = options.SpeakText!,
            VoiceId = voiceId,
            Rate = options.Rate,
            Volume = options.Volume,
        });

        if (!string.IsNullOrEmpty(service.LastError))
        {
            output.AppendLine(Loc.T("朗读失败：", "Speak failed: ") + service.LastError);
            output.AppendLine(Loc.T("（常见原因：当前会话没有可用音频输出设备，或所选音色不可用）", "(Common causes: the current session has no audio output device, or the selected voice is unavailable)"));
            return 3;
        }

        output.AppendLine(Loc.T("朗读完成。", "Speech finished."));
        return 0;
    }

    /// <summary>前 2KB 里找 BEGIN:VCALENDAR，用于无扩展名的 .ics。</summary>
    private static bool LooksLikeIcs(string file)
    {
        try
        {
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[2048];
            var read = reader.Read(buffer, 0, buffer.Length);

            return new string(buffer, 0, read).Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int Import(CommandLineOptions options, StringBuilder output)
    {
        var file = options.ImportFile!;
        if (!File.Exists(file))
        {
            output.AppendLine(Loc.T("文件不存在：", "File not found: ") + file);
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

        // Snapshot 返回副本，导入服务就地修改该副本，再整体写回仓储
        var items = repository.Snapshot();

        var isIcs = file.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)
            || LooksLikeIcs(file);

        var report = isIcs
            ? new IcsImportService().Import(new IcsImportOptions
            {
                Mode = mode,
                SkipPastItems = options.SkipPast,
                SkipDuplicates = !options.NoSkipDuplicates,
                DefaultCategory = string.IsNullOrWhiteSpace(settings.CsvDefaultCategory)
                    ? Loc.T("导入", "Imported")
                    : settings.CsvDefaultCategory,
                DefaultColor = settings.DefaultEventColor,
                DefaultReminderEnabled = settings.DefaultReminderEnabled,
                DefaultReminderOffsets = new List<int>(settings.DefaultReminderOffsets),
                DefaultRemindBeforeMinutes = settings.DefaultRemindBeforeMinutes,
                SourceTag = Path.GetFileName(file),
            }, file, items)
            : new CsvImportService().Import(new CsvImportOptions
            {
                FilePath = file,
                EncodingName = string.IsNullOrWhiteSpace(options.Encoding) ? settings.CsvEncoding : options.Encoding,
                Mode = mode,
                SkipPastItems = options.SkipPast,
                SkipDuplicates = !options.NoSkipDuplicates,
                DefaultCategory = string.IsNullOrWhiteSpace(settings.CsvDefaultCategory)
                    ? Loc.T("导入", "Imported")
                    : settings.CsvDefaultCategory,
                DefaultColor = settings.DefaultEventColor,
                DefaultReminderEnabled = settings.DefaultReminderEnabled,
                DefaultReminderOffsets = new List<int>(settings.DefaultReminderOffsets),
                DefaultRemindBeforeMinutes = settings.DefaultRemindBeforeMinutes,
            }, items);

        repository.ReplaceAll(items);
        repository.Save(force: true);

        output.AppendLine(Loc.T("导入文件：", "Import file: ") + file);
        output.AppendLine(Loc.T("格式：", "Format: ") + (isIcs ? Loc.T("iCalendar（.ics）", "iCalendar (.ics)") : "CSV"));
        output.AppendLine(Loc.T("编码：", "Encoding: ") + report.EncodingName);

        if (report.Blocked)
        {
            output.AppendLine(Loc.T("已终止导入（未改动任何日程）：", "Import aborted (no events were changed):"));
            output.AppendLine(report.BlockReason ?? Loc.T("格式不匹配。", "Format does not match."));
            return 5;
        }

        output.AppendLine(report.ToSummary());

        foreach (var warning in report.Warnings)
        {
            output.AppendLine(Loc.T("[提示] ", "[Notice] ") + warning);
        }

        foreach (var error in report.Errors.Take(50))
        {
            output.AppendLine(Loc.F("[第 {0} 行] ", "[Line {0}] ", error.Line) + error.Message);
        }

        if (report.Errors.Count > 50)
        {
            output.AppendLine(Loc.F("（还有 {0} 条错误未显示）", "({0} more errors not shown)", report.Errors.Count - 50));
        }

        return report.Failed > 0 && report.Imported == 0 && report.Updated == 0 ? 5 : 0;
    }

    private static int Export(CommandLineOptions options, StringBuilder output)
    {
        var repository = new ScheduleRepository();
        repository.Load();
        var items = repository.Snapshot();

        var file = options.ExportFile!;

        if (file.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
        {
            IcsWriter.WriteFile(file, items);
            output.AppendLine(Loc.F("已导出 {0} 条日程到：{1}（iCalendar，可导入 Google 日历 / Outlook）", "Exported {0} events to {1} (iCalendar, importable by Google Calendar / Outlook)", items.Count, file));
        }
        else
        {
            CsvExporter.WriteFile(file, items);
            output.AppendLine(Loc.F("已导出 {0} 条日程到：{1}（统一格式 CSV，可原样再导入）", "Exported {0} events to {1} (unified CSV, can be imported back as-is)", items.Count, file));
        }

        return 0;
    }

    private static int Info(StringBuilder output)
    {
        var repository = new ScheduleRepository();
        repository.Load();
        var settings = LoadSettings();
        var voices = new SapiVoiceService();

        output.AppendLine(Loc.T("数据目录：", "Data directory: ") + AppPaths.DataDirectory);
        output.AppendLine(Loc.T("便携模式：", "Portable mode: ") + (AppPaths.IsPortable ? Loc.T("是", "Yes") : Loc.T("否", "No")));
        output.AppendLine(Loc.T("日程文件：", "Events file: ") + repository.FilePath);
        output.AppendLine(Loc.F("日程数量：{0}", "Event count: {0}", repository.Count));
        output.AppendLine(Loc.T("语音引擎：", "Voice engine: ") + (voices.IsAvailable ? Loc.F("可用（{0} 个音色）", "Available ({0} voices)", voices.GetVoices().Count) : Loc.T("不可用", "Unavailable")));
        output.AppendLine(Loc.T("当前音色：", "Current voice: ") + (string.IsNullOrWhiteSpace(settings.VoiceName) ? Loc.T("系统默认", "System default") : settings.VoiceName));
        output.AppendLine(Loc.T("语音模板：", "Voice template: ") + settings.VoiceTemplate);
        output.AppendLine(Loc.T("默认提醒档位：", "Default reminder offsets: ") + Schedule.Core.Models.ReminderPlan.FormatOffsets(settings.DefaultReminderOffsets));
        output.AppendLine(Loc.T("日志目录：", "Log directory: ") + AppPaths.LogDirectory);
        var (autostartMode, autostartCommand) = StartupService.Detect();
        output.AppendLine(Loc.T("开机启动：", "Start with Windows: ") + StartupService.Describe(autostartMode)
            + (autostartCommand.Length > 0 ? Loc.F("（{0}）", " ({0})", autostartCommand) : string.Empty));
        output.AppendLine(Loc.T("桌面快捷方式：", "Desktop shortcut: ") + (ShortcutService.Exists("desktop") ? Loc.T("已创建", "created") : Loc.T("未创建", "not created"))
            + Loc.T("；开始菜单：", "; Start menu: ") + (ShortcutService.Exists("startmenu") ? Loc.T("已创建", "created") : Loc.T("未创建", "not created")));
        output.AppendLine(Loc.T("提醒弹窗位置：", "Reminder popup position: ") + Schedule.Core.Models.ReminderPositionMath.Describe(settings.ReminderPosition)
            + (settings.ReminderPosition == Schedule.Core.Models.ReminderPositionMode.Custom
                ? Loc.F("（{0}, {1}）", " ({0}, {1})", settings.ReminderPositionX, settings.ReminderPositionY)
                : string.Empty));
        output.AppendLine(Loc.T("本地接口：", "Local API: ") + (settings.ApiEnabled
            ? Loc.F("已启用（端口 {0}，令牌见 {1}）", "Enabled (port {0}, token in {1})", settings.ApiPort, Path.Combine(AppPaths.DataDirectory, "api.json"))
            : Loc.T("已禁用（设置 apiEnabled=false）", "Disabled (apiEnabled=false in settings)")));
        output.AppendLine(Loc.T("全局档位文案：", "Offset templates: ") + (settings.OffsetVoiceTemplates.Count == 0
            ? Loc.T("未配置", "Not configured")
            : string.Join(" / ", settings.OffsetVoiceTemplates.OrderByDescending(p => p.Key).Select(p => Loc.F("{0} 分钟 → {1}", "{0} min → {1}", p.Key, p.Value)))));
        return 0;
    }

    private static AppSettings LoadSettings()
    {
        var service = new SettingsService();
        service.Load();
        return service.Current;
    }

    /// <summary>导出统一格式的 CSV 模板（表头 + 两行示例）。</summary>
    private static int WriteCsvTemplate(string path, StringBuilder output)
    {
        try
        {
            CsvSchema.WriteTemplate(path);

            output.AppendLine(Loc.T("已写入 CSV 模板：", "CSV template written: ") + path);
            output.AppendLine(Loc.T("表头：", "Header: ") + CsvSchema.HeaderLine);
            output.AppendLine();
            output.AppendLine(CsvSchema.DescribeColumns());
            return 0;
        }
        catch (Exception ex)
        {
            output.AppendLine(Loc.T("写入模板失败：", "Failed to write the template: ") + ex.Message);
            return 4;
        }
    }

    // ================= AI 对话（无界面验证 / 脚本用） =================

    private static int AiModels(StringBuilder output)
    {
        var settings = new SettingsService();
        settings.Load();

        return AiCliRunner.ListModels(settings, output);
    }

    private static int AiChat(CommandLineOptions options, StringBuilder output)
    {
        var settings = new SettingsService();
        settings.Load();

        var repository = new ScheduleRepository(backupKeepCount: settings.Current.BackupKeepCount);
        repository.Load();

        return AiCliRunner.Chat(settings, repository, options.AiChatText!.Trim(), output);
    }
}
