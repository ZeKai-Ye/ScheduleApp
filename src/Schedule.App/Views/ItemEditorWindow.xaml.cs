using System.Windows;
using Microsoft.Win32;
using Schedule.App.Services;
using Schedule.Core.Csv;
using Schedule.Core.Models;
using Schedule.Core.Services;
using Schedule.Core.Templating;

namespace Schedule.App.Views;

/// <summary>新建 / 编辑日程窗口。窗口本身不写仓储，结果通过 <see cref="Result"/> 返回调用方。</summary>
public partial class ItemEditorWindow : Window
{
    private readonly AppHost _host;
    private readonly ScheduleItem _working;

    public ItemEditorWindow(AppHost host, ScheduleItem item, bool isNew)
    {
        _host = host;
        _working = item.Clone();

        InitializeComponent();

        Title = isNew ? "新建日程" : "编辑日程";
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

        var icon = AppIcons.LoadWindowIcon();
        if (icon != null) Icon = icon;

        LoadCategories();
        LoadValues();

        TitleBox.TextChanged += (_, _) => UpdateVoicePreview();
        VoiceTextBox.TextChanged += (_, _) => UpdateVoicePreview();
        RemindBox.TextChanged += (_, _) => UpdateRemindPreview();
        StartDatePicker.SelectedDateChanged += (_, _) => UpdateRemindPreview();
        StartTimeBox.TextChanged += (_, _) => UpdateRemindPreview();

        Loaded += (_, _) =>
        {
            if (isNew)
            {
                TitleBox.Focus();
            }
            else
            {
                UpdateVoicePreview();
                UpdateRemindPreview();
            }
        };
    }

    /// <summary>保存成功后的结果（新建或修改后的日程）。</summary>
    public ScheduleItem? Result { get; private set; }

    /// <summary>用户选择了删除。</summary>
    public bool DeleteRequested { get; private set; }

    private void LoadCategories()
    {
        var categories = _host.Repository.Snapshot()
            .Select(i => i.Category?.Trim() ?? string.Empty)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_working.Category) && !categories.Contains(_working.Category.Trim(), StringComparer.CurrentCultureIgnoreCase))
        {
            categories.Insert(0, _working.Category.Trim());
        }

        CategoryBox.ItemsSource = categories;
        CategoryBox.Text = _working.Category ?? string.Empty;
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

        if (_working.RepeatUntil.HasValue)
        {
            RepeatUntilCheck.IsChecked = true;
            RepeatUntilPicker.SelectedDate = _working.RepeatUntil.Value.Date;
            RepeatUntilPicker.IsEnabled = true;
        }

        PriorityBox.SelectedIndex = _working.Priority switch
        {
            Priority.High => 0,
            Priority.Low => 2,
            _ => 1,
        };

        RepeatBox.SelectedIndex = (int)_working.Repeat;
        StatusBox.SelectedIndex = (int)_working.Status;

        RemindBox.Text = BuildRemindText(_working);

        VoiceTextBox.Text = _working.VoiceText;
        SoundBox.Text = _working.SoundFile;

        UpdateRemindPreview();
        UpdateVoicePreview();
    }

    private static string BuildRemindText(ScheduleItem item)
    {
        if (!item.ReminderEnabled) return string.Empty;
        if (item.RemindAtOverride.HasValue) return item.RemindAtOverride.Value.ToString("yyyy-MM-dd HH:mm");
        return item.RemindBeforeMinutes.ToString();
    }

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

    private bool TryReadEnd(DateTime start, out DateTime? end)
    {
        end = null;

        if (AllDayCheck.IsChecked == true)
        {
            var endDate = EndDatePicker.SelectedDate;
            if (endDate.HasValue) end = endDate.Value.Date.AddDays(1);
            return true;
        }

        var hasDate = EndDatePicker.SelectedDate.HasValue;
        var hasTime = !string.IsNullOrWhiteSpace(EndTimeBox.Text);
        if (!hasDate && !hasTime) return true;

        var date = EndDatePicker.SelectedDate ?? start.Date;
        if (hasTime)
        {
            if (!FlexibleDateTime.TryParse(EndTimeBox.Text, out var parsed, date)) return false;
            end = parsed;
            return true;
        }

        end = date.Date;
        return true;
    }

    private void AllDayCheck_Click(object sender, RoutedEventArgs e)
    {
        var allDay = AllDayCheck.IsChecked == true;
        StartTimeBox.IsEnabled = !allDay;
        EndTimeBox.IsEnabled = !allDay;
        EndDatePicker.IsEnabled = !allDay;
        UpdateRemindPreview();
    }

    private void RepeatUntilCheck_Click(object sender, RoutedEventArgs e)
        => RepeatUntilPicker.IsEnabled = RepeatUntilCheck.IsChecked == true;

    private void QuickRemind_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty;
        RemindBox.Text = tag == "none" ? string.Empty : tag;
    }

    private void UpdateRemindPreview()
    {
        if (!TryReadStart(out var start))
        {
            RemindPreviewText.Text = "提醒时间：开始时间格式无法识别";
            return;
        }

        var text = RemindBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            RemindPreviewText.Text = "提醒时间：将不会提醒";
            return;
        }

        if (!FieldParsers.TryParseRemind(text, start, out var spec))
        {
            RemindPreviewText.Text = "提醒时间：无法识别，请填分钟数或具体时间";
            return;
        }

        if (!spec.Enabled)
        {
            RemindPreviewText.Text = "提醒时间：将不会提醒";
            return;
        }

        var at = spec.Absolute ?? start.AddMinutes(-spec.BeforeMinutes);
        RemindPreviewText.Text = "提醒时间：" + at.ToString("yyyy-MM-dd HH:mm") + "（" + VoiceTemplate.DescribeCountdown(start, at) + "开始）";
    }

    private void UpdateVoicePreview()
    {
        if (VoicePreviewText == null) return;

        if (!TryReadStart(out var start))
        {
            VoicePreviewText.Text = string.Empty;
            return;
        }

        var template = string.IsNullOrWhiteSpace(VoiceTextBox.Text)
            ? _host.Settings.VoiceTemplate
            : VoiceTextBox.Text;

        var title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "（未命名日程）" : TitleBox.Text;
        var probe = new ScheduleItem
        {
            Title = title,
            Category = CategoryBox.Text ?? string.Empty,
            Notes = NotesBox.Text ?? string.Empty,
            Priority = ReadPriority(),
            Start = start,
        };

        VoicePreviewText.Text = "试听效果：" + VoiceTemplate.Render(template, probe, start, DateTime.Now);
    }

    private Priority ReadPriority() => PriorityBox.SelectedIndex switch
    {
        0 => Priority.High,
        2 => Priority.Low,
        _ => Priority.Normal,
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadStart(out var start))
        {
            ShowError("开始时间格式无法识别（示例：09:30）。");
            return;
        }

        if (!TryReadEnd(start, out var end))
        {
            ShowError("结束时间格式无法识别（示例：18:00）。");
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
        item.Repeat = (RepeatKind)Math.Max(0, RepeatBox.SelectedIndex);
        item.RepeatUntil = RepeatUntilCheck.IsChecked == true ? RepeatUntilPicker.SelectedDate : null;
        item.Status = (ItemStatus)Math.Max(0, StatusBox.SelectedIndex);
        item.VoiceText = (VoiceTextBox.Text ?? string.Empty).Trim();
        item.SoundFile = (SoundBox.Text ?? string.Empty).Trim();

        var remindText = RemindBox.Text;
        if (string.IsNullOrWhiteSpace(remindText))
        {
            item.ReminderEnabled = false;
            item.RemindAtOverride = null;
        }
        else if (FieldParsers.TryParseRemind(remindText, start, out var spec))
        {
            item.ReminderEnabled = spec.Enabled;
            item.RemindAtOverride = spec.Absolute;
            item.RemindBeforeMinutes = spec.BeforeMinutes;
        }
        else
        {
            ShowError("提醒设置无法识别，请填写分钟数（如 15）或具体时间（如 2025-06-01 08:00）。");
            return;
        }

        if (item.AllDay)
        {
            item.Start = item.Start.Date;
            if (item.End.HasValue)
            {
                item.End = item.End.Value.Date.AddDays(1);
            }
        }

        var errors = ScheduleItemValidator.Validate(item);
        if (errors.Count > 0)
        {
            ShowError(string.Join("；", errors));
            return;
        }

        Result = item;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "确定删除这条日程吗？", "删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        DeleteRequested = true;
        DialogResult = true;
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择提醒提示音",
            Filter = "音频文件 (*.wav;*.mp3;*.wma;*.m4a)|*.wav;*.mp3;*.wma;*.m4a|所有文件 (*.*)|*.*",
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
            ShowError("请先填写合法的开始时间。");
            return;
        }

        var template = string.IsNullOrWhiteSpace(VoiceTextBox.Text) ? _host.Settings.VoiceTemplate : VoiceTextBox.Text;
        var probe = new ScheduleItem
        {
            Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "（未命名日程）" : TitleBox.Text,
            Category = CategoryBox.Text ?? string.Empty,
            Notes = NotesBox.Text ?? string.Empty,
            Priority = ReadPriority(),
            Start = start,
        };

        var settings = _host.Settings;
        _host.Voice.Speak(new SpeechRequest
        {
            Text = VoiceTemplate.Render(template, probe, start, DateTime.Now),
            VoiceId = settings.VoiceId,
            Rate = settings.VoiceRate,
            Volume = settings.VoiceVolume,
        });
    }

    private void ShowError(string message) => ErrorText.Text = message;
}
