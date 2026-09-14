namespace Schedule.Core.Ics;

/// <summary>iCalendar 里的一行属性（已展开折行）。</summary>
public sealed class IcsProperty
{
    /// <summary>属性名（大写，例如 DTSTART）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>参数（例如 TZID=Asia/Shanghai、VALUE=DATE）。</summary>
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>原始值（未反转义）。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>取值文本（去掉转义）。</summary>
    public string Text => IcsText.Unescape(Value);

    public string? Parameter(string name) => Parameters.TryGetValue(name, out var value) ? value : null;
}

/// <summary>一个 VEVENT（含其 VALARM）。</summary>
public sealed class IcsEvent
{
    public List<IcsProperty> Properties { get; } = new();

    public List<List<IcsProperty>> Alarms { get; } = new();

    public IcsProperty? Find(string name)
        => Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<IcsProperty> FindAll(string name)
        => Properties.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public string Get(string name) => Find(name)?.Value ?? string.Empty;

    /// <summary>UID（没有就为空，导入时会自动补一个）。</summary>
    public string Uid => Find("UID")?.Value.Trim() ?? string.Empty;

    /// <summary>是不是某一次发生的"单独修订"（RECURRENCE-ID）。</summary>
    public bool IsOverride => Find("RECURRENCE-ID") != null;

    /// <summary>VEVENT 在源文件里的行号（报错定位用）。</summary>
    public int Line { get; set; }
}

/// <summary>解析结果。</summary>
public sealed class IcsParseResult
{
    public List<IcsEvent> Events { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>解析层面的错误（行号 + 说明）。</summary>
    public List<(int Line, string Message)> Errors { get; } = new();

    public bool LooksLikeCalendar { get; set; }
}

/// <summary>导入选项。</summary>
public sealed class IcsImportOptions
{
    public Schedule.Core.Models.ImportMode Mode { get; set; } = Schedule.Core.Models.ImportMode.Append;

    /// <summary>匹配到已存在的「标题 + 开始时间」时跳过。</summary>
    public bool SkipDuplicates { get; set; } = true;

    /// <summary>跳过已经过去的日程。</summary>
    public bool SkipPastItems { get; set; }

    public string DefaultCategory { get; set; } = string.Empty;

    public string DefaultColor { get; set; } = string.Empty;

    public bool DefaultReminderEnabled { get; set; } = true;

    public List<int> DefaultReminderOffsets { get; set; } = new();

    public int DefaultRemindBeforeMinutes { get; set; } = 10;

    public string SourceTag { get; set; } = string.Empty;
}

/// <summary>文本反转义 / 转义（iCalendar 规定 \n \, \; \\）。</summary>
public static class IcsText
{
    public static string Unescape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                continue;
            }

            var next = value[++i];
            sb.Append(next switch
            {
                'n' or 'N' => '\n',
                ',' => ',',
                ';' => ';',
                '\\' => '\\',
                _ => next,
            });
        }

        return sb.ToString();
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");
    }

    /// <summary>折行：iCalendar 规定单行不超过 75 字节，续行以空格开头。</summary>
    public static string Fold(string line)
    {
        if (line.Length <= 73) return line;

        var sb = new System.Text.StringBuilder();
        var index = 0;

        while (index < line.Length)
        {
            var take = index == 0 ? 73 : 72;
            if (index + take > line.Length) take = line.Length - index;

            if (index > 0) sb.Append("\r\n ");
            sb.Append(line, index, take);

            index += take;
        }

        return sb.ToString();
    }
}
