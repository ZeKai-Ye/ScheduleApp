using System.Windows.Markup;
using Schedule.Core.Localization;

namespace Schedule.App.Mvvm;

/// <summary>
/// XAML 里取当前语言的文案：<c>Text="{l:Text '新建日程|New event'}"</c>。
///
/// 两段文案用 <c>|</c> 分隔，左边中文、右边英文；只写一段时中英文都用它（例如纯数字 / 符号）。
/// 因为标记扩展的参数只能是字符串，所以英文里的撇号请写成全角 <c>’</c>，
/// 否则会与 XAML 的单引号定界符冲突。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension()
    {
    }

    public TextExtension(string pair) => Pair = pair;

    /// <summary>「中文|English」形式的双语文案。</summary>
    public string Pair { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Pair);
}
