using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Piko.Models;

namespace Piko.Services;

public sealed class LocalBridgeService : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private readonly int _port;
    private readonly string _token;

    public event Action<BridgeMessage>? MessageReceived;
    public event Action<string>? StatusChanged;
    public event Action? DiagnosticRecoverRequested;
    public event Action? ToggleVisualRequested;
    public event Action? ExpandRequested;
    public event Action? CollapseRequested;
    public event Action? SimulateMusicRequested;
    public event Action? SimulateNotificationRequested;
    public event Action? SimulateSleepyRequested;
    public event Action<string, string>? MediaPlayingReceived;
    public event Action<string, string, BridgeMessage>? MediaPlayingWithPayloadReceived;
    public event Action<BridgeMessage>? MediaMetadataReceived;
    public event Action<string>? MediaPausedReceived;
    public event Action<string, BridgeMessage>? MediaPausedWithPayloadReceived;
    public event Action<string>? MediaStoppedReceived;
    public event Action<string, BridgeMessage>? MediaStoppedWithPayloadReceived;
    public event Action<BridgeMessage>? MediaCommandResultReceived;
    public event Action<string>? DiagnosticLogged;

    private readonly System.Collections.Concurrent.ConcurrentQueue<MediaControlCommand> _pendingCommands = new();

    public bool IsRunning { get; private set; }

    public MediaControlCommand EnqueueMediaCommand(string action, int? tabId = null)
    {
        var cmd = new MediaControlCommand
        {
            Id = Guid.NewGuid().ToString("N"),
            Action = action,
            TabId = tabId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        _pendingCommands.Enqueue(cmd);
        DiagnosticLogged?.Invoke($"Bridge command queued: id={cmd.Id}, action={action}, tabId={tabId}");
        return cmd;
    }

    public LocalBridgeService(int port = 17321, string token = "piko-dev-bridge-token-2026")
    {
        _port = port;
        _token = token;
    }

    public void Start()
    {
        if (IsRunning) return;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                _cancellation = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.ExclusiveAddressUse = false;
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                IsRunning = true;
                StatusChanged?.Invoke($"Bridge aktif di 127.0.0.1:{_port}");
                _ = AcceptClientsAsync(_listener, _cancellation.Token);
                return;
            }
            catch (SocketException ex)
            {
                try { _listener?.Stop(); } catch { }
                _listener = null;
                if (attempt == 4)
                {
                    IsRunning = false;
                    StatusChanged?.Invoke($"Port {_port} sedang digunakan aplikasi lain. ({ex.Message})");
                }
                else
                {
                    Thread.Sleep(250);
                }
            }
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cancellation?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Ignore socket close exceptions
        }
        StatusChanged?.Invoke("Bridge dinonaktifkan.");
    }

    private async Task AcceptClientsAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsRunning)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Continue listening
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var headerBytes = new List<byte>(1024);
                var oneByte = new byte[1];
                var matched = 0;
                var terminator = new byte[] { 13, 10, 13, 10 };

                while (headerBytes.Count < 8192 && matched < terminator.Length)
                {
                    var read = await stream.ReadAsync(oneByte.AsMemory(0, 1), token);
                    if (read == 0) return;
                    var b = oneByte[0];
                    headerBytes.Add(b);
                    matched = b == terminator[matched] ? matched + 1 : (b == terminator[0] ? 1 : 0);
                }

                if (matched != terminator.Length)
                {
                    await WriteResponseAsync(stream, 413, "Request headers too large", null);
                    return;
                }

                var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
                var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                {
                    await WriteResponseAsync(stream, 400, "Bad request", null);
                    return;
                }

                var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (requestLine.Length < 2)
                {
                    await WriteResponseAsync(stream, 400, "Bad request", null);
                    return;
                }

                var method = requestLine[0].ToUpperInvariant();
                var path = requestLine[1];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var line in lines.Skip(1))
                {
                    var separator = line.IndexOf(':');
                    if (separator > 0)
                        headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }

                headers.TryGetValue("Origin", out var origin);

                if (method == "OPTIONS")
                {
                    await WriteResponseAsync(stream, 204, "", IsAllowedOrigin(origin) ? origin : "*");
                    return;
                }

                // Diagnostic fallback endpoints accessible via HTTP
                if (path == "/recover" || path == "/diagnostics/recover")
                {
                    DiagnosticRecoverRequested?.Invoke();
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"recovered\"}", origin);
                    return;
                }

                if (path == "/toggle-visual" || path == "/diagnostics/toggle-visual")
                {
                    ToggleVisualRequested?.Invoke();
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"visual_toggled\"}", origin);
                    return;
                }

                if (path == "/status" || path == "/health")
                {
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"running\"}", origin);
                    return;
                }

                if (path == "/expand" || path == "/diagnostics/expand")
                {
                    ExpandRequested?.Invoke();
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"expanded\"}", origin);
                    return;
                }

                if (path == "/collapse" || path == "/diagnostics/collapse")
                {
                    CollapseRequested?.Invoke();
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"collapsed\"}", origin);
                    return;
                }

                if (path == "/simulate/ai-start" || path == "/ai-start")
                {
                    MessageReceived?.Invoke(new BridgeMessage { Type = BridgeMessage.TypeStart, Token = _token, Source = "ChatGPT", Title = "ChatGPT sedang berpikir..." });
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"ai_started\"}", origin);
                    return;
                }

                if (path == "/simulate/ai-complete" || path == "/ai-complete")
                {
                    MessageReceived?.Invoke(new BridgeMessage { Type = BridgeMessage.TypeComplete, Token = _token, Source = "ChatGPT", Title = "Jawaban ChatGPT sudah siap ✨" });
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"ai_completed\"}", origin);
                    return;
                }

                if (path == "/simulate/ai-cancel" || path == "/ai-cancel")
                {
                    MessageReceived?.Invoke(new BridgeMessage { Type = BridgeMessage.TypeCancel, Token = _token, Source = "ChatGPT", Title = "Generasi ChatGPT dihentikan." });
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"ai_cancelled\"}", origin);
                    return;
                }

                if (path == "/simulate/music" || path == "/music" || path == "/simulate/media-playing" || path == "/media-playing")
                {
                    SimulateMusicRequested?.Invoke();
                    MediaPlayingReceived?.Invoke("YouTube", "Midnight City (YouTube)");
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"media_playing_simulated\"}", origin);
                    return;
                }

                if (path == "/simulate/media-paused" || path == "/media-paused")
                {
                    MediaPausedReceived?.Invoke("YouTube");
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"media_paused_simulated\"}", origin);
                    return;
                }

                if (path == "/simulate/media-stopped" || path == "/media-stopped")
                {
                    MediaStoppedReceived?.Invoke("YouTube");
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"media_stopped_simulated\"}", origin);
                    return;
                }

                if (path == "/simulate/notification" || path == "/notification")
                {
                    SimulateNotificationRequested?.Invoke();
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"notification_simulated\"}", origin);
                    return;
                }

                if (path == "/simulate/sleepy" || path == "/sleepy")
                {
                    SimulateSleepyRequested?.Invoke();
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"sleepy_simulated\"}", origin);
                    return;
                }

                if (path == "/simulate/media-metadata" || path == "/media-metadata")
                {
                    var sampleMsg = new BridgeMessage
                    {
                        Type = BridgeMessage.TypeMediaMetadata,
                        Token = _token,
                        Source = "YouTube",
                        Title = "Midnight City (Official Video)",
                        Channel = "M83",
                        VideoId = "dX3k_QDnzHE",
                        ThumbnailUrl = "https://i.ytimg.com/vi/dX3k_QDnzHE/hqdefault.jpg",
                        PageUrl = "https://www.youtube.com/watch?v=dX3k_QDnzHE",
                        Duration = 244,
                        CurrentTime = 42,
                        PlaybackState = "playing"
                    };
                    MediaMetadataReceived?.Invoke(sampleMsg);
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"status\":\"media_metadata_simulated\"}", origin);
                    return;
                }

                if (method == "GET" && (path == "/command" || path == "/commands"))
                {
                    if (_pendingCommands.TryDequeue(out var cmd))
                    {
                        var json = JsonSerializer.Serialize(new { ok = true, command = cmd });
                        await WriteResponseAsync(stream, 200, json, origin);
                    }
                    else
                    {
                        await WriteResponseAsync(stream, 200, "{\"ok\":true,\"command\":null}", origin);
                    }
                    return;
                }

                if (method != "POST" || path != "/event")
                {
                    await WriteResponseAsync(stream, 404, "Not found", IsAllowedOrigin(origin) ? origin : null);
                    return;
                }

                if (!IsAllowedOrigin(origin))
                {
                    await WriteResponseAsync(stream, 403, "Origin forbidden", null);
                    return;
                }

                var contentLength = headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var parsedLength)
                    ? parsedLength
                    : 0;

                if (contentLength < 1 || contentLength > 16384)
                {
                    await WriteResponseAsync(stream, 413, "Invalid payload size", origin);
                    return;
                }

                var body = new byte[contentLength];
                var offset = 0;
                while (offset < body.Length)
                {
                    var read = await stream.ReadAsync(body.AsMemory(offset, body.Length - offset), token);
                    if (read == 0) return;
                    offset += read;
                }

                BridgeMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<BridgeMessage>(
                        Encoding.UTF8.GetString(body),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    message = null;
                }

                if (message is null || !message.IsValid(_token))
                {
                    await WriteResponseAsync(stream, 403, "Invalid message or token", origin);
                    return;
                }

                if (message.Type == BridgeMessage.TypeDiagnosticsRecover)
                {
                    DiagnosticRecoverRequested?.Invoke();
                }
                else if (message.Type == BridgeMessage.TypeToggleVisual)
                {
                    ToggleVisualRequested?.Invoke();
                }
                else if (message.Type is BridgeMessage.TypeMediaPlaying or BridgeMessage.TypeMediaPlayingAlt)
                {
                    DiagnosticLogged?.Invoke($"Bridge event received: media_playing (source={message.Source}, title={message.Title}, channel={message.Channel}, videoId={message.VideoId})");
                    MediaPlayingReceived?.Invoke(message.Source ?? "YouTube", message.Title ?? "Media Playing");
                    MediaPlayingWithPayloadReceived?.Invoke(message.Source ?? "YouTube", message.Title ?? "Media Playing", message);
                }
                else if (message.Type is BridgeMessage.TypeMediaPaused or BridgeMessage.TypeMediaPausedAlt)
                {
                    DiagnosticLogged?.Invoke($"Bridge event received: media_paused (source={message.Source}, tabId={message.TabId})");
                    MediaPausedReceived?.Invoke(message.Source ?? "YouTube");
                    MediaPausedWithPayloadReceived?.Invoke(message.Source ?? "YouTube", message);
                }
                else if (message.Type is BridgeMessage.TypeMediaStopped or BridgeMessage.TypeMediaStoppedAlt)
                {
                    DiagnosticLogged?.Invoke($"Bridge event received: media_stopped (source={message.Source}, tabId={message.TabId})");
                    MediaStoppedReceived?.Invoke(message.Source ?? "YouTube");
                    MediaStoppedWithPayloadReceived?.Invoke(message.Source ?? "YouTube", message);
                }
                else if (message.Type is BridgeMessage.TypeMediaMetadata or BridgeMessage.TypeMediaMetadataAlt or BridgeMessage.TypeMediaProgress)
                {
                    DiagnosticLogged?.Invoke($"Bridge event received: media_metadata_changed (title={message.Title}, channel={message.Channel}, videoId={message.VideoId})");
                    MediaMetadataReceived?.Invoke(message);
                }
                else if (message.Type is BridgeMessage.TypeMediaCommandResult)
                {
                    DiagnosticLogged?.Invoke($"Bridge event received: media_command_result (id={message.CommandId}, action={message.CommandAction}, success={message.CommandSuccess})");
                    MediaCommandResultReceived?.Invoke(message);
                }
                else
                {
                    DiagnosticLogged?.Invoke($"Bridge event received: type={message.Type}, source={message.Source}");
                }

                MessageReceived?.Invoke(message);
                await WriteResponseAsync(stream, 200, "{\"ok\":true}", origin);
            }
            catch
            {
                // Graceful client handling
            }
        }
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        // Local loopback clients, curl, or background service workers may omit the Origin header
        if (string.IsNullOrWhiteSpace(origin)) return true;
        return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
               origin.StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase) ||
               origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
               origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
               origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("chat.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, string? allowedOrigin)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var statusText = status switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            413 => "Payload Too Large",
            _ => "Error"
        };

        var originHeaderValue = string.IsNullOrWhiteSpace(allowedOrigin) ? "*" : allowedOrigin;
        var cors = $"Access-Control-Allow-Origin: {originHeaderValue}\r\nVary: Origin\r\nAccess-Control-Allow-Methods: POST, GET, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With, Access-Control-Request-Private-Network, *\r\nAccess-Control-Allow-Private-Network: true\r\nAccess-Control-Max-Age: 86400\r\n";

        var headers = $"HTTP/1.1 {status} {statusText}\r\n{cors}Content-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);

        await stream.WriteAsync(headerBytes.AsMemory());
        if (bytes.Length > 0)
            await stream.WriteAsync(bytes.AsMemory());
        await stream.FlushAsync();
    }

    public void Dispose()
    {
        Stop();
        _cancellation?.Dispose();
    }
}

public sealed class MediaControlCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Action { get; set; } = string.Empty;
    public int? TabId { get; set; }
    public long Timestamp { get; set; }
}
