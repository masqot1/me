using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record RecoveryResult(
    bool Ok,
    bool Complete,
    string Message,
    string ReportPath,
    int InitialMissing,
    int Attempted,
    int Recovered,
    int Skipped,
    int Failed,
    int FinalMissing);

public sealed class MissingResourceRecovery
{
    public const int MaxRecoveryItems = 16;
    public const int MaxRecoveredBodyBytes = 512 * 1024;

    private static readonly string[] SensitiveQueryNames =
    [
        "token", "access_token", "auth", "authorization", "password", "passwd",
        "session", "sessionid", "jwt", "signature", "sig", "api_key", "apikey", "key"
    ];

    public async Task<RecoveryResult> RecoverAsync(string captureRoot, CancellationToken cancellationToken = default)
    {
        captureRoot = Path.GetFullPath(captureRoot);
        var sessionPath = Path.Combine(captureRoot, "_network", "session.json");
        var bodiesPath = Path.Combine(captureRoot, "_bodies", "bodies.jsonl");
        var missingPath = Path.Combine(captureRoot, "offline", "missing-resources.json");
        var reportPath = Path.Combine(captureRoot, "offline", "recovery-report.json");

        if (!File.Exists(sessionPath) || !File.Exists(bodiesPath) || !File.Exists(missingPath))
            return new(false, false, "Capture must have session.json, bodies.jsonl and a V0.4 missing-resources.json report.", reportPath, 0, 0, 0, 0, 0, 0);

        using var sessionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath, cancellationToken));
        var targetUrl = sessionDoc.RootElement.GetProperty("targetUrl").GetString() ?? string.Empty;
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var targetUri) || !IsLoopbackHttp(targetUri))
            return new(false, false, "V0.7 recovery is restricted to loopback Test Lab captures.", reportPath, 0, 0, 0, 0, 0, 0);

        var missing = ReadMissing(missingPath);
        var initialMissing = missing.Count;
        if (initialMissing == 0)
        {
            await WriteReportAsync(reportPath, targetUri, initialMissing, 0, 0, 0, 0, 0, Array.Empty<object>(), cancellationToken);
            return new(true, true, "No missing resources require recovery.", reportPath, 0, 0, 0, 0, 0, 0);
        }

        var existingUrls = ReadExistingBodyUrls(bodiesPath);
        var attempted = 0;
        var recovered = 0;
        var skipped = 0;
        var failed = 0;
        var details = new List<object>();

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

        foreach (var item in missing.DistinctBy(item => item.ResolvedUrl, StringComparer.OrdinalIgnoreCase).Take(MaxRecoveryItems))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(item.ResolvedUrl, UriKind.Absolute, out var uri) || !SameOrigin(targetUri, uri) || !IsLoopbackHttp(uri))
            {
                skipped++;
                details.Add(new { url = item.ResolvedUrl, result = "skipped", reason = "not_same_loopback_origin" });
                continue;
            }

            if (HasSensitiveQuery(uri))
            {
                skipped++;
                details.Add(new { url = item.ResolvedUrl, result = "skipped", reason = "sensitive_query_parameter" });
                continue;
            }

            if (existingUrls.Contains(NormalizeUrl(uri.AbsoluteUri)))
            {
                skipped++;
                details.Add(new { url = item.ResolvedUrl, result = "skipped", reason = "already_captured" });
                continue;
            }

            attempted++;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("TrueWebsiteCloner-Recovery/0.7");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failed++;
                    details.Add(new { url = item.ResolvedUrl, result = "failed", reason = "http_status", status = (int)response.StatusCode });
                    continue;
                }

                if (response.Content.Headers.ContentLength is > MaxRecoveredBodyBytes)
                {
                    failed++;
                    details.Add(new { url = item.ResolvedUrl, result = "failed", reason = "body_too_large_header" });
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length > MaxRecoveredBodyBytes)
                {
                    failed++;
                    details.Add(new { url = item.ResolvedUrl, result = "failed", reason = "body_too_large" });
                    continue;
                }

                var mime = NormalizeMime(response.Content.Headers.ContentType?.MediaType);
                if (!AllowedMime(mime))
                {
                    failed++;
                    details.Add(new { url = item.ResolvedUrl, result = "failed", reason = "mime_not_allowed", mimeType = mime });
                    continue;
                }

                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
                var fileName = $"recovery-{recovered + 1:D4}-{hash[..10]}{ExtensionForMime(mime)}";
                var bodyFile = Path.Combine(captureRoot, "_bodies", fileName);
                await File.WriteAllBytesAsync(bodyFile, bytes, cancellationToken);

                var relativeFile = $"_bodies/{fileName}";
                var logLine = JsonSerializer.Serialize(new
                {
                    requestId = "recovery-" + hash[..12],
                    url = uri.AbsoluteUri,
                    mimeType = mime,
                    resourceType = ResourceTypeForMime(mime),
                    status = 200,
                    base64Encoded = false,
                    byteLength = bytes.Length,
                    file = relativeFile,
                    recovered = true,
                    recoveryVersion = "0.7.0"
                });
                await File.AppendAllTextAsync(bodiesPath, logLine + Environment.NewLine, cancellationToken);
                existingUrls.Add(NormalizeUrl(uri.AbsoluteUri));
                recovered++;
                details.Add(new { url = item.ResolvedUrl, result = "recovered", mimeType = mime, byteLength = bytes.Length, file = relativeFile });
            }
            catch (Exception ex)
            {
                failed++;
                details.Add(new { url = item.ResolvedUrl, result = "failed", reason = "request_error", message = ex.Message });
            }
        }

        var build = await new OfflineSiteBuilder().BuildAsync(captureRoot, cancellationToken);
        if (!build.Ok)
            return new(false, false, "Recovery finished but offline rebuild failed: " + build.Message, reportPath, initialMissing, attempted, recovered, skipped, failed, initialMissing);

        var finalMissing = ReadMissing(Path.Combine(captureRoot, "offline", "missing-resources.json")).Count;
        var complete = finalMissing == 0;
        await WriteReportAsync(reportPath, targetUri, initialMissing, attempted, recovered, skipped, failed, finalMissing, details, cancellationToken);

        return new(true, complete,
            complete ? "Missing-resource recovery completed." : "Recovery completed with unresolved resources.",
            reportPath, initialMissing, attempted, recovered, skipped, failed, finalMissing);
    }

    private static List<MissingItem> ReadMissing(string path)
    {
        if (!File.Exists(path)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        return doc.RootElement.EnumerateArray()
            .Select(element => new MissingItem(
                GetString(element, "sourceUrl") ?? string.Empty,
                GetString(element, "reference") ?? string.Empty,
                GetString(element, "resolvedUrl") ?? string.Empty))
            .Where(item => !string.IsNullOrWhiteSpace(item.ResolvedUrl))
            .ToList();
    }

    private static HashSet<string> ReadExistingBodyUrls(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var url = GetString(doc.RootElement, "url");
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) set.Add(NormalizeUrl(uri.AbsoluteUri));
            }
            catch { }
        }
        return set;
    }

    private static async Task WriteReportAsync(
        string reportPath, Uri targetUri, int initial, int attempted, int recovered, int skipped, int failed, int final,
        IEnumerable<object> details, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new
        {
            version = "0.7.0",
            result = final == 0 ? "PASS" : "PARTIAL",
            targetOrigin = targetUri.GetLeftPart(UriPartial.Authority),
            initialMissing = initial,
            attempted,
            recovered,
            skipped,
            failed,
            finalMissing = final,
            recoveryCoveragePercent = initial == 0 ? 100 : Math.Round((initial - final) * 100.0 / initial, 2),
            policy = new
            {
                loopbackOnly = true,
                sameOriginOnly = true,
                maxItems = MaxRecoveryItems,
                maxBodyBytes = MaxRecoveredBodyBytes,
                cookiesSent = false,
                authorizationSent = false,
                redirectsFollowed = false,
                sensitiveQueryParametersSkipped = true
            },
            details
        };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
    }

    private static bool IsLoopbackHttp(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

    private static bool HasSensitiveQuery(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query)) return false;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
            if (SensitiveQueryNames.Any(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private static bool AllowedMime(string mime) =>
        mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mime is "application/json" or "application/ld+json" or "application/javascript" or "application/x-javascript" or
            "image/svg+xml" or "image/png" or "image/jpeg" or "image/webp" or "image/gif";

    private static string ResourceTypeForMime(string mime) => mime switch
    {
        "text/html" => "Document",
        "text/css" => "Stylesheet",
        "text/javascript" or "application/javascript" or "application/x-javascript" => "Script",
        "image/svg+xml" or "image/png" or "image/jpeg" or "image/webp" or "image/gif" => "Image",
        _ => "Fetch"
    };

    private static string ExtensionForMime(string mime) => mime switch
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

    private static string NormalizeMime(string? mime) => (mime ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
    private static string NormalizeUrl(string url) { var builder = new UriBuilder(new Uri(url)) { Fragment = string.Empty }; return builder.Uri.AbsoluteUri; }
    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private sealed record MissingItem(string SourceUrl, string Reference, string ResolvedUrl);
    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
