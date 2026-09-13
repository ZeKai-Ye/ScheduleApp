using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Schedule.Core.Api;

/// <summary>
/// 极简 HTTP/1.1 服务器（仅监听 127.0.0.1）。
/// 刻意使用 TcpListener 而不是 HttpListener：后者需要 urlacl / 管理员权限，
/// 而 TcpListener 只占用一个回环监听套接字，普通用户权限即可运行。
/// 仅实现本地 AI / 脚本客户端所需的子集：请求行、请求头、Content-Length 正文、Connection: close。
/// </summary>
public sealed class MiniHttpServer : IDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private readonly TcpListener _listener;
    private readonly Func<ApiRequest, ApiResponse> _handler;
    private readonly Action<string>? _log;
    private volatile bool _running;
    private Thread? _acceptThread;

    public MiniHttpServer(int port, Func<ApiRequest, ApiResponse> handler, Action<string>? log = null)
    {
        _handler = handler;
        _log = log;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>实际监听端口（传入 0 时由系统分配，便于自测）。</summary>
    public int Port { get; private set; }

    public bool IsRunning => _running;

    public bool Start(out string? error)
    {
        error = null;

        try
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "ScheduleApp.Api",
            };
            _acceptThread.Start();

            _log?.Invoke("接口已启动：http://127.0.0.1:" + Port + "/api/schema");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Stop()
    {
        _running = false;

        try
        {
            _listener.Stop();
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;

            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                if (!_running) return;
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;

                using var stream = client.GetStream();

                var request = ReadRequest(stream);
                if (request == null)
                {
                    WriteResponse(stream, ApiResponse.Error(400, "请求格式无法解析"));
                    return;
                }

                if (request.RequiresLength)
                {
                    WriteResponse(stream, ApiResponse.Error(411, "不支持 Transfer-Encoding: chunked，请改用 Content-Length。"));
                    return;
                }

                var response = _handler(request) ?? ApiResponse.Error(500, "处理器返回空响应");
                WriteResponse(stream, response);
            }
            catch (Exception ex)
            {
                _log?.Invoke("处理请求失败：" + ex.Message);
            }
        }
    }

    private static ApiRequest? ReadRequest(NetworkStream stream)
    {
        var accumulated = new MemoryStream();
        var buffer = new byte[8192];
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) return null;

            accumulated.Write(buffer, 0, read);
            if (accumulated.Length > MaxHeaderBytes) return null;

            headerEnd = FindHeaderEnd(accumulated.GetBuffer(), (int)accumulated.Length);
        }

        var headerText = Encoding.UTF8.GetString(accumulated.GetBuffer(), 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        var method = requestLine[0].ToUpperInvariant();
        var target = requestLine[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        var path = target;
        string? rawQuery = null;
        var queryIndex = target.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = target[..queryIndex];
            rawQuery = target[(queryIndex + 1)..];
        }

        var chunked = headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var lengthText)) int.TryParse(lengthText, out contentLength);
        if (contentLength < 0 || contentLength > MaxBodyBytes) return null;

        var body = new byte[contentLength];
        var bodyStart = headerEnd + 4;
        var available = Math.Max(0, (int)accumulated.Length - bodyStart);
        var filled = Math.Min(available, contentLength);

        if (filled > 0) Buffer.BlockCopy(accumulated.GetBuffer(), bodyStart, body, 0, filled);

        while (filled < contentLength)
        {
            var read = stream.Read(body, filled, contentLength - filled);
            if (read <= 0) break;
            filled += read;
        }

        return new ApiRequest
        {
            Method = method,
            Path = SafeUnescape(path),
            Headers = headers,
            Query = ParseQuery(rawQuery),
            RawQuery = rawQuery,
            Body = filled > 0 ? Encoding.UTF8.GetString(body, 0, filled) : string.Empty,
            RequiresLength = chunked,
        };
    }

    private static int FindHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' && data[i + 2] == (byte)'\r' && data[i + 3] == (byte)'\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static Dictionary<string, string> ParseQuery(string? rawQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(rawQuery)) return result;

        foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[SafeUnescape(pair)] = string.Empty;
                continue;
            }

            var key = SafeUnescape(pair[..eq]);
            var value = SafeUnescape(pair[(eq + 1)..]).Replace('+', ' ');
            result[key] = value;
        }

        return result;
    }

    private static string SafeUnescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (Exception)
        {
            return text;
        }
    }

    private static void WriteResponse(NetworkStream stream, ApiResponse response)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body ?? string.Empty);

        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(ReasonPhrase(response.Status)).Append("\r\n");
        head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        head.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
        head.Append("Connection: close\r\n");
        head.Append("Cache-Control: no-store\r\n");

        foreach (var header in response.Headers)
        {
            head.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }

        head.Append("\r\n");

        var headBytes = Encoding.UTF8.GetBytes(head.ToString());
        stream.Write(headBytes, 0, headBytes.Length);
        if (bodyBytes.Length > 0) stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        411 => "Length Required",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        _ => "OK",
    };

    public void Dispose() => Stop();
}
