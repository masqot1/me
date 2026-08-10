using System.Text;
using System.Text.Json;
using TrueWebsiteCloner.Core;

static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-WebSocketGate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var manager = new CaptureSessionManager();
    manager.SetProjectRoot(tempRoot);
    const int tabId = 77;

    var start = await manager.HandleAsync("capture.start", Json(new
    {
        tabId,
        targetUrl = "https://example.test/page",
        title = "Gate 1.1",
        startedAt = DateTimeOffset.UtcNow
    }));
    Require(start.Ok && !string.IsNullOrWhiteSpace(start.SessionPath), "Capture session did not start.");

    var created = await manager.HandleAsync("capture.websocket.created", Json(new
    {
        tabId,
        requestId = "ws-1",
        url = "wss://example.test/socket",
        initiatorType = "script",
        headers = new { Cookie = "session=secret" }
    }));
    Require(created.Ok, "Same-origin WebSocket creation was rejected.");

    var handshake = await manager.HandleAsync("capture.websocket.handshake", Json(new
    {
        tabId,
        requestId = "ws-1",
        url = "wss://example.test/socket",
        status = 101,
        statusText = "Switching Protocols",
        timestamp = 1.25,
        authorization = "Bearer should-not-persist"
    }));
    Require(handshake.Ok, "Same-origin WebSocket handshake was rejected.");

    const string payload = "{\"kind\":\"hello\",\"value\":42}";
    var payloadBytes = Encoding.UTF8.GetByteCount(payload);
    var frame = await manager.HandleAsync("capture.websocket.frame", Json(new
    {
        tabId,
        requestId = "ws-1",
        url = "wss://example.test/socket",
        direction = "received",
        opcode = 1,
        mask = false,
        payloadCaptured = true,
        byteLength = payloadBytes,
        payloadData = payload,
        timestamp = 2.5
    }));
    Require(frame.Ok, "Valid WebSocket frame was rejected.");

    var oversizedPayload = new string('x', CaptureSessionManager.MaxWebSocketFrameBytes + 1);
    var oversized = await manager.HandleAsync("capture.websocket.frame", Json(new
    {
        tabId,
        requestId = "ws-1",
        url = "wss://example.test/socket",
        direction = "sent",
        opcode = 1,
        mask = true,
        payloadCaptured = true,
        byteLength = Encoding.UTF8.GetByteCount(oversizedPayload),
        payloadData = oversizedPayload,
        timestamp = 3.0
    }));
    Require(!oversized.Ok, "Oversized WebSocket frame was not rejected.");

    var boundedMetadata = await manager.HandleAsync("capture.websocket.frame", Json(new
    {
        tabId,
        requestId = "ws-1",
        url = "wss://example.test/socket",
        direction = "sent",
        opcode = 2,
        mask = true,
        payloadCaptured = false,
        byteLength = CaptureSessionManager.MaxWebSocketFrameBytes + 4096,
        payloadData = "must-not-persist",
        reason = "frame-too-large",
        timestamp = 3.25
    }));
    Require(boundedMetadata.Ok, "Oversized-frame metadata should be retained without payloadData.");

    var crossOrigin = await manager.HandleAsync("capture.websocket.created", Json(new
    {
        tabId,
        requestId = "ws-cross-origin",
        url = "wss://other.test/socket",
        initiatorType = "script"
    }));
    Require(!crossOrigin.Ok, "Cross-origin WebSocket event was not rejected.");

    var closed = await manager.HandleAsync("capture.websocket.closed", Json(new
    {
        tabId,
        requestId = "ws-1",
        url = "wss://example.test/socket",
        timestamp = 4.0
    }));
    Require(closed.Ok, "WebSocket close event was rejected.");

    var stop = await manager.HandleAsync("capture.stop", Json(new
    {
        tabId,
        reason = "gate-1.1",
        stoppedAt = DateTimeOffset.UtcNow
    }));
    Require(stop.Ok, "Capture session did not stop cleanly.");

    var networkLog = Path.Combine(start.SessionPath!, "_network", "network.jsonl");
    var summaryPath = Path.Combine(start.SessionPath!, "_network", "summary.json");
    var sessionPath = Path.Combine(start.SessionPath!, "_network", "session.json");
    Require(File.Exists(networkLog), "network.jsonl was not created.");
    Require(File.Exists(summaryPath), "summary.json was not created.");
    Require(File.Exists(sessionPath), "session.json was not created.");

    var networkText = await File.ReadAllTextAsync(networkLog);
    Require(networkText.Contains("capture.websocket.created", StringComparison.Ordinal), "WebSocket creation event is missing.");
    Require(networkText.Contains("capture.websocket.handshake", StringComparison.Ordinal), "WebSocket handshake event is missing.");
    Require(networkText.Contains("capture.websocket.frame", StringComparison.Ordinal), "WebSocket frame event is missing.");
    Require(networkText.Contains(payload, StringComparison.Ordinal), "Captured WebSocket payload is missing.");
    Require(!networkText.Contains("must-not-persist", StringComparison.Ordinal), "Uncaptured oversized payload leaked into network.jsonl.");
    Require(!networkText.Contains("session=secret", StringComparison.Ordinal), "Cookie-like handshake data leaked into network.jsonl.");
    Require(!networkText.Contains("Bearer should-not-persist", StringComparison.Ordinal), "Authorization-like handshake data leaked into network.jsonl.");
    Require(!networkText.Contains("wss://other.test/socket", StringComparison.Ordinal), "Cross-origin WebSocket data leaked into network.jsonl.");

    using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath));
    Require(summary.RootElement.GetProperty("webSocketEventCount").GetInt32() == 5, "Unexpected WebSocket event count.");
    Require(summary.RootElement.GetProperty("webSocketFrameCount").GetInt32() == 2, "Rejected WebSocket frames must not be counted.");
    Require(summary.RootElement.GetProperty("webSocketFrameBytes").GetInt64() == payloadBytes, "Unexpected captured WebSocket frame byte count.");
    Require(summary.RootElement.GetProperty("maxWebSocketFrameBytes").GetInt32() == CaptureSessionManager.MaxWebSocketFrameBytes, "WebSocket frame limit missing from summary.");

    using var session = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath));
    var policy = session.RootElement.GetProperty("webSocketPolicy");
    Require(policy.GetProperty("sameOriginOnly").GetBoolean(), "WebSocket same-origin policy is not enabled.");
    Require(!policy.GetProperty("handshakeHeadersSaved").GetBoolean(), "Handshake headers must remain disabled.");
    Require(!policy.GetProperty("cookiesSaved").GetBoolean(), "Cookie capture must remain disabled.");
    Require(!policy.GetProperty("authorizationHeadersSaved").GetBoolean(), "Authorization header capture must remain disabled.");

    Console.WriteLine("RESULT: TrueWebsiteCloner WebSocket Gate 1.1 PASS");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
