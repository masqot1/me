using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed class SafeHeaderCaptureManager
{
    public const int MaxHeaderCount = 16;
    public const int MaxHeaderValueBytes = 2 * 1024;
    public const int MaxHeaderTotalBytes = 8 * 1024;

    public static readonly IReadOnlySet<string> AllowedRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "accept",
        "accept-language",
        "cache-control",
        "content-type",
        "if-modified-since",
        "if-none-match",
        "pragma"
    };

    public static readonly IReadOnlySet<string> AllowedResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "accept-ranges",
        "cache-control",
        "content-encoding",
        "content-language",
        "content-length",
        "content-type",
        "etag",
        "last-modified",
        "vary"
    };

    public static readonly IReadOnlySet<string> ExplicitlySensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
        "x-api-key",
        "api-key",
        "x-auth-token",
        "x-access-token",
        "x-csrf-token",
        "csrf-token"
    };

    private sealed class Session
    {
        public required int TabId { get; init; }
        public required Uri TargetOrigin { get; init; }
        public required string NetworkLog { get; init; }
        public required string PolicyPath { get; init; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<int, Session> _sessions = new();

    public async Task<HeaderCaptureResult> RegisterAsync(JsonElement captureStartPayload, string captureSessionPath, CancellationToken cancellationToken = default)
    {
        if (!TryGetInt(captureStartPayload, "tabId", out var tabId))
            return new(false, "tabId is required for header capture registration.");

        var targetUrl = GetString(captureStartPayload, "targetUrl") ?? string.Empty;
        if (!TryGetHttpOrigin(targetUrl, out var targetOrigin))
            return new(false, "Header capture target must be an http:// or https:// URL.");

        if (string.IsNullOrWhiteSpace(captureSessionPath))
            return new(false, "Capture session path is required for header capture registration.");

        var networkDir = Path.Combine(captureSessionPath, "_network");
        Directory.CreateDirectory(networkDir);
        var session = new Session
        {
            TabId = tabId,
            TargetOrigin = targetOrigin,
            NetworkLog = Path.Combine(networkDir, "network.jsonl"),
            PolicyPath = Path.Combine(networkDir, "header-policy.json")
        };
        _sessions[tabId] = session;

        var policy = new
        {
            format = "TrueWebsiteCloner.SafeHeaderPolicy",
            version = "1.3",
            sameOriginOnly = true,
            requestAllowlist = AllowedRequestHeaders.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            responseAllowlist = AllowedResponseHeaders.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            explicitlySensitiveHeaders = ExplicitlySensitiveHeaders.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            maxHeaderCount = MaxHeaderCount,
            maxHeaderValueBytes = MaxHeaderValueBytes,
            maxHeaderTotalBytes = MaxHeaderTotalBytes,
            unknownHeadersPersisted = false,
            sensitiveHeadersPersisted = false,
            rawHeaderBlocksPersisted = false
        };
        await File.WriteAllTextAsync(session.PolicyPath, JsonSerializer.Serialize(policy, JsonOptionsIndented), cancellationToken);
        return new(true, "Safe header capture registered.");
    }

    public void Unregister(JsonElement captureStopPayload)
    {
        if (TryGetInt(captureStopPayload, "tabId", out var tabId))
            _sessions.TryRemove(tabId, out _);
    }

    public async Task<HeaderCaptureResult> HandleAsync(string type, JsonElement payload, CancellationToken cancellationToken = default)
    {
        var direction = type switch
        {
            "capture.request.headers" => "request",
            "capture.response.headers" => "response",
            _ => null
        };
        if (direction is null) return new(false, $"Unsupported header capture event: {type}");
        if (!TryGetInt(payload, "tabId", out var tabId)) return new(false, "tabId is required.");
        if (!_sessions.TryGetValue(tabId, out var session)) return new(false, $"No active header capture for tab {tabId}.");

        var requestId = GetString(payload, "requestId");
        var url = GetString(payload, "url");
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(url))
            return new(false, "Header capture event is incomplete.");
        if (!IsSameOrigin(session.TargetOrigin, url))
            return new(false, "Cross-origin HTTP headers rejected by Gate 1.3 policy.");

        if (!payload.TryGetProperty("headers", out var headersElement) || headersElement.ValueKind != JsonValueKind.Object)
            return new(false, "Header capture event must contain a headers object.");

        var allowlist = direction == "request" ? AllowedRequestHeaders : AllowedResponseHeaders;
        var safeHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var droppedHeaderCount = 0;
        var totalBytes = 0;

        foreach (var property in headersElement.EnumerateObject())
        {
            var normalizedName = property.Name.Trim().ToLowerInvariant();
            if (!allowlist.Contains(normalizedName) || ExplicitlySensitiveHeaders.Contains(normalizedName))
            {
                droppedHeaderCount++;
                continue;
            }
            if (safeHeaders.ContainsKey(normalizedName))
            {
                droppedHeaderCount++;
                continue;
            }
            if (safeHeaders.Count >= MaxHeaderCount)
            {
                droppedHeaderCount++;
                continue;
            }
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                droppedHeaderCount++;
                continue;
            }

            var value = property.Value.GetString()?.Trim() ?? string.Empty;
            if (value.Contains('\r') || value.Contains('\n'))
            {
                droppedHeaderCount++;
                continue;
            }

            var valueBytes = Encoding.UTF8.GetByteCount(value);
            var entryBytes = Encoding.UTF8.GetByteCount(normalizedName) + valueBytes;
            if (valueBytes > MaxHeaderValueBytes || totalBytes + entryBytes > MaxHeaderTotalBytes)
            {
                droppedHeaderCount++;
                continue;
            }

            safeHeaders[normalizedName] = value;
            totalBytes += entryBytes;
        }

        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tabId"] = tabId,
            ["requestId"] = requestId,
            ["url"] = url,
            ["resourceType"] = GetString(payload, "resourceType"),
            ["headers"] = safeHeaders,
            ["acceptedHeaderCount"] = safeHeaders.Count,
            ["droppedHeaderCount"] = droppedHeaderCount,
            ["headerBytes"] = totalBytes,
            ["timestamp"] = GetNumber(payload, "timestamp")
        };
        if (direction == "request")
            data["method"] = (GetString(payload, "method") ?? string.Empty).ToUpperInvariant();
        else
            data["status"] = TryGetInt(payload, "status", out var status) ? status : 0;

        var line = JsonSerializer.Serialize(new
        {
            eventType = type,
            receivedAtUtc = DateTimeOffset.UtcNow,
            data
        });

        await session.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(session.NetworkLog, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            session.WriteLock.Release();
        }

        return new(true, "Safe HTTP headers saved.", safeHeaders.Count, droppedHeaderCount, totalBytes);
    }

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

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static double? GetNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number) return null;
        return property.TryGetDouble(out var value) ? value : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public sealed record HeaderCaptureResult(
    bool Ok,
    string Message,
    int AcceptedHeaderCount = 0,
    int DroppedHeaderCount = 0,
    int HeaderBytes = 0);
