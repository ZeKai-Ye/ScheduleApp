using System.Windows;
using System.Windows.Media;
using Schedule.Core.Models;

namespace Schedule.App.Services;

/// <summary>
/// 把提醒弹窗放到用户配置的位置。
/// WinForms 的 <see cref="System.Windows.Forms.Screen"/> 给出的是**设备像素**，
/// 而 WPF 的 Left/Top 是设备无关单位（1/96 英寸），因此这里统一在设备像素下计算再换算回去，
/// 否则在 125% / 150% 缩放的屏幕上会明显偏移。
/// </summary>
public static class ReminderPositioner
{
    public static string DescribeTarget(AppSettings settings)
        => ReminderPositionMath.Describe(settings.ReminderPosition);

    public static void Place(Window window, AppSettings settings, int cascadeIndex)
    {
        try
        {
            var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            if (scale <= 0) scale = 1;

            var custom = settings.ReminderPosition == ReminderPositionMode.Custom;
            var screen = ResolveScreen(settings);

            // 自定义坐标按「整个虚拟桌面」约束：否则用户把弹窗拖到第二块屏幕后，
            // 会被拉回主屏工作区，看起来就是「记住坐标没生效」。
            var area = custom ? VirtualScreen() : screen.WorkingArea;

            var widthInDevicePixels = ReminderPositionMath.DipsToPixels(window.ActualWidth, scale);
            var heightInDevicePixels = ReminderPositionMath.DipsToPixels(window.ActualHeight, scale);

            // 设置里的 X / Y 是设备无关单位（拖动弹窗保存的正是窗口 Left / Top），
            // 而 Compute 统一按设备像素算，因此这里必须先换算，否则 125% / 150% 缩放下会偏。
            var (left, top) = ReminderPositionMath.Compute(
                settings.ReminderPosition,
                ReminderPositionMath.DipsToPixels(settings.ReminderPositionX, scale),
                ReminderPositionMath.DipsToPixels(settings.ReminderPositionY, scale),
                widthInDevicePixels,
                heightInDevicePixels,
                area.Left,
                area.Top,
                area.Width,
                area.Height,
                cascadeIndex);

            window.Left = ReminderPositionMath.PixelsToDips(left, scale);
            window.Top = ReminderPositionMath.PixelsToDips(top, scale);
        }
        catch (Exception ex)
        {
            // 定位失败不应影响提醒本身
            Core.Logging.AppLog.Warn("弹窗定位失败，回退默认位置：" + ex.Message);
            try
            {
                var area = SystemParameters.WorkArea;
                window.Left = area.Right - window.Width - 18;
                window.Top = area.Bottom - window.ActualHeight - 18;
            }
            catch (Exception)
            {
                window.Left = 100;
                window.Top = 100;
            }
        }
    }

    /// <summary>整个虚拟桌面（所有显示器的并集，设备像素）。</summary>
    private static System.Drawing.Rectangle VirtualScreen()
    {
        try
        {
            return System.Windows.Forms.SystemInformation.VirtualScreen;
        }
        catch (Exception)
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            return screen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
    }

    /// <summary>按设置选择目标显示器（默认跟随鼠标所在屏幕）。</summary>
    private static System.Windows.Forms.Screen ResolveScreen(AppSettings settings)
    {
        try
        {
            if (settings.ReminderFollowCursorScreen)
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(cursor);
                if (screen != null) return screen;
            }
        }
        catch (Exception)
        {
            // 忽略，回退主屏
        }

        return System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
    }
}
