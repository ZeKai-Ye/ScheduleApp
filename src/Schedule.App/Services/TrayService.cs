using System.Drawing;
using System.Windows.Forms;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;
using Schedule.Core.Templating;

namespace Schedule.App.Services;

/// <summary>系统托盘图标与菜单。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private readonly ToolStripMenuItem _nextItem;

    public TrayService(Action onOpen, Action onNew, Action onToday, Action onExit)
    {
        _ownedIcon = AppIcons.LoadTrayIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面(&O)", null, (_, _) => onOpen());
        menu.Items.Add("新建日程(&N)", null, (_, _) => onNew());
        menu.Items.Add("查看今天(&T)", null, (_, _) => onToday());
        menu.Items.Add(new ToolStripSeparator());
        _nextItem = new ToolStripMenuItem("下一次提醒：无") { Enabled = false };
        menu.Items.Add(_nextItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出(&X)", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            Icon = _ownedIcon,
            Text = "日程助手",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => onOpen();
    }

    /// <summary>更新托盘悬停提示与菜单里的下一次提醒。</summary>
    public void UpdateNextReminder(ScheduleOccurrence? occurrence)
    {
        var text = "下一次提醒：无";
        var tooltip = "日程助手";

        if (occurrence != null)
        {
            text = "下一次：" + occurrence.Item.Title + " " + occurrence.ReminderAt.ToString("MM-dd HH:mm");
            tooltip = "日程助手 - 下一次 " + occurrence.ReminderAt.ToString("HH:mm") + " " + occurrence.Item.Title;
        }

        try
        {
            _nextItem.Text = text.Length > 90 ? text.Substring(0, 87) + "..." : text;
            // NotifyIcon.Text 上限 63 字符
            _icon.Text = tooltip.Length > 62 ? tooltip.Substring(0, 59) + "..." : tooltip;
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
            _ownedIcon.Dispose();
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
