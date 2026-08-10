using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using TrueWebsiteCloner.Core;
using TrueWebsiteCloner.Shared;

namespace TrueWebsiteCloner.Desktop;

public sealed class BridgeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly CaptureSessionManager _captures = new();
    private readonly SafeHeaderCaptureManager _headers = new();
    private Task? _acceptLoop;
    private string _token = string.Empty;

    public int Port { get; private set; }
    public DateTimeOffset? LastExtensionMessageAtUtc { get; private set; }
    public string LastMessageSummary { get; private set; } = "—";

    public event EventHandler? StateChanged;

    public void SetProjectRoot(string path) => _captures.SetProjectRoot(path);

    public async Task StartAsync()
    {
        AppPaths.EnsureDirectories();
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var info = new BridgeInfo(Port, _token, Environment.ProcessId, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(AppPaths.BridgeInfoPath, JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var doc = await FramedJson.ReadDocumentAsync(stream, ProtocolConstants.MaxBridgeMessageBytes, cancellationToken);
                if (doc is null) return;

                var envelope = doc.RootElement.Deserialize<NativeBridgeEnvelope>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (envelope is null || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(envelope.Token), System.Text.Encoding.UTF8.GetBytes(_token)))
                {
                    await FramedJson.WriteAsync(stream, new BridgeReply(false, "unauthorized", "Bridge token rejected.", "0.2.0", DateTimeOffset.UtcNow), ProtocolConstants.MaxBridgeMessageBytes, cancellationToken);
                    return;
                }

                var type = envelope.Payload.TryGetProperty("type", out var typeValue) ? typeValue.GetString() ?? "unknown" : "unknown";
                object? data = null;
                if (type is "capture.request.headers" or "capture.response.headers")
                {
                    data = await _headers.HandleAsync(type, envelope.Payload, cancellationToken);
                }
                else if (type.StartsWith("capture.", StringComparison.Ordinal))
                {
                    var captureResult = await _captures.HandleAsync(type, envelope.Payload, cancellationToken);
                    data = captureResult;
                    if (type == "capture.start" && captureResult.Ok && !string.IsNullOrWhiteSpace(captureResult.SessionPath))
                    {
                        var headerRegistration = await _headers.RegisterAsync(envelope.Payload, captureResult.SessionPath, cancellationToken);
                        if (!headerRegistration.Ok)
                            data = new { capture = captureResult, headers = headerRegistration };
                    }
                    else if (type == "capture.stop")
                    {
                        _headers.Unregister(envelope.Payload);
                    }
                }

                LastExtensionMessageAtUtc = DateTimeOffset.UtcNow;
                LastMessageSummary = $"{type} from {envelope.Origin ?? "unknown origin"}";
                StateChanged?.Invoke(this, EventArgs.Empty);

                await FramedJson.WriteAsync(stream,
                    new BridgeReply(true, "desktop_bridge_ok", "Desktop bridge received the Chrome extension message.", "0.2.0", DateTimeOffset.UtcNow,
                        data ?? new { desktopPid = Environment.ProcessId, bridgePort = Port, receivedType = type }),
                    ProtocolConstants.MaxBridgeMessageBytes, cancellationToken);
            }
            catch { }
        }
    }

    public bool WasExtensionSeenRecently(TimeSpan window) => LastExtensionMessageAtUtc is { } at && DateTimeOffset.UtcNow - at <= window;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null) { try { await _acceptLoop; } catch { } }
        try { if (File.Exists(AppPaths.BridgeInfoPath)) File.Delete(AppPaths.BridgeInfoPath); } catch { }
        _cts.Dispose();
    }
}
