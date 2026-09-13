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

            var screen = ResolveScreen(settings);

            var area = screen.WorkingArea;
            var widthInDevicePixels = window.ActualWidth * scale;
            var heightInDevicePixels = window.ActualHeight * scale;

            var (left, top) = ReminderPositionMath.Compute(
                settings.ReminderPosition,
                settings.ReminderPositionX,
                settings.ReminderPositionY,
                widthInDevicePixels,
                heightInDevicePixels,
                area.Left,
                area.Top,
                area.Width,
                area.Height,
                cascadeIndex);

            window.Left = left / scale;
            window.Top = top / scale;
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
