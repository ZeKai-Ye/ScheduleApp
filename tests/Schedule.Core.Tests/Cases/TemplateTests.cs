using Schedule.Core.Models;
using Schedule.Core.Templating;

namespace Schedule.Core.Tests.Cases;

public static class TemplateTests
{
    public static void Run()
    {
        TestRunner.Section("语音模板");

        var now = new DateTime(2025, 6, 1, 13, 50, 0);
        var item = new ScheduleItem
        {
            Title = "项目周会",
            Category = "工作",
            Notes = "会议室 A",
            Priority = Priority.High,
            Start = now.AddMinutes(10),
        };

        var text = VoiceTemplate.Render("{title}|{time}|{category}|{date}", item, item.Start, now);
        TestRunner.Equal("项目周会|14:00|工作|2025-06-01", text, "占位符替换");

        var countdown = VoiceTemplate.Render("{title}{countdown}", item, item.Start, now);
        TestRunner.Equal("项目周会还有 10 分钟", countdown, "倒计时占位符");

        var emptyCategory = new ScheduleItem { Title = "喝水", Start = item.Start };
        TestRunner.Equal("喝水", VoiceTemplate.Render("{title}。{category}", emptyCategory, emptyCategory.Start, now), "空分类清理标点");

        var fallback = VoiceTemplate.Render(null, item, item.Start, now);
        TestRunner.Check(fallback.Contains("项目周会", StringComparison.Ordinal), "默认模板包含标题");
        TestRunner.Check(fallback.Contains("14:00", StringComparison.Ordinal), "默认模板包含时间");

        TestRunner.Check(VoiceTemplate.Render("   ", emptyCategory, emptyCategory.Start, now).Length > 0, "空模板回退不产生空字符串");

        var preview = VoiceTemplate.Preview("{title} - {category} - {priority}", now);
        TestRunner.Check(preview.Contains("项目周会", StringComparison.Ordinal), "预览包含标题");
        TestRunner.Check(preview.Contains("高优先级", StringComparison.Ordinal), "预览包含优先级");

        TestRunner.Equal("现在开始", VoiceTemplate.DescribeCountdown(now, now), "恰好开始");
        TestRunner.Equal("还有 2 小时", VoiceTemplate.DescribeCountdown(now.AddHours(2), now), "小时倒计时");
        TestRunner.Equal("还有 3 天", VoiceTemplate.DescribeCountdown(now.AddDays(3), now), "天倒计时");
        TestRunner.Check(VoiceTemplate.DescribeCountdown(now.AddMinutes(-5), now).StartsWith("已经开始", StringComparison.Ordinal), "已开始文案");

        TestRunner.Equal("星期日", VoiceTemplate.WeekdayText(DayOfWeek.Sunday), "星期文案");
        TestRunner.Equal("星期一", VoiceTemplate.WeekdayText(DayOfWeek.Monday), "星期一文案");
    }
}
