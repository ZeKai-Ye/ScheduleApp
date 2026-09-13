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

/// <summary>颜色选项。Hex 为空表示「跟随分类」自动配色。</summary>
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

    public Brush Brush => string.IsNullOrEmpty(Hex) ? BrushCache.Get("#E5E7EB") : BrushCache.Get(Hex);

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
