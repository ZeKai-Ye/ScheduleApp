using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Schedule.App.Services;
using Schedule.App.ViewModels;
using Schedule.Core.Csv;
using Schedule.Core.Localization;
using Schedule.Core.Models;
using Schedule.Core.Services;
using Schedule.Core.Templating;

namespace Schedule.App.Views;

/// <summary>
/// 新建 / 编辑日程窗口。窗口本身不写仓储，结果通过 <see cref="Result"/> 返回调用方。
/// 支持多档提醒、事件颜色与周期规则（间隔 / 多星期 / 次数）。
/// </summary>
/// <summary>修改范围：系列事件中的某一次，还是整个系列。</summary>
public enum ItemEditScope
{
    SingleOccurrence = 0,
    WholeSeries = 1,
}

public partial class ItemEditorWindow : Window
{
    private static readonly int[] PresetOffsets = { 0, 5, 10, 15, 30, 60, 120, 1440, 2880 };

    private readonly AppHost _host;
    private readonly ScheduleItem _working;
    private readonly OccurrenceOverride? _existingOverride;
    private readonly DateTime? _seriesStart;
    private readonly List<OffsetChoice> _offsetChoices = new();
    private readonly List<OffsetVoiceRow> _offsetRows = new();
    private readonly List<ColorChoice> _colorChoices = new();
    private readonly List<WeekdayChoice> _weekdayChoices = new();
    private bool _loading = true;
    private string _selectedColorHex = string.Empty;

    /// <param name="host">应用组合根。</param>
    /// <param name="item">系列本体（新建时为空白日程）。</param>
    /// <param name="isNew">是否新建。</param>
    /// <param name="occurrence">从日历上点开的那一次发生；为 null 表示编辑整个系列。</param>
    public ItemEditorWindow(AppHost host, ScheduleItem item, bool isNew, ScheduleOccurrence? occurrence = null)
    {
        _host = host;

        var editingOccurrence = !isNew && occurrence != null && item.Repeat != RepeatKind.None;

        // 编辑某一次时，界面上要显示这一天的「当前有效值」（改期后的时间、专属文案等）
        _working = editingOccurrence ? occurrence!.Item.Clone() : item.Clone();
        _seriesStart = editingOccurrence ? occurrence!.SeriesStart : null;
        _existingOverride = editingOccurrence ? item.FindOverride(occurrence!.SeriesStart) : null;

        InitializeComponent();

        Title = isNew
            ? Loc.T("新建日程", "New event")
            : editingOccurrence
                ? Loc.F("编辑这一次（{0}）", "Edit this event ({0})", occurrence!.Start.ToString(Loc.IsEnglish ? "MMM d HH:mm" : "M月d日 HH:mm", Loc.Culture))
                : Loc.T("编辑日程", "Edit event");
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        ScopePanel.Visibility = editingOccurrence ? Visibility.Visible : Visibility.Collapsed;

        PlaceholderHintText.Text = Loc.T(
            "可用占位符：{title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status} {offset}",
            "Placeholders: {title} {time} {date} {weekday} {category} {notes} {priority} {countdown} {status} {offset}");

        if (editingOccurrence)
        {
            UpdateScopeUi();
            RestoreButton.Visibility = _existingOverride != null ? Visibility.Visible : Visibility.Collapsed;
            OccurrenceStateText.Text = _existingOverride == null
                ? Loc.T("这一次目前没有任何单独修改。", "No changes have been made to this event yet.")
                : Loc.T("这一次已被单独修改：", "This event was edited: ") + _existingOverride.Describe();
        }

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        LoadCategories();
        BuildOffsetChoices();
        BuildColorChoices();
        BuildWeekdayChoices();
        LoadValues();

        // 注意：必须先把 _loading 置回 false —— 各 Update* / Rebuild* 都有
        // 「加载中不刷新」的保护，否则打开编辑窗口时「按档位自定义播报」会是空的
        _loading = false;

        RebuildOffsetDetails();

        RepeatBox.SelectionChanged += (_, _) =>
        {
            UpdateRepeatPanels();
            UpdateRepeatSummary();
        };

        TitleBox.TextChanged += (_, _) => UpdateVoicePreview();
        VoiceTextBox.TextChanged += (_, _) => UpdateVoicePreview();
        CategoryBox.SelectionChanged += (_, _) => UpdateVoicePreview();
        NotesBox.TextChanged += (_, _) => UpdateVoicePreview();

        // 自定义档位输入完成后（失焦）再重建明细行，避免打字时列表被刷新
        CustomOffsetsBox.LostFocus += (_, _) => RebuildOffsetDetails();

        // 结束时间的实时提示（跨夜 / 时长写法）
        EndTimeBox.TextChanged += (_, _) => UpdateEndHint();
        EndDatePicker.SelectedDateChanged += (_, _) => UpdateEndHint();
        StartTimeBox.TextChanged += (_, _) => UpdateEndHint();
        StartDatePicker.SelectedDateChanged += (_, _) => UpdateEndHint();

        UpdateRepeatPanels();
        UpdateRepeatSummary();
        UpdateReminderSummary();
        UpdateVoicePreview();

        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>保存成功后的结果（新建或修改后的日程）。</summary>
    public ScheduleItem? Result { get; private set; }

    /// <summary>用户选择了删除。</summary>
    public bool DeleteRequested { get; private set; }

    /// <summary>修改范围（仅对系列事件的某一次发生有意义）。</summary>
    public ItemEditScope Scope { get; private set; } = ItemEditScope.WholeSeries;

    /// <summary>用户选择了「恢复本次默认」（撤销这一次的例外）。</summary>
    public bool RestoreRequested { get; private set; }

    /// <summary>这一次发生在系列中的原始开始时间（仅编辑某一次时有值）。</summary>
    public DateTime? SeriesStart => _seriesStart;

    /// <summary>当前「按档位自定义播报」的明细行数（供界面冒烟自检用）。</summary>
    public int OffsetDetailRowCount => _offsetRows.Count;

    private void Scope_Changed(object sender, RoutedEventArgs e) => UpdateScopeUi();

    /// <summary>范围变化时联动按钮文案与提示，并禁用「仅这一次」下无意义的重复规则。</summary>
    private void UpdateScopeUi()
    {
        if (ScopePanel == null) return;

        var single = ScopeSingleRadio.IsChecked == true;
        Scope = single ? ItemEditScope.SingleOccurrence : ItemEditScope.WholeSeries;

        if (DeleteButton != null)
        {
            DeleteButton.Content = single ? Loc.T("删除本次", "Delete this event") : Loc.T("删除整个系列", "Delete all events");
        }

        if (ScopeHintText != null)
        {
            ScopeHintText.Text = single
                ? Loc.T("只影响这一次：改时间即「改期」，改文案 / 档位 / 状态也只对这一条生效。",
                    "Only this event: changing the time reschedules it, and text / offsets / status apply to this event only.")
                : Loc.T("影响整个系列（含以后的所有发生），已记录的「仅本次」修改会保留。",
                    "Applies to every event in the series (including future ones); existing per-event edits are kept.");
        }

        // 重复规则属于系列级属性，「仅这一次」下不允许改
        var enabled = !single;
        if (RepeatBox != null) RepeatBox.IsEnabled = enabled;
        if (IntervalBox != null) IntervalBox.IsEnabled = enabled;
        if (CountBox != null) CountBox.IsEnabled = enabled;
        if (RepeatUntilCheck != null) RepeatUntilCheck.IsEnabled = enabled;
        if (RepeatUntilPicker != null) RepeatUntilPicker.IsEnabled = enabled && RepeatUntilCheck?.IsChecked == true;
        if (WeekdayPanel != null) WeekdayPanel.IsEnabled = enabled;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        RestoreRequested = true;
        DialogResult = true;
    }

    // ================= 初始化 =================

    private void LoadCategories()
    {
        var categories = _host.Repository.Snapshot()
            .Select(i => i.Category?.Trim() ?? string.Empty)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_working.Category)
            && !categories.Contains(_working.Category.Trim(), StringComparer.CurrentCultureIgnoreCase))
        {
            categories.Insert(0, _working.Category.Trim());
        }

        CategoryBox.ItemsSource = categories;
        CategoryBox.Text = _working.Category ?? string.Empty;
    }

    private void BuildOffsetChoices()
    {
        var selected = _working.NormalizedOffsets();
        var all = new SortedSet<int>(PresetOffsets);
        foreach (var offset in selected) all.Add(offset);
        foreach (var offset in _host.Settings.DefaultReminderOffsets) all.Add(offset);

        _offsetChoices.Clear();
        foreach (var minutes in all.OrderByDescending(m => m))
        {
            var choice = new OffsetChoice
            {
                Minutes = minutes,
                Label = OffsetLabel(minutes),
                IsSelected = selected.Contains(minutes),
            };

            choice.PropertyChanged += (_, _) =>
            {
                if (!_loading) UpdateReminderSummary();
            };

            _offsetChoices.Add(choice);
        }

        OffsetList.ItemsSource = _offsetChoices;
    }

    private void BuildColorChoices()
    {
        var itemColor = EventColors.Normalize(_working.Color);

        _colorChoices.Clear();
        _colorChoices.Add(new ColorChoice { Hex = string.Empty, Name = Loc.T("跟随分类", "Follow category"), IsSelected = itemColor.Length == 0 });
        foreach (var (name, hex) in EventColors.Presets)
        {
            _colorChoices.Add(new ColorChoice { Hex = hex, Name = name, IsSelected = itemColor == hex });
        }

        ColorList.ItemsSource = _colorChoices;

        _selectedColorHex = itemColor;
        if (itemColor.Length == 7 && EventColors.Presets.All(p => p.Hex != itemColor))
        {
            CustomColorBox.Text = itemColor.TrimStart('#');
        }
    }

    private void BuildWeekdayChoices()
    {
        _weekdayChoices.Clear();
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7);
            _weekdayChoices.Add(new WeekdayChoice
            {
                Day = day,
                Label = WeekdayLabel(day),
                IsSelected = _working.RepeatWeekdays.Contains(day),
            });
        }

        WeekdayList.ItemsSource = _weekdayChoices;
    }

    private void LoadValues()
    {
        TitleBox.Text = _working.Title;
        NotesBox.Text = _working.Notes;

        StartDatePicker.SelectedDate = _working.Start.Date;
        StartTimeBox.Text = _working.AllDay ? string.Empty : _working.Start.ToString("HH:mm");

        if (_working.End.HasValue)
        {
            EndDatePicker.SelectedDate = _working.End.Value.Date;
            EndTimeBox.Text = _working.AllDay ? string.Empty : _working.End.Value.ToString("HH:mm");
        }

        AllDayCheck.IsChecked = _working.AllDay;

        PriorityBox.SelectedIndex = _working.Priority switch
        {
            Priority.High => 0,
            Priority.Low => 2,
            _ => 1,
        };

        StatusBox.SelectedIndex = (int)_working.Status;
        RepeatBox.SelectedIndex = (int)_working.Repeat;
        IntervalBox.Text = Math.Max(1, _working.RepeatInterval).ToString();
        CountBox.Text = _working.RepeatCount?.ToString() ?? string.Empty;

        if (_working.RepeatUntil.HasValue)
        {
            RepeatUntilCheck.IsChecked = true;
            RepeatUntilPicker.SelectedDate = _working.RepeatUntil.Value.Date;
            RepeatUntilPicker.IsEnabled = true;
        }

        ReminderEnabledCheck.IsChecked = _working.ReminderEnabled;

        if (_working.RemindAtOverride.HasValue)
        {
            AbsoluteCheck.IsChecked = true;
            AbsoluteDatePicker.SelectedDate = _working.RemindAtOverride.Value.Date;
            AbsoluteTimeBox.Text = _working.RemindAtOverride.Value.ToString("HH:mm");
            AbsoluteDatePicker.IsEnabled = true;
            AbsoluteTimeBox.IsEnabled = true;
        }

        VoiceTextBox.Text = _working.VoiceText;
        SoundBox.Text = _working.SoundFile;
    }

    // ================= 交互 =================

    private void AllDayCheck_Click(object sender, RoutedEventArgs e)
    {
        var allDay = AllDayCheck.IsChecked == true;
        StartTimeBox.IsEnabled = !allDay;
        EndTimeBox.IsEnabled = !allDay;
        EndDatePicker.IsEnabled = !allDay;
        UpdateReminderSummary();
    }

    private void RepeatUntilCheck_Click(object sender, RoutedEventArgs e)
    {
        RepeatUntilPicker.IsEnabled = RepeatUntilCheck.IsChecked == true;
        UpdateRepeatSummary();
    }

    private void AbsoluteCheck_Click(object sender, RoutedEventArgs e)
    {
        var enabled = AbsoluteCheck.IsChecked == true;
        AbsoluteDatePicker.IsEnabled = enabled;
        AbsoluteTimeBox.IsEnabled = enabled;

        if (enabled && string.IsNullOrWhiteSpace(AbsoluteTimeBox.Text) && TryReadStart(out var start))
        {
            AbsoluteTimeBox.Text = start.AddMinutes(-30).ToString("HH:mm");
        }

        UpdateReminderSummary();
    }

    private void ReminderInput_Changed(object sender, RoutedEventArgs e)
    {
        UpdateReminderSummary();
        RebuildOffsetDetails();
    }

    /// <summary>为每个已选档位提供「专属播报文案 + 专属铃声」编辑行。</summary>
    private void RebuildOffsetDetails()
    {
        if (_loading || OffsetDetailList == null) return;

        var kept = _offsetRows.ToDictionary(r => r.Minutes, r => (r.Text, r.SoundFile));
        var offsets = CollectOffsets();

        _offsetRows.Clear();

        foreach (var minutes in offsets)
        {
            var row = new OffsetVoiceRow
            {
                Minutes = minutes,
                Label = ReminderPlan.DescribeOffset(minutes),
            };

            if (kept.TryGetValue(minutes, out var previous))
            {
                row.Text = previous.Text;
                row.SoundFile = previous.SoundFile;
            }
            else
            {
                _working.ReminderVoiceTexts.TryGetValue(minutes, out var text);
                _working.ReminderSoundFiles.TryGetValue(minutes, out var sound);
                row.Text = text ?? string.Empty;
                row.SoundFile = sound ?? string.Empty;
            }

            _offsetRows.Add(row);
        }

        OffsetDetailList.ItemsSource = null;
        OffsetDetailList.ItemsSource = _offsetRows;

        OffsetDetailHint.Text = _offsetRows.Count == 0
            ? Loc.T("先勾选上面的提醒档位，这里会为每个档位提供单独的播报文案与铃声。",
                "Select reminder offsets above; each offset then gets its own voice text and sound.")
            : Loc.T("优先级：本档位专属文案 > 本日程通用文案 > 全局档位文案 > 全局模板。",
                "Priority: this offset’s text > this event’s text > global offset text > global template.");
    }

    private void OffsetSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OffsetVoiceRow row) return;

        var dialog = new OpenFileDialog
        {
            Title = Loc.F("为「{0}」选择提示音", "Choose a sound for {0}", row.Label),
            Filter = Loc.T("音频文件", "Audio files") + " (*.wav;*.mp3;*.wma;*.m4a)|*.wav;*.mp3;*.wma;*.m4a|"
                + Loc.T("所有文件", "All files") + " (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true) row.SoundFile = dialog.FileName;
    }

    private void OffsetSoundClear_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OffsetVoiceRow row) row.SoundFile = string.Empty;
    }

    private void ReminderText_Changed(object sender, TextChangedEventArgs e) => UpdateReminderSummary();

    private void RepeatInput_Changed(object sender, RoutedEventArgs e) => UpdateRepeatSummary();

    private void RepeatText_Changed(object sender, TextChangedEventArgs e) => UpdateRepeatSummary();

    private void ColorSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        if ((sender as FrameworkElement)?.DataContext is ColorChoice choice)
        {
            _selectedColorHex = choice.Hex;
            if (CustomColorBox != null) CustomColorBox.Text = string.Empty;
        }
    }

    private void CustomColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;

        var normalized = EventColors.Normalize(CustomColorBox.Text);
        if (normalized.Length != 7) return;

        _selectedColorHex = normalized;

        // 自定义颜色覆盖预设选择
        foreach (var choice in _colorChoices) choice.IsSelected = choice.Hex == normalized;
    }

    // ================= 摘要 =================

    private void UpdateRepeatPanels()
        => WeekdayPanel.Visibility = RepeatBox.SelectedIndex == (int)RepeatKind.Weekly ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateRepeatSummary()
    {
        if (_loading) return;

        var repeat = (RepeatKind)Math.Max(0, RepeatBox.SelectedIndex);
        if (repeat == RepeatKind.None)
        {
            RepeatSummaryText.Text = Loc.T("只发生一次。", "Happens once.");
            return;
        }

        var interval = int.TryParse(IntervalBox.Text?.Trim(), out var parsed) && parsed > 0 ? parsed : 1;
        var weekdays = _weekdayChoices.Where(c => c.IsSelected).Select(c => c.Day).ToList();
        var count = int.TryParse(CountBox.Text?.Trim(), out var parsedCount) && parsedCount > 0 ? parsedCount : (int?)null;

        var text = repeat switch
        {
            RepeatKind.Daily => interval > 1 ? Loc.F("每 {0} 天", "Every {0} days", interval) : Loc.T("每天", "Every day"),
            RepeatKind.Weekdays => Loc.T("每个工作日", "Every weekday"),
            RepeatKind.Weekly => interval > 1 ? Loc.F("每 {0} 周", "Every {0} weeks", interval) : Loc.T("每周", "Every week"),
            RepeatKind.Monthly => interval > 1 ? Loc.F("每 {0} 个月", "Every {0} months", interval) : Loc.T("每月", "Every month"),
            RepeatKind.Yearly => interval > 1 ? Loc.F("每 {0} 年", "Every {0} years", interval) : Loc.T("每年", "Every year"),
            _ => Loc.T("不重复", "Does not repeat"),
        };

        if (repeat == RepeatKind.Weekly && weekdays.Count > 0)
        {
            text += Loc.T("（", " (") + string.Join(Loc.T("、", ", "), weekdays.Select(WeekdayLabel)) + Loc.T("）", ")");
        }

        if (count.HasValue) text += Loc.F(" · 共 {0} 次", " · {0} times", count.Value);
        else if (RepeatUntilCheck.IsChecked == true && RepeatUntilPicker.SelectedDate.HasValue)
        {
            text += Loc.F(" · 至 {0}", " · until {0}", RepeatUntilPicker.SelectedDate.Value.ToString("yyyy-MM-dd"));
        }
        else
        {
            text += Loc.T(" · 无限重复", " · repeats forever");
        }

        RepeatSummaryText.Text = Loc.T("效果：", "Result: ") + text;
    }

    private List<int> CollectOffsets()
    {
        var offsets = _offsetChoices.Where(c => c.IsSelected).Select(c => c.Minutes).ToList();

        foreach (var part in (CustomOffsetsBox.Text ?? string.Empty)
                     .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var minutes) && minutes >= 0 && minutes <= 60 * 24 * 365)
            {
                offsets.Add(minutes);
            }
        }

        return offsets.Distinct().OrderByDescending(o => o).ToList();
    }

    private void UpdateReminderSummary()
    {
        if (_loading || ReminderSummaryText == null) return;

        if (ReminderEnabledCheck.IsChecked != true)
        {
            ReminderSummaryText.Text = Loc.T("提醒已关闭，不会弹窗也不会播报。", "Reminders are off; nothing will pop up and nothing will be spoken.");
            return;
        }

        if (!TryReadStart(out var start))
        {
            ReminderSummaryText.Text = Loc.T("开始时间格式无法识别，暂时无法计算提醒时刻。", "Can’t read the start time, so reminder times can’t be calculated.");
            return;
        }

        var points = new List<string>();

        if (AbsoluteCheck.IsChecked == true
            && FlexibleDateTime.TryParse(AbsoluteTimeBox.Text, out var absolute, AbsoluteDatePicker.SelectedDate ?? start.Date))
        {
            points.Add(absolute.ToString("MM-dd HH:mm") + Loc.T("（指定时刻）", " (set time)"));
        }

        foreach (var offset in CollectOffsets())
        {
            points.Add(start.AddMinutes(-offset).ToString("MM-dd HH:mm") + Loc.T("（", " (") + ReminderPlan.DescribeOffset(offset) + ")");
        }

        ReminderSummaryText.Text = points.Count == 0
            ? Loc.T("还没有选择任何档位，保存时将使用默认档位。", "No offset selected yet; the default offsets are used when saving.")
            : Loc.T("提醒时间点：", "Reminder times: ") + string.Join(Loc.T("、", ", "), points);
    }

    private void UpdateVoicePreview()
    {
        if (_loading || VoicePreviewText == null) return;

        if (!TryReadStart(out var start))
        {
            VoicePreviewText.Text = string.Empty;
            return;
        }

        var template = string.IsNullOrWhiteSpace(VoiceTextBox.Text) ? _host.Settings.VoiceTemplate : VoiceTextBox.Text;
        var offset = CollectOffsets().FirstOrDefault();
        var hasOffset = CollectOffsets().Count > 0;

        var probe = new ScheduleItem
        {
            Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? Loc.T("（未命名日程）", "(Untitled event)") : TitleBox.Text,
            Category = CategoryBox.Text ?? string.Empty,
            Notes = NotesBox.Text ?? string.Empty,
            Priority = ReadPriority(),
            Start = start,
        };

        VoicePreviewText.Text = Loc.T("试听效果：", "Preview: ") + VoiceTemplate.Render(template, probe, start, DateTime.Now, hasOffset ? offset : null);
    }

    private static string OffsetLabel(int minutes)
    {
        if (minutes <= 0) return Loc.T("准点", "On time");

        return minutes switch
        {
            60 => Loc.T("1 小时", "1 hour"),
            120 => Loc.T("2 小时", "2 hours"),
            1440 => Loc.T("1 天", "1 day"),
            2880 => Loc.T("2 天", "2 days"),
            _ => minutes % 60 == 0
                ? Loc.F("{0} 小时", "{0} hours", minutes / 60)
                : Loc.F("{0} 分钟", "{0} min", minutes),
        };
    }

    // ================= 时间读取 =================

    private bool TryReadStart(out DateTime start)
    {
        start = DateTime.Now;
        var date = StartDatePicker.SelectedDate ?? DateTime.Today;

        if (AllDayCheck.IsChecked == true || string.IsNullOrWhiteSpace(StartTimeBox.Text))
        {
            start = date.Date;
            return true;
        }

        if (!FlexibleDateTime.TryParse(StartTimeBox.Text, out var parsed, date))
        {
            return false;
        }

        start = parsed;
        return true;
    }

    /// <summary>
    /// 读取结束时间。除了普通时间，还支持两类容易踩坑的输入：
    /// 1) 只填时间且早于开始时间 → 视为**次日**（跨夜的长日程，例如 22:00 → 04:00）；
    /// 2) 直接填时长（如 ~~6小时~~ / ~~90分钟~~ / ~~6h~~）→ 开始时间 + 时长。
    /// </summary>
    private bool TryReadEnd(DateTime start, out DateTime? end, out string? note)
    {
        end = null;
        note = null;

        if (AllDayCheck.IsChecked == true)
        {
            var endDate = EndDatePicker.SelectedDate;
            if (endDate.HasValue) end = endDate.Value.Date.AddDays(1);
            return true;
        }

        var hasDate = EndDatePicker.SelectedDate.HasValue;
        var hasTime = !string.IsNullOrWhiteSpace(EndTimeBox.Text);
        if (!hasDate && !hasTime) return true;

        // 时长写法：6小时 / 90分钟 / 1.5小时 / 6h
        if (hasTime && !hasDate && TryParseDuration(EndTimeBox.Text, out var duration))
        {
            end = start + duration;
            note = Loc.F("结束时间按「开始 + {0}」计算：{1}", "End time is calculated as start + {0}: {1}", HumanizeDuration(duration), end.Value.ToString("MM-dd HH:mm"));
            return true;
        }

        var date = EndDatePicker.SelectedDate ?? start.Date;
        if (hasTime)
        {
            if (!FlexibleDateTime.TryParse(EndTimeBox.Text, out var parsed, date)) return false;

            // 只填了时间、没选结束日期，且时间早于开始 → 跨夜到次日
            if (!hasDate && parsed <= start)
            {
                parsed = parsed.AddDays(1);
                note = Loc.F("结束时间早于开始时间，已按次日处理：{0}", "End time is earlier than the start, so it was treated as the next day: {0}", parsed.ToString("MM-dd HH:mm"));
            }

            end = parsed;
            return true;
        }

        end = date.Date;
        return true;
    }

    /// <summary>解析「6小时 / 90分钟 / 2天 / 6h」这类时长写法。</summary>
    private static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            text.Trim(),
            @"^(\d+(?:\.\d+)?)\s*(分钟|分|小时|时|天|min|mins|m|h|hr|hrs|d)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return false;

        var amount = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        duration = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "小时" or "时" or "h" or "hr" or "hrs" => TimeSpan.FromHours(amount),
            "天" or "d" => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromMinutes(amount),
        };

        return duration > TimeSpan.Zero;
    }

    private static string HumanizeDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 60) return Loc.F("{0} 分钟", "{0} min", (int)duration.TotalMinutes);

        var hours = (int)duration.TotalHours;
        var minutes = (int)(duration.TotalMinutes % 60);

        if (minutes == 0) return hours == 1 ? Loc.T("1 小时", "1 hour") : Loc.F("{0} 小时", "{0} hours", hours);

        return Loc.F("{0} 小时 {1} 分钟", "{0} h {1} min", hours, minutes);
    }

    /// <summary>紧凑星期名（重复设置里的胶囊）。</summary>
    private static string WeekdayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Loc.T("周一", "Mon"),
        DayOfWeek.Tuesday => Loc.T("周二", "Tue"),
        DayOfWeek.Wednesday => Loc.T("周三", "Wed"),
        DayOfWeek.Thursday => Loc.T("周四", "Thu"),
        DayOfWeek.Friday => Loc.T("周五", "Fri"),
        DayOfWeek.Saturday => Loc.T("周六", "Sat"),
        _ => Loc.T("周日", "Sun"),
    };

    private Priority ReadPriority() => PriorityBox.SelectedIndex switch
    {
        0 => Priority.High,
        2 => Priority.Low,
        _ => Priority.Normal,
    };

    // ================= 保存 =================

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadStart(out var start))
        {
            ShowError(Loc.T("开始时间格式无法识别（示例：09:30）。", "Can’t read the start time (example: 09:30)."));
            return;
        }

        if (!TryReadEnd(start, out var end, out var endNote))
        {
            ShowError(Loc.T("结束时间格式无法识别（示例：18:00，或直接写 6小时）。", "Can’t read the end time (example: 18:00, or type 6h)."));
            return;
        }

        EndHintText.Text = endNote ?? string.Empty;

        var customColorText = CustomColorBox.Text?.Trim() ?? string.Empty;
        if (customColorText.Length > 0 && EventColors.Normalize(customColorText).Length != 7)
        {
            ShowError(Loc.T("自定义颜色格式不正确，请填 6 位十六进制（例如 2563EB）。", "Custom color is invalid; enter 6 hex digits (e.g. 2563EB)."));
            return;
        }

        var item = _working.Clone();

        item.Title = (TitleBox.Text ?? string.Empty).Trim();
        item.Notes = NotesBox.Text ?? string.Empty;
        item.Category = (CategoryBox.Text ?? string.Empty).Trim();
        item.Start = start;
        item.End = end;
        item.AllDay = AllDayCheck.IsChecked == true;
        item.Priority = ReadPriority();
        item.Status = (ItemStatus)Math.Max(0, StatusBox.SelectedIndex);
        item.Color = EventColors.Normalize(customColorText).Length == 7
            ? EventColors.Normalize(customColorText)
            : _selectedColorHex;
        item.VoiceText = (VoiceTextBox.Text ?? string.Empty).Trim();
        item.SoundFile = (SoundBox.Text ?? string.Empty).Trim();

        // 周期规则
        var repeat = (RepeatKind)Math.Max(0, RepeatBox.SelectedIndex);
        item.Repeat = repeat;
        item.RepeatInterval = int.TryParse(IntervalBox.Text?.Trim(), out var interval) && interval > 0 ? interval : 1;
        item.RepeatWeekdays = repeat == RepeatKind.Weekly
            ? _weekdayChoices.Where(c => c.IsSelected).Select(c => c.Day).ToList()
            : new List<DayOfWeek>();
        item.RepeatCount = repeat != RepeatKind.None && int.TryParse(CountBox.Text?.Trim(), out var count) && count > 0
            ? count
            : null;
        item.RepeatUntil = repeat != RepeatKind.None && RepeatUntilCheck.IsChecked == true
            ? RepeatUntilPicker.SelectedDate
            : null;

        // 多档提醒
        var offsets = CollectOffsets();
        DateTime? absolute = null;

        if (AbsoluteCheck.IsChecked == true)
        {
            var date = AbsoluteDatePicker.SelectedDate ?? start.Date;
            if (!FlexibleDateTime.TryParse(AbsoluteTimeBox.Text, out var parsed, date))
            {
                ShowError(Loc.T("指定提醒时刻格式无法识别（示例：08:00）。", "Can’t read the set reminder time (example: 08:00)."));
                return;
            }

            absolute = parsed;
        }

        if (ReminderEnabledCheck.IsChecked == true && offsets.Count == 0 && absolute == null)
        {
            offsets = _host.Settings.DefaultReminderOffsets.Count > 0
                ? new List<int>(_host.Settings.DefaultReminderOffsets)
                : new List<int> { _host.Settings.DefaultRemindBeforeMinutes };
        }

        // 收集「按档位」的专属文案与铃声
        item.ReminderVoiceTexts = _offsetRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .GroupBy(r => r.Minutes)
            .ToDictionary(g => g.Key, g => g.First().Text.Trim());

        item.ReminderSoundFiles = _offsetRows
            .Where(r => !string.IsNullOrWhiteSpace(r.SoundFile))
            .GroupBy(r => r.Minutes)
            .ToDictionary(g => g.Key, g => g.First().SoundFile.Trim());

        item.ReminderEnabled = ReminderEnabledCheck.IsChecked == true && (offsets.Count > 0 || absolute != null);
        item.ReminderOffsets = offsets;
        item.RemindAtOverride = absolute;
        item.RemindBeforeMinutes = offsets.Count > 0 ? offsets.Min() : _host.Settings.DefaultRemindBeforeMinutes;

        if (item.AllDay)
        {
            item.Start = item.Start.Date;
            if (item.End.HasValue) item.End = item.End.Value.Date.AddDays(1);
        }

        // 自愈：旧版本把「仅这一次」写到了非重复日程上，这里清掉，否则保存永远被校验拦下
        ScheduleItemValidator.DropOccurrenceEditsWhenNotRepeating(item);

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0)
        {
            ShowError(string.Join(Loc.T("；", "; "), errors));
            return;
        }

        Result = item;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var single = Scope == ItemEditScope.SingleOccurrence && _seriesStart.HasValue;

        var message = single
            ? Loc.F("只删除这一次（{0}）？其它发生不受影响。", "Delete only this event ({0})? Other events are not affected.",
                _seriesStart!.Value.ToString(Loc.IsEnglish ? "MMM d HH:mm" : "M月d日 HH:mm", Loc.Culture))
            : Loc.T("删除这条日程", "Delete this event") + (_working.Repeat == RepeatKind.None
                ? Loc.T("吗？", "?")
                : Loc.T(" 的整个系列吗？", " and every event in the series?"));

        var result = MessageBox.Show(this, message, Loc.T("删除", "Delete"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        DeleteRequested = true;
        DialogResult = true;
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("选择提醒提示音", "Choose a reminder sound"),
            Filter = Loc.T("音频文件", "Audio files") + " (*.wav;*.mp3;*.wma;*.m4a)|*.wav;*.mp3;*.wma;*.m4a|"
                + Loc.T("所有文件", "All files") + " (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            SoundBox.Text = dialog.FileName;
        }
    }

    private void PreviewSound_Click(object sender, RoutedEventArgs e)
    {
        var file = (SoundBox.Text ?? string.Empty).Trim();
        _host.Audio.Play(string.IsNullOrWhiteSpace(file) ? _host.Settings.DefaultSoundFile : file);
    }

    private void PreviewVoice_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadStart(out var start))
        {
            ShowError(Loc.T("请先填写合法的开始时间。", "Enter a valid start time first."));
            return;
        }

        var template = string.IsNullOrWhiteSpace(VoiceTextBox.Text) ? _host.Settings.VoiceTemplate : VoiceTextBox.Text;
        var offsets = CollectOffsets();

        var probe = new ScheduleItem
        {
            Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? Loc.T("（未命名日程）", "(Untitled event)") : TitleBox.Text,
            Category = CategoryBox.Text ?? string.Empty,
            Notes = NotesBox.Text ?? string.Empty,
            Priority = ReadPriority(),
            Start = start,
        };

        var settings = _host.Settings;
        _host.Voice.Speak(new SpeechRequest
        {
            Text = VoiceTemplate.Render(template, probe, start, DateTime.Now, offsets.Count > 0 ? offsets[0] : null),
            VoiceId = settings.VoiceId,
            Rate = settings.VoiceRate,
            Volume = settings.VoiceVolume,
        });
    }

    /// <summary>实时说明结束时间是怎么算出来的（跨夜 / 时长）。</summary>
    private void UpdateEndHint()
    {
        if (_loading || EndHintText == null) return;

        if (!TryReadStart(out var start))
        {
            EndHintText.Text = string.Empty;
            return;
        }

        if (TryReadEnd(start, out var end, out var note))
        {
            EndHintText.Text = note ?? (end.HasValue
                ? Loc.F("结束：{0}（时长 {1}）", "End: {0} (duration {1})", end.Value.ToString("MM-dd HH:mm"), HumanizeDuration(end.Value - start))
                : string.Empty);
        }
        else
        {
            EndHintText.Text = Loc.T("结束时间格式无法识别。", "Can’t read the end time.");
        }
    }

    private void ShowError(string message) => ErrorText.Text = message;
}
