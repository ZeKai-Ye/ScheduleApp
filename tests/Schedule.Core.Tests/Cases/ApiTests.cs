using System.Net.Http;
using System.Text;
using System.Text.Json;
using Schedule.Core.Api;
using Schedule.Core.Models;
using Schedule.Core.Services;
using Schedule.Core.Storage;

namespace Schedule.Core.Tests.Cases;

/// <summary>本地 JSON 接口的真实 HTTP 端到端测试（真的起服务器、真的发请求）。</summary>
public static class ApiTests
{
    private const string Token = "test-token";

    public static void Run()
    {
        TestRunner.Section("本地接口");

        var dir = TestPaths.NewTempDir();
        AppPaths.Initialize(dir);

        var repository = new ScheduleRepository(Path.Combine(dir, "schedules.json"));
        repository.Load();

        var settings = new SettingsService(Path.Combine(dir, "settings.json"));
        settings.Load();
        settings.Current.ApiToken = Token;

        var now = new DateTime(2025, 6, 2, 8, 0, 0); // 周一
        var api = new ScheduleApi(repository, settings, new NullApiActions(), () => now, "test");

        using var server = new MiniHttpServer(0, api.Handle);
        if (!server.Start(out var error))
        {
            TestRunner.Check(false, "接口启动失败：" + error);
            return;
        }

        api.Port = server.Port;

        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + server.Port),
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Add("X-Api-Token", Token);

        try
        {
            Schema(client);
            Auth(server);
            var id = Crud(client, repository);
            Occurrences(client);
            GlobalOffsetVoice(client, settings);
            RemindersNext(client);
            ImportExport(client, repository);
            SettingsPatch(client, settings);
            Reload(client);
            Errors(client, id);
        }
        finally
        {
            server.Stop();
        }
    }

    // ==================== 用例 ====================

    private static void Schema(HttpClient client)
    {
        var (status, body, _) = Send(client, HttpMethod.Get, "/api/schema");

        TestRunner.Equal(200, status, "schema 状态码");
        TestRunner.Check(body.Contains("reminderVoiceTexts", StringComparison.Ordinal), "schema 描述档位文案字段");
        TestRunner.Check(body.Contains("/api/items", StringComparison.Ordinal), "schema 列出接口");
        TestRunner.Check(body.Contains("voiceResolution", StringComparison.Ordinal), "schema 说明文案优先级");

        var (indexStatus, indexBody, _) = Send(client, HttpMethod.Get, "/");
        TestRunner.Equal(200, indexStatus, "首页状态码");
        TestRunner.Check(indexBody.Contains("/api/schema", StringComparison.Ordinal), "首页引导读取 schema");
    }

    private static void Auth(MiniHttpServer server)
    {
        using var anonymous = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + server.Port),
            Timeout = TimeSpan.FromSeconds(20),
        };

        var (status, body, _) = Send(anonymous, HttpMethod.Get, "/api/info");
        TestRunner.Equal(401, status, "缺少令牌返回 401");
        TestRunner.Check(body.Contains("令牌", StringComparison.Ordinal), "401 提示令牌位置");

        var (tokenStatus, _, _) = Send(anonymous, HttpMethod.Get, "/api/info?token=" + Token);
        TestRunner.Equal(200, tokenStatus, "查询串令牌可用");

        var (wrongStatus, _, _) = Send(anonymous, HttpMethod.Get, "/api/info?token=wrong");
        TestRunner.Equal(401, wrongStatus, "错误令牌返回 401");
    }

    private static Guid Crud(HttpClient client, ScheduleRepository repository)
    {
        const string createBody = """
        {
          "title": "项目周会",
          "start": "2025-06-02 10:00",
          "end": "2025-06-02 11:00",
          "category": "工作",
          "color": "红",
          "repeat": "Weekly",
          "repeatWeekdays": ["Monday", "Wednesday"],
          "reminderOffsets": [30, 15, 5, 0],
          "reminderVoiceTexts": { "30": "还有30分钟开会", "5": "马上开始", "0": "现在开始" },
          "voiceText": "周会提醒",
          "aliases": ["忽略我"]
        }
        """;

        var (status, body, ignored) = Send(client, HttpMethod.Post, "/api/items", createBody);
        TestRunner.Equal(201, status, "新建返回 201");
        TestRunner.Check(ignored.Contains("aliases", StringComparison.Ordinal), "未识别字段写入响应头");

        var id = ReadGuid(body, "item", "id");
        TestRunner.Check(id != Guid.Empty, "返回新建日程 id");

        using (var document = JsonDocument.Parse(body))
        {
            var item = document.RootElement.GetProperty("item");
            TestRunner.Equal("项目周会", item.GetProperty("title").GetString(), "标题");
            TestRunner.Equal("#DC2626", item.GetProperty("resolvedColor").GetString(), "中文色名解析");
            TestRunner.Equal(4, item.GetProperty("reminderOffsets").GetArrayLength(), "四个提醒档位");
            TestRunner.Equal("还有30分钟开会", item.GetProperty("reminderVoiceTexts").GetProperty("30").GetString(), "档位文案写入");
            TestRunner.Equal("Weekly", item.GetProperty("repeat").GetString(), "重复类型");
            TestRunner.Equal(2, item.GetProperty("repeatWeekdays").GetArrayLength(), "两个星期");
        }

        var stored = repository.Find(id);
        TestRunner.Check(stored != null, "日程已落库");
        TestRunner.Equal(4, stored!.NormalizedOffsets().Count, "落库档位数量");

        var (getStatus, getBody, _) = Send(client, HttpMethod.Get, "/api/items/" + id);
        TestRunner.Equal(200, getStatus, "读取单条状态码");
        TestRunner.Check(getBody.Contains("项目周会", StringComparison.Ordinal), "读取单条内容");

        // PATCH：只改标题，档位与文案必须保留
        var (patchStatus, patchBody, _) = Send(client, HttpMethod.Patch, "/api/items/" + id, """{ "title": "周会（已改名）" }""");
        TestRunner.Equal(200, patchStatus, "PATCH 状态码");
        using (var document = JsonDocument.Parse(patchBody))
        {
            var item = document.RootElement.GetProperty("item");
            TestRunner.Equal("周会（已改名）", item.GetProperty("title").GetString(), "PATCH 改了标题");
            TestRunner.Equal(4, item.GetProperty("reminderOffsets").GetArrayLength(), "PATCH 保留档位");
            TestRunner.Equal("工作", item.GetProperty("category").GetString(), "PATCH 保留分类");
        }

        // PUT：全量替换，未提供的字段复位
        var (putStatus, putBody, _) = Send(client, HttpMethod.Put, "/api/items/" + id,
            """{ "title": "全量替换", "start": "2025-06-03 08:00" }""");
        TestRunner.Equal(200, putStatus, "PUT 状态码");
        using (var document = JsonDocument.Parse(putBody))
        {
            var item = document.RootElement.GetProperty("item");
            TestRunner.Equal("全量替换", item.GetProperty("title").GetString(), "PUT 标题");
            TestRunner.Equal(string.Empty, item.GetProperty("category").GetString(), "PUT 复位分类");
            TestRunner.Equal(1, item.GetProperty("reminderOffsets").GetArrayLength(), "PUT 复位为默认单档位");
            TestRunner.Equal(0, item.GetProperty("reminderVoiceTexts").EnumerateObject().Count(), "PUT 清空档位文案");
        }

        // 状态切换
        var (doneStatus, doneBody, _) = Send(client, HttpMethod.Post, "/api/items/" + id + "/status", """{ "status": "Done" }""");
        TestRunner.Equal(200, doneStatus, "改状态状态码");
        TestRunner.Check(doneBody.Contains("Done", StringComparison.Ordinal), "状态改为 Done");

        var (backStatus, _, _) = Send(client, HttpMethod.Post, "/api/items/" + id + "/complete", null);
        TestRunner.Equal(200, backStatus, "/complete 简写可用");
        TestRunner.Equal(ItemStatus.Done, repository.Find(id)!.Status, "落库状态为 Done");

        Send(client, HttpMethod.Post, "/api/items/" + id + "/status", """{ "status": "Pending" }""");
        TestRunner.Equal(ItemStatus.Pending, repository.Find(id)!.Status, "状态改回 Pending");

        var (badStatus, _, _) = Send(client, HttpMethod.Post, "/api/items/" + id + "/status", """{ "status": "Whatever" }""");
        TestRunner.Equal(400, badStatus, "非法状态返回 400");

        return id;
    }

    private static void Occurrences(HttpClient client)
    {
        // 上面 PUT 之后档位被复位了，这里重新建一条带档位文案的周期日程
        const string body = """
        {
          "title": "阶段提醒验证",
          "start": "2025-06-02 10:00",
          "repeat": "Weekly",
          "repeatWeekdays": ["Monday", "Wednesday"],
          "reminderOffsets": [30, 5, 0],
          "reminderVoiceTexts": { "30": "还有30分钟", "5": "还有5分钟", "0": "现在开始" }
        }
        """;

        var (createStatus, _, _) = Send(client, HttpMethod.Post, "/api/items", body);
        TestRunner.Equal(201, createStatus, "周期日程创建");

        var (status, text, _) = Send(client, HttpMethod.Get,
            "/api/occurrences?from=2025-06-01&to=2025-06-15&q=阶段提醒验证");
        TestRunner.Equal(200, status, "occurrences 状态码");

        using var document = JsonDocument.Parse(text);
        var occurrences = document.RootElement.GetProperty("occurrences");
        TestRunner.Equal(4, occurrences.GetArrayLength(), "Mon/Wed 两周内 4 次发生");

        var first = occurrences[0];
        TestRunner.Equal("2025-06-02T10:00:00", first.GetProperty("start").GetString(), "首次发生时间");
        var reminders = first.GetProperty("reminders");
        TestRunner.Equal(3, reminders.GetArrayLength(), "三次提醒档位");
        TestRunner.Equal(30, reminders[0].GetProperty("offsetMinutes").GetInt32(), "首档 30 分钟");
        TestRunner.Equal("还有30分钟", reminders[0].GetProperty("voiceText").GetString(), "首档播报文本");
        TestRunner.Equal("本日程 · 该档位", reminders[0].GetProperty("voiceSource").GetString(), "首档文案来源");
        TestRunner.Equal("现在开始", reminders[2].GetProperty("voiceText").GetString(), "准点档播报文本");
    }

    private static void GlobalOffsetVoice(HttpClient client, SettingsService settings)
    {
        var (patchStatus, _, _) = Send(client, HttpMethod.Patch, "/api/settings",
            """{ "offsetVoiceTemplates": { "30": "全局30分钟提示", "0": "全局准点提示" } }""");
        TestRunner.Equal(200, patchStatus, "PATCH settings 状态码");
        TestRunner.Equal("全局30分钟提示", settings.Current.OffsetVoiceTemplates[30], "全局档位文案落库");

        // 新建一条「没有任何自定义文案」的日程，应当自动套用全局档位文案
        var (createStatus, _, _) = Send(client, HttpMethod.Post, "/api/items",
            """{ "title": "只用全局文案", "start": "2025-06-16 10:00", "reminderOffsets": [30, 0] }""");
        TestRunner.Equal(201, createStatus, "全局文案日程创建");

        var (_, text, _) = Send(client, HttpMethod.Get, "/api/occurrences?from=2025-06-16&to=2025-06-17&q=只用全局文案");
        using var document = JsonDocument.Parse(text);
        var reminders = document.RootElement.GetProperty("occurrences")[0].GetProperty("reminders");

        TestRunner.Equal("全局30分钟提示", reminders[0].GetProperty("voiceText").GetString(), "套用全局档位文案");
        TestRunner.Equal("全局 · 该档位文案", reminders[0].GetProperty("voiceSource").GetString(), "来源标记为全局档位");
    }

    private static void RemindersNext(HttpClient client)
    {
        var (status, text, _) = Send(client, HttpMethod.Get, "/api/reminders/next?limit=3");
        TestRunner.Equal(200, status, "reminders/next 状态码");

        using var document = JsonDocument.Parse(text);
        var reminders = document.RootElement.GetProperty("reminders");
        TestRunner.Check(reminders.GetArrayLength() >= 1, "至少有一条即将到来的提醒");
        TestRunner.Check(reminders[0].GetProperty("voiceText").GetString()!.Length > 0, "含有解析后的播报文本");
        TestRunner.Check(reminders[0].GetProperty("fireAt").GetString()!.Length > 0, "含有触发时刻");
    }

    private static void ImportExport(HttpClient client, ScheduleRepository repository)
    {
        var before = repository.Count;

        const string importBody = """
        {
          "fileName": "api-test.csv",
          "content": "标题,开始时间,分类,提前提醒,语音内容\n接口导入项,2025-06-10 09:00,工作,\"30,5\",\"30:接口30分钟|5:接口5分钟\"\n"
        }
        """;

        var (status, text, _) = Send(client, HttpMethod.Post, "/api/import", importBody);
        TestRunner.Equal(200, status, "import 状态码");

        using (var document = JsonDocument.Parse(text))
        {
            TestRunner.Equal(1, document.RootElement.GetProperty("imported").GetInt32(), "导入 1 条");
            TestRunner.Equal(0, document.RootElement.GetProperty("failed").GetInt32(), "无失败");
        }

        TestRunner.Equal(before + 1, repository.Count, "仓储数量增加");
        var imported = repository.Snapshot().First(i => i.Title == "接口导入项");
        TestRunner.Equal(2, imported.ReminderVoiceTexts.Count, "导入的档位文案");
        TestRunner.Equal("接口30分钟", imported.ReminderVoiceTexts[30], "导入的 30 分钟文案");

        var (exportStatus, csv, _) = Send(client, HttpMethod.Get, "/api/export?format=csv");
        TestRunner.Equal(200, exportStatus, "export 状态码");
        TestRunner.Check(csv.Contains("接口导入项", StringComparison.Ordinal), "导出包含导入项");
        TestRunner.Check(csv.Contains("30:接口30分钟", StringComparison.Ordinal), "导出包含档位文案编码");

        var (jsonStatus, json, _) = Send(client, HttpMethod.Get, "/api/export?format=json");
        TestRunner.Equal(200, jsonStatus, "export json 状态码");
        TestRunner.Check(json.Contains("reminderVoiceTexts", StringComparison.Ordinal), "json 导出包含档位文案字段");
    }

    private static void SettingsPatch(HttpClient client, SettingsService settings)
    {
        var (status, text, _) = Send(client, HttpMethod.Patch, "/api/settings",
            """{ "voiceRate": 3, "voiceVolume": 88, "notARealField": 1 }""");

        TestRunner.Equal(200, status, "PATCH settings 状态码");
        TestRunner.Equal(3, settings.Current.VoiceRate, "voiceRate 生效");
        TestRunner.Equal(88, settings.Current.VoiceVolume, "voiceVolume 生效");

        using (var document = JsonDocument.Parse(text))
        {
            var ignored = document.RootElement.GetProperty("ignoredFields");
            TestRunner.Equal(1, ignored.GetArrayLength(), "未知字段被列出");
            TestRunner.Equal("notARealField", ignored[0].GetString(), "未知字段名正确");
        }

        var (getStatus, getBody, _) = Send(client, HttpMethod.Get, "/api/settings");
        TestRunner.Equal(200, getStatus, "GET settings 状态码");
        TestRunner.Check(getBody.Contains("offsetVoiceTemplates", StringComparison.Ordinal), "设置包含全局档位文案");
        TestRunner.Check(!getBody.Contains(Token, StringComparison.Ordinal), "设置不回传访问令牌");
    }

    private static void Reload(HttpClient client)
    {
        var (status, body, _) = Send(client, HttpMethod.Post, "/api/reload", null);
        TestRunner.Equal(200, status, "reload 状态码");
        TestRunner.Check(body.Contains("reloaded", StringComparison.Ordinal), "reload 结果");
    }

    private static void Errors(HttpClient client, Guid id)
    {
        var (badIdStatus, _, _) = Send(client, HttpMethod.Get, "/api/items/not-a-guid");
        TestRunner.Equal(400, badIdStatus, "非法 id 返回 400");

        var (missingStatus, _, _) = Send(client, HttpMethod.Get, "/api/items/" + Guid.NewGuid());
        TestRunner.Equal(404, missingStatus, "不存在的 id 返回 404");

        var (unknownStatus, _, _) = Send(client, HttpMethod.Get, "/api/nope");
        TestRunner.Equal(404, unknownStatus, "未知接口返回 404");

        var (methodStatus, _, _) = Send(client, HttpMethod.Delete, "/api/schema");
        TestRunner.Equal(405, methodStatus, "不支持的方法返回 405");

        var (invalidStatus, _, _) = Send(client, HttpMethod.Post, "/api/items", """{ "title": "" }""");
        TestRunner.Equal(422, invalidStatus, "空标题返回 422");

        var (badJsonStatus, _, _) = Send(client, HttpMethod.Post, "/api/items", "{ 这不是 JSON");
        TestRunner.Equal(400, badJsonStatus, "非法 JSON 返回 400");

        // 分块传输：本服务器不支持，应回 411（实测客户端体验）
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/items"))
        {
            request.Content = new StringContent("""{ "title": "chunked" }""", Encoding.UTF8, "application/json");
            request.Headers.TransferEncodingChunked = true;

            using var response = client.Send(request);
            TestRunner.Equal(411, (int)response.StatusCode, "分块传输返回 411");
        }

        // 删除
        var (deleteStatus, _, _) = Send(client, HttpMethod.Delete, "/api/items/" + id);
        TestRunner.Equal(204, deleteStatus, "删除返回 204");

        var (afterDelete, _, _) = Send(client, HttpMethod.Get, "/api/items/" + id);
        TestRunner.Equal(404, afterDelete, "删除后返回 404");
    }

    // ==================== 辅助 ====================

    private static (int Status, string Body, string IgnoredFields) Send(
        HttpClient client,
        HttpMethod method,
        string path,
        string? json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = client.Send(request, HttpCompletionOption.ResponseContentRead);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var ignored = response.Headers.TryGetValues("X-Ignored-Fields", out var values)
            ? string.Join(",", values)
            : string.Empty;

        return ((int)response.StatusCode, body, ignored);
    }

    private static Guid ReadGuid(string body, params string[] path)
    {
        using var document = JsonDocument.Parse(body);
        var element = document.RootElement;
        foreach (var step in path) element = element.GetProperty(step);

        return element.GetGuid();
    }
}
