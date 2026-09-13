namespace Schedule.Core.Tests;

/// <summary>测试用临时目录（位于测试程序输出目录下，避免污染用户数据）。</summary>
internal static class TestPaths
{
    public static string NewTempDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "testdata", Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
