using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrueWebsiteCloner.Core;

public sealed record OfflineBuildResult(
    bool Ok,
    string Message,
    string? OutputRoot = null,
    int ResourceCount = 0,
    int RewrittenReferences = 0,
    int MissingReferences = 0);

public sealed class OfflineSiteBuilder
{
    private sealed record BodyEntry(string RequestId, string Url, string MimeType, string ResourceType, string File);
    private sealed record ResourceMap(string Url, string MimeType, string ResourceType, string SourceFile, string LocalPath);
    private sealed record MissingResource(string SourceUrl, string Reference, string ResolvedUrl);
    private sealed class RewriteCounter { public int Value; }

    private static readonly Regex HtmlReferenceRegex = new(
        "(?<prefix>\\b(?:src|href|poster)\\s*=\\s*)(?<quote>[\\\"'])(?<url>[^\\\"']+)(?:\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssUrlRegex = new(
        "url\\(\\s*(?<quote>[\\\"']?)(?<url>[^)\\\"']+)(?:\\k<quote>)\\s*\\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssImportRegex = new(
        "(?<prefix>@import\\s+)(?<quote>[\\\"'])(?<url>[^\\\"']+)(?:\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<OfflineBuildResult> BuildAsync(string captureRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            return new(false, "Capture root is required.");

        captureRoot = Path.GetFullPath(captureRoot);
        var sessionPath = Path.Combine(captureRoot, "_network", "session.json");
        var bodiesLogPath = Path.Combine(captureRoot, "_bodies", "bodies.jsonl");
        if (!File.Exists(sessionPath)) return new(false, "_network/session.json was not found.");
        if (!File.Exists(bodiesLogPath)) return new(false, "_bodies/bodies.jsonl was not found.");

        using var sessionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath, cancellationToken));
        if (!sessionDoc.RootElement.TryGetProperty("targetUrl", out var targetUrlElement) ||
            targetUrlElement.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(targetUrlElement.GetString(), UriKind.Absolute, out var targetUri) ||
            targetUri.Scheme is not ("http" or "https"))
            return new(false, "session.json does not contain a valid HTTP/HTTPS targetUrl.");

        var entries = new List<BodyEntry>();
        foreach (var line in await File.ReadAllLinesAsync(bodiesLogPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var entry = new BodyEntry(
                GetString(root, "requestId") ?? "request",
                GetString(root, "url") ?? string.Empty,
                NormalizeMime(GetString(root, "mimeType")),
                GetString(root, "resourceType") ?? string.Empty,
                GetString(root, "file") ?? string.Empty);
            if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(entry.File))
                entries.Add(entry);
        }

        if (entries.Count == 0) return new(false, "No captured response bodies are available to build.");

        var offlineRoot = Path.Combine(captureRoot, "offline");
        var siteRoot = Path.Combine(offlineRoot, "site");
        if (Directory.Exists(offlineRoot)) Directory.Delete(offlineRoot, true);
        Directory.CreateDirectory(siteRoot);

        var mappings = BuildMappings(captureRoot, entries);
        var mapByUrl = mappings.ToDictionary(m => NormalizeUrl(m.Url), StringComparer.OrdinalIgnoreCase);
        var missing = new List<MissingResource>();
        var rewriteCounter = new RewriteCounter();

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = SafeCapturePath(captureRoot, mapping.SourceFile);
            if (sourcePath is null || !File.Exists(sourcePath))
                return new(false, $"Captured body file is missing or invalid: {mapping.SourceFile}");

            var destinationPath = Path.Combine(siteRoot, mapping.LocalPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (IsHtml(mapping.MimeType) || IsCss(mapping.MimeType))
            {
                var text = await File.ReadAllTextAsync(sourcePath, cancellationToken);
                if (IsHtml(mapping.MimeType))
                    text = RewriteHtml(text, mapping, targetUri, mapByUrl, missing, rewriteCounter);
                else
                    text = RewriteCss(text, mapping, targetUri, mapByUrl, missing, rewriteCounter);
                await File.WriteAllTextAsync(destinationPath, text, new UTF8Encoding(false), cancellationToken);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, true);
            }
        }

        var uniqueMissing = missing
            .DistinctBy(m => $"{m.SourceUrl}\n{m.ResolvedUrl}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.ResolvedUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifest = new
        {
            version = "0.4.0",
            mode = "offline-resource-path-builder",
            targetUrl = targetUri.AbsoluteUri,
            targetOrigin = targetUri.GetLeftPart(UriPartial.Authority),
            resourceCount = mappings.Count,
            rewrittenReferenceCount = rewriteCounter.Value,
            missingReferenceCount = uniqueMissing.Length,
            htmlCssRewriting = true,
            javascriptRewriting = false,
            apiReplay = false,
            mappings = mappings
                .OrderBy(m => m.Url, StringComparer.OrdinalIgnoreCase)
                .Select(m => new { m.Url, m.MimeType, m.ResourceType, m.LocalPath })
        };
        await File.WriteAllTextAsync(
            Path.Combine(offlineRoot, "offline-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptionsIndented),
            new UTF8Encoding(false), cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(offlineRoot, "missing-resources.json"),
            JsonSerializer.Serialize(uniqueMissing, JsonOptionsIndented),
            new UTF8Encoding(false), cancellationToken);

        return new(true, "Offline resource tree built.", offlineRoot, mappings.Count, rewriteCounter.Value, uniqueMissing.Length);
    }

    private static List<ResourceMap> BuildMappings(string captureRoot, IReadOnlyList<BodyEntry> entries)
    {
        var mappings = new List<ResourceMap>();
        var usedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var normalizedUrl = NormalizeUrl(entry.Url);
            if (!seenUrls.Add(normalizedUrl)) continue;

            var uri = new Uri(entry.Url);
            var localPath = UrlToLocalPath(uri, entry.MimeType, entry.ResourceType);
            if (usedPaths.TryGetValue(localPath, out var existingUrl) && !string.Equals(existingUrl, entry.Url, StringComparison.OrdinalIgnoreCase))
                localPath = AddHash(localPath, uri.AbsoluteUri);
            usedPaths[localPath] = entry.Url;

            var safeSource = SafeCapturePath(captureRoot, entry.File);
            var sourceRelative = safeSource is null
                ? entry.File
                : Path.GetRelativePath(captureRoot, safeSource).Replace('\\', '/');
            mappings.Add(new ResourceMap(entry.Url, entry.MimeType, entry.ResourceType, sourceRelative, localPath));
        }

        return mappings;
    }

    private static string RewriteHtml(
        string text,
        ResourceMap source,
        Uri targetUri,
        IReadOnlyDictionary<string, ResourceMap> mapByUrl,
        List<MissingResource> missing,
        RewriteCounter counter)
    {
        return HtmlReferenceRegex.Replace(text, match =>
        {
            var original = match.Groups["url"].Value;
            var replacement = ResolveReference(source, original, targetUri, mapByUrl, missing);
            if (replacement is null || replacement == original) return match.Value;
            counter.Value++;
            return match.Groups["prefix"].Value + match.Groups["quote"].Value + replacement + match.Groups["quote"].Value;
        });
    }

    private static string RewriteCss(
        string text,
        ResourceMap source,
        Uri targetUri,
        IReadOnlyDictionary<string, ResourceMap> mapByUrl,
        List<MissingResource> missing,
        RewriteCounter counter)
    {
        text = CssUrlRegex.Replace(text, match =>
        {
            var original = match.Groups["url"].Value.Trim();
            var replacement = ResolveReference(source, original, targetUri, mapByUrl, missing);
            if (replacement is null || replacement == original) return match.Value;
            counter.Value++;
            var quote = match.Groups["quote"].Value;
            return $"url({quote}{replacement}{quote})";
        });

        return CssImportRegex.Replace(text, match =>
        {
            var original = match.Groups["url"].Value;
            var replacement = ResolveReference(source, original, targetUri, mapByUrl, missing);
            if (replacement is null || replacement == original) return match.Value;
            counter.Value++;
            return match.Groups["prefix"].Value + match.Groups["quote"].Value + replacement + match.Groups["quote"].Value;
        });
    }

    private static string? ResolveReference(
        ResourceMap source,
        string reference,
        Uri targetUri,
        IReadOnlyDictionary<string, ResourceMap> mapByUrl,
        List<MissingResource> missing)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('#') ||
            reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!Uri.TryCreate(new Uri(source.Url), reference, out var resolved) || resolved.Scheme is not ("http" or "https"))
            return null;

        var key = NormalizeUrl(resolved.AbsoluteUri);
        if (mapByUrl.TryGetValue(key, out var mapped))
        {
            var sourceDirectory = Path.GetDirectoryName(source.LocalPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
            var relative = Path.GetRelativePath(
                    string.IsNullOrEmpty(sourceDirectory) ? "." : sourceDirectory,
                    mapped.LocalPath.Replace('/', Path.DirectorySeparatorChar))
                .Replace('\\', '/');
            return relative;
        }

        if (SameOrigin(targetUri, resolved))
            missing.Add(new MissingResource(source.Url, reference, resolved.AbsoluteUri));

        return null;
    }

    private static string UrlToLocalPath(Uri uri, string mimeType, string resourceType)
    {
        var rawPath = Uri.UnescapeDataString(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath == "/") return "index.html";

        var segments = rawPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SafeSegment).ToList();
        if (segments.Count == 0) return "index.html";

        var last = segments[^1];
        var extension = Path.GetExtension(last);
        if (string.IsNullOrEmpty(extension))
            segments[^1] = last + ExtensionForMime(mimeType, resourceType);

        var path = string.Join('/', segments);
        if (!string.IsNullOrEmpty(uri.Query)) path = AddHash(path, uri.Query);
        return path;
    }

    private static string AddHash(string localPath, string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..8];
        var extension = Path.GetExtension(localPath);
        return string.IsNullOrEmpty(extension)
            ? $"{localPath}-{hash}"
            : $"{localPath[..^extension.Length]}-{hash}{extension}";
    }

    private static string ExtensionForMime(string mimeType, string resourceType) => mimeType switch
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
        _ when string.Equals(resourceType, "Document", StringComparison.OrdinalIgnoreCase) => ".html",
        _ => ".txt"
    };

    private static string SafeSegment(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        var result = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "resource" : result;
    }

    private static string? SafeCapturePath(string captureRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var combined = Path.GetFullPath(Path.Combine(captureRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = captureRoot.EndsWith(Path.DirectorySeparatorChar) ? captureRoot : captureRoot + Path.DirectorySeparatorChar;
        return combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static bool IsHtml(string mime) => string.Equals(mime, "text/html", StringComparison.OrdinalIgnoreCase);
    private static bool IsCss(string mime) => string.Equals(mime, "text/css", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeMime(string? mime) => (mime ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    private static string NormalizeUrl(string url)
    {
        var uri = new Uri(url);
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
