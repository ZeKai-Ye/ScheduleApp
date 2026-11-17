using Schedule.Core.Localization;
using Schedule.Core.Models;
using Schedule.Core.Services;

namespace Schedule.Core.Ics;

/// <summary>
/// 把解析出来的 VEVENT 映射成程序内的 <see cref="ScheduleItem"/>。
/// 所有"能认但支持不了"的情况都会写进 warnings，绝不静默丢信息。
/// </summary>
public static class IcsMapper
{
    public static List<ScheduleItem> Map(
        IcsParseResult parsed,
        IcsImportOptions options,
        DateTime now,
        string sourceTag,
        List<string> warnings,
        List<ImportError> errors)
    {
        var items = new List<ScheduleItem>();
        var groups = new Dictionary<string, List<IcsEvent>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var ev in parsed.Events)
        {
            var uid = ev.Uid;
            if (uid.Length == 0) uid = "#" + ev.Line; // 没有 UID 的按独立日程处理

            if (!groups.TryGetValue(uid, out var list))
            {
                list = new List<IcsEvent>();
                groups[uid] = list;
                order.Add(uid);
            }

            list.Add(ev);
        }

        foreach (var uid in order)
        {
            var events = groups[uid];
            var master = events.FirstOrDefault(e => !e.IsOverride) ?? events[0];

            var item = MapOne(master, options, now, sourceTag, warnings, errors);
            if (item == null) continue;

            // RECURRENCE-ID：系列里被单独改过的那几次
            foreach (var edited in events.Where(e => e.IsOverride))
            {
                var keyProperty = edited.Find("RECURRENCE-ID");
                if (keyProperty == null) continue;

                if (!IcsReader.TryParseDate(keyProperty, out var originalStart, out _, out var isUtc))
                {
                    warnings.Add(Loc.F("「{0}」有一次单独修改的 RECURRENCE-ID 无法解析，已忽略这一次的修改。", "\"{0}\" has a RECURRENCE-ID change that cannot be parsed; that change was ignored.", item.Title));
                    continue;
                }

                originalStart = IcsReader.ToLocal(originalStart, isUtc);

                if (item.Repeat == RepeatKind.None)
                {
                    warnings.Add(Loc.F("「{0}」不是重复日程，但文件里带了 RECURRENCE-ID，已忽略该次修订。", "\"{0}\" is not a repeating event, but the file contains a RECURRENCE-ID; that revision was ignored.", item.Title));
                    continue;
                }

                var effective = MapOne(edited, options, now, sourceTag, warnings, errors);
                if (effective == null) continue;

                item.UpsertOverride(OccurrenceOverride.FromItem(originalStart, effective, item));
            }

            items.Add(item);
        }

        return items;
    }

    private static ScheduleItem? MapOne(
        IcsEvent ev,
        IcsImportOptions options,
        DateTime now,
        string sourceTag,
        List<string> warnings,
        List<ImportError> errors)
    {
        var title = ev.Find("SUMMARY")?.Text.Trim() ?? string.Empty;
        var label = title.Length > 0 ? Loc.F("「{0}」", "\"{0}\"", title) : Loc.F("第 {0} 行的事件", "event on line {0}", ev.Line);

        if (!IcsReader.TryParseDate(ev.Find("DTSTART"), out var start, out var allDay, out var startIsUtc))
        {
            errors.Add(new ImportError
            {
                Line = ev.Line,
                Message = Loc.T("缺少或无法解析 DTSTART（开始时间）", "Missing or unparsable DTSTART (start time)"),
                Raw = Shorten(ev.Get("SUMMARY")),
            });

            return null;
        }

        start = IcsReader.ToLocal(start, startIsUtc);

        if (title.Length == 0)
        {
            title = Loc.T("(无标题)", "(Untitled)");
            warnings.Add(Loc.F("{0}：没有 SUMMARY（标题），已按「{1}」导入。", "{0}: no SUMMARY (title), imported as \"{1}\".", label, title));
        }

        var item = new ScheduleItem
        {
            Title = title,
            Start = allDay ? start.Date : start,
            AllDay = allDay,
            Category = FirstCategory(ev, warnings, label),
            Origin = ItemOrigin.IcsImport,
            Source = sourceTag,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (item.Category.Length == 0) item.Category = options.DefaultCategory;

        // 结束时间：DTEND 优先，其次 DURATION；全天事件 DTEND 是"次日"（与我们的模型一致）
        var endProperty = ev.Find("DTEND");
        if (endProperty != null && IcsReader.TryParseDate(endProperty, out var end, out _, out var endIsUtc))
        {
            // iCalendar 的 DTEND 对全天事件本来就是"次日"（exclusive），与我们的 End 语义一致，别再 +1 天
            item.End = IcsReader.ToLocal(end, endIsUtc);
        }
        else
        {
            var duration = IcsReader.ParseDuration(ev.Get("DURATION"), out _);
            if (duration > TimeSpan.Zero)
            {
                item.End = item.Start + duration;
            }
            else if (item.AllDay)
            {
                item.End = item.Start.AddDays(1);
            }
            else
            {
                item.End = item.Start.AddHours(1);
            }
        }

        if (item.End.HasValue && item.End.Value <= item.Start) item.End = item.Start.AddHours(1);

        // 备注：DESCRIPTION + 地点
        var notes = new List<string>();
        var description = ev.Find("DESCRIPTION")?.Text.Trim();
        if (!string.IsNullOrEmpty(description)) notes.Add(description);

        var location = ev.Find("LOCATION")?.Text.Trim();
        if (!string.IsNullOrEmpty(location)) notes.Add(Loc.F("地点：{0}", "Location: {0}", location));

        item.Notes = string.Join(Environment.NewLine, notes);

        // 状态 / 优先级
        item.Status = (ev.Get("STATUS").Trim().ToUpperInvariant()) switch
        {
            "CANCELLED" => ItemStatus.Canceled,
            "COMPLETED" => ItemStatus.Done,
            _ => ItemStatus.Pending,
        };

        if (int.TryParse(ev.Get("PRIORITY").Trim(), out var priority))
        {
            item.Priority = priority switch
            {
                >= 1 and <= 4 => Priority.High,
                >= 6 and <= 9 => Priority.Low,
                _ => Priority.Normal,
            };
        }

        // 重复规则 + 例外日期
        var rule = ev.Get("RRULE");
        if (rule.Length > 0)
        {
            var spec = IcsReader.ParseRule(rule, item.Start, warnings, label);
            item.Repeat = spec.Kind;
            item.RepeatInterval = Math.Max(1, spec.Interval);
            item.RepeatWeekdays = new List<DayOfWeek>(spec.Weekdays);
            item.RepeatCount = spec.Count;
            item.RepeatUntil = spec.Until;

            if (spec.Kind == RepeatKind.None)
            {
                warnings.Add(Loc.F("{0}：重复规则 {1} 暂不支持，已按单次日程导入。", "{0}: repeat rule {1} is not supported yet, imported as a single event.", label, rule));
            }
        }

        foreach (var exdate in ev.FindAll("EXDATE"))
        {
            foreach (var part in exdate.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var single = new IcsProperty { Name = "EXDATE", Value = part.Trim(), Parameters = exdate.Parameters };
                if (!IcsReader.TryParseDate(single, out var excluded, out _, out var excludedIsUtc)) continue;

                item.ExcludedOccurrences.Add(IcsReader.ToLocal(excluded, excludedIsUtc));
            }
        }

        ApplyAlarms(ev, item, warnings, label);

        var problems = ScheduleItemValidator.Validate(item);
        if (problems.Count > 0)
        {
            errors.Add(new ImportError
            {
                Line = ev.Line,
                Message = string.Join(Loc.T("；", "; "), problems),
                Raw = Shorten(item.Title),
            });

            return null;
        }

        return item;
    }

    /// <summary>VALARM 的 TRIGGER → 提醒档位（相对开始时间的分钟数）。</summary>
    private static void ApplyAlarms(IcsEvent ev, ScheduleItem item, List<string> warnings, string label)
    {
        var offsets = new List<int>();
        DateTime? absolute = null;

        foreach (var alarm in ev.Alarms)
        {
            var trigger = alarm.FirstOrDefault(p => string.Equals(p.Name, "TRIGGER", StringComparison.OrdinalIgnoreCase));
            if (trigger == null) continue;

            var isAbsolute = string.Equals(trigger.Parameter("VALUE"), "DATE-TIME", StringComparison.OrdinalIgnoreCase);

            if (isAbsolute)
            {
                if (IcsReader.TryParseDate(trigger, out var when, out _, out var isUtc))
                {
                    absolute = IcsReader.ToLocal(when, isUtc);
                }

                continue;
            }

            // PT0M 也是合法的（= 准点），必须与"根本不是时长"区分开，否则会丢掉准点提醒
            if (!IcsReader.TryParseDuration(trigger.Value, out var duration, out _)) continue;

            // 以开始时间为基准：-PT30M 表示提前 30 分钟
            var minutes = (int)Math.Round(-duration.TotalMinutes);

            if (minutes < 0)
            {
                warnings.Add(Loc.F("{0}：提醒写在开始之后（TRIGGER={1}），已调整为「准点」。", "{0}: the reminder is after the start (TRIGGER={1}), adjusted to \"On time\".", label, trigger.Value));
                minutes = 0;
            }

            if (minutes > 60 * 24 * 365) minutes = 60 * 24 * 365;

            if (!offsets.Contains(minutes)) offsets.Add(minutes);
        }

        if (offsets.Count > 0)
        {
            offsets.Sort((a, b) => b.CompareTo(a));
            item.ReminderEnabled = true;
            item.ReminderOffsets = offsets;
            item.RemindBeforeMinutes = offsets.Min();
            return;
        }

        if (absolute.HasValue)
        {
            item.ReminderEnabled = true;
            item.RemindAtOverride = absolute;
        }
    }

    private static string FirstCategory(IcsEvent ev, List<string> warnings, string label)
    {
        var raw = ev.Find("CATEGORIES")?.Text.Trim() ?? string.Empty;
        if (raw.Length == 0) return string.Empty;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return string.Empty;

        if (parts.Count > 1) warnings.Add(Loc.F("{0}：有多个分类（{1}），已取第一个：「{2}」。", "{0}: multiple categories ({1}), using the first one: \"{2}\".", label, raw, parts[0]));

        return parts[0];
    }

    private static string Shorten(string? text, int max = 120)
    {
        var value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value.Substring(0, max) + "…";
    }
}
