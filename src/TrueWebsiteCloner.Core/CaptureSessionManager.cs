using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrueWebsiteCloner.Core;

public sealed record CaptureResult(bool Ok, string Message, string? SessionPath = null, int EventCount = 0);

public sealed class CaptureSessionManager
{
    public const int MaxBodyBytes = 512 * 1024;
    public const int MaxWebSocketFrameBytes = 64 * 1024;
    public const int MaxRequestPayloadBytes = 64 * 1024;

    private const string RedactedValue = "[REDACTED]";

    private sealed class Session
    {
        public required int TabId { get; init; }
        public required string Root { get; init; }
        public required string NetworkLog { get; init; }
        public required string BodiesDirectory { get; init; }
        public required string BodiesLog { get; init; }
        public required string RequestPayloadDirectory { get; init; }
        public required string RequestPayloadLog { get; init; }
        public required Uri TargetOrigin { get; init; }
        public required DateTimeOffset StartedAtUtc { get; init; }
        public int EventCount;
        public int BodyCount;
        public long BodyBytes;
        public int WebSocketEventCount;
        public int WebSocketFrameCount;
        public long WebSocketFrameBytes;
        public int RequestPayloadCount;
        public long RequestPayloadBytes;
        public int RequestPayloadMetadataOnlyCount;
        public int RequestPayloadRedactedFieldCount;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private static readonly IReadOnlyDictionary<string, string[]> AllowedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["capture.start"] = ["tabId", "targetUrl", "title", "startedAt"],
        ["capture.request"] = ["tabId", "requestId", "loaderId", "url", "method", "resourceType", "documentUrl", "timestamp", "wallTime"],
        ["capture.request.payload"] = ["tabId", "requestId", "url", "method", "resourceType", "contentType", "payloadCaptured", "byteLength", "body", "reason", "timestamp"],
        ["capture.response"] = ["tabId", "requestId", "url", "status", "statusText", "mimeType", "resourceType", "protocol", "fromDiskCache", "fromServiceWorker", "encodedDataLength", "timing", "timestamp"],
        ["capture.finished"] = ["tabId", "requestId", "encodedDataLength", "timestamp"],
        ["capture.failed"] = ["tabId", "requestId", "errorText", "canceled", "blockedReason", "resourceType", "timestamp"],
        ["capture.body"] = ["tabId", "requestId", "url", "mimeType", "resourceType", "status", "base64Encoded", "byteLength"],
        ["capture.websocket.created"] = ["tabId", "requestId", "url", "initiatorType"],
        ["capture.websocket.handshake"] = ["tabId", "requestId", "url", "status", "statusText", "timestamp"],
        ["capture.websocket.frame"] = ["tabId", "requestId", "url", "direction", "opcode", "mask", "payloadCaptured", "byteLength", "payloadData", "reason", "timestamp"],
        ["capture.websocket.error"] = ["tabId", "requestId", "url", "errorMessage", "timestamp"],
        ["capture.websocket.closed"] = ["tabId", "requestId", "url", "timestamp"],
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

    private static readonly HashSet<string> RequestPayloadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    private static readonly HashSet<string> RequestPayloadResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "XHR", "Fetch"
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
        if (type == "capture.request.payload") return await SaveRequestPayloadAsync(session, payload, cancellationToken);
        if (type.StartsWith("capture.websocket.", StringComparison.Ordinal)) return await SaveWebSocketEventAsync(session, type, payload, cancellationToken);

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
        var requestPayloadDir = Path.Combine(root, "_requests");
        Directory.CreateDirectory(networkDir);
        Directory.CreateDirectory(bodiesDir);
        Directory.CreateDirectory(requestPayloadDir);

        var session = new Session
        {
            TabId = tabId,
            Root = root,
            NetworkLog = Path.Combine(networkDir, "network.jsonl"),
            BodiesDirectory = bodiesDir,
            BodiesLog = Path.Combine(bodiesDir, "bodies.jsonl"),
            RequestPayloadDirectory = requestPayloadDir,
            RequestPayloadLog = Path.Combine(requestPayloadDir, "request-payloads.jsonl"),
            TargetOrigin = targetOrigin,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        if (!_sessions.TryAdd(tabId, session)) return new(false, "Unable to create capture session.");

        var sessionInfo = new
        {
            version = "0.5.0-dev",
            mode = "same-origin-response-bodies+same-origin-websocket-frames+redacted-request-payloads",
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
            },
            webSocketPolicy = new
            {
                sameOriginOnly = true,
                maxFrameBytes = MaxWebSocketFrameBytes,
                handshakeHeadersSaved = false,
                cookiesSaved = false,
                authorizationHeadersSaved = false
            },
            requestPayloadPolicy = new
            {
                sameOriginOnly = true,
                resourceTypes = RequestPayloadResourceTypes.OrderBy(value => value).ToArray(),
                methods = RequestPayloadMethods.OrderBy(value => value).ToArray(),
                allowedContentTypes = new[] { "application/json", "application/*+json", "application/x-www-form-urlencoded" },
                maxPayloadBytes = MaxRequestPayloadBytes,
                sensitiveFieldsRedacted = true,
                unsupportedPayloadsMetadataOnly = true,
                rawHeadersSaved = false,
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

    private async Task<CaptureResult> SaveRequestPayloadAsync(Session session, JsonElement payload, CancellationToken cancellationToken)
    {
        var requestId = GetString(payload, "requestId");
        var url = GetString(payload, "url");
        var method = (GetString(payload, "method") ?? string.Empty).ToUpperInvariant();
        var resourceType = GetString(payload, "resourceType") ?? string.Empty;
        var contentType = NormalizeMime(GetString(payload, "contentType"));
        var payloadCaptured = GetBool(payload, "payloadCaptured");
        var declaredBytes = TryGetInt(payload, "byteLength", out var parsedBytes) ? parsedBytes : 0;

        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(url))
            return new(false, "Request payload event is incomplete.", session.Root, session.EventCount);

        if (!IsSameOrigin(session.TargetOrigin, url))
            return new(false, "Cross-origin request payload rejected by Gate 1.2 policy.", session.Root, session.EventCount);

        if (!RequestPayloadResourceTypes.Contains(resourceType))
            return new(false, $"Request payload resource type is not enabled: {resourceType}", session.Root, session.EventCount);

        if (!RequestPayloadMethods.Contains(method))
            return new(false, $"Request payload method is not enabled: {method}", session.Root, session.EventCount);

        if (!payloadCaptured)
        {
            await session.WriteLock.WaitAsync(cancellationToken);
            try
            {
                var metadataLine = JsonSerializer.Serialize(new
                {
                    eventType = "capture.request.payload",
                    receivedAtUtc = DateTimeOffset.UtcNow,
                    data = new
                    {
                        requestId,
                        url,
                        method,
                        resourceType,
                        contentType,
                        payloadCaptured = false,
                        byteLength = declaredBytes,
                        reason = GetString(payload, "reason") ?? "metadata-only",
                        timestamp = GetNumber(payload, "timestamp")
                    }
                });
                await File.AppendAllTextAsync(session.NetworkLog, metadataLine + Environment.NewLine, cancellationToken);
                session.EventCount++;
                session.RequestPayloadMetadataOnlyCount++;
                return new(true, "Request payload metadata saved without body content.", session.Root, session.EventCount);
            }
            finally
            {
                session.WriteLock.Release();
            }
        }

        if (!IsAllowedRequestPayloadContentType(contentType))
            return new(false, $"Request payload content type is not enabled: {contentType}", session.Root, session.EventCount);

        var body = GetString(payload, "body");
        if (body is null)
            return new(false, "Captured request payload is missing body content.", session.Root, session.EventCount);

        var originalBytes = Encoding.UTF8.GetByteCount(body);
        if (originalBytes > MaxRequestPayloadBytes)
            return new(false, $"Request payload exceeds {MaxRequestPayloadBytes} byte limit.", session.Root, session.EventCount);

        if (payload.TryGetProperty("byteLength", out _) && declaredBytes != originalBytes)
            return new(false, "Request payload byteLength does not match body content.", session.Root, session.EventCount);

        if (!TryRedactRequestPayload(contentType, body, out var sanitizedBody, out var redactedFields, out var redactionError))
            return new(false, redactionError ?? "Request payload could not be sanitized.", session.Root, session.EventCount);

        var storedBytes = Encoding.UTF8.GetByteCount(sanitizedBody);
        if (storedBytes > MaxRequestPayloadBytes)
            return new(false, $"Sanitized request payload exceeds {MaxRequestPayloadBytes} byte limit.", session.Root, session.EventCount);

        await session.WriteLock.WaitAsync(cancellationToken);
        try
        {
            var next = session.RequestPayloadCount + 1;
            var extension = contentType == "application/x-www-form-urlencoded" ? ".form" : ".json";
            var fileName = $"{next:D4}-{SafeToken(requestId)}{extension}";
            var filePath = Path.Combine(session.RequestPayloadDirectory, fileName);
            await File.WriteAllTextAsync(filePath, sanitizedBody, Encoding.UTF8, cancellationToken);

            var relativePath = $"_requests/{fileName}";
            var manifestLine = JsonSerializer.Serialize(new
            {
                requestId,
                url,
                method,
                resourceType,
                contentType,
                originalByteLength = originalBytes,
                storedByteLength = storedBytes,
                redactedFieldCount = redactedFields,
                file = relativePath,
                capturedAtUtc = DateTimeOffset.UtcNow
            });
            await File.AppendAllTextAsync(session.RequestPayloadLog, manifestLine + Environment.NewLine, cancellationToken);

            var networkLine = JsonSerializer.Serialize(new
            {
                eventType = "capture.request.payload",
                receivedAtUtc = DateTimeOffset.UtcNow,
                data = new
                {
                    requestId,
                    url,
                    method,
                    resourceType,
                    contentType,
                    payloadCaptured = true,
                    originalByteLength = originalBytes,
                    storedByteLength = storedBytes,
                    redactedFieldCount = redactedFields,
                    file = relativePath,
                    timestamp = GetNumber(payload, "timestamp")
                }
            });
            await File.AppendAllTextAsync(session.NetworkLog, networkLine + Environment.NewLine, cancellationToken);

            session.EventCount++;
            session.RequestPayloadCount++;
            session.RequestPayloadBytes += storedBytes;
            session.RequestPayloadRedactedFieldCount += redactedFields;
            return new(true, "Redacted request payload saved.", session.Root, session.EventCount);
        }
        finally
        {
            session.WriteLock.Release();
        }
    }

    private async Task<CaptureResult> SaveWebSocketEventAsync(Session session, string type, JsonElement payload, CancellationToken cancellationToken)
    {
        var requestId = GetString(payload, "requestId");
        var url = GetString(payload, "url");
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(url))
            return new(false, "WebSocket event is incomplete.", session.Root, session.EventCount);

        if (!IsSameWebSocketOrigin(session.TargetOrigin, url))
            return new(false, "Cross-origin WebSocket event rejected by Gate 1.1 policy.", session.Root, session.EventCount);

        var sanitized = Sanitize(type, payload);
        var isFrame = type == "capture.websocket.frame";
        var frameBytes = 0;
        if (isFrame)
        {
            var payloadCaptured = GetBool(payload, "payloadCaptured");
            if (!payloadCaptured)
            {
                sanitized.Remove("payloadData");
            }
            else
            {
                var payloadData = GetString(payload, "payloadData");
                if (payloadData is null)
                    return new(false, "Captured WebSocket frame is missing payloadData.", session.Root, session.EventCount);

                frameBytes = Encoding.UTF8.GetByteCount(payloadData);
                if (frameBytes > MaxWebSocketFrameBytes)
                    return new(false, $"WebSocket frame exceeds {MaxWebSocketFrameBytes} byte limit.", session.Root, session.EventCount);

                if (TryGetInt(payload, "byteLength", out var declaredFrameBytes) && declaredFrameBytes != frameBytes)
                    return new(false, "WebSocket frame byteLength does not match payloadData.", session.Root, session.EventCount);
            }
        }

        await session.WriteLock.WaitAsync(cancellationToken);
        try
        {
            var line = JsonSerializer.Serialize(new { eventType = type, receivedAtUtc = DateTimeOffset.UtcNow, data = sanitized });
            await File.AppendAllTextAsync(session.NetworkLog, line + Environment.NewLine, cancellationToken);
            session.EventCount++;
            session.WebSocketEventCount++;
            if (isFrame)
            {
                session.WebSocketFrameCount++;
                if (frameBytes > 0) session.WebSocketFrameBytes += frameBytes;
            }
            return new(true, "WebSocket event saved.", session.Root, session.EventCount);
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
            version = "0.5.0-dev",
            tabId,
            startedAtUtc = session.StartedAtUtc,
            stoppedAtUtc = DateTimeOffset.UtcNow,
            eventCount = session.EventCount,
            bodyCount = session.BodyCount,
            bodyBytes = session.BodyBytes,
            maxBodyBytes = MaxBodyBytes,
            webSocketEventCount = session.WebSocketEventCount,
            webSocketFrameCount = session.WebSocketFrameCount,
            webSocketFrameBytes = session.WebSocketFrameBytes,
            maxWebSocketFrameBytes = MaxWebSocketFrameBytes,
            requestPayloadCount = session.RequestPayloadCount,
            requestPayloadBytes = session.RequestPayloadBytes,
            requestPayloadMetadataOnlyCount = session.RequestPayloadMetadataOnlyCount,
            requestPayloadRedactedFieldCount = session.RequestPayloadRedactedFieldCount,
            maxRequestPayloadBytes = MaxRequestPayloadBytes,
            reason = GetString(payload, "reason") ?? "user",
            mode = "same-origin-response-bodies+same-origin-websocket-frames+redacted-request-payloads"
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

    private static bool TryRedactRequestPayload(string contentType, string body, out string sanitizedBody, out int redactedFields, out string? error)
    {
        sanitizedBody = string.Empty;
        redactedFields = 0;
        error = null;

        if (contentType == "application/x-www-form-urlencoded")
        {
            var parts = body.Split('&');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var separator = part.IndexOf('=');
                var rawKey = separator >= 0 ? part[..separator] : part;
                var decodedKey = DecodeFormComponent(rawKey);
                if (!IsSensitiveFieldName(decodedKey)) continue;
                parts[i] = rawKey + "=%5BREDACTED%5D";
                redactedFields++;
            }
            sanitizedBody = string.Join('&', parts);
            return true;
        }

        if (IsJsonRequestPayloadContentType(contentType))
        {
            try
            {
                var node = JsonNode.Parse(body);
                if (node is null)
                {
                    sanitizedBody = "null";
                    return true;
                }
                RedactJsonNode(node, ref redactedFields);
                sanitizedBody = node.ToJsonString();
                return true;
            }
            catch (JsonException)
            {
                error = "JSON request payload is invalid and was not persisted.";
                return false;
            }
        }

        error = $"Request payload content type is not enabled: {contentType}";
        return false;
    }

    private static void RedactJsonNode(JsonNode node, ref int redactedFields)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
            {
                if (IsSensitiveFieldName(key))
                {
                    obj[key] = JsonValue.Create(RedactedValue);
                    redactedFields++;
                    continue;
                }

                if (obj[key] is { } child) RedactJsonNode(child, ref redactedFields);
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null) RedactJsonNode(child, ref redactedFields);
            }
        }
    }

    private static bool IsSensitiveFieldName(string name)
    {
        var canonical = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (string.IsNullOrWhiteSpace(canonical)) return false;
        return canonical.Contains("password", StringComparison.Ordinal)
               || canonical.Contains("passwd", StringComparison.Ordinal)
               || canonical.Contains("passcode", StringComparison.Ordinal)
               || canonical == "pwd"
               || canonical.Contains("accesstoken", StringComparison.Ordinal)
               || canonical.Contains("refreshtoken", StringComparison.Ordinal)
               || canonical.Contains("idtoken", StringComparison.Ordinal)
               || canonical.EndsWith("token", StringComparison.Ordinal)
               || canonical.Contains("apikey", StringComparison.Ordinal)
               || canonical.Contains("clientsecret", StringComparison.Ordinal)
               || canonical.EndsWith("secret", StringComparison.Ordinal)
               || canonical is "auth" or "authorization" or "session" or "sessionid" or "cookie";
    }

    private static string DecodeFormComponent(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
        catch (UriFormatException) { return value; }
    }

    private static bool IsAllowedRequestPayloadContentType(string contentType) =>
        contentType == "application/x-www-form-urlencoded" || IsJsonRequestPayloadContentType(contentType);

    private static bool IsJsonRequestPayloadContentType(string contentType) =>
        contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase)
        || (contentType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

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

    private static double? GetNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number) return null;
        return property.TryGetDouble(out var value) ? value : null;
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

    private static bool IsSameWebSocketOrigin(Uri origin, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate)) return false;
        if (candidate.Scheme is not ("ws" or "wss")) return false;
        var expectedScheme = origin.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        return string.Equals(expectedScheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
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
