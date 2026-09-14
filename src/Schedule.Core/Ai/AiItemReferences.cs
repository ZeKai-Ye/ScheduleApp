using System.Text.RegularExpressions;

namespace Schedule.Core.Ai;

/// <summary>一个引用指向的目标：日程本体，外加「系列中的哪一次」（如果有）。</summary>
public sealed class AiReferenceTarget
{
    public string Id { get; init; } = string.Empty;

    /// <summary>该引用对应系列中的哪一次发生（原始开始时间）；null 表示整条日程。</summary>
    public DateTime? OccurrenceStart { get; init; }

    public string Title { get; init; } = string.Empty;
}

/// <summary>
/// 日程引用表：把 GUID 换成模型容易复述的 #1 / #2。
///
/// 本地 8B 模型抄一长串 GUID 很容易抄错或干脆编一个，所以每次 list_items / 搜索之后
/// 都由这里重新编号，模型后续只需要把 "#2" 原样传回来。
/// 展开重复日程时还会记住「这一次」的原始开始时间，于是「把明天的例会改到 16:00」
/// 这类要求能落成「仅这一次」的修改，而不是把整个系列搬走。
/// </summary>
public sealed class AiItemReferences
{
    private static readonly Regex Numbered = new(@"^#?(\d{1,3})$", RegexOptions.Compiled);

    private readonly Dictionary<string, AiReferenceTarget> _byReference = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byReference.Count;

    /// <summary>登记一个目标，返回它的引用（同一目标复用同一个编号）。</summary>
    public string Register(string id, DateTime? occurrenceStart = null, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;

        var key = Key(id, occurrenceStart);
        if (_byKey.TryGetValue(key, out var existing)) return existing;

        var reference = "#" + (_byReference.Count + 1);
        _byReference[reference] = new AiReferenceTarget
        {
            Id = id.Trim(),
            OccurrenceStart = occurrenceStart,
            Title = title ?? string.Empty,
        };

        _byKey[key] = reference;
        return reference;
    }

    /// <summary>清空编号（新一轮列表会重新从 #1 开始）。</summary>
    public void Reset()
    {
        _byReference.Clear();
        _byKey.Clear();
    }

    /// <summary>
    /// 把模型给的东西解析成目标：
    /// "#2" / "2" → 查表；直接给 GUID → 整条日程；查不到返回 null。
    /// </summary>
    public AiReferenceTarget? TryResolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var text = reference.Trim().Trim('\'', '"', '。', '，', ',', '.', '\u3002');

        var numbered = Numbered.Match(text);
        if (numbered.Success)
        {
            return _byReference.TryGetValue("#" + numbered.Groups[1].Value, out var target) ? target : null;
        }

        return Guid.TryParse(text, out _) ? new AiReferenceTarget { Id = text } : null;
    }

    /// <summary>只取日程 id（不需要区分某一次时用）。</summary>
    public string? Resolve(string? reference) => TryResolve(reference)?.Id;

    /// <summary>模型给了一个不认识的引用时的提示语。</summary>
    public string DescribeUnknown(string? reference)
        => "找不到日程 " + (reference ?? "(空)") + "。请先用 list_items 查询，再用它返回的 ref（例如 #1）操作。"
            + (_byReference.Count == 0 ? " 目前还没有任何编号。" : " 当前有效编号：" + string.Join(" ", _byReference.Keys.OrderBy(k => k)));

    private static string Key(string id, DateTime? occurrenceStart)
        => id.Trim().ToLowerInvariant() + "|" + (occurrenceStart?.ToString("yyyyMMddHHmmss") ?? "-");
}
