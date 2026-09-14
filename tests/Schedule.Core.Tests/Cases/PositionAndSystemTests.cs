using System.Net.Http;
using System.Text;
using System.Text.Json;
using Schedule.Core.Api;
using Schedule.Core.Models;
using Schedule.Core.Services;
using Schedule.Core.Storage;

namespace Schedule.Core.Tests.Cases;

/// <summary>提醒弹窗定位计算，以及快捷方式 / 开机启动相关接口。</summary>
public static class PositionAndSystemTests
{
    private const double WorkWidth = 1920;
    private const double WorkHeight = 1040;
    private const double WindowWidth = 470;
    private const double WindowHeight = 300;

    public static void Run()
    {
        TestRunner.Section("弹窗定位");
        PositionMath();
        SystemApi();
    }

    // ==================== 定位计算 ====================

    private static void PositionMath()
    {
        // 右下角：留 18 像素边距
        var bottomRight = ReminderPositionMath.Compute(
            ReminderPositionMode.BottomRight, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(1432d, bottomRight.Left, "右下角 X");
        TestRunner.Equal(722d, bottomRight.Top, "右下角 Y");

        var bottomLeft = ReminderPositionMath.Compute(
            ReminderPositionMode.BottomLeft, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(18d, bottomLeft.Left, "左下角 X");
        TestRunner.Equal(722d, bottomLeft.Top, "左下角 Y");

        var topRight = ReminderPositionMath.Compute(
            ReminderPositionMode.TopRight, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(1432d, topRight.Left, "右上角 X");
        TestRunner.Equal(18d, topRight.Top, "右上角 Y");

        var topLeft = ReminderPositionMath.Compute(
            ReminderPositionMode.TopLeft, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(18d, topLeft.Left, "左上角 X");
        TestRunner.Equal(18d, topLeft.Top, "左上角 Y");

        var center = ReminderPositionMath.Compute(
            ReminderPositionMode.Center, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(725d, center.Left, "居中 X");
        TestRunner.Equal(370d, center.Top, "居中 Y");

        var custom = ReminderPositionMath.Compute(
            ReminderPositionMode.Custom, 100, 200, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(100d, custom.Left, "自定义 X");
        TestRunner.Equal(200d, custom.Top, "自定义 Y");

        // 偏移量叠加
        var offset = ReminderPositionMath.Compute(
            ReminderPositionMode.BottomRight, 10, -20, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(1442d, offset.Left, "带偏移 X");
        TestRunner.Equal(702d, offset.Top, "带偏移 Y");

        // 多个弹窗依次错开
        var cascade = ReminderPositionMath.Compute(
            ReminderPositionMode.BottomRight, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight, cascadeIndex: 2);
        TestRunner.Equal(722d - 68d, cascade.Top, "第 3 个弹窗向上错开");

        // 越界必须被夹回工作区
        var clampedFar = ReminderPositionMath.Compute(
            ReminderPositionMode.Custom, 5000, 5000, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(1450d, clampedFar.Left, "越界 X 被夹回");
        TestRunner.Equal(740d, clampedFar.Top, "越界 Y 被夹回");

        var clampedNegative = ReminderPositionMath.Compute(
            ReminderPositionMode.Custom, -100, -100, WindowWidth, WindowHeight, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(0d, clampedNegative.Left, "负坐标被夹回");
        TestRunner.Equal(0d, clampedNegative.Top, "负坐标被夹回（Y）");

        // 第二块屏幕在主屏左侧（工作区左边界为负）
        var leftMonitor = ReminderPositionMath.Compute(
            ReminderPositionMode.BottomRight, 0, 0, WindowWidth, WindowHeight, -1920, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(-488d, leftMonitor.Left, "左侧副屏 X");
        TestRunner.Equal(722d, leftMonitor.Top, "左侧副屏 Y");

        // 任务栏在下方导致工作区变矮
        var withTaskbar = ReminderPositionMath.Compute(
            ReminderPositionMode.BottomRight, 0, 0, WindowWidth, WindowHeight, 0, 0, WorkWidth, 1000);
        TestRunner.Equal(682d, withTaskbar.Top, "任务栏占位后的 Y");

        // 窗口比工作区还大：贴住左上角而不是算出负数
        var oversized = ReminderPositionMath.Compute(
            ReminderPositionMode.Center, 0, 0, 3000, 2000, 0, 0, WorkWidth, WorkHeight);
        TestRunner.Equal(0d, oversized.Left, "超大窗口贴左");
        TestRunner.Equal(0d, oversized.Top, "超大窗口贴上");

        // 自定义坐标落在第二块屏幕（两台 1920x1080 并排的虚拟桌面）时，
        // 不能按主屏工作区夹回去 —— 否则「拖动弹窗记住坐标」看起来没生效。
        var secondScreen = ReminderPositionMath.Compute(
            ReminderPositionMode.Custom, 2400, 300, WindowWidth, WindowHeight, 0, 0, 3840, 1080);
        TestRunner.Equal(2400d, secondScreen.Left, "自定义坐标可落在第二块屏幕");
        TestRunner.Equal(300d, secondScreen.Top, "第二块屏幕上的 Y");

        var offDesktop = ReminderPositionMath.Compute(
            ReminderPositionMode.Custom, 9000, 300, WindowWidth, WindowHeight, 0, 0, 3840, 1080);
        TestRunner.Equal(3840d - WindowWidth, offDesktop.Left, "超出整个虚拟桌面时仍被夹回");

        // 设备无关单位 ↔ 设备像素：设置里保存的 X / Y 与窗口 Left / Top 都是 DIP，
        // 定位计算在设备像素下进行，125% / 150% 缩放下必须先换算，否则会偏。
        TestRunner.Equal(375d, ReminderPositionMath.DipsToPixels(300, 1.25), "DIP → 设备像素");
        TestRunner.Equal(300d, ReminderPositionMath.PixelsToDips(375, 1.25), "设备像素 → DIP");
        TestRunner.Equal(300d, ReminderPositionMath.DipsToPixels(300, 0), "缩放非法时原样返回");
        TestRunner.Equal(300d, ReminderPositionMath.PixelsToDips(300, -1), "负缩放时原样返回");

        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var round = ReminderPositionMath.RoundTripForTest(1200, 640, scale);
            TestRunner.Equal(1200d, Math.Round(round.Left, 6), "记住弹窗位置往返一致（X，缩放 " + scale + "）");
            TestRunner.Equal(640d, Math.Round(round.Top, 6), "记住弹窗位置往返一致（Y，缩放 " + scale + "）");
        }

        TestRunner.Equal("左下角", ReminderPositionMath.Describe(ReminderPositionMode.BottomLeft), "位置描述");
        TestRunner.Equal("自定义位置", ReminderPositionMath.Describe(ReminderPositionMode.Custom), "自定义位置描述");
    }

    // ==================== 系统接口 ====================

    private sealed class FakeActions : IApiActions
    {
        public string? LastShortcutTarget { get; private set; }

        public bool? LastShortcutCreate { get; private set; }

        public string? LastShortcutDirectory { get; private set; }

        public bool? LastAutostartEnabled { get; private set; }

        public string? LastAutostartArguments { get; private set; }

        public bool FailShortcut { get; set; }

        public bool FailAutostart { get; set; }

        public bool IsAvailable => true;

        public IReadOnlyList<VoiceDescriptor> GetVoices() => Array.Empty<VoiceDescriptor>();

        public bool Speak(string text, string? voiceId, int? rate, int? volume, out string? error)
        {
            error = null;
            return true;
        }

        public bool TriggerReminder(Guid itemId, int offsetMinutes, out string? error)
        {
            error = null;
            return true;
        }

        public bool SetShortcut(string target, bool create, string? directoryOverride, out string? path, out string? error)
        {
            LastShortcutTarget = target;
            LastShortcutCreate = create;
            LastShortcutDirectory = directoryOverride;

            if (FailShortcut)
            {
                path = null;
                error = "沙箱拒绝写入桌面";
                return false;
            }

            path = "C:\\fake\\桌面\\日程助手.lnk";
            error = null;
            return true;
        }

        public bool SetAutostart(bool enabled, string? arguments, out string? mode, out string? error)
        {
            LastAutostartEnabled = enabled;
            LastAutostartArguments = arguments;

            if (FailAutostart)
            {
                mode = null;
                error = "注册表被安全软件拦截";
                return false;
            }

            mode = enabled ? "Registry" : "Disabled";
            error = null;
            return true;
        }

        public SystemStatusInfo GetSystemStatus() => new()
        {
            Supported = true,
            ExecutablePath = "C:\\fake\\ScheduleApp.exe",
            DataDirectory = "C:\\fake\\data",
            DesktopShortcutExists = LastShortcutCreate ?? false,
            AutostartEnabled = LastAutostartEnabled ?? false,
            AutostartMode = (LastAutostartEnabled ?? false) ? "Registry" : "Disabled",
        };
    }

    private static void SystemApi()
    {
        var dir = TestPaths.NewTempDir();
        AppPaths.Initialize(dir);

        var repository = new ScheduleRepository(Path.Combine(dir, "schedules.json"));
        repository.Load();
        var settings = new SettingsService(Path.Combine(dir, "settings.json"));
        settings.Load();
        settings.Current.ApiToken = "sys-token";

        var actions = new FakeActions();
        var api = new ScheduleApi(repository, settings, actions, () => new DateTime(2025, 6, 2, 8, 0, 0), "test");

        using var server = new MiniHttpServer(0, api.Handle);
        if (!server.Start(out var startError))
        {
            TestRunner.Check(false, "系统接口测试启动失败：" + startError);
            return;
        }

        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + server.Port),
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Add("X-Api-Token", "sys-token");

        try
        {
            var (infoStatus, infoBody, _) = Send(client, HttpMethod.Get, "/api/system/info");
            TestRunner.Equal(200, infoStatus, "system/info 状态码");
            using (var document = JsonDocument.Parse(infoBody))
            {
                var system = document.RootElement.GetProperty("system");
                TestRunner.Check(system.GetProperty("supported").GetBoolean(), "宿主支持系统能力");
                TestRunner.Equal("C:\\fake\\ScheduleApp.exe", system.GetProperty("executablePath").GetString(), "可执行文件路径");
                TestRunner.Equal("Disabled", system.GetProperty("autostartMode").GetString(), "初始未开机启动");
            }

            // 创建开始菜单快捷方式（带自定义目录）
            var (createStatus, createBody, _) = Send(client, HttpMethod.Post, "/api/system/shortcut",
                """{ "target": "startmenu", "create": true, "directory": "D:\\links" }""");
            TestRunner.Equal(200, createStatus, "创建快捷方式状态码");
            TestRunner.Equal("startmenu", actions.LastShortcutTarget, "目标为开始菜单");
            TestRunner.Equal(true, actions.LastShortcutCreate, "动作为创建");
            TestRunner.Equal("D:\\links", actions.LastShortcutDirectory, "自定义目录透传");
            TestRunner.Check(createBody.Contains(".lnk", StringComparison.Ordinal), "返回快捷方式路径");

            // 删除桌面快捷方式（target / create 可省略，默认桌面 + 创建，这里显式给 create=false）
            var (deleteStatus, _, _) = Send(client, HttpMethod.Post, "/api/system/shortcut", """{ "create": false }""");
            TestRunner.Equal(200, deleteStatus, "删除快捷方式状态码");
            TestRunner.Equal("desktop", actions.LastShortcutTarget, "缺省目标为桌面");
            TestRunner.Equal(false, actions.LastShortcutCreate, "动作为删除");

            // 开启开机启动
            var (autoStatus, autoBody, _) = Send(client, HttpMethod.Post, "/api/system/autostart",
                """{ "enabled": true, "arguments": "--minimized --serve" }""");
            TestRunner.Equal(200, autoStatus, "开机启动状态码");
            TestRunner.Equal(true, actions.LastAutostartEnabled, "动作为启用");
            TestRunner.Equal("--minimized --serve", actions.LastAutostartArguments, "启动参数透传");
            TestRunner.Check(settings.Current.StartWithWindows, "设置同步为已启用");
            TestRunner.Check(autoBody.Contains("Registry", StringComparison.Ordinal), "返回落地方式");

            // 通过 PATCH 设置也能真正生效
            var (patchStatus, patchBody, _) = Send(client, HttpMethod.Patch, "/api/settings",
                """{ "startWithWindows": false, "reminderPosition": "TopLeft", "reminderPositionX": 40 }""");
            TestRunner.Equal(200, patchStatus, "PATCH 设置状态码");
            TestRunner.Equal(false, actions.LastAutostartEnabled, "PATCH 停用开机启动");
            TestRunner.Equal(ReminderPositionMode.TopLeft, settings.Current.ReminderPosition, "位置策略生效");
            TestRunner.Equal(40, settings.Current.ReminderPositionX, "位置偏移生效");
            TestRunner.Check(patchBody.Contains("开机启动", StringComparison.Ordinal), "返回开机启动结果说明");

            // 宿主拒绝时给出 501 与原因
            actions.FailShortcut = true;
            var (failStatus, failBody, _) = Send(client, HttpMethod.Post, "/api/system/shortcut", """{ "target": "desktop" }""");
            TestRunner.Equal(501, failStatus, "快捷方式失败返回 501");
            TestRunner.Check(failBody.Contains("沙箱拒绝写入桌面", StringComparison.Ordinal), "失败原因回传");

            actions.FailShortcut = false;
            actions.FailAutostart = true;
            var (autoFailStatus, _, _) = Send(client, HttpMethod.Post, "/api/system/autostart", """{ "enabled": true }""");
            TestRunner.Equal(501, autoFailStatus, "开机启动失败返回 501");
            actions.FailAutostart = false;

            // 参数与路由校验
            var (badEnabled, _, _) = Send(client, HttpMethod.Post, "/api/system/autostart", """{ "arguments": "--minimized" }""");
            TestRunner.Equal(400, badEnabled, "缺少 enabled 返回 400");

            var (wrongMethod, _, _) = Send(client, HttpMethod.Get, "/api/system/shortcut");
            TestRunner.Equal(405, wrongMethod, "GET 快捷方式接口返回 405");

            var (unknownAction, _, _) = Send(client, HttpMethod.Get, "/api/system/nope");
            TestRunner.Equal(404, unknownAction, "未知系统接口返回 404");

            // 位置相关的返回字段
            var (settingsStatus, settingsBody, _) = Send(client, HttpMethod.Get, "/api/settings");
            TestRunner.Equal(200, settingsStatus, "读取设置状态码");
            TestRunner.Check(settingsBody.Contains("reminderPosition", StringComparison.Ordinal), "设置包含弹窗位置");
            TestRunner.Check(settingsBody.Contains("startMinimized", StringComparison.Ordinal), "设置包含开机最小化");
        }
        finally
        {
            server.Stop();
        }
    }

    private static (int Status, string Body, string IgnoredFields) Send(HttpClient client, HttpMethod method, string path, string? json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = client.Send(request, HttpCompletionOption.ResponseContentRead);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var ignored = response.Headers.TryGetValues("X-Ignored-Fields", out var values) ? string.Join(",", values) : string.Empty;

        return ((int)response.StatusCode, body, ignored);
    }
}
