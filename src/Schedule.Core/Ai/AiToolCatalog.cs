using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Schedule.Core.Ai;

/// <summary>一次工具调用的结果。</summary>
public sealed class AiToolResult
{
    public string Content { get; init; } = "{}";

    public bool IsError { get; init; }

    /// <summary>界面展示用的一句话（例如「已新建日程：客户沟通」）。</summary>
    public string? Summary { get; init; }

    public static AiToolResult Ok(string content, string? summary = null) => new() { Content = content, Summary = summary };

    public static AiToolResult Error(string message)
        => new()
        {
            IsError = true,
            Summary = message,
            Content = AiJson.Text(new JsonObject { ["error"] = message }),
        };
}

/// <summary>列表 / 查询条件。</summary>
public sealed class AiListQuery
{
    /// <summary>某一天（今天 / 明天 / 2026-09-15 …）。</summary>
    public string? Date { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public string? Category { get; set; }

    public string? Keyword { get; set; }

    public int Limit { get; set; } = 20;

    public bool IncludeDone { get; set; } = true;
}

/// <summary>新建 / 修改日程的字段（null = 不改）。</summary>
public sealed class AiItemDraft
{
    public string? Title { get; set; }

    public string? Start { get; set; }

    public string? End { get; set; }

    public bool? AllDay { get; set; }

    public string? Category { get; set; }

    public string? Notes { get; set; }

    public string? VoiceText { get; set; }

    public string? Color { get; set; }

    public string? Status { get; set; }

    public List<int>? ReminderOffsets { get; set; }
}

/// <summary>
/// 日程操作能力（由宿主实现）。工具目录只负责把模型给的 JSON 参数翻译成这些调用，
/// 再把结果整理成 JSON 回给模型，因此这层可以脱离界面完整自测。
/// </summary>
public interface IAiActions
{
    AiToolResult ListItems(AiListQuery query);

    AiToolResult CreateItem(AiItemDraft draft);

    AiToolResult UpdateItem(string reference, AiItemDraft draft);

    AiToolResult RescheduleItem(string reference, string newStart, string? newEnd);

    AiToolResult DeleteItem(string reference);

    AiToolResult SetStatus(string reference, string status);

    AiToolResult NextReminders(int count);

    AiToolResult AppInfo();

    AiToolResult SetTheme(string theme);
}

/// <summary>
/// 提供给模型的工具清单 + 参数校验 + 分发。
/// 参数不合法时**不抛异常**，而是回一个 {"error":...}，让模型自己看着改（本地小模型经常一次说不对）。
/// </summary>
public sealed class AiToolCatalog
{
    private readonly IAiActions _actions;

    public AiToolCatalog(IAiActions actions) => _actions = actions;

    /// <summary>工具清单（顺序固定，便于对比与自测）。</summary>
    public static IReadOnlyList<AiToolDefinition> Definitions { get; } = new List<AiToolDefinition>
    {
        new()
        {
            Name = "list_items",
            Description = "查询日程列表。可以按日期（今天 / 明天 / 2026-09-15）、时间段、分类或关键词过滤。"
                + "返回的每一项都带 ref（形如 #1），后续要修改 / 删除某一项时把它的 ref 原样传回来即可。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "date": { "type": "string", "description": "只查某一天，例如 今天 / 明天 / 2026-09-15" },
                "from": { "type": "string", "description": "起始日期或时间，例如 2026-09-01 或 今天" },
                "to": { "type": "string", "description": "结束日期或时间（含当天）" },
                "category": { "type": "string", "description": "分类名称" },
                "keyword": { "type": "string", "description": "标题 / 备注 / 分类里的关键词" },
                "limit": { "type": "integer", "description": "最多返回多少条，默认 20" },
                "includeDone": { "type": "boolean", "description": "是否包含已完成 / 已取消，默认 true" }
              }
            }
            """,
        },
        new()
        {
            Name = "create_item",
            Description = "新建一条日程。start 必须是具体时间（如 2026-09-15 15:00，也支持 明天15:00 这类写法）。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "title": { "type": "string", "description": "标题" },
                "start": { "type": "string", "description": "开始时间，例如 2026-09-15 15:00" },
                "end": { "type": "string", "description": "结束时间，不填默认开始后 1 小时" },
                "allDay": { "type": "boolean", "description": "是否全天" },
                "category": { "type": "string", "description": "分类" },
                "notes": { "type": "string", "description": "备注" },
                "voiceText": { "type": "string", "description": "提醒时播报的文案，留空用全局模板" },
                "color": { "type": "string", "description": "颜色 #RRGGBB" },
                "reminderOffsets": {
                  "type": "array",
                  "items": { "type": "integer" },
                  "description": "提前多少分钟提醒，可多个，例如 [30,5,0]；0 表示准点"
                }
              },
              "required": ["title", "start"]
            }
            """,
        },
        new()
        {
            Name = "update_item",
            Description = "修改某一条日程的字段（只传要改的）。id 用 list_items 返回的 ref。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "id": { "type": "string", "description": "list_items 返回的 ref，例如 #2" },
                "title": { "type": "string" },
                "start": { "type": "string" },
                "end": { "type": "string" },
                "allDay": { "type": "boolean" },
                "category": { "type": "string" },
                "notes": { "type": "string" },
                "voiceText": { "type": "string" },
                "color": { "type": "string" },
                "status": { "type": "string", "description": "pending / done / cancelled" },
                "reminderOffsets": { "type": "array", "items": { "type": "integer" } }
              },
              "required": ["id"]
            }
            """,
        },
        new()
        {
            Name = "reschedule_item",
            Description = "把某一条日程改期（只改时间，不改内容）。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "id": { "type": "string", "description": "list_items 返回的 ref" },
                "newStart": { "type": "string", "description": "新的开始时间" },
                "newEnd": { "type": "string", "description": "新的结束时间，不填则保持原时长" }
              },
              "required": ["id", "newStart"]
            }
            """,
        },
        new()
        {
            Name = "delete_item",
            Description = "删除某一条日程。删除前请先向用户确认。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "id": { "type": "string", "description": "list_items 返回的 ref" }
              },
              "required": ["id"]
            }
            """,
        },
        new()
        {
            Name = "set_status",
            Description = "把某一条日程标记为待办 / 已完成 / 已取消。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "id": { "type": "string", "description": "list_items 返回的 ref" },
                "status": { "type": "string", "description": "pending / done / cancelled（也认 待办 / 已完成 / 已取消）" }
              },
              "required": ["id", "status"]
            }
            """,
        },
        new()
        {
            Name = "next_reminders",
            Description = "接下来会响的提醒（含日程标题、提醒时刻、档位）。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "count": { "type": "integer", "description": "要几条，默认 5" }
              }
            }
            """,
        },
        new()
        {
            Name = "app_info",
            Description = "查看软件当前状态：数据目录、日程总数、今天有几条、当前主题、版本。",
            ParametersJson = """{ "type": "object", "properties": {} }""",
        },
        new()
        {
            Name = "set_theme",
            Description = "切换界面主题。",
            ParametersJson = """
            {
              "type": "object",
              "properties": {
                "theme": { "type": "string", "description": "light / dark / system（也认 浅色 / 夜间 / 跟随系统）" }
              },
              "required": ["theme"]
            }
            """,
        },
    };

    /// <summary>按名字执行一次工具调用。</summary>
    public AiToolResult Execute(AiToolCall call) => Execute(call.Name, call.Arguments, _actions);

    public static AiToolResult Execute(string name, string? argumentsJson, IAiActions actions)
    {
        if (string.IsNullOrWhiteSpace(name)) return AiToolResult.Error("工具名为空。");

        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson!);
            args = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return AiToolResult.Error("参数不是合法 JSON：" + ex.Message);
        }

        if (args.ValueKind != JsonValueKind.Object) return AiToolResult.Error("参数必须是 JSON 对象。");

        return name.Trim().ToLowerInvariant() switch
        {
            "list_items" => ListItems(args, actions),
            "create_item" => CreateItem(args, actions),
            "update_item" => UpdateItem(args, actions),
            "reschedule_item" => RescheduleItem(args, actions),
            "delete_item" => DeleteItem(args, actions),
            "set_status" => SetStatus(args, actions),
            "next_reminders" => NextReminders(args, actions),
            "app_info" => actions.AppInfo(),
            "set_theme" => SetTheme(args, actions),
            _ => AiToolResult.Error("没有名为 " + name + " 的工具。可用工具：" + string.Join(" / ", Definitions.Select(d => d.Name))),
        };
    }

    private static AiToolResult ListItems(JsonElement args, IAiActions actions)
    {
        var query = new AiListQuery
        {
            Date = Text(args, "date"),
            From = Text(args, "from"),
            To = Text(args, "to"),
            Category = Text(args, "category"),
            Keyword = Text(args, "keyword"),
            Limit = Number(args, "limit") ?? 20,
            IncludeDone = Bool(args, "includeDone") ?? true,
        };

        query.Limit = Math.Clamp(query.Limit, 1, 100);

        if (string.IsNullOrWhiteSpace(query.Date)
            && string.IsNullOrWhiteSpace(query.From)
            && string.IsNullOrWhiteSpace(query.To)
            && string.IsNullOrWhiteSpace(query.Category)
            && string.IsNullOrWhiteSpace(query.Keyword))
        {
            return AiToolResult.Error("至少要给一个查询条件：date / from / to / category / keyword。"
                + "想查今天就用 date=今天。");
        }

        return actions.ListItems(query);
    }

    private static AiToolResult CreateItem(JsonElement args, IAiActions actions)
    {
        var title = Text(args, "title");
        if (string.IsNullOrWhiteSpace(title)) return AiToolResult.Error("缺少必填参数 title（日程标题）。");

        var start = Text(args, "start");
        if (string.IsNullOrWhiteSpace(start)) return AiToolResult.Error("缺少必填参数 start（开始时间，例如 2026-09-15 15:00）。");

        var draft = new AiItemDraft
        {
            Title = title.Trim(),
            Start = start.Trim(),
            End = Text(args, "end"),
            AllDay = Bool(args, "allDay"),
            Category = Text(args, "category"),
            Notes = Text(args, "notes"),
            VoiceText = Text(args, "voiceText"),
            Color = Text(args, "color"),
            Status = Text(args, "status"),
            ReminderOffsets = IntList(args, "reminderOffsets"),
        };

        return actions.CreateItem(draft);
    }

    private static AiToolResult UpdateItem(JsonElement args, IAiActions actions)
    {
        var id = Text(args, "id");
        if (string.IsNullOrWhiteSpace(id)) return AiToolResult.Error("缺少必填参数 id（用 list_items 返回的 ref，例如 #1）。");

        var draft = new AiItemDraft
        {
            Title = Text(args, "title"),
            Start = Text(args, "start"),
            End = Text(args, "end"),
            AllDay = Bool(args, "allDay"),
            Category = Text(args, "category"),
            Notes = Text(args, "notes"),
            VoiceText = Text(args, "voiceText"),
            Color = Text(args, "color"),
            Status = Text(args, "status"),
            ReminderOffsets = IntList(args, "reminderOffsets"),
        };

        if (draft.Title == null && draft.Start == null && draft.End == null && draft.AllDay == null
            && draft.Category == null && draft.Notes == null && draft.VoiceText == null
            && draft.Color == null && draft.Status == null && draft.ReminderOffsets == null)
        {
            return AiToolResult.Error("没有给出任何要修改的字段。");
        }

        return actions.UpdateItem(id.Trim(), draft);
    }

    private static AiToolResult RescheduleItem(JsonElement args, IAiActions actions)
    {
        var id = Text(args, "id");
        if (string.IsNullOrWhiteSpace(id)) return AiToolResult.Error("缺少必填参数 id。");

        var newStart = Text(args, "newStart");
        if (string.IsNullOrWhiteSpace(newStart)) return AiToolResult.Error("缺少必填参数 newStart（新的开始时间）。");

        return actions.RescheduleItem(id.Trim(), newStart.Trim(), Text(args, "newEnd"));
    }

    private static AiToolResult DeleteItem(JsonElement args, IAiActions actions)
    {
        var id = Text(args, "id");
        if (string.IsNullOrWhiteSpace(id)) return AiToolResult.Error("缺少必填参数 id。");

        return actions.DeleteItem(id.Trim());
    }

    private static AiToolResult SetStatus(JsonElement args, IAiActions actions)
    {
        var id = Text(args, "id");
        if (string.IsNullOrWhiteSpace(id)) return AiToolResult.Error("缺少必填参数 id。");

        var status = Text(args, "status");
        if (string.IsNullOrWhiteSpace(status)) return AiToolResult.Error("缺少必填参数 status（pending / done / cancelled）。");

        return actions.SetStatus(id.Trim(), status.Trim());
    }

    private static AiToolResult NextReminders(JsonElement args, IAiActions actions)
    {
        var count = Number(args, "count") ?? 5;
        return actions.NextReminders(Math.Clamp(count, 1, 50));
    }

    private static AiToolResult SetTheme(JsonElement args, IAiActions actions)
    {
        var theme = Text(args, "theme");
        if (string.IsNullOrWhiteSpace(theme)) return AiToolResult.Error("缺少必填参数 theme（light / dark / system）。");

        return actions.SetTheme(theme.Trim());
    }

    // ---------- 参数读取小工具（宽容：数字 / 布尔允许用字符串表示） ----------

    public static string? Text(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Object or JsonValueKind.Array => null,
            _ => value.ToString(),
        };
    }

    public static int? Number(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value)) return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static bool? Bool(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => null,
        };
    }

    public static List<int>? IntList(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array) return null;

        var result = new List<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number)) result.Add(number);
            else if (item.ValueKind == JsonValueKind.String
                     && int.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }
}
