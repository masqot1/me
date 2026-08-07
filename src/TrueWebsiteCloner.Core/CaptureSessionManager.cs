using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record CaptureResult(bool Ok, string Message, string? SessionPath = null, int EventCount = 0);

public sealed class CaptureSessionManager
{
    public const int MaxBodyBytes = 512 * 1024;

    private sealed class Session
    {
        public required int TabId { get; init; }
        public required string Root { get; init; }
        public required string NetworkLog { get; init; }
        public required string BodiesDirectory { get; init; }
        public required string BodiesLog { get; init; }
        public required Uri TargetOrigin { get; init; }
        public required DateTimeOffset StartedAtUtc { get; init; }
        public int EventCount;
        public int BodyCount;
        public long BodyBytes;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private static readonly IReadOnlyDictionary<string, string[]> AllowedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["capture.start"] = ["tabId", "targetUrl", "title", "startedAt"],
        ["capture.request"] = ["tabId", "requestId", "loaderId", "url", "method", "resourceType", "documentUrl", "timestamp", "wallTime"],
        ["capture.response"] = ["tabId", "requestId", "url", "status", "statusText", "mimeType", "resourceType", "protocol", "fromDiskCache", "fromServiceWorker", "encodedDataLength", "timing", "timestamp"],
        ["capture.finished"] = ["tabId", "requestId", "encodedDataLength", "timestamp"],
        ["capture.failed"] = ["tabId", "requestId", "errorText", "canceled", "blockedReason", "resourceType", "timestamp"],
        ["capture.body"] = ["tabId", "requestId", "url", "mimeType", "resourceType", "status", "base64Encoded", "byteLength"],
        ["capture.stop"] = ["tabId", "reason", "stoppedAt"]
    };

    private static readonly HashSet<string> BinaryMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif"
    };

    private static readonly HashSet<string> ApplicationMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json", "application/ld+json", "application/javascript", "application/x-javascript", "image/svg+xml"
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
        if (type == "capture.body") return await SaveBodyAsync(session, payload, cancellationToken);

        await AppendMetadataAsync(session, type, payload, cancellationToken);
        return new(true, "Metadata event saved.", session.Root, session.EventCount);
    }

    private async Task<CaptureResult> StartAsync(int tabId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (_sessions.ContainsKey(tabId)) return new(false, $"Capture already active for tab {tabId}.");

        var targetUrl = GetString(payload, "targetUrl") ?? string.Empty;
        if (!TryGetHttpOrigin(targetUrl, out var targetOrigin)) return new(false, "Capture target must be an http:// or https:// URL.");

        var host = SafeHost(targetUrl);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var root = Path.Combine(_projectRoot, host, $"capture-{stamp}");
        var networkDir = Path.Combine(root, "_network");
        var bodiesDir = Path.Combine(root, "_bodies");
        Directory.CreateDirectory(networkDir);
        Directory.CreateDirectory(bodiesDir);

        var session = new Session
        {
            TabId = tabId,
            Root = root,
            NetworkLog = Path.Combine(networkDir, "network.jsonl"),
            BodiesDirectory = bodiesDir,
            BodiesLog = Path.Combine(bodiesDir, "bodies.jsonl"),
            TargetOrigin = targetOrigin,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        if (!_sessions.TryAdd(tabId, session)) return new(false, "Unable to create capture session.");

        var sessionInfo = new
        {
            version = "0.3.0",
            mode = "same-origin-response-bodies",
            tabId,
            targetUrl,
            targetOrigin = targetOrigin.GetLeftPart(UriPartial.Authority),
            title = GetString(payload, "title"),
            startedAtUtc = session.StartedAtUtc,
            responseBodyPolicy = new
            {
                sameOriginOnly = true,
                getRequestsOnly = true,
                maxBodyBytes = MaxBodyBytes,
                requestBodiesSaved = false,
                cookiesSaved = false,
                authorizationHeadersSaved = false
            }
        };
        await File.WriteAllTextAsync(Path.Combine(networkDir, "session.json"), JsonSerializer.Serialize(sessionInfo, JsonOptionsIndented), cancellationToken);
        return new(true, "Capture session started.", root, 0);
    }

    private async Task<CaptureResult> SaveBodyAsync(Session session, JsonElement payload, CancellationToken cancellationToken)
    {
        var requestId = GetString(payload, "requestId");
        var url = GetString(payload, "url");
        var mimeType = NormalizeMime(GetString(payload, "mimeType"));
        var body = GetString(payload, "body");
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(mimeType) || body is null)
            return new(false, "Response body event is incomplete.", session.Root, session.EventCount);

        if (!IsSameOrigin(session.TargetOrigin, url))
            return new(false, "Cross-origin response body rejected by Gate 0.3 policy.", session.Root, session.EventCount);

        if (!IsAllowedMime(mimeType))
            return new(false, $"MIME type not enabled for body capture: {mimeType}", session.Root, session.EventCount);

        byte[] bytes;
        var base64Encoded = GetBool(payload, "base64Encoded");
        try
        {
            bytes = base64Encoded ? Convert.FromBase64String(body) : Encoding.UTF8.GetBytes(body);
        }
        catch (FormatException)
        {
            return new(false, "Invalid base64 response body.", session.Root, session.EventCount);
        }

        if (bytes.Length > MaxBodyBytes)
            return new(false, $"Response body exceeds {MaxBodyBytes} byte limit.", session.Root, session.EventCount);

        await session.WriteLock.WaitAsync(cancellationToken);
        try
        {
            var next = session.BodyCount + 1;
            var fileName = $"{next:D4}-{SafeToken(requestId)}{ExtensionForMime(mimeType)}";
            var bodyPath = Path.Combine(session.BodiesDirectory, fileName);
            await File.WriteAllBytesAsync(bodyPath, bytes, cancellationToken);

            var relativePath = $"_bodies/{fileName}";
            var manifestLine = JsonSerializer.Serialize(new
            {
                requestId,
                url,
                mimeType,
                resourceType = GetString(payload, "resourceType"),
                status = TryGetInt(payload, "status", out var status) ? status : 0,
                base64Encoded,
                byteLength = bytes.Length,
                file = relativePath,
                capturedAtUtc = DateTimeOffset.UtcNow
            });
            await File.AppendAllTextAsync(session.BodiesLog, manifestLine + Environment.NewLine, cancellationToken);

            var networkLine = JsonSerializer.Serialize(new
            {
                eventType = "capture.body",
                receivedAtUtc = DateTimeOffset.UtcNow,
                data = new
                {
                    requestId,
                    url,
                    mimeType,
                    resourceType = GetString(payload, "resourceType"),
                    status = TryGetInt(payload, "status", out var bodyStatus) ? bodyStatus : 0,
                    base64Encoded,
                    byteLength = bytes.Length,
                    file = relativePath
                }
            });
            await File.AppendAllTextAsync(session.NetworkLog, networkLine + Environment.NewLine, cancellationToken);

            session.BodyCount++;
            session.BodyBytes += bytes.Length;
            session.EventCount++;
            return new(true, "Response body saved.", session.Root, session.EventCount);
        }
        finally
        {
            session.WriteLock.Release();
        }
    }

    private async Task<CaptureResult> StopAsync(int tabId, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!_sessions.TryRemove(tabId, out var session)) return new(false, $"No active capture for tab {tabId}.");
        var summary = new
        {
            version = "0.3.0",
            tabId,
            startedAtUtc = session.StartedAtUtc,
            stoppedAtUtc = DateTimeOffset.UtcNow,
            eventCount = session.EventCount,
            bodyCount = session.BodyCount,
            bodyBytes = session.BodyBytes,
            maxBodyBytes = MaxBodyBytes,
            reason = GetString(payload, "reason") ?? "user",
            mode = "same-origin-response-bodies"
        };
        await File.WriteAllTextAsync(Path.Combine(session.Root, "_network", "summary.json"), JsonSerializer.Serialize(summary, JsonOptionsIndented), cancellationToken);
        return new(true, "Capture session stopped.", session.Root, session.EventCount);
    }

    private static async Task AppendMetadataAsync(Session session, string type, JsonElement payload, CancellationToken cancellationToken)
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
            if (name == "body") continue;
            if (payload.TryGetProperty(name, out var value)) result[name] = value.Clone();
        }
        return result;
    }

    private static bool IsAllowedMime(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        ApplicationMimeTypes.Contains(mimeType) || BinaryMimeTypes.Contains(mimeType);

    private static string NormalizeMime(string? mimeType) =>
        (mimeType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static string ExtensionForMime(string mimeType) => mimeType switch
    {
        "text/html" => ".html",
        "text/css" => ".css",
        "text/javascript" or "application/javascript" or "application/x-javascript" => ".js",
        "application/json" or "application/ld+json" => ".json",
        "image/svg+xml" => ".svg",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".txt"
    };

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False && property.GetBoolean();

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static bool TryGetHttpOrigin(string url, out Uri origin)
    {
        origin = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
        return true;
    }

    private static bool IsSameOrigin(Uri origin, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate)) return false;
        if (candidate.Scheme is not ("http" or "https")) return false;
        return string.Equals(origin.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(origin.IdnHost, candidate.IdnHost, StringComparison.OrdinalIgnoreCase)
               && origin.Port == candidate.Port;
    }

    private static string SafeHost(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown-host";
        foreach (var c in Path.GetInvalidFileNameChars()) host = host.Replace(c, '_');
        return string.IsNullOrWhiteSpace(host) ? "unknown-host" : host;
    }

    private static string SafeToken(string value)
    {
        var chars = value.Take(80).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        var token = new string(chars);
        return string.IsNullOrWhiteSpace(token) ? "request" : token;
    }

    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
