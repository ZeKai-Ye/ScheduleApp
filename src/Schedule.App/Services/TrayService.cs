using System.Drawing;
using System.Windows.Forms;
using Schedule.Core.Localization;
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
        menu.Items.Add(Loc.T("打开主界面(&O)", "Open (&O)"), null, (_, _) => onOpen());
        menu.Items.Add(Loc.T("新建日程(&N)", "New event (&N)"), null, (_, _) => onNew());
        menu.Items.Add(Loc.T("查看今天(&T)", "Today (&T)"), null, (_, _) => onToday());
        menu.Items.Add(new ToolStripSeparator());

        _nextItem = new ToolStripMenuItem(Loc.T("下一次提醒：无", "Next reminder: none")) { Enabled = false };
        menu.Items.Add(_nextItem);
        menu.Items.Add(new ToolStripSeparator());

        if (onCreateShortcut != null)
        {
            menu.Items.Add(Loc.T("创建桌面快捷方式(&S)", "Create desktop shortcut (&S)"), null, (_, _) => onCreateShortcut());
        }

        if (onToggleAutostart != null)
        {
            _autostartItem = new ToolStripMenuItem(Loc.T("开机自动启动(&A)", "Start with Windows (&A)")) { CheckOnClick = false };
            _autostartItem.Click += (_, _) => onToggleAutostart();
            menu.Items.Add(_autostartItem);
        }
        else
        {
            _autostartItem = new ToolStripMenuItem(Loc.T("开机自动启动(&A)", "Start with Windows (&A)")) { Enabled = false };
        }

        if (isAutostartEnabled != null)
        {
            _autostartItem.Checked = isAutostartEnabled();
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("退出(&X)", "Exit (&X)"), null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            Icon = _ownedIcon,
            Text = Loc.T("日程助手", "Schedule Assistant"),
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => onOpen();
    }

    /// <summary>更新托盘悬停提示与菜单里的下一次提醒。</summary>
    public void UpdateNextReminder(DueReminder? reminder)
    {
        var text = Loc.T("下一次提醒：无", "Next reminder: none");
        var tooltip = Loc.T("日程助手", "Schedule Assistant");

        if (reminder != null)
        {
            text = Loc.F("下一次：{0} {1}（{2}）", "Next: {0} {1} ({2})",
                reminder.Item.Title, reminder.FireAt.ToString("MM-dd HH:mm"), reminder.Plan.OffsetText);
            tooltip = Loc.F("日程助手 - 下一次 {0} {1}", "Schedule Assistant - next {0} {1}",
                reminder.FireAt.ToString("HH:mm"), reminder.Item.Title);
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
