using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Schedule.App.Services;

/// <summary>图标加载（托盘与窗口共用 Assets/app.ico）。</summary>
public static class AppIcons
{
    private const string IconUri = "pack://application:,,,/Assets/app.ico";

    public static Icon LoadTrayIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri(IconUri))?.Stream;
            if (stream != null) return new Icon(stream);
        }
        catch (Exception)
        {
            // 回退到系统图标
        }

        return SystemIcons.Application;
    }

    /// <summary>窗口图标需要 32x32 左右的中等尺寸，避免 ICO 里的小图被拉伸。</summary>
    public static System.Windows.Media.ImageSource? LoadWindowIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri(IconUri))?.Stream;
            if (stream == null) return null;

            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
