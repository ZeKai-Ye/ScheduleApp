using System.Text.Json.Nodes;
using System.Windows;
using Schedule.Core.Ai;
using Schedule.Core.Csv;
using Schedule.Core.Logging;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;

namespace Schedule.App.Services;

/// <summary>
/// 把 AI 的工具调用落到真实日程上。
///
/// 仓储、语音与 WPF 都要求 UI 线程访问，而对话是在后台线程推进的，
/// 因此这里所有操作都通过 <see cref="Invoke{T}"/> 切到 UI 线程（与本地接口同一套做法）。
/// </summary>
public sealed class ScheduleAiActions : IAiActions
{
    private readonly IAiAppContext _context;

    public ScheduleAiActions(IAiAppContext context) => _context = context;

    /// <summary>当前的 #1 / #2 编号表（每查一次列表就重新编号）。</summary>
    public AiItemReferences References { get; } = new();

    // ================= 查询 =================

    public AiToolResult ListItems(AiListQuery query) => Invoke(() =>
    {
        var now = DateTime.Now;
        var (from, to) = ResolveRange(query, now);

        References.Reset();

        var matched = new List<(ScheduleItem Item, ScheduleOccurrence Occurrence)>();

        foreach (var item in _context.Repository.Snapshot())
        {
            if (!query.IncludeDone && item.Status != ItemStatus.Pending) continue;

            if (!string.IsNullOrWhiteSpace(query.Category)
                && !string.Equals((item.Category ?? string.Empty).Trim(), query.Category!.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(query.Keyword) && !MatchesKeyword(item, query.Keyword!)) continue;

            var occurrences = OccurrenceCalculator.ExpandWithOverrides(item, from, to);
            if (occurrences.Count == 0) continue;

            var head = occurrences.OrderBy(o => o.Start).First();
            matched.Add((head.Item, head));
        }

        var ordered = matched.OrderBy(m => m.Occurrence.Start).Take(Math.Clamp(query.Limit, 1, 100)).ToList();
        var array = new JsonArray();

        foreach (var (item, occurrence) in ordered)
        {
            var reference = References.Register(item.Id.ToString(), occurrence.SeriesStart, item.Title);
            array.Add(DescribeItem(item, occurrence, reference));
        }

        var payload = new JsonObject
        {
            ["count"] = ordered.Count,
            ["range"] = from.ToString("yyyy-MM-dd HH:mm") + " ~ " + to.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm"),
            ["items"] = array,
        };

        if (ordered.Count == 0) payload["hint"] = "这个范围里没有日程。";

        var summary = ordered.Count == 0
            ? "这个范围里没有日程"
            : "查到 " + ordered.Count + " 条日程：" + string.Join("、", ordered.Take(3).Select(m => m.Item.Title));

        return AiToolResult.Ok(payload.ToJsonString(AiJson.Options), summary);
    });

    // ================= 写入 =================

    public AiToolResult CreateItem(AiItemDraft draft) => Invoke(() =>
    {
        var start = ParseDate(draft.Start, DateTime.Today);
        if (start == null)
        {
            return AiToolResult.Error("开始时间无法识别：" + draft.Start + "。请用 2026-09-15 15:00 这种格式，"
                + "也支持 明天15:00 / 下周三 上午10点。");
        }

        var allDay = draft.AllDay ?? false;
        if (allDay) start = start.Value.Date;

        var end = ParseDate(draft.End, start.Value);
        if (end == null || end <= start.Value)
        {
            end = allDay ? start.Value.AddDays(1) : start.Value.AddHours(1);
        }

        var settings = _context.Settings;
        var offsets = draft.ReminderOffsets != null
            ? draft.ReminderOffsets.Where(o => o is >= 0 and <= 60 * 24 * 365).Distinct().OrderByDescending(o => o).ToList()
            : new List<int>(settings.DefaultReminderOffsets);

        var item = new ScheduleItem
        {
            Title = (draft.Title ?? string.Empty).Trim(),
            Start = start.Value,
            End = end,
            AllDay = allDay,
            Category = (draft.Category ?? string.Empty).Trim(),
            Notes = (draft.Notes ?? string.Empty).Trim(),
            VoiceText = (draft.VoiceText ?? string.Empty).Trim(),
            Color = EventColors.Normalize(draft.Color),
            Status = ParseStatus(draft.Status) ?? ItemStatus.Pending,
            ReminderEnabled = draft.ReminderOffsets != null ? offsets.Count > 0 : settings.DefaultReminderEnabled,
            ReminderOffsets = offsets,
            RemindBeforeMinutes = offsets.Count > 0 ? offsets.Min() : settings.DefaultRemindBeforeMinutes,
            Origin = ItemOrigin.Manual,
        };

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0) return AiToolResult.Error("新建失败：" + string.Join("；", errors));

        _context.Repository.Add(item);
        _context.Repository.Save();
        LogAction("新建", item);

        var reference = References.Register(item.Id.ToString(), null, item.Title);

        return AiToolResult.Ok(new JsonObject
        {
            ["ok"] = true,
            ["ref"] = reference,
            ["summary"] = "已新建日程：" + item.Title,
            ["title"] = item.Title,
            ["start"] = item.Start.ToString("yyyy-MM-dd HH:mm"),
            ["end"] = item.End?.ToString("yyyy-MM-dd HH:mm"),
            ["reminders"] = item.ReminderEnabled ? ReminderText(item) : "不提醒",
        }.ToJsonString(AiJson.Options), "已新建「" + item.Title + "」" + item.Start.ToString("MM-dd HH:mm"));
    });

    public AiToolResult UpdateItem(string reference, AiItemDraft draft) => Invoke(() =>
    {
        var item = ResolveItem(reference, out var target, out var error);
        if (item == null || target == null) return error!;

        var notes = new List<string>();

        if (draft.Title != null)
        {
            if (string.IsNullOrWhiteSpace(draft.Title)) return AiToolResult.Error("标题不能为空。");
            item.Title = draft.Title.Trim();
            notes.Add("标题");
        }

        if (draft.Start != null)
        {
            var start = ParseDate(draft.Start, item.Start);
            if (start == null) return AiToolResult.Error("开始时间无法识别：" + draft.Start);

            var duration = item.End.HasValue && item.End.Value > item.Start ? item.End.Value - item.Start : TimeSpan.FromHours(1);
            var shifted = start.Value - item.Start;

            if (shifted != TimeSpan.Zero && item.HasOccurrenceEdits) item.Shift(shifted);

            item.Start = start.Value;
            if (draft.End == null) item.End = item.Start + duration;
            notes.Add("开始时间");
        }

        if (draft.End != null)
        {
            var end = ParseDate(draft.End, item.Start);
            if (end == null) return AiToolResult.Error("结束时间无法识别：" + draft.End);
            if (end.Value <= item.Start) end = item.Start.AddHours(1);

            item.End = end;
            notes.Add("结束时间");
        }

        if (draft.AllDay.HasValue)
        {
            item.AllDay = draft.AllDay.Value;
            if (item.AllDay)
            {
                item.Start = item.Start.Date;
                if (item.End.HasValue) item.End = item.End.Value.Date.AddDays(1);
            }

            notes.Add("全天");
        }

        if (draft.Category != null)
        {
            item.Category = draft.Category.Trim();
            notes.Add("分类");
        }

        if (draft.Notes != null)
        {
            item.Notes = draft.Notes;
            notes.Add("备注");
        }

        if (draft.VoiceText != null)
        {
            item.VoiceText = draft.VoiceText.Trim();
            notes.Add("播报文案");
        }

        if (draft.Color != null)
        {
            item.Color = EventColors.Normalize(draft.Color);
            notes.Add("颜色");
        }

        if (draft.Status != null)
        {
            var status = ParseStatus(draft.Status);
            if (status == null) return AiToolResult.Error("状态只能是 pending / done / cancelled（或 待办 / 已完成 / 已取消）。");
            item.Status = status.Value;
            notes.Add("状态");
        }

        if (draft.ReminderOffsets != null)
        {
            var offsets = draft.ReminderOffsets.Where(o => o is >= 0 and <= 60 * 24 * 365).Distinct().OrderByDescending(o => o).ToList();
            item.ReminderOffsets = offsets;
            item.ReminderEnabled = offsets.Count > 0;
            item.RemindBeforeMinutes = offsets.Count > 0 ? offsets.Min() : item.RemindBeforeMinutes;
            item.PruneFiredReminders();
            notes.Add("提醒档位");
        }

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0) return AiToolResult.Error("修改失败：" + string.Join("；", errors));

        item.UpdatedAt = DateTime.Now;
        _context.Repository.Update(item);
        _context.Repository.Save();
        LogAction("修改", item);

        var payload = new JsonObject
        {
            ["ok"] = true,
            ["summary"] = "已修改：" + string.Join("、", notes),
            ["ref"] = References.Register(item.Id.ToString(), target.OccurrenceStart, item.Title),
            ["title"] = item.Title,
            ["start"] = item.Start.ToString("yyyy-MM-dd HH:mm"),
            ["end"] = item.End?.ToString("yyyy-MM-dd HH:mm"),
        };

        if (item.Repeat != RepeatKind.None)
        {
            payload["note"] = "这是重复日程，内容 / 时间改动作用于整个系列。";
        }

        return AiToolResult.Ok(payload.ToJsonString(AiJson.Options), "已修改「" + item.Title + "」的" + string.Join("、", notes));
    });

    public AiToolResult RescheduleItem(string reference, string newStart, string? newEnd) => Invoke(() =>
    {
        var item = ResolveItem(reference, out var target, out var error);
        if (item == null || target == null) return error!;

        var start = ParseDate(newStart, item.Start);
        if (start == null) return AiToolResult.Error("新的开始时间无法识别：" + newStart);

        if (item.AllDay) start = start.Value.Date;

        var duration = item.End.HasValue && item.End.Value > item.Start ? item.End.Value - item.Start : TimeSpan.FromHours(1);
        var end = ParseDate(newEnd, start.Value) ?? start.Value + duration;
        if (end <= start.Value) end = start.Value + duration;

        var onlyThisTime = item.Repeat != RepeatKind.None && target.OccurrenceStart.HasValue;

        if (onlyThisTime)
        {
            var occurrenceStart = target.OccurrenceStart!.Value;
            var effective = OccurrenceCalculator.Create(item, occurrenceStart).Item.Clone();
            effective.Start = start.Value;
            effective.End = end;
            effective.RemindAtOverride = null;

            item.UpsertOverride(OccurrenceOverride.FromItem(occurrenceStart, effective, item));
        }
        else
        {
            var delta = start.Value - item.Start;

            // 整体平移：已记录的「仅本次」例外键一起搬走，否则改期后全都对不上
            if (delta != TimeSpan.Zero && item.HasOccurrenceEdits) item.Shift(delta);

            item.Start = start.Value;
            item.End = end;
        }

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0) return AiToolResult.Error("改期失败：" + string.Join("；", errors));

        item.UpdatedAt = DateTime.Now;
        _context.Repository.Update(item);
        _context.Repository.Save();
        LogAction(onlyThisTime ? "改期（仅这一次）" : "改期", item);

        var payload = new JsonObject
        {
            ["ok"] = true,
            ["title"] = item.Title,
            ["start"] = start.Value.ToString("yyyy-MM-dd HH:mm"),
            ["end"] = end.ToString("yyyy-MM-dd HH:mm"),
            ["scope"] = onlyThisTime ? "仅这一次" : "整个日程",
            ["summary"] = "已改到 " + start.Value.ToString("MM-dd HH:mm") + (onlyThisTime ? "（只改这一次）" : string.Empty),
        };

        return AiToolResult.Ok(payload.ToJsonString(AiJson.Options), payload["summary"]!.GetValue<string>());
    });

    public AiToolResult DeleteItem(string reference) => Invoke(() =>
    {
        var target = References.TryResolve(reference);
        if (target == null) return AiToolResult.Error(References.DescribeUnknown(reference));

        if (!Guid.TryParse(target.Id, out var id)) return AiToolResult.Error("日程 id 不合法：" + target.Id);

        var item = _context.Repository.Find(id);
        if (item == null) return AiToolResult.Error("这条日程已经不存在了（可能刚被删掉）。");

        var title = item.Title;

        if (item.Repeat != RepeatKind.None && target.OccurrenceStart.HasValue)
        {
            item.ExcludeOccurrence(target.OccurrenceStart.Value);
            item.UpdatedAt = DateTime.Now;
            _context.Repository.Update(item);
            _context.Repository.Save();
            LogAction("删除本次", item);

            return AiToolResult.Ok(new JsonObject
            {
                ["ok"] = true,
                ["scope"] = "仅这一次",
                ["summary"] = "已删除 " + target.OccurrenceStart.Value.ToString("MM-dd HH:mm") + " 这一次，其它发生不受影响",
            }.ToJsonString(AiJson.Options), "已删除「" + title + "」的这一次");
        }

        _context.Repository.Remove(item.Id);
        _context.Repository.Save();
        LogAction("删除", item);

        return AiToolResult.Ok(new JsonObject
        {
            ["ok"] = true,
            ["scope"] = item.Repeat == RepeatKind.None ? "整条日程" : "整个系列",
            ["summary"] = "已删除：" + title,
        }.ToJsonString(AiJson.Options), "已删除「" + title + "」");
    });

    public AiToolResult SetStatus(string reference, string status) => Invoke(() =>
    {
        var item = ResolveItem(reference, out _, out var error);
        if (item == null) return error!;

        var parsed = ParseStatus(status);
        if (parsed == null) return AiToolResult.Error("状态只能是 pending / done / cancelled（或 待办 / 已完成 / 已取消）。");

        item.Status = parsed.Value;
        item.UpdatedAt = DateTime.Now;
        _context.Repository.Update(item);
        _context.Repository.Save();
        LogAction("标记 " + StatusText(parsed.Value), item);

        return AiToolResult.Ok(new JsonObject
        {
            ["ok"] = true,
            ["title"] = item.Title,
            ["status"] = StatusText(parsed.Value),
            ["summary"] = "已把「" + item.Title + "」标记为" + StatusText(parsed.Value),
        }.ToJsonString(AiJson.Options), "已把「" + item.Title + "」标记为" + StatusText(parsed.Value));
    });

    public AiToolResult NextReminders(int count) => Invoke(() =>
    {
        var items = _context.Repository.Snapshot();
        var upcoming = ReminderPlanner.Upcoming(items, DateTime.Now, Math.Clamp(count, 1, 50));

        var array = new JsonArray();
        foreach (var due in upcoming)
        {
            var reference = References.Register(due.Occurrence.Series.Id.ToString(), due.Occurrence.SeriesStart, due.Item.Title);
            array.Add(new JsonObject
            {
                ["ref"] = reference,
                ["title"] = due.Item.Title,
                ["start"] = due.Occurrence.Start.ToString("yyyy-MM-dd HH:mm"),
                ["fireAt"] = due.Plan.FireAt.ToString("yyyy-MM-dd HH:mm"),
                ["offset"] = due.Plan.OffsetText,
                ["category"] = string.IsNullOrWhiteSpace(due.Item.Category) ? null : due.Item.Category,
            });
        }

        var payload = new JsonObject
        {
            ["count"] = upcoming.Count,
            ["now"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["reminders"] = array,
        };

        return AiToolResult.Ok(payload.ToJsonString(AiJson.Options),
            upcoming.Count == 0 ? "接下来没有提醒" : "接下来 " + upcoming.Count + " 个提醒");
    });

    public AiToolResult AppInfo() => Invoke(() =>
    {
        var items = _context.Repository.Snapshot();
        var today = items.Count(i => OccurrenceCalculator.ExpandWithOverrides(i, DateTime.Today, DateTime.Today.AddDays(1)).Count > 0);

        var payload = new JsonObject
        {
            ["app"] = "日程助手 ScheduleApp",
            ["version"] = _context.Version,
            ["dataDirectory"] = _context.DataDirectory,
            ["itemCount"] = items.Count,
            ["todayCount"] = today,
            ["pendingCount"] = items.Count(i => i.Status == ItemStatus.Pending),
            ["theme"] = Schedule.App.Services.ThemeService.Describe(_context.Settings.Theme),
            ["model"] = _context.Settings.AiModel,
            ["baseUrl"] = OpenAiCompatibleClient.Normalize(_context.Settings.AiBaseUrl),
            ["now"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };

        return AiToolResult.Ok(payload.ToJsonString(AiJson.Options), "共 " + items.Count + " 条日程，今天 " + today + " 条");
    });

    public AiToolResult SetTheme(string theme) => Invoke(() =>
    {
        var parsed = Schedule.App.Services.ThemeService.Parse(theme);
        if (parsed == null) return AiToolResult.Error("主题只能是 light / dark / system（或 浅色 / 夜间 / 跟随系统）。");

        _context.Settings.Theme = parsed.Value;
        _context.SaveSettings();

        return AiToolResult.Ok(new JsonObject
        {
            ["ok"] = true,
            ["theme"] = Schedule.App.Services.ThemeService.Describe(parsed.Value),
            ["summary"] = "主题已切换为" + Schedule.App.Services.ThemeService.Describe(parsed.Value),
        }.ToJsonString(AiJson.Options), "主题已切换为" + Schedule.App.Services.ThemeService.Describe(parsed.Value));
    });

    // ================= 内部工具 =================

    private ScheduleItem? ResolveItem(string reference, out AiReferenceTarget? target, out AiToolResult? error)
    {
        target = References.TryResolve(reference);
        error = null;

        if (target == null)
        {
            error = AiToolResult.Error(References.DescribeUnknown(reference));
            return null;
        }

        if (!Guid.TryParse(target.Id, out var id))
        {
            error = AiToolResult.Error("日程 id 不合法：" + target.Id);
            return null;
        }

        var item = _context.Repository.Find(id);
        if (item == null)
        {
            error = AiToolResult.Error("这条日程已经不存在了（可能刚被删掉）。请重新 list_items。");
            return null;
        }

        return item;
    }

    private static (DateTime From, DateTime To) ResolveRange(AiListQuery query, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(query.Date))
        {
            var day = ParseDate(query.Date, now) ?? now;
            return (day.Date, day.Date.AddDays(1));
        }

        var from = ParseDate(query.From, now) ?? now;
        var to = ParseDate(query.To, from.AddDays(30)) ?? from.AddDays(30);

        // 只给了日期（没有时间）就理解成"含当天整天"
        if (to.TimeOfDay == TimeSpan.Zero) to = to.Date.AddDays(1);
        if (to <= from) to = from.AddDays(1);

        return (from, to);
    }

    /// <summary>宽容解析时间：支持 2026-09-15 15:00、明天15:00、下周三上午10点 等写法。</summary>
    public static DateTime? ParseDate(string? text, DateTime fallback)
        => FlexibleDateTime.TryParse(text, out var parsed, fallback) ? parsed : null;

    private static bool MatchesKeyword(ScheduleItem item, string keyword)
    {
        var needle = keyword.Trim();
        if (needle.Length == 0) return true;

        return Contains(item.Title, needle) || Contains(item.Notes, needle) || Contains(item.Category, needle);

        static bool Contains(string? text, string needle)
            => !string.IsNullOrEmpty(text) && text.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
    }

    private static JsonObject DescribeItem(ScheduleItem item, ScheduleOccurrence occurrence, string reference)
    {
        var node = new JsonObject
        {
            ["ref"] = reference,
            ["title"] = item.Title,
            ["start"] = occurrence.Start.ToString("yyyy-MM-dd HH:mm"),
            ["end"] = occurrence.End.ToString("yyyy-MM-dd HH:mm"),
            ["status"] = StatusText(item.Status),
        };

        if (item.AllDay) node["allDay"] = true;
        if (!string.IsNullOrWhiteSpace(item.Category)) node["category"] = item.Category;

        var repeat = RepeatText(item);
        if (repeat.Length > 0) node["repeat"] = repeat;

        node["reminders"] = item.ReminderEnabled ? ReminderText(item) : "不提醒";

        if (!string.IsNullOrWhiteSpace(item.Notes)) node["notes"] = Shorten(item.Notes, 80);

        return node;
    }

    private static string RepeatText(ScheduleItem item) => item.Repeat switch
    {
        RepeatKind.None => string.Empty,
        RepeatKind.Daily => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 天" : "每天",
        RepeatKind.Weekdays => "每个工作日",
        RepeatKind.Weekly => (item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 周" : "每周")
            + (item.RepeatWeekdays.Count > 0
                ? "（" + string.Join("、", item.RepeatWeekdays.OrderBy(d => d).Select(WeekdayText)) + "）"
                : string.Empty),
        RepeatKind.Monthly => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 个月" : "每月",
        RepeatKind.Yearly => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 年" : "每年",
        _ => string.Empty,
    };

    private static string WeekdayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };

    private static string ReminderText(ScheduleItem item)
    {
        var offsets = item.NormalizedOffsets();
        if (offsets.Count == 0) return item.RemindAtOverride.HasValue ? "指定时刻 " + item.RemindAtOverride.Value.ToString("MM-dd HH:mm") : "无";

        // DescribeOffset 自己已经带「提前」前缀，这里别再加一次（否则会变成「提前 提前 30 分钟」）
        return string.Join(" / ", offsets.Select(ReminderPlan.DescribeOffset));
    }

    private static string StatusText(ItemStatus status) => status switch
    {
        ItemStatus.Done => "已完成",
        ItemStatus.Canceled => "已取消",
        _ => "待办",
    };

    private static ItemStatus? ParseStatus(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "pending" or "todo" or "待办" or "未完成" => ItemStatus.Pending,
        "done" or "completed" or "complete" or "已完成" or "完成" => ItemStatus.Done,
        "cancelled" or "canceled" or "已取消" or "取消" => ItemStatus.Canceled,
        _ => null,
    };

    private static string Shorten(string text, int max)
    {
        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= max ? single : single.Substring(0, max) + "…";
    }

    private void LogAction(string action, ScheduleItem item)
    {
        if (!_context.Settings.AiLogActions) return;
        AppLog.Info("AI " + action + "：" + item.Title + " @ " + item.Start.ToString("yyyy-MM-dd HH:mm"));
    }

    /// <summary>切到 UI 线程执行（仓储与 WPF 都要求同线程）。</summary>
    private static T Invoke<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) return action();

        return dispatcher.Invoke(action);
    }
}
