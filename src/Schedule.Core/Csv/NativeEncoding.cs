using System.Runtime.InteropServices;

namespace Schedule.Core.Csv;

/// <summary>
/// 通过 Win32 MultiByteToWideChar 解码传统代码页（GBK/GB18030/Big5 等）。
/// .NET Core 默认不含 GBK 代码页表，而中文 Excel 导出的 CSV 大量使用 GBK，
/// 这里用 P/Invoke 绕开 System.Text.Encoding.CodePages 依赖，保持零 NuGet 包。
/// </summary>
public static class NativeEncoding
{
    private const uint MbErrInvalidChars = 0x00000008;

    public const int CodePageGbk = 936;
    public const int CodePageGb18030 = 54936;
    public const int CodePageBig5 = 950;
    public const int CodePageUtf8 = 65001;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MultiByteToWideChar(
        uint codePage,
        uint dwFlags,
        [In] byte[] lpMultiByteStr,
        int cbMultiByte,
        [Out] char[]? lpWideCharStr,
        int cchWideChar);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WideCharToMultiByte(
        uint codePage,
        uint dwFlags,
        [In] char[] lpWideCharStr,
        int cchWideChar,
        [Out] byte[]? lpMultiByteStr,
        int cbMultiByte,
        IntPtr lpDefaultChar,
        IntPtr lpUsedDefaultChar);

    /// <summary>把字符串编码为指定代码页的字节（主要用于 GBK 导出与测试）。</summary>
    public static bool TryGetBytes(string text, int codePage, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrEmpty(text)) return true;

        try
        {
            var chars = text.ToCharArray();
            var needed = WideCharToMultiByte((uint)codePage, 0, chars, chars.Length, null, 0, IntPtr.Zero, IntPtr.Zero);
            if (needed <= 0) return false;

            var buffer = new byte[needed];
            var written = WideCharToMultiByte((uint)codePage, 0, chars, chars.Length, buffer, needed, IntPtr.Zero, IntPtr.Zero);
            if (written <= 0) return false;

            bytes = buffer;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>宽松解码：非法字节会被替换而不是失败。</summary>
    public static bool TryGetString(byte[] bytes, int codePage, out string result)
        => TryDecode(bytes, codePage, MbErrInvalidChars * 0u, out result);

    /// <summary>严格解码：遇到非法字节返回 false（仅 UTF-8 / GB18030 支持该标志）。</summary>
    public static bool TryGetStringStrict(byte[] bytes, int codePage, out string result)
        => TryDecode(bytes, codePage, MbErrInvalidChars, out result);

    private static bool TryDecode(byte[] bytes, int codePage, uint flags, out string result)
    {
        result = string.Empty;
        if (bytes.Length == 0) return true;

        try
        {
            var needed = MultiByteToWideChar((uint)codePage, flags, bytes, bytes.Length, null, 0);
            if (needed <= 0) return false;

            var buffer = new char[needed];
            var written = MultiByteToWideChar((uint)codePage, flags, bytes, bytes.Length, buffer, needed);
            if (written <= 0) return false;

            result = new string(buffer, 0, written);
            return true;
        }
        catch (Exception)
        {
            // 非 Windows 平台或 API 不可用时静默失败，由调用方回退
            return false;
        }
    }
}
