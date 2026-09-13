using System.Drawing;
using System.Windows.Forms;
using Schedule.Core.Models;
using Schedule.Core.Scheduling;

namespace Schedule.App.Services;

/// <summary>系统托盘图标与菜单。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private readonly ToolStripMenuItem _nextItem;
    private readonly ToolStripMenuItem _autostartItem;

    public TrayService(
        Action onOpen,
        Action onNew,
        Action onToday,
        Action onExit,
        Action? onToggleAutostart = null,
        Action? onCreateShortcut = null,
        Func<bool>? isAutostartEnabled = null)
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

        if (onCreateShortcut != null)
        {
            menu.Items.Add("创建桌面快捷方式(&S)", null, (_, _) => onCreateShortcut());
        }

        if (onToggleAutostart != null)
        {
            _autostartItem = new ToolStripMenuItem("开机自动启动(&A)") { CheckOnClick = false };
            _autostartItem.Click += (_, _) => onToggleAutostart();
            menu.Items.Add(_autostartItem);
        }
        else
        {
            _autostartItem = new ToolStripMenuItem("开机自动启动(&A)") { Enabled = false };
        }

        if (isAutostartEnabled != null)
        {
            _autostartItem.Checked = isAutostartEnabled();
        }

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
    public void UpdateNextReminder(DueReminder? reminder)
    {
        var text = "下一次提醒：无";
        var tooltip = "日程助手";

        if (reminder != null)
        {
            text = "下一次：" + reminder.Item.Title + " " + reminder.FireAt.ToString("MM-dd HH:mm")
                + "（" + reminder.Plan.OffsetText + "）";
            tooltip = "日程助手 - 下一次 " + reminder.FireAt.ToString("HH:mm") + " " + reminder.Item.Title;
        }

        try
        {
            _nextItem.Text = text.Length > 90 ? text[..87] + "..." : text;
            // NotifyIcon.Text 上限 63 字符
            _icon.Text = tooltip.Length > 62 ? tooltip[..59] + "..." : tooltip;
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    /// <summary>同步「开机自动启动」勾选状态。</summary>
    public void UpdateAutostart(bool enabled)
    {
        try
        {
            _autostartItem.Checked = enabled;
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
