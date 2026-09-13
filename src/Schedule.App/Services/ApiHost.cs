using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Schedule.Core.Api;
using Schedule.Core.Logging;
using Schedule.Core.Storage;

namespace Schedule.App.Services;

/// <summary>
/// 本地接口宿主：监听 127.0.0.1，供 AI / 脚本直接读写日程与语音设置。
/// 所有请求都会被切到 UI 线程执行——仓储、MediaPlayer 与 SAPI 都要求同线程访问。
/// </summary>
public sealed class ApiHost : IDisposable
{
    private readonly AppHost _host;
    private MiniHttpServer? _server;
    private ScheduleApi? _api;

    public ApiHost(AppHost host) => _host = host;

    public bool IsRunning => _server?.IsRunning == true;

    /// <summary>实际监听端口。</summary>
    public int Port { get; private set; }

    public string? LastError { get; private set; }

    public string Url => "http://127.0.0.1:" + Port;

    public string DiscoveryFile => Path.Combine(AppPaths.DataDirectory, "api.json");

    public void Start(int? portOverride = null, bool enabled = true)
    {
        if (!enabled)
        {
            AppLog.Info("本地接口已按启动参数禁用");
            return;
        }

        var settings = _host.SettingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.ApiToken))
        {
            settings.ApiToken = GenerateToken();
            _host.SettingsService.MarkDirty();
        }

        var port = Math.Clamp(portOverride ?? settings.ApiPort, 1024, 65535);

        _api = new ScheduleApi(
            _host.Repository,
            _host.SettingsService,
            new ApiActions(_host),
            () => DateTime.Now,
            typeof(ApiHost).Assembly.GetName().Version?.ToString(3) ?? "0.3.0",
            _host.CsvImporter)
        {
            Port = port,
        };

        _server = new MiniHttpServer(port, Dispatch, message => AppLog.Info(message));

        if (!_server.Start(out var error))
        {
            LastError = error;
            _server = null;
            AppLog.Error("本地接口启动失败（端口 " + port + "）：" + error);
            return;
        }

        Port = _server.Port;
        _api.Port = Port;

        WriteDiscoveryFile();
        AppLog.Info("本地接口已就绪：" + Url + "/api/schema（令牌见 " + DiscoveryFile + "）");
    }

    /// <summary>把请求切到 UI 线程执行。</summary>
    private ApiResponse Dispatch(ApiRequest request)
    {
        var api = _api;
        if (api == null) return ApiResponse.Error(503, "接口尚未就绪。");

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            try
            {
                return dispatcher.Invoke(() => api.Handle(request));
            }
            catch (Exception ex)
            {
                AppLog.Warn("接口请求调度失败：" + ex.Message);
                return ApiResponse.Error(500, "调度失败：" + ex.Message);
            }
        }

        return api.Handle(request);
    }

    /// <summary>写出发现文件，AI / 脚本读它即可拿到地址与令牌。</summary>
    private void WriteDiscoveryFile()
    {
        try
        {
            var payload = new
            {
                name = "日程助手 ScheduleApp 本地接口",
                url = Url,
                schema = Url + "/api/schema",
                port = Port,
                token = _host.SettingsService.Current.ApiToken,
                authHeader = "X-Api-Token",
                version = typeof(ApiHost).Assembly.GetName().Version?.ToString(3) ?? "0.3.0",
                dataDirectory = AppPaths.DataDirectory,
                docs = "docs/API.md",
                startedAt = DateTime.Now,
            };

            AtomicFile.WriteAllText(DiscoveryFile, JsonSerializer.Serialize(payload, ApiJson.Options), createBackup: false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("写入接口发现文件失败：" + ex.Message);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Stop()
    {
        try
        {
            _server?.Stop();
        }
        catch (Exception)
        {
            // 忽略
        }

        _server = null;
    }

    public void Dispose() => Stop();
}
