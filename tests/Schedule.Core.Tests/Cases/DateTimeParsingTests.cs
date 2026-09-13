using Schedule.Core.Csv;

namespace Schedule.Core.Tests.Cases;

public static class DateTimeParsingTests
{
    public static void Run()
    {
        TestRunner.Section("日期时间解析");

        Parse("2025-01-15 09:30", new DateTime(2025, 1, 15, 9, 30, 0), "ISO 短横线");
        Parse("2025/1/5 9:05", new DateTime(2025, 1, 5, 9, 5, 0), "斜杠 + 单位数");
        Parse("2025.01.15 18:00", new DateTime(2025, 1, 15, 18, 0, 0), "点号格式");
        Parse("2025年1月15日", new DateTime(2025, 1, 15, 0, 0, 0), "中文日期");
        Parse("2025年1月15日 下午3点", new DateTime(2025, 1, 15, 15, 0, 0), "中文日期 + 下午");
        Parse("2025年1月15日 9点30分", new DateTime(2025, 1, 15, 9, 30, 0), "中文日期 + 点分");
        Parse("20250115", new DateTime(2025, 1, 15, 0, 0, 0), "紧凑日期");
        Parse("20250115103000", new DateTime(2025, 1, 15, 10, 30, 0), "紧凑日期时间");
        Parse("2025-01-15T10:30:00", new DateTime(2025, 1, 15, 10, 30, 0), "ISO 8601");

        // Excel 序列号
        if (FlexibleDateTime.TryParse("45000", out var serial))
        {
            TestRunner.Equal(2023, serial.Year, "Excel 序列号年份");
            TestRunner.Equal(3, serial.Month, "Excel 序列号月份");
        }
        else
        {
            TestRunner.Check(false, "Excel 序列号解析");
        }

        // 只含时间，需要默认日期
        ParseWithDefault("9:30", new DateTime(2025, 3, 8), new DateTime(2025, 3, 8, 9, 30, 0), "时间 + 默认日期");
        ParseWithDefault("930", new DateTime(2025, 3, 8), new DateTime(2025, 3, 8, 9, 30, 0), "紧凑时间 930");
        ParseWithDefault("晚上8点", new DateTime(2025, 3, 8), new DateTime(2025, 3, 8, 20, 0, 0), "晚上 8 点");
        ParseWithDefault("08:15:30", new DateTime(2025, 3, 8), new DateTime(2025, 3, 8, 8, 15, 30), "带秒时间");

        // 相对日期
        if (FlexibleDateTime.TryParse("明天 9:00", out var tomorrow))
        {
            TestRunner.Equal(DateTime.Today.AddDays(1).Date, tomorrow.Date, "明天 9:00 日期");
            TestRunner.Equal(9, tomorrow.Hour, "明天 9:00 小时");
        }
        else
        {
            TestRunner.Check(false, "明天 9:00 解析");
        }

        if (FlexibleDateTime.TryParse("后天", out var afterTomorrow))
        {
            TestRunner.Equal(DateTime.Today.AddDays(2).Date, afterTomorrow.Date, "后天");
        }
        else
        {
            TestRunner.Check(false, "后天解析");
        }

        if (FlexibleDateTime.TryParse("3天后", out var offset))
        {
            TestRunner.Equal(DateTime.Today.AddDays(3).Date, offset.Date, "3天后");
        }
        else
        {
            TestRunner.Check(false, "3天后解析");
        }

        if (FlexibleDateTime.TryParse("2小时后", out var hours))
        {
            TestRunner.Check(hours > DateTime.Now.AddMinutes(110) && hours < DateTime.Now.AddMinutes(130), "2小时后");
        }
        else
        {
            TestRunner.Check(false, "2小时后解析");
        }

        if (FlexibleDateTime.TryParse("下周三 14:00", out var nextWednesday))
        {
            TestRunner.Equal(DayOfWeek.Wednesday, nextWednesday.DayOfWeek, "下周三星期");
            TestRunner.Equal(14, nextWednesday.Hour, "下周三小时");
            TestRunner.Check(nextWednesday.Date > DateTime.Today, "下周三在未来");
        }
        else
        {
            TestRunner.Check(false, "下周三解析");
        }

        // 非法输入
        TestRunner.Check(!FlexibleDateTime.TryParse("abc", out _), "非法文本被拒绝");
        TestRunner.Check(!FlexibleDateTime.TryParse("", out _), "空文本被拒绝");
        TestRunner.Check(!FlexibleDateTime.TryParse("   ", out _), "空白文本被拒绝");

        // 日/月歧义
        if (FlexibleDateTime.TryParse("03/04/2025", out var ambiguous, null, dayFirst: true))
        {
            TestRunner.Equal(4, ambiguous.Month, "日月歧义 dayFirst");
            TestRunner.Equal(3, ambiguous.Day, "日月歧义 dayFirst 日");
        }
        else
        {
            TestRunner.Check(false, "日月歧义解析");
        }
    }

    private static void Parse(string text, DateTime expected, string name)
    {
        if (FlexibleDateTime.TryParse(text, out var value))
        {
            TestRunner.Equal(expected, value, name);
        }
        else
        {
            TestRunner.Check(false, name + "（解析失败）");
        }
    }

    private static void ParseWithDefault(string text, DateTime defaultDate, DateTime expected, string name)
    {
        if (FlexibleDateTime.TryParse(text, out var value, defaultDate))
        {
            TestRunner.Equal(expected, value, name);
        }
        else
        {
            TestRunner.Check(false, name + "（解析失败）");
        }
    }
}
