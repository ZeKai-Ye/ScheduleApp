using System.Windows;
using System.Windows.Controls;
using Schedule.App.Services;
using Schedule.Core.Csv;
using Schedule.Core.Models;
using Schedule.Core.Templating;

namespace Schedule.App.Views;

/// <summary>
/// 改期对话框：只搬时间，不改内容。
/// 系列事件可选「仅这一次」（写成本次例外）或「整个系列」（整体平移）。
/// </summary>
public partial class RescheduleWindow : Window
{
    private readonly bool _isSeries;
    private readonly TimeSpan _originalDuration;

    public RescheduleWindow(AppHost host, ScheduleItem item, DateTime occurrenceStart, DateTime occurrenceEnd, bool isSeries)
    {
        _isSeries = isSeries;
        _originalDuration = occurrenceEnd > occurrenceStart ? occurrenceEnd - occurrenceStart : TimeSpan.FromHours(1);

        InitializeComponent();

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        HeaderText.Text = "改期：" + item.Title;
        CurrentText.Text = "当前：" + occurrenceStart.ToString("yyyy-MM-dd HH:mm")
            + (occurrenceEnd > occurrenceStart ? " - " + occurrenceEnd.ToString("HH:mm") : string.Empty)
            + "（" + MainViewModelTimeText(occurrenceStart, _originalDuration) + "）";

        DateBox.SelectedDate = occurrenceStart.Date;
        TimeBox.Text = occurrenceStart.ToString("HH:mm");
        DurationBox.SelectedIndex = 0;

        ScopePanel.Visibility = isSeries ? Visibility.Visible : Visibility.Collapsed;
        ScopeHint.Text = isSeries
            ? "「仅这一次」只把这一次搬走，其它发生不变；「整个系列」按同样的时间差整体平移（已记录的本次修改会一起跟随）。"
            : string.Empty;

        Loaded += (_, _) =>
        {
            TimeBox.Focus();
            TimeBox.SelectAll();
        };
    }

    private static string MainViewModelTimeText(DateTime start, TimeSpan duration)
        => "原时长 " + HumanizeDuration(duration);

    private static string HumanizeDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 60) return (int)duration.TotalMinutes + " 分钟";
        if (duration.TotalMinutes % 60 == 0) return (int)duration.TotalHours + " 小时";
        return (int)duration.TotalHours + " 小时 " + (int)(duration.TotalMinutes % 60) + " 分钟";
    }

    /// <summary>改期后的开始时间。</summary>
    public DateTime NewStart { get; private set; }

    /// <summary>改期后的结束时间（未指定时长时为 null，表示沿用原时长）。</summary>
    public DateTime? NewEnd { get; private set; }

    /// <summary>是否应用到整个系列。</summary>
    public bool WholeSeries => ScopeSeriesRadio.IsChecked == true;

    private DateTime CurrentStart()
    {
        var date = DateBox.SelectedDate ?? DateTime.Today;
        return FlexibleDateTime.TryParse(TimeBox.Text, out var parsed, date) ? parsed : date;
    }

    private void Today_Click(object sender, RoutedEventArgs e) => DateBox.SelectedDate = DateTime.Today;

    private void Tomorrow_Click(object sender, RoutedEventArgs e) => DateBox.SelectedDate = DateTime.Today.AddDays(1);

    private void ShiftDays_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || !int.TryParse(tag, out var days)) return;
        DateBox.SelectedDate = CurrentStart().AddDays(days).Date;
    }

    private void ShiftMinutes_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || !int.TryParse(tag, out var minutes)) return;

        var moved = CurrentStart().AddMinutes(minutes);
        DateBox.SelectedDate = moved.Date;
        TimeBox.Text = moved.ToString("HH:mm");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!FlexibleDateTime.TryParse(TimeBox.Text, out var start, DateBox.SelectedDate ?? DateTime.Today))
        {
            ErrorText.Text = "开始时间格式无法识别（示例：15:30）。";
            return;
        }

        var duration = (DurationBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "15" => TimeSpan.FromMinutes(15),
            "30" => TimeSpan.FromMinutes(30),
            "45" => TimeSpan.FromMinutes(45),
            "60" => TimeSpan.FromHours(1),
            "90" => TimeSpan.FromMinutes(90),
            "120" => TimeSpan.FromHours(2),
            _ => (TimeSpan?)null,
        };

        NewStart = start;
        NewEnd = duration.HasValue ? start + duration.Value : null;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
