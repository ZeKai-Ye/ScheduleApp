using System.Windows.Media;
using Schedule.App.Mvvm;
using Schedule.Core.Models;

namespace Schedule.App.ViewModels;

/// <summary>月视图中的一条小色条。</summary>
public sealed class MonthChipViewModel
{
    public required ScheduleItem Item { get; init; }

    public required DateTime Start { get; init; }

    public required string TimeText { get; init; }

    public required string TitleText { get; init; }

    public required string ColorHex { get; init; }

    public required bool IsDone { get; init; }

    public Brush ColorBrush => BrushCache.Get(ColorHex);

    public Brush SoftBrush => BrushCache.Softer(ColorHex);

    public string ToolTipText { get; init; } = string.Empty;
}

/// <summary>月视图的日期格。</summary>
public sealed class MonthCellViewModel : ObservableObject
{
    private bool _isSelected;

    public required DateOnly Date { get; init; }

    public required string DayNumber { get; init; }

    public required bool IsCurrentMonth { get; init; }

    public required bool IsToday { get; init; }

    public required bool IsWeekend { get; init; }

    /// <summary>该格内是否包含「今天」之外的其它标记（用于节气/占位）。</summary>
    public int EventCount { get; init; }

    public string MoreText { get; init; } = string.Empty;

    public bool HasMore => MoreText.Length > 0;

    public List<MonthChipViewModel> Chips { get; init; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;

            OnPropertyChanged(nameof(CellBackground));
            OnPropertyChanged(nameof(CellBorderBrush));
            OnPropertyChanged(nameof(CellBorderThickness));
        }
    }

    // ---------- 直接可用的画刷，避免 XAML 里堆叠触发器 ----------

    public System.Windows.Media.Brush CellBackground => IsSelected
        ? BrushCache.Get("#EFF6FF")
        : IsCurrentMonth ? System.Windows.Media.Brushes.White : BrushCache.Get("#FAFAFB");

    public System.Windows.Media.Brush CellBorderBrush => IsSelected
        ? BrushCache.Get("#2563EB")
        : BrushCache.Get("#E5E7EB");

    public System.Windows.Thickness CellBorderThickness => IsSelected
        ? new System.Windows.Thickness(2)
        : new System.Windows.Thickness(0, 0, 1, 1);

    public double CellOpacity => IsCurrentMonth ? 1.0 : 0.55;

    public System.Windows.Media.Brush DayNumberForeground => IsToday
        ? System.Windows.Media.Brushes.White
        : IsCurrentMonth ? BrushCache.Get("#111827") : BrushCache.Get("#9CA3AF");

    public System.Windows.Media.Brush DayNumberBackground => IsToday
        ? BrushCache.Get("#2563EB")
        : System.Windows.Media.Brushes.Transparent;
}

/// <summary>周视图中的一个时间块。</summary>
public sealed class WeekBlockViewModel
{
    public required ScheduleItem Item { get; init; }

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    public required string TitleText { get; init; }

    public required string TimeText { get; init; }

    public required string ColorHex { get; init; }

    public required bool IsDone { get; init; }

    /// <summary>距当天 0 点的像素位置。</summary>
    public required double Top { get; init; }

    public required double Height { get; init; }

    /// <summary>重叠分列后的左偏移与宽度（像素）。</summary>
    public required double Left { get; init; }

    public required double Width { get; init; }

    public Brush ColorBrush => BrushCache.Get(ColorHex);

    public Brush SoftBrush => BrushCache.Soft(ColorHex);

    public string ToolTipText { get; init; } = string.Empty;
}

/// <summary>周视图中的一天。</summary>
public sealed class WeekColumnViewModel
{
    public required DateOnly Date { get; init; }

    public System.Windows.Media.Brush HeaderForeground => IsToday
        ? BrushCache.Get("#2563EB")
        : IsWeekend ? BrushCache.Get("#9CA3AF") : BrushCache.Get("#111827");

    public System.Windows.Media.Brush DayNumberForeground => IsToday
        ? System.Windows.Media.Brushes.White
        : System.Windows.Media.Brushes.Transparent;

    public System.Windows.Media.Brush DayNumberBackground => IsToday
        ? BrushCache.Get("#2563EB")
        : System.Windows.Media.Brushes.Transparent;

    public System.Windows.Media.Brush ColumnBackground => IsWeekend
        ? BrushCache.Get("#FCFCFD")
        : System.Windows.Media.Brushes.Transparent;

    public required string HeaderText { get; init; }

    public required string DayNumberText { get; init; }

    public required bool IsToday { get; init; }

    public required bool IsWeekend { get; init; }

    public required double CanvasHeight { get; init; }

    public List<WeekBlockViewModel> Blocks { get; init; } = new();

    public List<MonthChipViewModel> AllDayChips { get; init; } = new();

    public bool HasAllDay => AllDayChips.Count > 0;
}

/// <summary>周视图左侧时间刻度。</summary>
public sealed class WeekHourViewModel
{
    public required string Label { get; init; }

    public required double Height { get; init; }

    public required int Hour { get; init; }

    public bool ShowLabel => Hour > 0;
}
