using System.Text.Json;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;

namespace Schedule.Core.Tests.Cases;

/// <summary>系列事件的「仅本次」例外：删除某一次、改期某一次。</summary>
public static class OccurrenceExceptionTests
{
    public static void Run()
    {
        TestRunner.Section("系列例外");
        ExcludeOne();
        Reschedule();
        EditOne();
        FiredBookkeeping();
        Layout();
        NormalizeAndValidate();
        PoisonedSingleItem();
        ApiOccurrence();
    }

    /// <summary>接口层的「仅本次」操作（真实 HTTP）。</summary>
    private static void ApiOccurrence()
    {
        var dir = TestPaths.NewTempDir();
        Schedule.Core.Storage.AppPaths.Initialize(dir);

        var repository = new Schedule.Core.Services.ScheduleRepository(Path.Combine(dir, "schedules.json"));
        repository.Load();
        var settings = new Schedule.Core.Services.SettingsService(Path.Combine(dir, "settings.json"));
        settings.Load();
        settings.Current.ApiToken = "occ-token";

        var api = new Schedule.Core.Api.ScheduleApi(repository, settings, new Schedule.Core.Api.NullApiActions(),
            () => new DateTime(2025, 6, 2, 8, 0, 0), "test");

        using var server = new Schedule.Core.Api.MiniHttpServer(0, api.Handle);
        if (!server.Start(out var error))
        {
            TestRunner.Check(false, "系列例外接口测试启动失败：" + error);
            return;
        }

        using var client = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + server.Port),
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Add("X-Api-Token", "occ-token");

        string Send(string method, string path, string? json = null)
        {
            using var request = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod(method), path);
            if (json != null) request.Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = client.Send(request, System.Net.Http.HttpCompletionOption.ResponseContentRead);
            request.Headers.ToString();
            return ((int)response.StatusCode) + "|" + response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        try
        {
            var created = Send("POST", "/api/items", """
            {
              "title": "接口系列",
              "start": "2025-06-02 10:00",
              "end": "2025-06-02 11:00",
              "repeat": "Weekly",
              "reminderOffsets": [30]
            }
            """);

            TestRunner.Check(created.StartsWith("201|", StringComparison.Ordinal), "接口建系列返回 201");
            using var createdDocument = JsonDocument.Parse(created[(created.IndexOf('|') + 1)..]);
            var id = createdDocument.RootElement.GetProperty("item").GetProperty("id").GetGuid();

            // 只删除 6/9 那一次
            var deleted = Send("POST", "/api/items/" + id + "/occurrence/delete", """{ "occurrenceStart": "2025-06-09 10:00" }""");
            TestRunner.Check(deleted.StartsWith("200|", StringComparison.Ordinal), "只删除本次返回 200");
            TestRunner.Equal(1, repository.Find(id)!.ExcludedOccurrences.Count, "删除记录已落库");

            var afterDelete = Send("GET", "/api/occurrences?from=2025-06-01&to=2025-07-01&q=接口系列");
            using (var document = JsonDocument.Parse(afterDelete[(afterDelete.IndexOf('|') + 1)..]))
            {
                var occurrences = document.RootElement.GetProperty("occurrences");
                TestRunner.Equal(4, occurrences.GetArrayLength(), "删除一次后接口展开剩 4 次");
            }

            // 只改期 6/16 → 6/18
            var rescheduled = Send("POST", "/api/items/" + id + "/occurrence",
                """{ "originalStart": "2025-06-16 10:00", "newStart": "2025-06-18 15:00", "newEnd": "2025-06-18 16:00" }""");
            TestRunner.Check(rescheduled.StartsWith("200|", StringComparison.Ordinal), "改期本次返回 200");

            var stored = repository.Find(id)!;
            TestRunner.Equal(1, stored.OccurrenceOverrides.Count, "覆盖已落库");
            TestRunner.Equal(new DateTime(2025, 6, 18, 15, 0, 0), stored.OccurrenceOverrides[0].NewStart!.Value, "落库的是新时间");

            var afterReschedule = Send("GET", "/api/occurrences?from=2025-06-15&to=2025-06-22&q=接口系列");
            using (var document = JsonDocument.Parse(afterReschedule[(afterReschedule.IndexOf('|') + 1)..]))
            {
                var occurrences = document.RootElement.GetProperty("occurrences");
                TestRunner.Check(
                    occurrences.EnumerateArray().Any(o => o.GetProperty("start").GetString() == "2025-06-18T15:00:00"),
                    "改期后的时间出现在接口展开结果里");
            }

            // 撤销
            var restored = Send("POST", "/api/items/" + id + "/occurrence/restore", """{ "occurrenceStart": "2025-06-16 10:00" }""");
            TestRunner.Check(restored.StartsWith("200|", StringComparison.Ordinal), "撤销本次例外返回 200");
            TestRunner.Equal(0, repository.Find(id)!.OccurrenceOverrides.Count, "覆盖已清除");

            // 错误分支
            var notRecurring = Send("POST", "/api/items", """{ "title": "单次", "start": "2025-06-03 09:00" }""");
            using (var document = JsonDocument.Parse(notRecurring[(notRecurring.IndexOf('|') + 1)..]))
            {
                var singleId = document.RootElement.GetProperty("item").GetProperty("id").GetGuid();
                var bad = Send("POST", "/api/items/" + singleId + "/occurrence/delete", """{ "occurrenceStart": "2025-06-03 09:00" }""");
                TestRunner.Check(bad.StartsWith("422|", StringComparison.Ordinal), "非重复日程返回 422");
            }

            var missing = Send("POST", "/api/items/" + id + "/occurrence/delete", """{ "occurrenceStart": "2025-06-05 10:00" }""");
            TestRunner.Check(missing.StartsWith("404|", StringComparison.Ordinal), "不存在的那一次返回 404");

            var empty = Send("POST", "/api/items/" + id + "/occurrence", """{ "originalStart": "2025-06-23 10:00" }""");
            TestRunner.Check(empty.StartsWith("422|", StringComparison.Ordinal), "空覆盖返回 422");

            var wrongMethod = Send("GET", "/api/items/" + id + "/occurrence");
            TestRunner.Check(wrongMethod.StartsWith("405|", StringComparison.Ordinal), "GET 返回 405");
        }
        finally
        {
            server.Stop();
        }
    }

    private static ScheduleItem WeeklySeries()
    {
        return new ScheduleItem
        {
            Title = "每周站会",
            Start = new DateTime(2025, 6, 2, 10, 0, 0), // 周一
            End = new DateTime(2025, 6, 2, 11, 0, 0),
            Category = "工作",
            Repeat = RepeatKind.Weekly,
            ReminderOffsets = new List<int> { 30, 10 },
            ReminderVoiceTexts = new Dictionary<int, string> { [30] = "还有 30 分钟" },
        };
    }

    private static void ExcludeOne()
    {
        var series = WeeklySeries();
        var all = OccurrenceCalculator.Expand(series, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30, 23, 59, 0));
        TestRunner.Equal(5, all.Count, "6 月共 5 次（6/2 6/9 6/16 6/23 6/30）");

        series.ExcludeOccurrence(new DateTime(2025, 6, 16, 10, 0, 0));

        var after = OccurrenceCalculator.Expand(series, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30, 23, 59, 0));
        TestRunner.Equal(4, after.Count, "删除一次后剩 4 次");
        TestRunner.Check(after.All(o => o.Start != new DateTime(2025, 6, 16, 10, 0, 0)), "被删的那次不再出现");
        TestRunner.Check(series.HasOccurrenceEdits, "标记为有本次例外");

        // Next 也要跳过被删的那次
        var next = OccurrenceCalculator.Next(series, new DateTime(2025, 6, 15, 0, 0, 0));
        TestRunner.NotNull(next, "Next 有结果");
        TestRunner.Equal(new DateTime(2025, 6, 23, 10, 0, 0), next!.Start, "Next 跳过被删的 6/16");

        // 撤销
        TestRunner.Check(series.ClearOccurrenceEdit(new DateTime(2025, 6, 16, 10, 0, 0)), "撤销成功");
        TestRunner.Equal(5, OccurrenceCalculator.Expand(series, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30, 23, 59, 0)).Count, "撤销后恢复 5 次");
        TestRunner.Check(!series.HasOccurrenceEdits, "例外已清空");
    }

    private static void Reschedule()
    {
        var series = WeeklySeries();
        var original = new DateTime(2025, 6, 9, 10, 0, 0);

        series.UpsertOverride(new OccurrenceOverride
        {
            OriginalStart = original,
            NewStart = new DateTime(2025, 6, 11, 15, 0, 0),
            NewEnd = new DateTime(2025, 6, 11, 16, 30, 0),
        });

        var occurrences = OccurrenceCalculator.Expand(series, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30, 23, 59, 0));
        var moved = occurrences.FirstOrDefault(o => o.IsOverride);

        TestRunner.NotNull(moved, "找到改期后的发生");
        TestRunner.Equal(new DateTime(2025, 6, 11, 15, 0, 0), moved!.Start, "改到 6/11 15:00");
        TestRunner.Equal(new DateTime(2025, 6, 11, 16, 30, 0), moved.End, "结束时间同步");
        TestRunner.Equal(original, moved.OriginalStart!.Value, "保留原始开始时间");
        TestRunner.Equal(original, moved.SeriesStart, "SeriesStart 指向原始时间");
        TestRunner.Check(ReferenceEquals(moved.Series, series), "Series 指向系列本体");
        TestRunner.Check(!ReferenceEquals(moved.Item, series), "Item 是套用覆盖的副本");
        TestRunner.Equal("每周站会", series.Title, "系列本体未被改动");

        // 提醒时刻基于新时间
        var plan30 = moved.Reminders.FirstOrDefault(p => p.OffsetMinutes == 30);
        TestRunner.NotNull(plan30, "改期后仍有 30 分钟档");
        TestRunner.Equal(new DateTime(2025, 6, 11, 14, 30, 0), plan30!.FireAt, "提醒按新时间计算");

        // 只改时间
        TestRunner.Check(series.OccurrenceOverrides[0].IsRescheduleOnly, "识别为纯改期");
        TestRunner.Check(series.OccurrenceOverrides[0].Describe().Contains("改期至", StringComparison.Ordinal), "改期描述");

        // 未改期的其它次不受影响
        TestRunner.Equal(1, occurrences.Count(o => o.IsOverride), "只有一次被覆盖");
        TestRunner.Check(occurrences.Any(o => o.Start == new DateTime(2025, 6, 2, 10, 0, 0) && !o.IsOverride), "其它次保持原时间");

        // 改期 + 时长继承（不给 NewEnd）
        var series2 = WeeklySeries();
        series2.UpsertOverride(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 10, 9, 0, 0),
        });

        var moved2 = OccurrenceCalculator.Expand(series2, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30))
            .First(o => o.IsOverride);
        TestRunner.Equal(new DateTime(2025, 6, 10, 10, 0, 0), moved2.End, "未给结束时间时继承原时长 1 小时");
    }

    private static void EditOne()
    {
        var series = WeeklySeries();
        var original = new DateTime(2025, 6, 9, 10, 0, 0);

        series.UpsertOverride(new OccurrenceOverride
        {
            OriginalStart = original,
            Title = "站会（改到线上）",
            Notes = "本次用腾讯会议",
            ReminderOffsets = new List<int> { 5 },
            Status = ItemStatus.Done,
        });

        var instance = OccurrenceCalculator.Expand(series, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30))
            .First(o => o.IsOverride);

        TestRunner.Equal("站会（改到线上）", instance.Item.Title, "有效日程标题被覆盖");
        TestRunner.Equal("本次用腾讯会议", instance.Item.Notes, "备注被覆盖");
        TestRunner.Equal("每周站会", series.Title, "系列标题不变");
        TestRunner.Equal("工作", instance.Item.Category, "未覆盖的分类沿用系列");
        TestRunner.Equal(1, instance.Reminders.Count, "档位被覆盖为单一档位");
        TestRunner.Equal(ItemStatus.Done, instance.Item.Status, "状态被覆盖");
        TestRunner.Equal(original, instance.Start, "时间未变");

        // 从编辑结果反推覆盖
        var edited = series.Clone();
        edited.Title = "临时改名";
        edited.Start = new DateTime(2025, 6, 12, 8, 0, 0);

        var derived = OccurrenceOverride.FromItem(original, edited, series);
        TestRunner.Equal(original, derived.OriginalStart, "反推保留原始时间");
        TestRunner.Equal(new DateTime(2025, 6, 12, 8, 0, 0), derived.NewStart!.Value, "反推识别改期");
        TestRunner.Equal("临时改名", derived.Title, "反推识别标题变化");
        TestRunner.Check(derived.Status is null, "未变化的字段不写入覆盖");

        // 空覆盖不入库
        var series3 = WeeklySeries();
        series3.UpsertOverride(new OccurrenceOverride { OriginalStart = original });
        TestRunner.Equal(0, series3.OccurrenceOverrides.Count, "空覆盖被忽略");
    }

    private static void FiredBookkeeping()
    {
        var series = WeeklySeries();
        var original = new DateTime(2025, 6, 9, 10, 0, 0);
        var newStart = new DateTime(2025, 6, 11, 15, 0, 0);

        series.UpsertOverride(new OccurrenceOverride { OriginalStart = original, NewStart = newStart, NewEnd = newStart.AddHours(1) });

        var moved = OccurrenceCalculator.ExpandWithOverrides(series, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30, 23, 59, 0))
            .First(o => o.IsOverride);

        // 用改期后的时间窗口收集提醒
        var due = ReminderPlanner.CollectDue(
            new[] { series },
            newStart.AddMinutes(-31),
            newStart.AddMinutes(-30));

        TestRunner.Equal(1, due.Count, "改期后按新时间触发提醒");
        if (due.Count == 0)
        {
            TestRunner.Check(false, "改期后未收集到提醒");
            return;
        }

        TestRunner.Equal(30, due[0].Plan.OffsetMinutes, "档位正确");
        TestRunner.Equal("每周站会", due[0].Item.Title, "提醒携带有效日程");

        ReminderPlanner.MarkFired(due[0].Item, due[0]);

        // 已触发记录必须写在系列本体上，否则会重复提醒
        TestRunner.Equal(1, series.FiredReminders.Count, "触发记录写入系列本体");
        TestRunner.Equal(newStart, series.FiredReminders[0].OccurrenceStart, "记录的是改期后的时间");
        TestRunner.Equal(0, moved.Item.FiredReminders.Count, "副本上没有残留记录");

        TestRunner.Equal(0, ReminderPlanner.CollectDue(
            new[] { series }, newStart.AddMinutes(-31), newStart.AddMinutes(-30)).Count, "不会重复触发");

        // 绝对提醒不套用到改期后的本次
        var withAbsolute = WeeklySeries();
        withAbsolute.RemindAtOverride = new DateTime(2025, 6, 2, 8, 0, 0);
        withAbsolute.UpsertOverride(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 11, 15, 0, 0),
        });

        var absoluteInstance = OccurrenceCalculator.Expand(withAbsolute, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30))
            .First(o => o.IsOverride);
        TestRunner.Check(absoluteInstance.Reminders.All(p => p.FireAt != new DateTime(2025, 6, 2, 8, 0, 0)), "改期后不再套用绝对提醒");
    }

    private static void Layout()
    {
        var series = WeeklySeries();
        series.UpsertOverride(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 11, 15, 0, 0),
            NewEnd = new DateTime(2025, 6, 11, 16, 0, 0),
        });

        var columns = CalendarLayout.BuildWeek(new[] { series }, new DateTime(2025, 6, 9), new DateTime(2025, 6, 9));

        var monday = columns[0];
        var wednesday = columns[2];

        TestRunner.Check(!monday.TimedBlocks.Any(b => b.StartMinutes == 600), "周一原位置不再有块");
        var movedBlock = wednesday.TimedBlocks.FirstOrDefault(b => b.StartMinutes == 900);
        TestRunner.NotNull(movedBlock, "周三 15:00 出现新块");
        TestRunner.Equal(60, movedBlock!.DurationMinutes, "块时长正确");

        var cells = CalendarLayout.BuildMonth(new[] { series }, 2025, 6, new DateTime(2025, 6, 9));
        TestRunner.Check(!cells.First(c => c.Date == new DateOnly(2025, 6, 9)).Events.Any(), "月视图 6/9 已无该事件");
        TestRunner.Check(cells.First(c => c.Date == new DateOnly(2025, 6, 11)).Events.Any(), "月视图 6/11 出现该事件");

        // 跨周改期：6/9 改到 6/18（下下周），下下周的周视图必须能看到它
        var crossWeek = WeeklySeries();
        crossWeek.UpsertOverride(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 18, 9, 30, 0),
            NewEnd = new DateTime(2025, 6, 18, 10, 30, 0),
        });

        var laterWeek = CalendarLayout.BuildWeek(new[] { crossWeek }, new DateTime(2025, 6, 16), new DateTime(2025, 6, 9));
        var wednesday18 = laterWeek[2];
        TestRunner.Check(wednesday18.TimedBlocks.Any(b => b.StartMinutes == 570), "跨周改期后在下下周的周视图可见");

        var laterMonth = CalendarLayout.BuildMonth(new[] { crossWeek }, 2025, 6, new DateTime(2025, 6, 9));
        TestRunner.Equal(1, laterMonth.First(c => c.Date == new DateOnly(2025, 6, 18)).Events.Count, "6/18 有改期来的那一次");
        TestRunner.Check(laterMonth.First(c => c.Date == new DateOnly(2025, 6, 9)).Events.Count == 0, "6/9 已被改走");
        TestRunner.Equal(1, laterMonth.First(c => c.Date == new DateOnly(2025, 6, 16)).Events.Count, "6/16 的那一次不受影响");
    }

    private static void NormalizeAndValidate()
    {
        var series = WeeklySeries();
        series.ExcludedOccurrences = new List<DateTime>
        {
            new(2025, 6, 9, 10, 0, 0),
            new(2025, 6, 9, 10, 0, 0),
            new(2025, 6, 16, 10, 0, 0),
        };
        series.OccurrenceOverrides = new List<OccurrenceOverride>
        {
            new() { OriginalStart = new DateTime(2025, 6, 23, 10, 0, 0), NewStart = new DateTime(2025, 6, 24, 10, 0, 0) },
            new() { OriginalStart = new DateTime(2025, 6, 30, 10, 0, 0) },
            new() { OriginalStart = new DateTime(2025, 6, 23, 10, 0, 0), NewEnd = new DateTime(2025, 6, 24, 12, 0, 0) },
        };

        var notes = ScheduleItemValidator.Normalize(series);
        TestRunner.Equal(2, series.ExcludedOccurrences.Count, "去重后的删除记录");
        TestRunner.Equal(1, series.OccurrenceOverrides.Count, "去重并丢弃空覆盖");
        TestRunner.Check(notes.Count > 0, "记录了清理说明");

        // 非重复日程不允许有例外
        var single = new ScheduleItem { Title = "单次", Start = new DateTime(2025, 6, 2, 10, 0, 0) };
        single.ExcludedOccurrences.Add(new DateTime(2025, 6, 2, 10, 0, 0));
        single.OccurrenceOverrides.Add(new OccurrenceOverride { OriginalStart = new DateTime(2025, 6, 2, 10, 0, 0), Title = "x" });

        ScheduleItemValidator.Normalize(single);
        TestRunner.Check(!single.HasOccurrenceEdits, "非重复日程的例外被清除");

        var dirty = WeeklySeries();
        dirty.OccurrenceOverrides.Add(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 11, 10, 0, 0),
        });

        dirty.Repeat = RepeatKind.None;
        TestRunner.Check(ScheduleItemValidator.Validate(dirty).Any(e => e.Contains("仅本次", StringComparison.Ordinal)), "校验拦截非重复日程上的例外");

        var badRange = WeeklySeries();
        badRange.OccurrenceOverrides.Add(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 11, 10, 0, 0),
            NewEnd = new DateTime(2025, 6, 11, 9, 0, 0),
        });
        TestRunner.Check(ScheduleItemValidator.Validate(badRange).Any(e => e.Contains("改期结束时间", StringComparison.Ordinal)), "校验拦截非法改期区间");
    }

    /// <summary>
    /// 回归：旧版本在非重复日程上执行「仅这一次」改期（拖动 / 改期对话框都会），
    /// 于是这条日程带着无意义的例外数据，之后再打开编辑窗口保存就永远被校验拦下。
    /// </summary>
    private static void PoisonedSingleItem()
    {
        var longEvent = new ScheduleItem
        {
            Title = "整车测试（8 小时）",
            Start = new DateTime(2025, 6, 2, 9, 0, 0),
            End = new DateTime(2025, 6, 2, 17, 0, 0),
        };

        longEvent.OccurrenceOverrides.Add(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 2, 9, 0, 0),
            NewStart = new DateTime(2025, 6, 2, 10, 0, 0),
        });

        TestRunner.Check(
            ScheduleItemValidator.Validate(longEvent).Any(e => e.Contains("仅本次", StringComparison.Ordinal)),
            "脏数据（非重复日程带例外）确实会被校验拦下");

        TestRunner.Check(ScheduleItemValidator.DropOccurrenceEditsWhenNotRepeating(longEvent), "自愈：识别出需要清理");
        TestRunner.Equal(0, longEvent.OccurrenceOverrides.Count, "例外已被清空");
        TestRunner.Equal(0, ScheduleItemValidator.Validate(longEvent).Count, "自愈后可以正常保存");
        TestRunner.Check(!ScheduleItemValidator.DropOccurrenceEditsWhenNotRepeating(longEvent), "没有例外时不做任何事");

        // 加载历史数据时的自愈路径（仓库 Load 会调用 Normalize）
        var onLoad = new ScheduleItem { Title = "长日程", Start = new DateTime(2025, 6, 3, 22, 0, 0), End = new DateTime(2025, 6, 4, 4, 0, 0) };
        onLoad.ExcludedOccurrences.Add(new DateTime(2025, 6, 3, 22, 0, 0));
        onLoad.OccurrenceOverrides.Add(new OccurrenceOverride { OriginalStart = new DateTime(2025, 6, 3, 22, 0, 0), Title = "x" });

        var notes = ScheduleItemValidator.Normalize(onLoad);
        TestRunner.Check(!onLoad.HasOccurrenceEdits, "Normalize 也会清掉非重复日程上的例外");
        TestRunner.Check(notes.Any(n => n.Contains("仅本次", StringComparison.Ordinal)), "并记录了清理说明");
        TestRunner.Equal(0, ScheduleItemValidator.Validate(onLoad).Count, "清理后通过校验");

        // 重复日程上的例外绝不能顺手清掉
        var series = WeeklySeries();
        series.OccurrenceOverrides.Add(new OccurrenceOverride
        {
            OriginalStart = new DateTime(2025, 6, 9, 10, 0, 0),
            NewStart = new DateTime(2025, 6, 11, 10, 0, 0),
        });

        TestRunner.Check(!ScheduleItemValidator.DropOccurrenceEditsWhenNotRepeating(series), "重复日程的例外不被清除");
        TestRunner.Equal(1, series.OccurrenceOverrides.Count, "重复日程的例外仍在");
        TestRunner.Equal(0, ScheduleItemValidator.Validate(series).Count, "重复日程校验通过");
    }
}
