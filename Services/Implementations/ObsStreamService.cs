using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChyguiSlide.Data;
using ChyguiSlide.Services.Abstractions;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// LAN-выход OBS через TcpListener (0.0.0.0) — работает в LAN без netsh urlacl,
/// в отличие от HttpListener, который на Windows часто доступен только на 127.0.0.1.
/// </summary>
public sealed class ObsStreamService : IObsStreamService, IDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly Dictionary<string, string> _stateByType = new(StringComparer.Ordinal);

    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _enabled;
    private int _port = 8765;
    private IReadOnlyList<string> _listenerAddresses = Array.Empty<string>();
    private string? _lastError;
    private bool _backdropEnabled;
    private double _backdropOpacity = 0.9;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _tcpListener is not null;
            }
        }
    }

    public int Port
    {
        get
        {
            lock (_gate)
            {
                return _port;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public async Task ApplySettingsAsync(bool enabled, int port, CancellationToken cancellationToken = default)
    {
        port = Math.Clamp(port, 1024, 65535);

        lock (_gate)
        {
            if (_enabled == enabled && _port == port && IsRunning)
            {
                return;
            }

            _enabled = enabled;
            _port = port;
        }

        await StopInternalAsync().ConfigureAwait(false);

        if (!enabled)
        {
            SetLastError(null);
            InteractionLogger.Log("[ObsStream] Stopped (disabled in settings)");
            return;
        }

        try
        {
            StartInternal(port);
            SetLastError(null);
            var addrs = string.Join(", ", GetListenerAddresses());
            InteractionLogger.Log($"[ObsStream] Started on 0.0.0.0:{port} (TcpListener). Addresses: {addrs}");
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            InteractionLogger.Log($"[ObsStream] Start failed: {ex.Message}");
            throw;
        }
    }

    public void ApplyBackdropSettings(bool enabled, double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        lock (_gate)
        {
            _backdropEnabled = enabled;
            _backdropOpacity = opacity;
        }

        var json = JsonSerializer.Serialize(new
        {
            type = "updateBackdrop",
            enabled,
            opacity
        });
        InteractionLogger.Log($"[ObsStream] Backdrop: enabled={enabled}, opacity={opacity:0.##}");
        BroadcastJson(json);
    }

    public (bool Enabled, double Opacity) GetBackdropSettings()
    {
        lock (_gate)
        {
            return (_backdropEnabled, _backdropOpacity);
        }
    }

    public void BroadcastJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var type = TryGetMessageType(json);
        if (type is "updateBackground")
        {
            return;
        }

        if (type is not null)
        {
            lock (_gate)
            {
                _stateByType[type] = json;
            }
        }

        if (type == "updateSlide")
        {
            InteractionLogger.Log($"[ObsStream] Broadcast updateSlide ({_clients.Count} client(s))");
        }

        if (_clients.IsEmpty)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var (id, socket) in _clients.ToArray())
        {
            if (socket.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }

            _ = SendSafeAsync(socket, bytes);
        }
    }

    public IReadOnlyList<string> GetListenerAddresses()
    {
        lock (_gate)
        {
            return _listenerAddresses;
        }
    }

    public string? GetPreferredLanIPv4() => ObsStreamNetworkHelper.GetPreferredLanIPv4();

    public bool IsListeningOnLan() => IsRunning;

    private void StartInternal(int port)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        var addresses = new List<string>();
        var lanIp = ObsStreamNetworkHelper.GetPreferredLanIPv4();
        if (!string.IsNullOrWhiteSpace(lanIp))
        {
            addresses.Add($"{lanIp}:{port}");
        }

        foreach (var ip in ObsStreamNetworkHelper.GetAllLanIPv4())
        {
            var entry = $"{ip}:{port}";
            if (!addresses.Contains(entry, StringComparer.Ordinal))
            {
                addresses.Add(entry);
            }
        }

        addresses.Add($"127.0.0.1:{port}");

        var cts = new CancellationTokenSource();
        var listenTask = Task.Run(() => ListenLoopAsync(listener, cts.Token), CancellationToken.None);

        lock (_gate)
        {
            _tcpListener = listener;
            _cts = cts;
            _listenTask = listenTask;
            _listenerAddresses = addresses;
        }
    }

    private async Task StopInternalAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? listenTask;

        lock (_gate)
        {
            listener = _tcpListener;
            cts = _cts;
            listenTask = _listenTask;
            _tcpListener = null;
            _cts = null;
            _listenTask = null;
            _listenerAddresses = Array.Empty<string>();
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (listener is not null)
        {
            try
            {
                listener.Stop();
            }
            catch
            {
                // ignore
            }
        }

        if (listenTask is not null)
        {
            try
            {
                await listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch
            {
                // ignore
            }
        }

        cts?.Dispose();

        foreach (var (id, socket) in _clients.ToArray())
        {
            _clients.TryRemove(id, out _);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }

            socket.Dispose();
        }
    }

    private async Task ListenLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleTcpClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            try
            {
                var request = await ReadHttpRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                if (request.IsWebSocket)
                {
                    await HandleWebSocketUpgradeAsync(stream, request, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHttpResponseAsync(stream, 405, "text/plain; charset=utf-8", "Method Not Allowed"u8.ToArray())
                        .ConfigureAwait(false);
                    return;
                }

                await HandleHttpGetAsync(stream, request.Path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InteractionLogger.Log($"[ObsStream] Client error: {ex.Message}");
            }
        }
    }

    private async Task HandleWebSocketUpgradeAsync(
        NetworkStream stream,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var accept = ComputeWebSocketAccept(request.Headers.GetValueOrDefault("Sec-WebSocket-Key"));
        var responseText =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        var responseBytes = Encoding.UTF8.GetBytes(responseText);
        await stream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);

        using var webSocket = WebSocket.CreateFromStream(
            stream,
            isServer: true,
            subProtocol: null,
            keepAliveInterval: TimeSpan.FromSeconds(30));

        var id = Guid.NewGuid();
        _clients[id] = webSocket;
        InteractionLogger.Log($"[ObsStream] WebSocket client connected ({_clients.Count} total)");

        try
        {
            foreach (var json in GetStateSnapshot())
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }

            var buffer = new byte[4096];
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (WebSocketException)
        {
            // disconnected
        }
        finally
        {
            _clients.TryRemove(id, out _);
            InteractionLogger.Log($"[ObsStream] WebSocket client disconnected ({_clients.Count} remaining)");
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task HandleHttpGetAsync(NetworkStream stream, string path)
    {
        switch (path)
        {
            case "":
            case "obs":
                await WriteWebFileAsync(stream, "obs.html", "text/html; charset=utf-8").ConfigureAwait(false);
                return;
            case "obs.js":
                await WriteWebFileAsync(stream, "obs.js", "text/javascript; charset=utf-8").ConfigureAwait(false);
                return;
            case "obs.css":
                await WriteWebFileAsync(stream, "obs.css", "text/css; charset=utf-8").ConfigureAwait(false);
                return;
            case "api/state":
                await WriteHttpResponseAsync(
                    stream,
                    200,
                    "application/json; charset=utf-8",
                    Encoding.UTF8.GetBytes(BuildStateJson())).ConfigureAwait(false);
                return;
            default:
                await WriteHttpResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found"u8.ToArray())
                    .ConfigureAwait(false);
                return;
        }
    }

    private static async Task WriteWebFileAsync(NetworkStream stream, string fileName, string contentType)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Web", fileName);
        if (!File.Exists(path))
        {
            await WriteHttpResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found"u8.ToArray())
                .ConfigureAwait(false);
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        await WriteHttpResponseAsync(stream, 200, contentType, bytes).ConfigureAwait(false);
    }

    private static async Task WriteHttpResponseAsync(
        NetworkStream stream,
        int statusCode,
        string contentType,
        byte[] body)
    {
        var statusText = statusCode switch
        {
            200 => "OK",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error"
        };

        var header =
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-cache\r\n" +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.UTF8.GetBytes(header);
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
        }
    }

    private static async Task<HttpRequest?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        var headerEndFound = false;

        while (ms.Length < 16 * 1024 && !headerEndFound)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            ms.Write(buffer, 0, read);
            var headerBytes = ms.GetBuffer().AsSpan(0, (int)ms.Length);
            headerEndFound = headerBytes.IndexOf("\r\n\r\n"u8) >= 0;
        }

        if (ms.Length == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(ms.ToArray());
        var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            return null;
        }

        var headerText = text[..headerEnd];
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            return null;
        }

        var method = requestLine[0];
        var rawPath = requestLine[1];
        var path = rawPath.Split('?', 2)[0].Trim('/').ToLowerInvariant();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = lines[i][..colon].Trim();
            var value = lines[i][(colon + 1)..].Trim();
            headers[name] = value;
        }

        var upgrade = headers.GetValueOrDefault("Upgrade");
        var connection = headers.GetValueOrDefault("Connection");
        var isWebSocket = (upgrade?.Contains("websocket", StringComparison.OrdinalIgnoreCase) ?? false)
                          && (connection?.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) ?? false);

        return new HttpRequest(method, path, headers, isWebSocket);
    }

    private static string ComputeWebSocketAccept(string? secWebSocketKey)
    {
        var key = secWebSocketKey ?? string.Empty;
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key + WebSocketGuid));
        return Convert.ToBase64String(hash);
    }

    private string BuildStateJson()
    {
        var messages = GetStateSnapshot();
        return JsonSerializer.Serialize(new { messages });
    }

    private IReadOnlyList<string> GetStateSnapshot()
    {
        lock (_gate)
        {
            return _stateByType.Values.ToList();
        }
    }

    private static async Task SendSafeAsync(WebSocket socket, byte[] bytes)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore single client failure
        }
    }

    private static string? TryGetMessageType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return typeProp.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private void SetLastError(string? message)
    {
        lock (_gate)
        {
            _lastError = message;
        }
    }

    public void Dispose()
    {
        _ = StopInternalAsync();
    }

    private sealed record HttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Headers,
        bool IsWebSocket);
}
