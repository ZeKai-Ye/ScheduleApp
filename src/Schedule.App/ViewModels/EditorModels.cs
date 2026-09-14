using System.Windows.Media;
using Schedule.App.Mvvm;

namespace Schedule.App.ViewModels;

/// <summary>提醒档位选项（编辑器里的可勾选胶囊）。</summary>
public sealed class OffsetChoice : ObservableObject
{
    private bool _isSelected;

    public required int Minutes { get; init; }

    public required string Label { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>按档位的播报文案 / 铃声编辑行。</summary>
public sealed class OffsetVoiceRow : ObservableObject
{
    private string _text = string.Empty;
    private string _soundFile = string.Empty;

    public required int Minutes { get; init; }

    public required string Label { get; init; }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value)) OnPropertyChanged(nameof(SourceHint));
        }
    }

    public string SoundFile
    {
        get => _soundFile;
        set
        {
            if (!SetProperty(ref _soundFile, value)) return;
            OnPropertyChanged(nameof(SoundLabel));
            OnPropertyChanged(nameof(SoundTooltip));
        }
    }

    public string SoundLabel => string.IsNullOrEmpty(SoundFile) ? "铃声" : "铃声 ✓";

    public string SoundTooltip => string.IsNullOrEmpty(SoundFile)
        ? "为该档位选择专属提示音"
        : SoundFile + "（点击可更换，「清」可移除）";

    public string SourceHint => string.IsNullOrWhiteSpace(Text) ? "留空 → 用通用 / 全局设置" : "本档位专属";
}

/// <summary>全局「按档位默认文案」编辑行（设置页）。</summary>
public sealed class OffsetTextRow : ObservableObject
{
    private string _text = string.Empty;

    public required int Minutes { get; init; }

    public required string Label { get; init; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string Hint => Minutes == 0 ? "准点" : "提前 " + Minutes + " 分钟";
}

/// <summary>
/// 颜色选项。Hex 为空表示「跟随分类」自动配色。
/// </summary>
public sealed class ColorChoice : ObservableObject
{
    private static readonly Brush AutoBrush = CreateAutoBrush();

    private bool _isSelected;

    public required string Hex { get; init; }

    public required string Name { get; init; }

    public bool IsAuto => string.IsNullOrEmpty(Hex);

    public Brush Brush => IsAuto ? AutoBrush : BrushCache.Get(Hex);

    private static Brush CreateAutoBrush()
    {
        var gradient = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF),
            System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));
        gradient.Freeze();
        return gradient;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>分类配色项（设置页可点击换色）。</summary>
public sealed class CategoryColorChoice : ObservableObject
{
    private string _hex = string.Empty;

    public required string Name { get; init; }

    public string Hex
    {
        get => _hex;
        set
        {
            if (!SetProperty(ref _hex, value)) return;
            OnPropertyChanged(nameof(Brush));
            OnPropertyChanged(nameof(Display));
        }
    }

    public Brush Brush => string.IsNullOrEmpty(Hex) ? ThemeBrushes.Border : BrushCache.Get(Hex);

    public string Display => string.IsNullOrEmpty(Hex) ? "自动" : Hex;

    /// <summary>点击一次切换到调色板中的下一个颜色。</summary>
    public void Cycle()
    {
        var palette = Schedule.Core.Services.EventColors.Palette;
        var index = Array.IndexOf(palette, Hex);
        Hex = index < 0 ? palette[0] : palette[(index + 1) % palette.Length];
    }

    public void Reset() => Hex = string.Empty;
}

/// <summary>星期选项（重复设置用）。</summary>
public sealed class WeekdayChoice : ObservableObject
{
    private bool _isSelected;

    public required DayOfWeek Day { get; init; }

    public required string Label { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
