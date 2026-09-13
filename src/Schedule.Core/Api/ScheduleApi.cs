using System.Text;
using System.Text.Json;
using Schedule.Core.Csv;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Services;
using Schedule.Core.Storage;
using Schedule.Core.Templating;

namespace Schedule.Core.Api;

/// <summary>
/// 本地 JSON 接口：让 AI / 脚本可以直接读写日程、设置与语音。
/// 只监听 127.0.0.1，并要求访问令牌（见数据目录 api.json）。
/// </summary>
public sealed class ScheduleApi
{
    private static readonly string[] KnownItemFields =
    {
        "title", "notes", "category", "start", "end", "allDay", "color", "priority",
        "repeat", "repeatInterval", "repeatWeekdays", "repeatCount", "repeatUntil",
        "reminderEnabled", "reminderOffsets", "remindBeforeMinutes", "reminderVoiceTexts",
        "reminderSoundFiles", "remindAtOverride", "voiceText", "soundFile", "status",
    };

    private readonly ScheduleRepository _repository;
    private readonly SettingsService _settings;
    private readonly CsvImportService _importer;
    private readonly IApiActions _actions;
    private readonly Func<DateTime> _clock;
    private readonly string _version;

    public ScheduleApi(
        ScheduleRepository repository,
        SettingsService settings,
        IApiActions? actions = null,
        Func<DateTime>? clock = null,
        string? version = null,
        CsvImportService? importer = null)
    {
        _repository = repository;
        _settings = settings;
        _actions = actions ?? new NullApiActions();
        _clock = clock ?? (() => DateTime.Now);
        _version = version ?? "0.3.0";
        _importer = importer ?? new CsvImportService(_clock);
    }

    public ApiResponse Handle(ApiRequest request)
    {
        try
        {
            if (request.RequiresLength)
            {
                return ApiResponse.Error(411, "不支持 Transfer-Encoding: chunked，请带上 Content-Length。");
            }

            var token = _settings.Current.ApiToken;
            if (!string.IsNullOrEmpty(token) && !TokenEquals(request.GetToken(), token))
            {
                return ApiResponse.Error(401, "缺少或错误的访问令牌。",
                    "请带上请求头 X-Api-Token（令牌与端口见数据目录 api.json），也可用 ?token= 传参。");
            }

            var segments = SplitPath(request.Path);
            if (segments.Count == 0 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                return Index();
            }

            var resource = segments.Count > 1 ? segments[1].ToLowerInvariant() : "schema";

            return resource switch
            {
                "schema" or "help" => RequireGet(request, Schema),
                "info" => RequireGet(request, Info),
                "items" => Items(request, segments),
                "occurrences" => RequireGet(request, () => Occurrences(request)),
                "reminders" => Reminders(request, segments),
                "voices" => RequireGet(request, Voices),
                "speak" => RequirePost(request, () => Speak(request)),
                "import" => RequirePost(request, () => Import(request)),
                "export" => RequireGet(request, () => Export(request)),
                "settings" => SettingsResource(request),
                "reload" => RequirePost(request, Reload),
                _ => ApiResponse.Error(404, "未知接口：" + request.Path, "可用接口见 GET /api/schema"),
            };
        }
        catch (JsonException ex)
        {
            return ApiResponse.Error(400, "JSON 解析失败：" + ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse.Error(500, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public int Port { get; set; }

    // ==================== 索引与自描述 ====================

    private static ApiResponse RequireGet(ApiRequest request, Func<ApiResponse> handler)
        => request.Method == "GET" || request.Method == "HEAD"
            ? handler()
            : ApiResponse.Error(405, "该接口只支持 GET。");

    private static ApiResponse RequirePost(ApiRequest request, Func<ApiResponse> handler)
        => request.Method == "POST"
            ? handler()
            : ApiResponse.Error(405, "该接口只支持 POST。");

    private ApiResponse Index()
    {
        var text = new StringBuilder();
        text.AppendLine("日程助手 ScheduleApp 本地接口 v" + _version);
        text.AppendLine();
        text.AppendLine("自描述（推荐先读这个）：GET /api/schema");
        text.AppendLine("需要请求头：X-Api-Token: <令牌>（见数据目录 api.json）");
        text.AppendLine();
        text.AppendLine("GET    /api/info");
        text.AppendLine("GET    /api/items?from=&to=&q=&category=&status=&limit=");
        text.AppendLine("POST   /api/items");
        text.AppendLine("GET    /api/items/{id}");
        text.AppendLine("PUT    /api/items/{id}            全量替换");
        text.AppendLine("PATCH  /api/items/{id}            局部修改");
        text.AppendLine("DELETE /api/items/{id}");
        text.AppendLine("POST   /api/items/{id}/status      {\"status\":\"Done\"}");
        text.AppendLine("POST   /api/items/{id}/complete");
        text.AppendLine("POST   /api/items/{id}/reminder-now {\"offsetMinutes\":30}");
        text.AppendLine("GET    /api/occurrences?from=&to=&days=");
        text.AppendLine("GET    /api/reminders/next?limit=5");
        text.AppendLine("POST   /api/reminders/test");
        text.AppendLine("GET    /api/voices");
        text.AppendLine("POST   /api/speak");
        text.AppendLine("POST   /api/import                 {\"path\":\"...\"} 或 {\"content\":\"...\"}");
        text.AppendLine("GET    /api/export?format=csv|json");
        text.AppendLine("GET    /api/settings");
        text.AppendLine("PATCH  /api/settings");
        text.AppendLine("POST   /api/reload");
        return ApiResponse.Text(text.ToString());
    }

    private ApiResponse Schema()
    {
        var schema = new
        {
            api = "日程助手 ScheduleApp 本地接口",
            version = _version,
            baseUrl = "http://127.0.0.1:" + Port,
            auth = new
            {
                header = "X-Api-Token",
                alternative = "?token=<令牌>",
                tokenFile = Path.Combine(AppPaths.DataDirectory, "api.json"),
                note = "仅监听 127.0.0.1；令牌首次启动自动生成",
            },
            endpoints = new object[]
            {
                new { method = "GET", path = "/api/info", desc = "版本、数据目录、日程数量、语音状态、下一次提醒" },
                new { method = "GET", path = "/api/items", desc = "查询日程；支持 from/to/q/category/status/limit/expand",
                      query = new { from = "起始时间（含）", to = "结束时间（不含）", q = "关键词（标题/备注/分类/播报文案）",
                          category = "分类精确匹配", status = "Pending|Done|Canceled|all", limit = "默认 200",
                          expand = "true 时按重复规则展开为发生（推荐 AI 使用）", days = "未给 to 时默认往后几天（默认 30）" } },
                new { method = "POST", path = "/api/items", desc = "新建日程；返回 201 与新建后的完整对象" },
                new { method = "GET", path = "/api/items/{id}", desc = "读取单条日程" },
                new { method = "PUT", path = "/api/items/{id}", desc = "全量替换（未提供的字段复位默认值）" },
                new { method = "PATCH", path = "/api/items/{id}", desc = "局部修改（只改提供的字段，推荐 AI 使用）" },
                new { method = "DELETE", path = "/api/items/{id}", desc = "删除" },
                new { method = "POST", path = "/api/items/{id}/status", desc = "改状态：{\"status\":\"Done\"}" },
                new { method = "POST", path = "/api/items/{id}/reminder-now", desc = "立即按某档位触发一次提醒：{\"offsetMinutes\":30}" },
                new { method = "GET", path = "/api/occurrences", desc = "把重复日程展开成具体发生，并给出每档位将播报的文本" },
                new { method = "GET", path = "/api/reminders/next", desc = "接下来 N 次提醒（含解析后的播报文本）" },
                new { method = "POST", path = "/api/reminders/test", desc = "立即触发提醒：{\"itemId\":\"...\",\"offsetMinutes\":30}" },
                new { method = "GET", path = "/api/voices", desc = "系统可用音色列表" },
                new { method = "POST", path = "/api/speak", desc = "朗读：{\"text\":\"...\"} 或 {\"itemId\":\"...\",\"offsetMinutes\":30}（后者自动套用该档位文案）" },
                new { method = "POST", path = "/api/import", desc = "导入 CSV：{\"path\":\"D:\\\\a.csv\"} 或 {\"content\":\"标题,开始时间...\",\"fileName\":\"x.csv\"}" },
                new { method = "GET", path = "/api/export", desc = "导出：format=csv（默认）或 json" },
                new { method = "GET", path = "/api/settings", desc = "读取全局设置（含语音与档位文案）" },
                new { method = "PATCH", path = "/api/settings", desc = "修改全局设置（只改提供的字段）" },
                new { method = "POST", path = "/api/reload", desc = "从磁盘重新加载（手工改过 json 后调用）" },
            },
            itemFields = new
            {
                title = "必填，日程标题",
                notes = "备注",
                category = "分类（决定自动配色）",
                start = "开始时间，字符串；支持 2025-06-03 14:00 / 2025-06-03T14:00:00 / 明天 9:00 等写法",
                end = "结束时间，可空",
                allDay = "是否全天",
                color = "#RRGGBB 或中文色名；空 = 跟随分类",
                priority = "Low|Normal|High",
                repeat = "None|Daily|Weekdays|Weekly|Monthly|Yearly",
                repeatInterval = "每 N 个周期，默认 1",
                repeatWeekdays = "[\"Monday\",\"Wednesday\"]，仅 repeat=Weekly 时有效",
                repeatCount = "共重复几次（含首次）",
                repeatUntil = "重复截止日期",
                reminderEnabled = "是否提醒，默认 true",
                reminderOffsets = "提醒档位（提前分钟数数组），例如 [30,15,5,0]；0 表示准点",
                remindBeforeMinutes = "单档简写，等价于 reminderOffsets=[值]",
                reminderVoiceTexts = "按档位自定义播报文案，例如 {\"30\":\"还有 30 分钟\",\"5\":\"马上开始\"}",
                reminderSoundFiles = "按档位自定义提示音，例如 {\"30\":\"D:\\\\bell.mp3\"}",
                remindAtOverride = "额外的绝对提醒时刻",
                voiceText = "本日程通用播报文案（留空则用全局）",
                soundFile = "本日程通用提示音",
                status = "Pending|Done|Canceled",
            },
            voiceResolution = new
            {
                order = new[]
                {
                    "1. reminderVoiceTexts[档位]（本日程 · 该档位）",
                    "2. voiceText（本日程 · 通用文案）",
                    "3. settings.offsetVoiceTemplates[档位]（全局 · 该档位文案，可用 PATCH /api/settings 修改）",
                    "4. settings.voiceTemplate（全局模板）",
                },
                placeholders = VoiceTemplate.Tokens,
                note = "最终文本会用占位符渲染：{title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status} {offset}",
            },
            examples = new
            {
                createWithStagedVoice = new
                {
                    title = "项目周会",
                    start = "2025-06-03 14:00",
                    end = "2025-06-03 15:00",
                    category = "工作",
                    repeat = "Weekly",
                    repeatWeekdays = new[] { "Monday", "Wednesday" },
                    reminderOffsets = new[] { 30, 15, 5, 0 },
                    reminderVoiceTexts = new Dictionary<string, string>
                    {
                        ["30"] = "还有 30 分钟：{title}",
                        ["5"] = "{title} 5 分钟后开始",
                        ["0"] = "{title} 现在开始",
                    },
                },
                patchVoiceOnly = new { reminderVoiceTexts = new Dictionary<string, string> { ["15"] = "还有 15 分钟，请准备" } },
                globalOffsetVoice = new { offsetVoiceTemplates = new Dictionary<string, string> { ["30"] = "还有 30 分钟", ["0"] = "现在开始" } },
            },
        };

        return ApiResponse.Json(schema);
    }

    private ApiResponse Info()
    {
        var items = _repository.Snapshot();
        var now = _clock();
        var next = ReminderPlanner.NextUpcoming(items, now);

        return ApiResponse.Json(new
        {
            version = _version,
            dataDirectory = AppPaths.DataDirectory,
            itemsFile = _repository.FilePath,
            itemCount = items.Count,
            pendingCount = items.Count(i => i.Status == ItemStatus.Pending),
            todayCount = items.Count(i => i.Start.Date == now.Date && i.Status == ItemStatus.Pending),
            voiceAvailable = _actions.IsAvailable,
            voiceCount = _actions.IsAvailable ? _actions.GetVoices().Count : 0,
            apiPort = Port,
            now,
            nextReminder = next == null ? null : new
            {
                itemId = next.Item.Id,
                title = next.Item.Title,
                occurrenceStart = next.Occurrence.Start,
                offsetMinutes = next.Plan.OffsetMinutes,
                offsetText = next.Plan.OffsetText,
                fireAt = next.FireAt,
            },
        });
    }

    // ==================== 日程 CRUD ====================

    private ApiResponse Items(ApiRequest request, List<string> segments)
    {
        // /api/items
        if (segments.Count == 2)
        {
            return request.Method switch
            {
                "GET" => ItemsList(request),
                "POST" => ItemsCreate(request),
                _ => ApiResponse.Error(405, "该路径只支持 GET / POST"),
            };
        }

        if (!Guid.TryParse(segments[2], out var id))
        {
            return ApiResponse.Error(400, "日程 id 必须是 GUID：" + segments[2]);
        }

        // /api/items/{id}
        if (segments.Count == 3)
        {
            return request.Method switch
            {
                "GET" => ItemGet(id),
                "PUT" => ItemWrite(request, id, partial: false),
                "PATCH" => ItemWrite(request, id, partial: true),
                "DELETE" => ItemDelete(id),
                _ => ApiResponse.Error(405, "该路径只支持 GET / PUT / PATCH / DELETE"),
            };
        }

        // /api/items/{id}/status | complete | reminder-now
        var action = segments[3].ToLowerInvariant();
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse.Error(405, "该路径只支持 POST");
        }

        return action switch
        {
            "status" => ItemSetStatus(request, id),
            "complete" => ItemSetStatus(new ApiRequest { Method = "POST", Body = "{\"status\":\"Done\"}" }, id),
            "reminder-now" => ItemReminderNow(request, id),
            _ => ApiResponse.Error(404, "未知动作：" + action),
        };
    }

    private ApiResponse ItemsList(ApiRequest request)
    {
        var now = _clock();
        var items = Filter(request.GetQuery("q"), request.GetQuery("category"), request.GetQuery("status"), _repository.Snapshot());
        var limit = Math.Clamp(request.GetInt("limit") ?? 200, 1, 2000);

        var from = request.GetDate("from");
        var to = request.GetDate("to");
        var expand = request.GetBool("expand");

        if (expand)
        {
            var windowFrom = from ?? now.Date;
            var windowTo = to ?? windowFrom.AddDays(request.GetInt("days") ?? 30);
            return ApiResponse.Json(OccurrenceList(items, windowFrom, windowTo, limit, now));
        }

        var filtered = items.Where(i => (!from.HasValue || i.Start >= from.Value) && (!to.HasValue || i.Start < to.Value));
        var result = filtered
            .OrderBy(i => i.Start)
            .Take(limit)
            .Select(ToItemDto)
            .ToList();

        return ApiResponse.Json(new { count = result.Count, total = items.Count, items = result });
    }

    private ApiResponse ItemsCreate(ApiRequest request)
    {
        var input = ApiJson.Deserialize<ScheduleItemInput>(request.Body);
        if (input == null) return ApiResponse.Error(400, "请求体必须是 JSON 对象。");

        var now = _clock();
        var item = new ScheduleItem { Origin = ItemOrigin.Manual, Source = "api" };
        input.ApplyTo(item, partial: false, now);

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0) return ApiResponse.Error(422, "日程校验失败", string.Join("；", errors));

        _repository.Add(item);
        _repository.Save();

        return WithIgnoredFields(
            ApiResponse.Json(new { item = ToItemDto(item) }, 201),
            request,
            KnownItemFields);
    }

    private ApiResponse ItemGet(Guid id)
    {
        var item = _repository.Find(id);
        return item == null
            ? ApiResponse.Error(404, "未找到日程：" + id)
            : ApiResponse.Json(new { item = ToItemDto(item) });
    }

    private ApiResponse ItemWrite(ApiRequest request, Guid id, bool partial)
    {
        var item = _repository.Find(id);
        if (item == null) return ApiResponse.Error(404, "未找到日程：" + id);

        var input = ApiJson.Deserialize<ScheduleItemInput>(request.Body);
        if (input == null) return ApiResponse.Error(400, "请求体必须是 JSON 对象。");

        input.ApplyTo(item, partial, _clock());

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0) return ApiResponse.Error(422, "日程校验失败", string.Join("；", errors));

        item.UpdatedAt = _clock();
        _repository.Update(item);
        _repository.Save();

        return WithIgnoredFields(ApiResponse.Json(new { item = ToItemDto(item) }), request, KnownItemFields);
    }

    private ApiResponse ItemDelete(Guid id)
    {
        var removed = _repository.Remove(id);
        if (!removed) return ApiResponse.Error(404, "未找到日程：" + id);

        _repository.Save();
        return ApiResponse.NoContent();
    }

    private ApiResponse ItemSetStatus(ApiRequest request, Guid id)
    {
        var item = _repository.Find(id);
        if (item == null) return ApiResponse.Error(404, "未找到日程：" + id);

        var raw = request.GetQuery("status");
        if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(request.Body))
        {
            using var document = JsonDocument.Parse(request.Body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var element))
            {
                raw = element.ToString();
            }
        }

        if (!Enum.TryParse<ItemStatus>(raw, ignoreCase: true, out var status))
        {
            return ApiResponse.Error(400, "status 必须是 Pending / Done / Canceled。", "例如 {\"status\":\"Done\"}");
        }

        item.Status = status;
        item.UpdatedAt = _clock();
        _repository.Update(item);
        _repository.Save();

        return ApiResponse.Json(new { item = ToItemDto(item) });
    }

    private ApiResponse ItemReminderNow(ApiRequest request, Guid id)
    {
        var item = _repository.Find(id);
        if (item == null) return ApiResponse.Error(404, "未找到日程：" + id);

        var offset = ReadOffsetFromBody(request, item);
        if (offset == null) return ApiResponse.Error(400, "需要提供 offsetMinutes（该日程的档位："
            + string.Join(", ", item.NormalizedOffsets()) + "）");

        if (!_actions.TriggerReminder(id, offset.Value, out var error))
        {
            return ApiResponse.Error(501, "当前进程无法触发提醒。", error);
        }

        return ApiResponse.Json(new { triggered = true, itemId = id, offsetMinutes = offset.Value });
    }

    // ==================== 发生与提醒 ====================

    private ApiResponse Occurrences(ApiRequest request)
    {
        var now = _clock();
        var from = request.GetDate("from") ?? now.Date;
        var to = request.GetDate("to") ?? from.AddDays(request.GetInt("days") ?? 30);
        var limit = Math.Clamp(request.GetInt("limit") ?? 500, 1, 5000);

        var items = Filter(request.GetQuery("q"), request.GetQuery("category"), request.GetQuery("status"), _repository.Snapshot());
        return ApiResponse.Json(OccurrenceList(items, from, to, limit, now));
    }

    private object OccurrenceList(List<ScheduleItem> items, DateTime from, DateTime to, int limit, DateTime now)
    {
        var settings = _settings.Current;
        var list = new List<object>();

        foreach (var item in items)
        {
            if (item.Status != ItemStatus.Pending) continue;

            foreach (var occurrence in OccurrenceCalculator.Expand(item, from, to))
            {
                list.Add(new
                {
                    itemId = item.Id,
                    title = item.Title,
                    start = occurrence.Start,
                    end = occurrence.End,
                    allDay = item.AllDay,
                    category = item.Category,
                    resolvedColor = EventColors.Resolve(item, settings.CategoryColors),
                    status = item.Status,
                    repeatDescription = DescribeRepeat(item),
                    reminders = occurrence.Reminders.Select(p => new
                    {
                        offsetMinutes = p.OffsetMinutes,
                        offsetText = p.OffsetText,
                        fireAt = p.FireAt,
                        voiceSource = item.DescribeVoiceSource(p.OffsetMinutes, settings),
                        voiceTemplate = item.ResolveVoiceTemplate(p.OffsetMinutes, settings),
                        voiceText = RenderVoice(item, p, now),
                        soundFile = item.ResolveSoundFile(p.OffsetMinutes, settings),
                        alreadyFired = ReminderPlanner.AlreadyFired(item, occurrence.Start, p),
                    }).ToList(),
                });

                if (list.Count >= limit) break;
            }

            if (list.Count >= limit) break;
        }

        return new { count = list.Count, from, to, limit, occurrences = list };
    }

    private ApiResponse Reminders(ApiRequest request, List<string> segments)
    {
        if (segments.Count >= 3 && segments[2].Equals("next", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method != "GET") return ApiResponse.Error(405, "该路径只支持 GET");

            var now = _clock();
            var limit = Math.Clamp(request.GetInt("limit") ?? 5, 1, 100);
            var upcoming = ReminderPlanner.Upcoming(_repository.Snapshot(), now, limit * 3);

            var result = new List<object>();
            foreach (var reminder in upcoming)
            {
                if (!MatchesFilters(reminder.Item, request.GetQuery("q"), request.GetQuery("category"))) continue;

                result.Add(new
                {
                    itemId = reminder.Item.Id,
                    title = reminder.Item.Title,
                    occurrenceStart = reminder.Occurrence.Start,
                    offsetMinutes = reminder.Plan.OffsetMinutes,
                    offsetText = reminder.Plan.OffsetText,
                    fireAt = reminder.FireAt,
                    voiceText = RenderVoice(reminder.Item, reminder.Plan, now),
                });

                if (result.Count >= limit) break;
            }

            return ApiResponse.Json(new { count = result.Count, now, reminders = result });
        }

        if (segments.Count >= 3 && segments[2].Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method != "POST") return ApiResponse.Error(405, "该路径只支持 POST");

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
            var root = document.RootElement;

            if (!root.TryGetProperty("itemId", out var idElement) || !Guid.TryParse(idElement.ToString(), out var id))
            {
                return ApiResponse.Error(400, "需要提供 itemId。");
            }

            var item = _repository.Find(id);
            if (item == null) return ApiResponse.Error(404, "未找到日程：" + id);

            var offset = ReadOffset(root, item);
            if (offset == null) return ApiResponse.Error(400, "需要提供 offsetMinutes。");

            if (!_actions.TriggerReminder(id, offset.Value, out var error))
            {
                return ApiResponse.Error(501, "当前进程无法触发提醒。", error);
            }

            return ApiResponse.Json(new
            {
                triggered = true,
                itemId = id,
                offsetMinutes = offset.Value,
                voiceText = RenderVoice(item, item.ComputeReminders(item.Start)
                    .FirstOrDefault(p => p.OffsetMinutes == offset.Value), _clock()),
            });
        }

        return ApiResponse.Error(404, "未知接口：" + request.Path);
    }

    // ==================== 语音 ====================

    private ApiResponse Voices()
    {
        if (!_actions.IsAvailable)
        {
            return ApiResponse.Json(new { available = false, count = 0, voices = Array.Empty<object>() });
        }

        var voices = _actions.GetVoices();
        return ApiResponse.Json(new
        {
            available = true,
            count = voices.Count,
            current = _settings.Current.VoiceName,
            voices = voices.Select(v => new { id = v.Id, name = v.Name, language = v.Language, gender = v.Gender, age = v.Age, isDefault = v.IsDefault }),
        });
    }

    private ApiResponse Speak(ApiRequest request)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
        var root = document.RootElement;

        if (!_actions.IsAvailable)
        {
            return ApiResponse.Error(501, "当前进程没有语音能力。", "请以正常模式或 --serve 模式启动程序。");
        }

        var settings = _settings.Current;
        string text;
        int? offset = null;
        ScheduleItem? item = null;

        if (root.TryGetProperty("itemId", out var idElement) && Guid.TryParse(idElement.ToString(), out var id))
        {
            item = _repository.Find(id);
            if (item == null) return ApiResponse.Error(404, "未找到日程：" + id);

            offset = ReadOffset(root, item);
            if (offset == null && item.NormalizedOffsets().Count > 0) offset = item.NormalizedOffsets()[0];

            var plan = offset.HasValue
                ? item.ComputeReminders(item.Start).FirstOrDefault(p => p.OffsetMinutes == offset.Value)
                : null;

            text = RenderVoice(item, plan, _clock());
        }
        else if (root.TryGetProperty("text", out var textElement))
        {
            text = textElement.ToString();
        }
        else
        {
            return ApiResponse.Error(400, "需要提供 text 或 itemId。");
        }

        if (string.IsNullOrWhiteSpace(text)) return ApiResponse.Error(400, "朗读文本为空。");

        var voiceId = root.TryGetProperty("voiceId", out var voiceElement) ? voiceElement.ToString() : settings.VoiceId;
        var rate = root.TryGetProperty("rate", out var rateElement) && rateElement.TryGetInt32(out var r) ? r : settings.VoiceRate;
        var volume = root.TryGetProperty("volume", out var volumeElement) && volumeElement.TryGetInt32(out var v) ? v : settings.VoiceVolume;

        if (!_actions.Speak(text, voiceId, rate, volume, out var error))
        {
            return ApiResponse.Error(500, "朗读失败。", error);
        }

        return ApiResponse.Json(new { spoken = true, text, voiceId, rate, volume, itemId = item?.Id, offsetMinutes = offset });
    }

    // ==================== 导入导出 ====================

    private ApiResponse Import(ApiRequest request)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
        var root = document.RootElement;

        var settings = _settings.Current;
        var path = root.TryGetProperty("path", out var pathElement) ? pathElement.ToString() : null;
        var content = root.TryGetProperty("content", out var contentElement) ? contentElement.ToString() : null;
        var fileName = root.TryGetProperty("fileName", out var nameElement) ? nameElement.ToString() : "api-import.csv";
        var modeText = root.TryGetProperty("mode", out var modeElement) ? modeElement.ToString() : "append";
        var encoding = root.TryGetProperty("encoding", out var encodingElement) ? encodingElement.ToString() : settings.CsvEncoding;
        var dayFirst = root.TryGetProperty("dayFirst", out var dayElement) && dayElement.ValueKind == JsonValueKind.True;

        var temporary = false;

        if (string.IsNullOrWhiteSpace(path))
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return ApiResponse.Error(400, "需要提供 path 或 content。", "content 可直接给 CSV 文本。");
            }

            var directory = Path.Combine(AppPaths.DataDirectory, "imports");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + SafeFileName(fileName));
            File.WriteAllText(path, content, new UTF8Encoding(false));
            temporary = true;
        }

        if (!File.Exists(path)) return ApiResponse.Error(404, "文件不存在：" + path);

        try
        {
            var mode = modeText?.ToLowerInvariant() switch
            {
                "upsert" or "update" => ImportMode.Upsert,
                "replace" => ImportMode.Replace,
                _ => ImportMode.Append,
            };

            var options = new CsvImportOptions
            {
                FilePath = path,
                EncodingName = string.IsNullOrWhiteSpace(encoding) ? "auto" : encoding,
                Mode = mode,
                DayFirst = dayFirst,
                SkipPastItems = false,
                SkipDuplicates = true,
                DefaultCategory = settings.CsvDefaultCategory,
                DefaultColor = settings.DefaultEventColor,
                DefaultReminderEnabled = settings.DefaultReminderEnabled,
                DefaultReminderOffsets = new List<int>(settings.DefaultReminderOffsets),
                DefaultRemindBeforeMinutes = settings.DefaultRemindBeforeMinutes,
                SourceTag = SafeFileName(fileName),
            };

            var items = _repository.Snapshot();
            var report = _importer.Import(options, items);
            _repository.ReplaceAll(items);
            _repository.Save();

            return ApiResponse.Json(new
            {
                file = path,
                encoding = report.EncodingName,
                total = report.TotalRows,
                imported = report.Imported,
                updated = report.Updated,
                skipped = report.Skipped,
                failed = report.Failed,
                warnings = report.Warnings,
                errors = report.Errors.Take(50).Select(e => new { line = e.Line, message = e.Message }),
            });
        }
        finally
        {
            if (temporary)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    // 忽略
                }
            }
        }
    }

    private ApiResponse Export(ApiRequest request)
    {
        var format = (request.GetQuery("format") ?? "csv").ToLowerInvariant();
        var items = Filter(request.GetQuery("q"), request.GetQuery("category"), request.GetQuery("status"), _repository.Snapshot());

        if (format is "json")
        {
            return ApiResponse.Json(new { count = items.Count, items = items.OrderBy(i => i.Start).Select(ToItemDto) });
        }

        var csv = CsvExporter.ToCsv(items);
        return ApiResponse.Text(csv, 200, "text/csv; charset=utf-8");
    }

    // ==================== 设置 ====================

    private ApiResponse SettingsResource(ApiRequest request)
    {
        return request.Method switch
        {
            "GET" => ApiResponse.Json(new { settings = ToSettingsDto() }),
            "PATCH" => SettingsPatch(request),
            _ => ApiResponse.Error(405, "该路径只支持 GET / PATCH"),
        };
    }

    private ApiResponse SettingsPatch(ApiRequest request)
    {
        var patch = ApiJson.Deserialize<Dictionary<string, JsonElement>>(request.Body);
        if (patch == null || patch.Count == 0) return ApiResponse.Error(400, "请求体必须是包含待修改字段的 JSON 对象。");

        var settings = _settings.Current;
        var applied = new List<string>();
        var unknown = new List<string>();
        var notes = new List<string>();

        foreach (var pair in patch)
        {
            var key = pair.Key.ToLowerInvariant();
            var value = pair.Value;

            try
            {
                switch (key)
                {
                    case "voiceid": settings.VoiceId = value.ToString(); applied.Add(pair.Key); break;
                    case "voicename": settings.VoiceName = value.ToString(); applied.Add(pair.Key); break;
                    case "voicerate": settings.VoiceRate = Math.Clamp(value.GetInt32(), -10, 10); applied.Add(pair.Key); break;
                    case "voicevolume": settings.VoiceVolume = Math.Clamp(value.GetInt32(), 0, 100); applied.Add(pair.Key); break;
                    case "voicetemplate": settings.VoiceTemplate = value.ToString(); applied.Add(pair.Key); break;
                    case "speechenabled": settings.SpeechEnabled = value.GetBoolean(); applied.Add(pair.Key); break;
                    case "soundenabled": settings.SoundEnabled = value.GetBoolean(); applied.Add(pair.Key); break;
                    case "defaultsoundfile": settings.DefaultSoundFile = value.ToString(); applied.Add(pair.Key); break;
                    case "soundbeforespeech": settings.SoundBeforeSpeech = value.GetBoolean(); applied.Add(pair.Key); break;
                    case "offsetvoicetemplates": settings.OffsetVoiceTemplates = ReadOffsetTextMap(value); applied.Add(pair.Key); break;
                    case "defaultreminderoffsets":
                        settings.DefaultReminderOffsets = value.EnumerateArray().Select(e => e.GetInt32()).Where(v => v >= 0).Distinct().OrderByDescending(v => v).ToList();
                        applied.Add(pair.Key);
                        break;
                    case "defaultremindbeforeminutes": settings.DefaultRemindBeforeMinutes = Math.Max(0, value.GetInt32()); applied.Add(pair.Key); break;
                    case "snoozeminutes": settings.SnoozeMinutes = Math.Clamp(value.GetInt32(), 1, 1440); applied.Add(pair.Key); break;
                    case "reminderautocloseconds": settings.ReminderAutoCloseSeconds = Math.Clamp(value.GetInt32(), 0, 600); applied.Add(pair.Key); break;
                    case "reminderalwaysontop": settings.ReminderAlwaysOnTop = value.GetBoolean(); applied.Add(pair.Key); break;
                    case "showcompleted": settings.ShowCompleted = value.GetBoolean(); applied.Add(pair.Key); break;
                    case "lookaheaddays": settings.LookAheadDays = Math.Clamp(value.GetInt32(), 1, 3650); applied.Add(pair.Key); break;
                    case "hourheight": settings.HourHeight = Math.Clamp(value.GetInt32(), 32, 140); applied.Add(pair.Key); break;
                    case "monthcellcapacity": settings.MonthCellCapacity = Math.Clamp(value.GetInt32(), 1, 8); applied.Add(pair.Key); break;
                    case "weekstartsonmonday": settings.WeekStartsOnMonday = value.GetBoolean(); applied.Add(pair.Key); break;
                    case "defaultview":
                        settings.DefaultView = Enum.TryParse<ScheduleViewMode>(value.ToString(), ignoreCase: true, out var view)
                            ? view
                            : settings.DefaultView;
                        applied.Add(pair.Key);
                        break;
                    case "categorycolors":
                        settings.CategoryColors = ReadStringMap(value);
                        applied.Add(pair.Key);
                        break;
                    case "apienabled": settings.ApiEnabled = value.GetBoolean(); applied.Add(pair.Key); notes.Add("apiEnabled 需要重启程序后生效"); break;
                    case "apiport": settings.ApiPort = Math.Clamp(value.GetInt32(), 1024, 65535); applied.Add(pair.Key); notes.Add("apiPort 需要重启程序后生效"); break;
                    default: unknown.Add(pair.Key); break;
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Error(400, "字段 " + pair.Key + " 取值不合法：" + ex.Message);
            }
        }

        if (applied.Count > 0)
        {
            _settings.MarkDirty();
            _settings.Save();
            _settings.RaiseChanged();
        }

        return ApiResponse.Json(new
        {
            applied,
            ignoredFields = unknown,
            notes,
            settings = ToSettingsDto(),
        });
    }

    private ApiResponse Reload()
    {
        _repository.Load();
        _settings.Load();

        return ApiResponse.Json(new
        {
            reloaded = true,
            itemCount = _repository.Count,
            settingsFile = AppPaths.SettingsFile,
        });
    }

    // ==================== 辅助 ====================

    private object ToSettingsDto()
    {
        var settings = _settings.Current;

        return new
        {
            voiceId = settings.VoiceId,
            voiceName = settings.VoiceName,
            voiceRate = settings.VoiceRate,
            voiceVolume = settings.VoiceVolume,
            voiceTemplate = settings.VoiceTemplate,
            speechEnabled = settings.SpeechEnabled,
            soundEnabled = settings.SoundEnabled,
            defaultSoundFile = settings.DefaultSoundFile,
            soundBeforeSpeech = settings.SoundBeforeSpeech,
            offsetVoiceTemplates = settings.OffsetVoiceTemplates,
            defaultReminderOffsets = settings.DefaultReminderOffsets,
            defaultRemindBeforeMinutes = settings.DefaultRemindBeforeMinutes,
            defaultReminderEnabled = settings.DefaultReminderEnabled,
            snoozeMinutes = settings.SnoozeMinutes,
            reminderAutoCloseSeconds = settings.ReminderAutoCloseSeconds,
            reminderAlwaysOnTop = settings.ReminderAlwaysOnTop,
            defaultEventColor = settings.DefaultEventColor,
            categoryColors = settings.CategoryColors,
            defaultView = settings.DefaultView,
            weekStartsOnMonday = settings.WeekStartsOnMonday,
            hourHeight = settings.HourHeight,
            monthCellCapacity = settings.MonthCellCapacity,
            showCompleted = settings.ShowCompleted,
            lookAheadDays = settings.LookAheadDays,
            csvEncoding = settings.CsvEncoding,
            csvDefaultCategory = settings.CsvDefaultCategory,
            apiEnabled = settings.ApiEnabled,
            apiPort = settings.ApiPort,
            dataDirectory = AppPaths.DataDirectory,
        };
    }

    private object ToItemDto(ScheduleItem item)
    {
        var settings = _settings.Current;

        return new
        {
            id = item.Id,
            title = item.Title,
            notes = item.Notes,
            category = item.Category,
            start = item.Start,
            end = item.End,
            allDay = item.AllDay,
            durationMinutes = (int)Math.Round(item.Duration.TotalMinutes),
            color = item.Color,
            resolvedColor = EventColors.Resolve(item, settings.CategoryColors),
            priority = item.Priority,
            repeat = item.Repeat,
            repeatInterval = item.RepeatInterval,
            repeatWeekdays = item.RepeatWeekdays,
            repeatCount = item.RepeatCount,
            repeatUntil = item.RepeatUntil,
            repeatDescription = DescribeRepeat(item),
            reminderEnabled = item.ReminderEnabled,
            reminderOffsets = item.NormalizedOffsets(),
            reminderVoiceTexts = item.ReminderVoiceTexts,
            reminderSoundFiles = item.ReminderSoundFiles,
            remindAtOverride = item.RemindAtOverride,
            voiceText = item.VoiceText,
            soundFile = item.SoundFile,
            reminderDescription = item.DescribeReminders(),
            status = item.Status,
            origin = item.Origin,
            source = item.Source,
            createdAt = item.CreatedAt,
            updatedAt = item.UpdatedAt,
        };
    }

    private string RenderVoice(ScheduleItem item, ReminderPlan? plan, DateTime now)
    {
        var settings = _settings.Current;
        var offset = plan?.OffsetMinutes ?? 0;
        var template = item.ResolveVoiceTemplate(offset, settings);
        var start = item.Start;

        return VoiceTemplate.Render(template, item, start, now, offset);
    }

    private static int? ReadOffset(JsonElement root, ScheduleItem item)
    {
        if (root.TryGetProperty("offsetMinutes", out var element) && element.TryGetInt32(out var offset)) return offset;
        if (root.TryGetProperty("offset", out var shortElement) && shortElement.TryGetInt32(out var shortOffset)) return shortOffset;

        var offsets = item.NormalizedOffsets();
        return offsets.Count > 0 ? offsets[0] : null;
    }

    private static int? ReadOffsetFromBody(ApiRequest request, ScheduleItem item)
    {
        if (string.IsNullOrWhiteSpace(request.Body)) return ReadOffset(default, item);

        using var document = JsonDocument.Parse(request.Body);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return ReadOffset(default, item);

        return ReadOffset(document.RootElement, item);
    }

    private static Dictionary<int, string> ReadOffsetTextMap(JsonElement element)
    {
        var result = new Dictionary<int, string>();
        if (element.ValueKind != JsonValueKind.Object) return result;

        foreach (var property in element.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var offset)) continue;

            var text = property.Value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) result[offset] = text;
        }

        return result;
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object) return result;

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ToString();
            if (!string.IsNullOrWhiteSpace(value)) result[property.Name] = value;
        }

        return result;
    }

    private ApiResponse WithIgnoredFields(ApiResponse response, ApiRequest request, string[] known)
    {
        if (string.IsNullOrWhiteSpace(request.Body)) return response;

        try
        {
            using var document = JsonDocument.Parse(request.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return response;

            var ignored = document.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .Where(name => !known.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (ignored.Count == 0) return response;

            var headers = new Dictionary<string, string>(response.Headers)
            {
                ["X-Ignored-Fields"] = string.Join(",", ignored),
                ["X-Ignored-Fields-Note"] = "这些字段未被识别，请核对拼写；已知字段见 GET /api/schema",
            };

            return new ApiResponse
            {
                Status = response.Status,
                ContentType = response.ContentType,
                Body = response.Body,
                Headers = headers,
            };
        }
        catch (Exception)
        {
            return response;
        }
    }

    private static List<ScheduleItem> Filter(string? keyword, string? category, string? status, List<ScheduleItem> items)
    {
        IEnumerable<ScheduleItem> query = items;

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = Enum.TryParse<ItemStatus>(status, ignoreCase: true, out var parsed)
                ? query.Where(i => i.Status == parsed)
                : query;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(i => string.Equals(i.Category?.Trim(), category.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(i =>
                Contains(i.Title, key) || Contains(i.Notes, key) || Contains(i.Category, key) || Contains(i.VoiceText, key));
        }

        return query.ToList();

        static bool Contains(string? source, string key)
            => !string.IsNullOrEmpty(source) && source.Contains(key, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool MatchesFilters(ScheduleItem item, string? keyword, string? category)
    {
        if (!string.IsNullOrWhiteSpace(category)
            && !string.Equals(item.Category?.Trim(), category.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyword)) return true;

        var key = keyword.Trim();
        return (item.Title?.Contains(key, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (item.Notes?.Contains(key, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (item.Category?.Contains(key, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private static string DescribeRepeat(ScheduleItem item)
    {
        if (item.Repeat == RepeatKind.None) return "不重复";

        var text = item.Repeat switch
        {
            RepeatKind.Daily => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 天" : "每天",
            RepeatKind.Weekdays => "每个工作日",
            RepeatKind.Weekly => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 周" : "每周",
            RepeatKind.Monthly => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 个月" : "每月",
            RepeatKind.Yearly => item.RepeatInterval > 1 ? "每 " + item.RepeatInterval + " 年" : "每年",
            _ => "不重复",
        };

        if (item.Repeat == RepeatKind.Weekly && item.RepeatWeekdays.Count > 0)
        {
            text += "（" + string.Join("、", item.RepeatWeekdays.Distinct().OrderBy(d => d).Select(VoiceTemplate.ShortWeekdayText)) + "）";
        }

        if (item.RepeatCount.HasValue) text += " · 共 " + item.RepeatCount.Value + " 次";
        else if (item.RepeatUntil.HasValue) text += " · 至 " + item.RepeatUntil.Value.ToString("yyyy-MM-dd");

        return text;
    }

    private static List<string> SplitPath(string path)
        => path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return cleaned.Length == 0 ? "import.csv" : cleaned;
    }

    /// <summary>固定时间比较，避免令牌比较被时序侧信道。</summary>
    private static bool TokenEquals(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided)) return false;

        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);

        var diff = a.Length ^ b.Length;
        for (var i = 0; i < a.Length && i < b.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }
}
