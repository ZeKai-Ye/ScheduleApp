using System.Text.RegularExpressions;
using Schedule.Core.Localization;

namespace Schedule.Core.Tests.Cases;

/// <summary>
/// 双语机制的自测 + 「漏翻」扫描。
///
/// 扫描部分直接读仓库源码（XAML 与 C#），确保：
/// 1. XAML 里不再有硬编码的中文属性（Text / Content / Header / ToolTip / Title）；
/// 2. 每一对 <c>{l:Text '中文|English'}</c> 都有非空英文；
/// 3. 每个 <c>Loc.T("中文", "English")</c> 的英文参数都非空。
/// 这样以后再往界面里写死中文，自测会直接失败。
/// </summary>
public static class LocalizationTests
{
    private static readonly Regex XamlPair = new(
        @"\{l:Text\s+'((?:[^'\\]|\\.)*)'\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex XamlCjkAttribute = new(
        @"(?<attr>Text|Content|Header|ToolTip|Title)=""(?<value>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CodePair = new(
        @"Loc\.(?:T|F)\(\s*""(?<zh>(?:[^""\\]|\\.)*)""\s*,\s*""(?<en>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Run()
    {
        TestRunner.Section("双语与本地化");

        // ---------- 基础 API ----------
        Loc.Reset();
        TestRunner.Equal(AppLanguage.Chinese, Loc.Current, "默认语言是中文");
        TestRunner.Equal("新建", Loc.T("新建", "New"), "中文取中文");
        TestRunner.Equal("共 3 条", Loc.F("共 {0} 条", "{0} items", 3), "中文格式化");

        Loc.Use(AppLanguage.English);
        TestRunner.Equal("New", Loc.T("新建", "New"), "英文取英文");
        TestRunner.Equal("3 items", Loc.F("共 {0} 条", "{0} items", 3), "英文格式化");
        TestRunner.Check(Loc.IsEnglish, "IsEnglish 跟随当前语言");
        TestRunner.Equal("New", Loc.T("新建|New"), "分隔符写法取英文");
        TestRunner.Equal("只有中文", Loc.T("只有中文|"), "英文为空时回退中文");

        Loc.Reset();
        TestRunner.Equal("新建", Loc.T("新建|New"), "重置后分隔符写法取中文");
        TestRunner.Equal("无分隔符", Loc.T("无分隔符"), "无分隔符原样返回");
        TestRunner.Equal(string.Empty, Loc.T(""), "空文案返回空串");

        // ---------- 语言解析 ----------
        TestRunner.Equal(AppLanguage.English, Loc.Parse("en"), "解析 en");
        TestRunner.Equal(AppLanguage.English, Loc.Parse("EN-US"), "解析大小写与地区");
        TestRunner.Equal(AppLanguage.English, Loc.Parse("英文"), "解析中文写法「英文」");
        TestRunner.Equal(AppLanguage.Chinese, Loc.Parse("zh-CN"), "解析 zh-CN");
        TestRunner.Equal(AppLanguage.Chinese, Loc.Parse("中文"), "解析「中文」");
        TestRunner.Equal(AppLanguage.System, Loc.Parse("system"), "解析 system");
        TestRunner.Check(Loc.Parse("klingon") == null, "无法识别的语言返回 null");
        TestRunner.Check(Loc.Parse(null) == null, "空值返回 null");

        TestRunner.Equal(AppLanguage.English, Loc.Resolve(AppLanguage.English), "明确指定英文不受系统影响");
        TestRunner.Equal(AppLanguage.Chinese, Loc.Resolve(AppLanguage.Chinese), "明确指定中文不受系统影响");

        // 跟随系统必须落在两种具体语言之一
        var system = Loc.Resolve(AppLanguage.System);
        TestRunner.Check(system is AppLanguage.Chinese or AppLanguage.English, "跟随系统解析为具体语言");

        // 设置字段默认值与拷贝
        var settings = new Schedule.Core.Models.AppSettings();
        TestRunner.Equal(AppLanguage.System, settings.Language, "设置默认跟随系统");
        settings.Language = AppLanguage.English;
        TestRunner.Equal(AppLanguage.English, settings.Clone().Language, "克隆保留语言设置");

        Loc.Reset();

        // ---------- 源码扫描 ----------
        var root = FindRepositoryRoot();
        if (root == null)
        {
            // 发布 / 拷贝运行时不扫描，避免误报
            return;
        }

        var xamlFiles = EnumerateSources(root, "*.xaml");
        var codeFiles = EnumerateSources(root, "*.cs");

        var pairs = 0;
        var problems = new List<string>();

        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);
            var shortName = Path.GetRelativePath(root, file);

            foreach (Match match in XamlPair.Matches(text))
            {
                pairs++;
                var pair = match.Groups[1].Value;
                CheckPair(shortName, pair, problems);
            }

            foreach (Match match in XamlCjkAttribute.Matches(text))
            {
                var value = match.Groups["value"].Value.Trim();
                if (value.StartsWith('{')) continue;
                if (!ContainsHan(value)) continue;

                problems.Add(shortName + " 硬编码中文 " + match.Groups["attr"].Value + "=\"" + Trim(value) + "\"");
            }
        }

        foreach (var file in codeFiles)
        {
            var text = File.ReadAllText(file);
            var shortName = Path.GetRelativePath(root, file);

            foreach (Match match in CodePair.Matches(text))
            {
                pairs++;
                var english = match.Groups["en"].Value;
                if (string.IsNullOrWhiteSpace(english))
                {
                    problems.Add(shortName + " 英文为空：" + Trim(match.Groups["zh"].Value));
                }
            }
        }

        TestRunner.Check(pairs > 100, "界面文案已接入双语（实际 " + pairs + " 处）");

        foreach (var problem in problems.Take(5))
        {
            Console.WriteLine("  [漏翻] " + problem);
        }

        TestRunner.Equal(0, problems.Count, "没有硬编码中文 / 空翻译（实际 " + problems.Count + " 处）");
    }

    private static void CheckPair(string file, string pair, List<string> problems)
    {
        var index = pair.IndexOf('|');
        if (index < 0)
        {
            problems.Add(file + " 缺少英文：" + Trim(pair));
            return;
        }

        var english = pair[(index + 1)..];
        if (string.IsNullOrWhiteSpace(english))
        {
            problems.Add(file + " 英文为空：" + Trim(pair[..index]));
            return;
        }

        if (ContainsHan(english))
        {
            problems.Add(file + " 英文里仍有中文：" + Trim(pair));
        }
    }

    private static bool ContainsHan(string value)
    {
        foreach (var ch in value)
        {
            if (ch >= '\u4e00' && ch <= '\u9fff') return true;
        }

        return false;
    }

    private static string Trim(string value)
    {
        var flat = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= 40 ? flat : flat[..40] + "…";
    }

    private static IEnumerable<string> EnumerateSources(string root, string pattern)
    {
        var source = Path.Combine(root, "src");
        if (!Directory.Exists(source)) yield break;

        foreach (var file in Directory.EnumerateFiles(source, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

            yield return file;
        }
    }

    /// <summary>从测试程序输出目录向上找带解决方案文件的仓库根。</summary>
    private static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ScheduleApp.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
