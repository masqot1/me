using System.Collections.Concurrent;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record CaptureResult(bool Ok, string Message, string? SessionPath = null, int EventCount = 0);

public sealed class CaptureSessionManager
{
    private sealed class Session
    {
        public required int TabId { get; init; }
        public required string Root { get; init; }
        public required string NetworkLog { get; init; }
        public required DateTimeOffset StartedAtUtc { get; init; }
        public int EventCount;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private static readonly IReadOnlyDictionary<string, string[]> AllowedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["capture.start"] = ["tabId", "targetUrl", "title", "startedAt"],
        ["capture.request"] = ["tabId", "requestId", "loaderId", "url", "method", "resourceType", "documentUrl", "timestamp", "wallTime"],
        ["capture.response"] = ["tabId", "requestId", "url", "status", "statusText", "mimeType", "resourceType", "protocol", "fromDiskCache", "fromServiceWorker", "encodedDataLength", "timing", "timestamp"],
        ["capture.finished"] = ["tabId", "requestId", "encodedDataLength", "timestamp"],
        ["capture.failed"] = ["tabId", "requestId", "errorText", "canceled", "blockedReason", "resourceType", "timestamp"],
        ["capture.stop"] = ["tabId", "reason", "stoppedAt"]
    };

    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private string _projectRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TrueWebsiteClonerProjects");

    public void SetProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
        Directory.CreateDirectory(projectRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public async Task<CaptureResult> HandleAsync(string type, JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (!AllowedFields.ContainsKey(type)) return new(false, $"Unsupported capture event: {type}");
        if (!TryGetInt(payload, "tabId", out var tabId)) return new(false, "tabId is required.");

        if (type == "capture.start") return await StartAsync(tabId, payload, cancellationToken);
        if (type == "capture.stop") return await StopAsync(tabId, payload, cancellationToken);

        if (!_sessions.TryGetValue(tabId, out var session)) return new(false, $"No active capture for tab {tabId}.");
        await AppendAsync(session, type, payload, cancellationToken);
        return new(true, "Metadata event saved.", session.Root, session.EventCount);
    }

    private async Task<CaptureResult> StartAsync(int tabId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (_sessions.ContainsKey(tabId)) return new(false, $"Capture already active for tab {tabId}.");

        var targetUrl = GetString(payload, "targetUrl") ?? "unknown";
        var host = SafeHost(targetUrl);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var root = Path.Combine(_projectRoot, host, $"capture-{stamp}");
        var networkDir = Path.Combine(root, "_network");
        Directory.CreateDirectory(networkDir);

        var session = new Session
        {
            TabId = tabId,
            Root = root,
            NetworkLog = Path.Combine(networkDir, "network.jsonl"),
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        if (!_sessions.TryAdd(tabId, session)) return new(false, "Unable to create capture session.");

        var sessionInfo = new
        {
            version = "0.2.0",
            mode = "metadata-only",
            tabId,
            targetUrl,
            title = GetString(payload, "title"),
            startedAtUtc = session.StartedAtUtc,
            sensitiveFieldsSaved = false
        };
        await File.WriteAllTextAsync(Path.Combine(networkDir, "session.json"), JsonSerializer.Serialize(sessionInfo, JsonOptionsIndented), cancellationToken);
        return new(true, "Capture session started.", root, 0);
    }

    private async Task<CaptureResult> StopAsync(int tabId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!_sessions.TryRemove(tabId, out var session)) return new(false, $"No active capture for tab {tabId}.");
        var summary = new
        {
            version = "0.2.0",
            tabId,
            startedAtUtc = session.StartedAtUtc,
            stoppedAtUtc = DateTimeOffset.UtcNow,
            eventCount = session.EventCount,
            reason = GetString(payload, "reason") ?? "user",
            mode = "metadata-only"
        };
        await File.WriteAllTextAsync(Path.Combine(session.Root, "_network", "summary.json"), JsonSerializer.Serialize(summary, JsonOptionsIndented), cancellationToken);
        return new(true, "Capture session stopped.", session.Root, session.EventCount);
    }

    private static async Task AppendAsync(Session session, string type, JsonElement payload, CancellationToken cancellationToken)
    {
        var sanitized = Sanitize(type, payload);
        var line = JsonSerializer.Serialize(new { eventType = type, receivedAtUtc = DateTimeOffset.UtcNow, data = sanitized });
        await session.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(session.NetworkLog, line + Environment.NewLine, cancellationToken);
            session.EventCount++;
        }
        finally
        {
            session.WriteLock.Release();
        }
    }

    private static Dictionary<string, JsonElement> Sanitize(string type, JsonElement payload)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in AllowedFields[type])
        {
            if (payload.TryGetProperty(name, out var value)) result[name] = value.Clone();
        }
        return result;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static string SafeHost(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown-host";
        foreach (var c in Path.GetInvalidFileNameChars()) host = host.Replace(c, '_');
        return string.IsNullOrWhiteSpace(host) ? "unknown-host" : host;
    }

    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
