using System.Globalization;

namespace Schedule.Core.Localization;

/// <summary>界面语言选项（顺序与设置页下拉一致）。</summary>
public enum AppLanguage
{
    /// <summary>跟随 Windows 的显示语言：中文系统用中文，其它用英文。</summary>
    System = 0,

    /// <summary>简体中文。</summary>
    Chinese = 1,

    /// <summary>English。</summary>
    English = 2,
}

/// <summary>
/// 双语文案中心。整个程序只在这里决定「当前说哪种语言」。
///
/// 设计取舍：**不做 key → 资源表**，而是让调用点自带两份文案
/// （<c>Loc.T("新建日程", "New event")</c>）。理由是：
/// 1. 没有 key 命名与维护成本，删代码时文案跟着一起走，不会留下孤儿资源；
/// 2. 编译期就能发现漏写的翻译（少一个参数直接编译不过）；
/// 3. Core 层不需要引用任何资源 / UI 框架，自测与命令行同样可用。
///
/// 默认语言是中文，因此既有的自测与命令行输出保持原样。
/// XAML 里的文案在窗口加载时求值，所以切换语言需要重启（与主题切换同一套机制）。
/// </summary>
public static class Loc
{
    /// <summary>XAML 里两段文案的分隔符（例如 <c>{l:Text '新建日程|New event'}</c>）。</summary>
    public const char PairSeparator = '|';

    private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-CN");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>当前生效的语言（一定是 <see cref="AppLanguage.Chinese"/> 或 <see cref="AppLanguage.English"/>）。</summary>
    public static AppLanguage Current { get; private set; } = AppLanguage.Chinese;

    /// <summary>当前是否英文界面。</summary>
    public static bool IsEnglish => Current == AppLanguage.English;

    /// <summary>与当前语言匹配的区域信息（用于数字 / 日期格式与拼句）。</summary>
    public static CultureInfo Culture => IsEnglish ? EnglishCulture : ChineseCulture;

    /// <summary>切换语言。传 <see cref="AppLanguage.System"/> 会按系统显示语言解析。</summary>
    public static void Use(AppLanguage language) => Current = Resolve(language);

    /// <summary>把「跟随系统」解析成具体语言。</summary>
    public static AppLanguage Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => AppLanguage.Chinese,
        AppLanguage.English => AppLanguage.English,
        _ => IsChineseSystem() ? AppLanguage.Chinese : AppLanguage.English,
    };

    /// <summary>当前系统的显示语言是否中文。</summary>
    public static bool IsChineseSystem()
    {
        try
        {
            if (string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 界面语言被切换过之后 CurrentUICulture 已经不可信，再看系统安装语言
            return string.Equals(CultureInfo.InstalledUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>取当前语言的文案。</summary>
    public static string T(string chinese, string english)
        => IsEnglish && !string.IsNullOrEmpty(english) ? english : chinese;

    /// <summary>
    /// 取当前语言的文案（单参数形式，两段用 <see cref="PairSeparator"/> 分隔）。
    /// 主要给 XAML 标记扩展用；代码里优先用双参数重载。
    /// </summary>
    public static string T(string pair)
    {
        if (string.IsNullOrEmpty(pair)) return string.Empty;

        var index = pair.IndexOf(PairSeparator);
        if (index < 0) return pair;

        var chinese = pair[..index];
        var english = pair[(index + 1)..];

        return IsEnglish && !string.IsNullOrEmpty(english) ? english : chinese;
    }

    /// <summary>带占位符的文案（格式串各自独立，避免两种语言的语序互相将就）。</summary>
    public static string F(string chineseFormat, string englishFormat, params object?[] args)
        => string.Format(Culture, IsEnglish && !string.IsNullOrEmpty(englishFormat) ? englishFormat : chineseFormat, args);

    /// <summary>带占位符的单参数形式：<c>"共 {0} 条|{0} items"</c>。</summary>
    public static string F(string pair, params object?[] args) => string.Format(Culture, T(pair), args);

    /// <summary>
    /// 解析命令行 / 设置里的语言写法。无法识别时返回 null。
    /// 中英文写法都接受，方便用户直接抄文档里的例子。
    /// </summary>
    public static AppLanguage? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return text.Trim().ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" or "chs" or "chinese" or "cn" or "中文" or "汉语" or "简体中文" => AppLanguage.Chinese,
            "en" or "en-us" or "en-gb" or "eng" or "english" or "英文" or "英语" => AppLanguage.English,
            "system" or "auto" or "os" or "跟随系统" or "系统" => AppLanguage.System,
            _ => null,
        };
    }

    /// <summary>把语言模式转成人能读的说明（设置页用）。</summary>
    public static string Describe(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "中文",
        AppLanguage.English => "English",
        _ => T("跟随系统（当前中文）", "Follow system (currently English)"),
    };

    /// <summary>供自测恢复默认语言，避免用例之间互相影响。</summary>
    public static void Reset() => Current = AppLanguage.Chinese;
}
