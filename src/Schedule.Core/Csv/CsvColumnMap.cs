using Schedule.Core.Models;

namespace Schedule.Core.Csv;

/// <summary>CSV 表头 → 字段的自动识别与展示名称。</summary>
public static class CsvColumnMap
{
    /// <summary>字段的识别别名（中英文）。匹配时会先做归一化（去空白/标点、转小写）。</summary>
    private static readonly Dictionary<CsvField, string[]> Aliases = new()
    {
        [CsvField.Remind] = new[] { "提醒", "提醒时间", "提前", "提前提醒", "提前量", "提醒设置", "提醒分钟", "提醒档位", "闹钟", "remind", "remindbefore", "reminder", "alarm", "notify", "advance" },
        [CsvField.Voice] = new[] { "语音", "语音内容", "语音提醒", "播报", "播报内容", "朗读", "语音播报", "voice", "speech", "tts", "voicetext" },
        [CsvField.Sound] = new[] { "铃声", "提示音", "音频", "声音", "提醒音", "sound", "ringtone", "audio" },
        [CsvField.End] = new[] { "结束", "结束时间", "截止", "截止时间", "结束日期", "终止", "结束时刻", "end", "endtime", "finish", "until", "to" },
        [CsvField.Start] = new[] { "开始", "开始时间", "起始", "起始时间", "开始日期", "开始日期时间", "start", "starttime", "begin", "from", "dtstart", "开始时刻" },
        [CsvField.Date] = new[] { "日期", "日历日", "计划日期", "date", "day" },
        [CsvField.Title] = new[] { "标题", "主题", "名称", "事项", "日程", "事件", "任务", "安排", "摘要", "日程标题", "title", "subject", "name", "event", "summary", "task", "item" },
        [CsvField.Notes] = new[] { "备注", "说明", "描述", "详情", "详细", "内容", "注释", "备注信息", "notes", "note", "description", "remark", "desc", "memo", "comment", "detail" },
        [CsvField.Category] = new[] { "分类", "类别", "类型", "标签", "分组", "组", "项目", "场景", "category", "tag", "tags", "group", "type", "kind", "label" },
        [CsvField.Priority] = new[] { "优先级", "重要", "重要程度", "紧急", "priority", "level", "importance" },
        [CsvField.Repeat] = new[] { "重复", "重复规则", "循环", "频率", "周期", "repeat", "recurrence", "rrule", "frequency", "cycle" },
        [CsvField.RepeatInterval] = new[] { "重复间隔", "间隔", "每几天", "每几周", "间隔数", "interval", "every", "step" },
        [CsvField.RepeatCount] = new[] { "重复次数", "次数", "共几次", "count", "times", "occurrences" },
        [CsvField.Weekdays] = new[] { "星期", "周几", "星期几", "每周几", "重复星期", "weekday", "weekdays", "dayofweek" },
        [CsvField.Color] = new[] { "颜色", "色彩", "事件颜色", "色", "color", "colour" },
        [CsvField.AllDay] = new[] { "全天", "整天", "是否全天", "allday", "fullday", "isallday", "wholeday" },
        [CsvField.Status] = new[] { "状态", "是否完成", "完成", "进度", "status", "done", "completed", "finished" },
        [CsvField.RepeatUntil] = new[] { "重复截止", "截止日期", "重复截止日期", "重复到", "直到", "until", "repeatuntil", "enddate", "recurrenceend" },
    };

    /// <summary>识别顺序：越具体的字段越先匹配，避免「提醒时间」被误判成「时间」。</summary>
    private static readonly CsvField[] MatchOrder =
    {
        CsvField.Remind, CsvField.Voice, CsvField.Sound, CsvField.End, CsvField.Start,
        CsvField.Date, CsvField.Title, CsvField.Notes, CsvField.Category,
        CsvField.Priority, CsvField.Repeat, CsvField.RepeatInterval, CsvField.RepeatCount,
        CsvField.Weekdays, CsvField.RepeatUntil, CsvField.Color, CsvField.AllDay, CsvField.Status,
    };

    public static IReadOnlyList<CsvField> AllFields => MatchOrder;

    public static string DisplayName(CsvField field) => field switch
    {
        CsvField.Title => "标题",
        CsvField.Start => "开始时间",
        CsvField.End => "结束时间",
        CsvField.Date => "日期",
        CsvField.Notes => "备注",
        CsvField.Category => "分类",
        CsvField.Priority => "优先级",
        CsvField.Repeat => "重复",
        CsvField.RepeatInterval => "重复间隔",
        CsvField.RepeatCount => "重复次数",
        CsvField.Weekdays => "星期",
        CsvField.Color => "颜色",
        CsvField.AllDay => "全天",
        CsvField.Remind => "提前提醒",
        CsvField.Voice => "语音播报内容",
        CsvField.Sound => "提醒铃声",
        CsvField.Status => "状态",
        CsvField.RepeatUntil => "重复截止",
        _ => "忽略",
    };

    public static string Hint(CsvField field) => field switch
    {
        CsvField.Title => "必填，日程名称",
        CsvField.Start => "开始时间，可含日期",
        CsvField.End => "结束时间，可留空",
        CsvField.Date => "单独的日期列（与开始时间拼接）",
        CsvField.Notes => "详细说明",
        CsvField.Category => "分类 / 标签，决定自动配色",
        CsvField.Priority => "高 / 中 / 低",
        CsvField.Repeat => "每天 / 每周 / 每月 / 工作日 / 每 2 周",
        CsvField.RepeatInterval => "每隔几个周期，例如 2",
        CsvField.RepeatCount => "共重复几次，例如 10",
        CsvField.Weekdays => "每周重复的星期，例如 周一、三、五",
        CsvField.Color => "颜色：红 / 蓝 / #2563EB / 留空跟随分类",
        CsvField.AllDay => "是 / 否",
        CsvField.Remind => "30,15,5 / 提前10分钟 / 1小时 / 2025-01-01 08:00 / 不提醒",
        CsvField.Voice => "自定义播报文本，留空用全局模板",
        CsvField.Sound => "自定义提示音文件路径",
        CsvField.Status => "待办 / 已完成 / 已取消",
        CsvField.RepeatUntil => "重复到哪一天，例如 2026-12-31",
        _ => string.Empty,
    };

    /// <summary>
    /// 对照统一格式诊断表头：识别到了哪些列、哪些列不认识、规范里还差哪些。
    /// 导入向导用它给出"格式不匹配"的明确原因，而不是默默导入 0 条。
    /// </summary>
    public static CsvHeaderDiagnosis Diagnose(IReadOnlyList<string> headers)
    {
        var diagnosis = new CsvHeaderDiagnosis
        {
            Headers = headers.ToList(),
            IsCanonical = CsvSchema.IsCanonical(headers),
        };

        var map = AutoMap(headers);
        var mappedColumns = new HashSet<int>();

        foreach (var field in MatchOrder)
        {
            if (!map.TryGetValue(field, out var column)) continue;

            mappedColumns.Add(column);
            diagnosis.Recognized.Add(new CsvHeaderMatch
            {
                Column = column,
                Header = column >= 0 && column < headers.Count ? headers[column] : string.Empty,
                Field = field,
                CanonicalHeader = CsvSchema.HeaderOf(field),
            });
        }

        for (var i = 0; i < headers.Count; i++)
        {
            if (!mappedColumns.Contains(i)) diagnosis.Unknown.Add(headers[i]);
        }

        foreach (var column in CsvSchema.Columns)
        {
            if (map.ContainsKey(column.Field)) continue;

            diagnosis.Missing.Add(column.Field);
            if (column.Required) diagnosis.MissingRequired.Add(column.Field);
        }

        return diagnosis;
    }

    /// <summary>根据表头自动建立列映射。</summary>
    public static Dictionary<CsvField, int> AutoMap(IReadOnlyList<string> headers)
    {
        var result = new Dictionary<CsvField, int>();
        var used = new HashSet<int>();

        // 第一轮只接受强匹配（完全相等 / 前缀后缀），避免「日期」被「开始」这类弱包含规则抢走
        Assign(headers, used, result, minimumScore: 60);

        // 第二轮再让剩余字段使用弱包含匹配
        Assign(headers, used, result, minimumScore: 30);

        return result;
    }

    private static void Assign(
        IReadOnlyList<string> headers,
        HashSet<int> used,
        Dictionary<CsvField, int> result,
        int minimumScore)
    {
        foreach (var field in MatchOrder)
        {
            if (result.ContainsKey(field)) continue;

            var bestIndex = -1;
            var bestScore = minimumScore - 1;

            for (var i = 0; i < headers.Count; i++)
            {
                if (used.Contains(i)) continue;

                var score = Score(field, headers[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestScore >= minimumScore)
            {
                result[field] = bestIndex;
                used.Add(bestIndex);
            }
        }
    }

    /// <summary>为没有表头的文件生成默认映射（按常见列序）。</summary>
    public static Dictionary<CsvField, int> DefaultPositionalMap(int columnCount)
    {
        var result = new Dictionary<CsvField, int>();
        var order = new[] { CsvField.Title, CsvField.Start, CsvField.End, CsvField.Notes, CsvField.Category };
        for (var i = 0; i < order.Length && i < columnCount; i++)
        {
            result[order[i]] = i;
        }

        return result;
    }

    /// <summary>给单列表头打一个提示用的字段建议。</summary>
    public static CsvField Suggest(string header)
    {
        var best = CsvField.Ignore;
        var bestScore = 0;
        foreach (var field in MatchOrder)
        {
            var score = Score(field, header);
            if (score > bestScore)
            {
                bestScore = score;
                best = field;
            }
        }

        return best;
    }

    private static int Score(CsvField field, string header)
    {
        if (!Aliases.TryGetValue(field, out var aliases)) return 0;

        var key = TextNormalizer.Key(header);
        if (key.Length == 0) return 0;

        var best = 0;
        foreach (var alias in aliases)
        {
            var aliasKey = TextNormalizer.Key(alias);
            if (aliasKey.Length == 0) continue;

            if (key == aliasKey) best = Math.Max(best, 100);
            else if (key.StartsWith(aliasKey, StringComparison.Ordinal) || key.EndsWith(aliasKey, StringComparison.Ordinal)) best = Math.Max(best, 60);
            else if (key.Contains(aliasKey, StringComparison.Ordinal)) best = Math.Max(best, 40);
            else if (aliasKey.Contains(key, StringComparison.Ordinal) && key.Length >= 2) best = Math.Max(best, 30);
        }

        return best;
    }
}
