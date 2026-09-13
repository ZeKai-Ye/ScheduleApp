using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schedule.Core.Csv;
using Schedule.Core.Models;

namespace Schedule.Core.Api;

/// <summary>一次接口请求。</summary>
public sealed class ApiRequest
{
    public string Method { get; init; } = "GET";

    public string Path { get; init; } = "/";

    public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string Body { get; init; } = string.Empty;

    public string? RawQuery { get; init; }

    /// <summary>客户端使用了分块传输（本服务器不支持，会回 411）。</summary>
    public bool RequiresLength { get; init; }

    public string? GetQuery(string name)
        => Query.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    public int? GetInt(string name)
        => int.TryParse(GetQuery(name), out var value) ? value : null;

    public bool GetBool(string name, bool fallback = false)
    {
        var raw = GetQuery(name);
        if (raw == null) return fallback;
        return raw.Equals("1", StringComparison.Ordinal) || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public DateTime? GetDate(string name, DateTime? fallback = null)
        => FlexibleDateTime.TryParse(GetQuery(name), out var value) ? value : fallback;

    /// <summary>本次请求携带的访问令牌（请求头或查询串）。</summary>
    public string? GetToken()
    {
        if (Headers.TryGetValue("X-Api-Token", out var header) && header.Length > 0) return header;
        if (Headers.TryGetValue("Authorization", out var auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth[7..].Trim();
        }

        return GetQuery("token");
    }
}

/// <summary>一次接口响应。</summary>
public sealed class ApiResponse
{
    public int Status { get; init; } = 200;

    public string ContentType { get; init; } = "application/json; charset=utf-8";

    public string Body { get; init; } = string.Empty;

    public Dictionary<string, string> Headers { get; init; } = new();

    public static ApiResponse Json(object? payload, int status = 200)
        => new() { Status = status, Body = JsonSerializer.Serialize(payload, ApiJson.Options) };

    public static ApiResponse Text(string text, int status = 200, string contentType = "text/plain; charset=utf-8")
        => new() { Status = status, Body = text, ContentType = contentType };

    public static ApiResponse Error(int status, string message, string? hint = null)
        => Json(new { error = message, hint, status }, status);

    public static ApiResponse NoContent() => new() { Status = 204, Body = string.Empty };
}

/// <summary>宿主提供的语音 / 提醒动作（Core 不依赖 WPF，由 App 层实现）。</summary>
public interface IApiActions
{
    bool IsAvailable { get; }

    IReadOnlyList<VoiceDescriptor> GetVoices();

    bool Speak(string text, string? voiceId, int? rate, int? volume, out string? error);

    bool TriggerReminder(Guid itemId, int offsetMinutes, out string? error);

    /// <summary>创建 / 删除桌面或开始菜单快捷方式。</summary>
    bool SetShortcut(string target, bool create, string? directoryOverride, out string? path, out string? error);

    /// <summary>启用 / 停用开机启动。</summary>
    bool SetAutostart(bool enabled, string? arguments, out string? mode, out string? error);

    /// <summary>读取系统状态（可执行文件、快捷方式、开机启动）。</summary>
    SystemStatusInfo GetSystemStatus();
}

/// <summary>宿主系统状态。</summary>
public sealed class SystemStatusInfo
{
    public string ExecutablePath { get; init; } = string.Empty;

    public string DataDirectory { get; init; } = string.Empty;

    public bool DesktopShortcutExists { get; init; }

    public bool StartMenuShortcutExists { get; init; }

    public bool AutostartEnabled { get; init; }

    /// <summary>Registry / StartupFolder / Disabled。</summary>
    public string AutostartMode { get; init; } = "Disabled";

    /// <summary>开机启动实际执行的命令行。</summary>
    public string AutostartCommand { get; init; } = string.Empty;

    /// <summary>宿主是否支持这些系统能力。</summary>
    public bool Supported { get; init; }
}

/// <summary>无语音能力时的空实现（自测与 --serve --no-voice 场景）。</summary>
public sealed class NullApiActions : IApiActions
{
    public bool IsAvailable => false;

    public IReadOnlyList<VoiceDescriptor> GetVoices() => Array.Empty<VoiceDescriptor>();

    public bool Speak(string text, string? voiceId, int? rate, int? volume, out string? error)
    {
        error = "当前进程未提供语音能力（Core 测试宿主）";
        return false;
    }

    public bool TriggerReminder(Guid itemId, int offsetMinutes, out string? error)
    {
        error = "当前进程未提供提醒能力（Core 测试宿主）";
        return false;
    }

    public bool SetShortcut(string target, bool create, string? directoryOverride, out string? path, out string? error)
    {
        path = null;
        error = "当前进程未提供快捷方式能力（Core 测试宿主）";
        return false;
    }

    public bool SetAutostart(bool enabled, string? arguments, out string? mode, out string? error)
    {
        mode = null;
        error = "当前进程未提供开机启动能力（Core 测试宿主）";
        return false;
    }

    public SystemStatusInfo GetSystemStatus() => new() { Supported = false };
}

/// <summary>接口使用的 JSON 约定。</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new FlexibleDateTimeConverter(),
        },
    };

    public static T? Deserialize<T>(string body) => JsonSerializer.Deserialize<T>(body, Options);
}

/// <summary>
/// 宽容的日期时间转换：接口同时接受 ~~2025-06-03T14:00:00~~、~~2025-06-03 14:00~~、
/// ~~2025/6/3 14:00~~、~~明天 9:00~~ 等写法，与 CSV 导入保持一致。
/// </summary>
public sealed class FlexibleDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("日期时间必须是字符串，例如 \"2025-06-03 14:00\"。");
        }

        var text = reader.GetString();
        if (FlexibleDateTime.TryParse(text, out var value)) return value;

        throw new JsonException("无法解析日期时间：" + text);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss"));
}

/// <summary>
/// 接口写入日程的入参。所有字段可空：PUT 会用默认值覆盖未提供的字段，PATCH 只改提供的字段。
/// </summary>
public sealed class ScheduleItemInput
{
    public string? Title { get; set; }

    public string? Notes { get; set; }

    public string? Category { get; set; }

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public bool? AllDay { get; set; }

    public string? Color { get; set; }

    public Priority? Priority { get; set; }

    public RepeatKind? Repeat { get; set; }

    public int? RepeatInterval { get; set; }

    public List<DayOfWeek>? RepeatWeekdays { get; set; }

    public int? RepeatCount { get; set; }

    public DateTime? RepeatUntil { get; set; }

    public bool? ReminderEnabled { get; set; }

    /// <summary>提醒档位（提前分钟数列表），例如 [30, 15, 5, 0]。</summary>
    public List<int>? ReminderOffsets { get; set; }

    /// <summary>兼容写法：单个提前分钟数（等价于 ReminderOffsets = [值]）。</summary>
    public int? RemindBeforeMinutes { get; set; }

    /// <summary>按档位自定义播报文案，键为提前分钟数。</summary>
    public Dictionary<int, string>? ReminderVoiceTexts { get; set; }

    /// <summary>按档位自定义提示音。</summary>
    public Dictionary<int, string>? ReminderSoundFiles { get; set; }

    public DateTime? RemindAtOverride { get; set; }

    public string? VoiceText { get; set; }

    public string? SoundFile { get; set; }

    public ItemStatus? Status { get; set; }

    /// <summary>把入参写入日程。</summary>
    /// <param name="item">目标日程。</param>
    /// <param name="partial">true = 只覆盖提供了的字段（PATCH）；false = 其余字段复位为默认值（PUT）。</param>
    /// <param name="now">当前时间，用于 CreatedAt / UpdatedAt。</param>
    public void ApplyTo(ScheduleItem item, bool partial, DateTime now)
    {
        if (!partial)
        {
            item.Notes = string.Empty;
            item.Category = string.Empty;
            item.End = null;
            item.AllDay = false;
            item.Color = string.Empty;
            item.Priority = Schedule.Core.Models.Priority.Normal;
            item.Repeat = RepeatKind.None;
            item.RepeatInterval = 1;
            item.RepeatWeekdays = new List<DayOfWeek>();
            item.RepeatCount = null;
            item.RepeatUntil = null;
            item.ReminderEnabled = true;
            item.ReminderOffsets = new List<int> { 10 };
            item.ReminderVoiceTexts = new Dictionary<int, string>();
            item.ReminderSoundFiles = new Dictionary<int, string>();
            item.RemindAtOverride = null;
            item.VoiceText = string.Empty;
            item.SoundFile = string.Empty;
            item.Status = ItemStatus.Pending;
        }

        if (Title != null) item.Title = Title.Trim();
        if (Notes != null) item.Notes = Notes;
        if (Category != null) item.Category = Category.Trim();
        if (Start.HasValue) item.Start = Start.Value;
        if (End.HasValue) item.End = End.Value;
        if (AllDay.HasValue) item.AllDay = AllDay.Value;
        if (Color != null)
        {
            // 支持 #RRGGBB，也支持「红 / 蓝色 / red」这类色名
            item.Color = Schedule.Core.Services.EventColors.TryParse(Color, out var parsedColor)
                ? parsedColor
                : Schedule.Core.Services.EventColors.Normalize(Color);
        }
        if (Priority.HasValue) item.Priority = Priority.Value;
        if (Repeat.HasValue) item.Repeat = Repeat.Value;
        if (RepeatInterval.HasValue) item.RepeatInterval = Math.Max(1, RepeatInterval.Value);
        if (RepeatWeekdays != null) item.RepeatWeekdays = RepeatWeekdays.Distinct().ToList();
        if (RepeatCount.HasValue) item.RepeatCount = RepeatCount.Value <= 0 ? null : RepeatCount.Value;
        if (RepeatUntil.HasValue) item.RepeatUntil = RepeatUntil.Value;
        if (ReminderEnabled.HasValue) item.ReminderEnabled = ReminderEnabled.Value;

        if (ReminderOffsets != null) item.ReminderOffsets = ReminderOffsets.Where(o => o >= 0).Distinct().OrderByDescending(o => o).ToList();
        else if (RemindBeforeMinutes.HasValue) item.ReminderOffsets = new List<int> { Math.Max(0, RemindBeforeMinutes.Value) };

        if (ReminderVoiceTexts != null) item.ReminderVoiceTexts = new Dictionary<int, string>(ReminderVoiceTexts);
        if (ReminderSoundFiles != null) item.ReminderSoundFiles = new Dictionary<int, string>(ReminderSoundFiles);

        if (RemindAtOverride.HasValue) item.RemindAtOverride = RemindAtOverride.Value;
        if (VoiceText != null) item.VoiceText = VoiceText;
        if (SoundFile != null) item.SoundFile = SoundFile;

        if (Status.HasValue) item.Status = Status.Value;

        if (item.ReminderOffsets.Count > 0)
        {
            item.RemindBeforeMinutes = item.ReminderOffsets.Min();
        }

        if (item.AllDay)
        {
            item.Start = item.Start.Date;
            if (item.End.HasValue) item.End = item.End.Value.Date.AddDays(1);
            if (item.RemindAtOverride.HasValue) item.RemindAtOverride = item.RemindAtOverride.Value.Date;
        }

        // 只保留仍然存在的档位文案 / 铃声
        var known = new HashSet<int>(item.NormalizedOffsets());
        item.ReminderVoiceTexts = item.ReminderVoiceTexts
            .Where(p => known.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value);
        item.ReminderSoundFiles = item.ReminderSoundFiles
            .Where(p => known.Contains(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value);

        if (partial) item.UpdatedAt = now;
    }
}
