using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrueWebsiteCloner.Core;

public sealed record DependencyGraphResult(
    bool Ok,
    bool Complete,
    string Message,
    string GraphPath,
    string CompletenessPath,
    string DotPath,
    int NodeCount,
    int EdgeCount,
    int MissingDependencies,
    double CompletenessScore,
    double WeightedCompletenessScore);

public sealed class DependencyGraphBuilder
{
    private sealed record BodyEntry(string Url, string MimeType, string ResourceType, string File, bool Recovered);
    private sealed record ManifestEntry(string Url, string MimeType, string ResourceType, string LocalPath);
    private sealed record GraphNode(string Url, string MimeType, string ResourceType, string Status, bool Recovered, string? LocalPath);
    private sealed record GraphEdge(string SourceUrl, string TargetUrl, string Kind, string Scope, bool Resolved, int Weight);
    private sealed record Reference(string Value, string Kind);

    private static readonly Regex HtmlReferenceRegex = new(
        "\\b(?<attr>src|href|poster)\\s*=\\s*(?<quote>[\\\"'])(?<url>[^\\\"']+)(?:\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssUrlRegex = new(
        "url\\(\\s*(?<quote>[\\\"']?)(?<url>[^)\\\"']+)(?:\\k<quote>)\\s*\\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssImportRegex = new(
        "@import\\s+(?<quote>[\\\"'])(?<url>[^\\\"']+)(?:\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsFetchRegex = new(
        "\\bfetch\\s*\\(\\s*(?<quote>[\\\"'])(?<url>[^\\\"']+)(?:\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsDynamicImportRegex = new(
        "\\bimport\\s*\\(\\s*(?<quote>[\\\"'])(?<url>[^\\\"']+)(?:\\k<quote>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<DependencyGraphResult> BuildAsync(string captureRoot, CancellationToken cancellationToken = default)
    {
        captureRoot = Path.GetFullPath(captureRoot);
        var sessionPath = Path.Combine(captureRoot, "_network", "session.json");
        var bodiesPath = Path.Combine(captureRoot, "_bodies", "bodies.jsonl");
        var manifestPath = Path.Combine(captureRoot, "offline", "offline-manifest.json");
        var graphPath = Path.Combine(captureRoot, "offline", "dependency-graph.json");
        var completenessPath = Path.Combine(captureRoot, "offline", "completeness-report.json");
        var dotPath = Path.Combine(captureRoot, "offline", "dependency-graph.dot");

        if (!File.Exists(sessionPath) || !File.Exists(bodiesPath) || !File.Exists(manifestPath))
            return new(false, false, "Capture must be built through V0.4 before dependency analysis.", graphPath, completenessPath, dotPath, 0, 0, 0, 0, 0);

        using var sessionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath, cancellationToken));
        var targetUrl = sessionDoc.RootElement.GetProperty("targetUrl").GetString() ?? string.Empty;
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var targetUri))
            return new(false, false, "Invalid targetUrl in session.json.", graphPath, completenessPath, dotPath, 0, 0, 0, 0, 0);

        var bodies = ReadBodies(bodiesPath);
        var bodyByUrl = bodies
            .GroupBy(body => NormalizeUrl(body.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var manifest = ReadManifest(manifestPath);
        var manifestByUrl = manifest.ToDictionary(entry => NormalizeUrl(entry.Url), StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, GraphEdge>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in manifest)
        {
            bodyByUrl.TryGetValue(NormalizeUrl(entry.Url), out var body);
            nodes[NormalizeUrl(entry.Url)] = new GraphNode(
                entry.Url, entry.MimeType, entry.ResourceType, "captured", body?.Recovered == true, entry.LocalPath);
        }

        foreach (var body in bodyByUrl.Values.OrderBy(body => body.Url, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = SafeCapturePath(captureRoot, body.File);
            if (sourcePath is null || !File.Exists(sourcePath)) continue;
            if (!IsTextAnalyzable(body.MimeType)) continue;

            var text = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            foreach (var reference in ExtractReferences(text, body.MimeType))
            {
                if (!TryResolve(body.Url, reference.Value, out var resolved)) continue;
                var targetKey = NormalizeUrl(resolved.AbsoluteUri);
                var sameOrigin = SameOrigin(targetUri, resolved);
                var scope = sameOrigin ? "same-origin" : "external";
                var isCaptured = manifestByUrl.TryGetValue(targetKey, out var targetEntry);
                var resourceType = isCaptured ? targetEntry!.ResourceType : InferResourceType(resolved, reference.Kind);
                var mimeType = isCaptured ? targetEntry!.MimeType : string.Empty;
                var recovered = isCaptured && bodyByUrl.TryGetValue(targetKey, out var targetBody) && targetBody.Recovered;

                if (!nodes.ContainsKey(targetKey))
                {
                    nodes[targetKey] = new GraphNode(
                        resolved.AbsoluteUri,
                        mimeType,
                        resourceType,
                        sameOrigin ? (isCaptured ? "captured" : "missing") : "external",
                        recovered,
                        isCaptured ? targetEntry!.LocalPath : null);
                }

                var weight = DependencyWeight(resourceType);
                var edge = new GraphEdge(body.Url, resolved.AbsoluteUri, reference.Kind, scope, !sameOrigin || isCaptured, weight);
                var edgeKey = $"{NormalizeUrl(body.Url)}\n{targetKey}\n{reference.Kind}";
                edges.TryAdd(edgeKey, edge);
            }
        }

        var edgeList = edges.Values
            .OrderBy(edge => edge.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.TargetUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nodeList = nodes.Values.OrderBy(node => node.Url, StringComparer.OrdinalIgnoreCase).ToArray();
        var sameOriginEdges = edgeList.Where(edge => edge.Scope == "same-origin").ToArray();
        var resolvedEdges = sameOriginEdges.Count(edge => edge.Resolved);
        var missingEdges = sameOriginEdges.Count(edge => !edge.Resolved);
        var totalWeight = sameOriginEdges.Sum(edge => edge.Weight);
        var resolvedWeight = sameOriginEdges.Where(edge => edge.Resolved).Sum(edge => edge.Weight);
        var score = sameOriginEdges.Length == 0 ? 100.0 : Math.Round(resolvedEdges * 100.0 / sameOriginEdges.Length, 2);
        var weightedScore = totalWeight == 0 ? 100.0 : Math.Round(resolvedWeight * 100.0 / totalWeight, 2);
        var complete = missingEdges == 0;
        var recoveredCount = nodeList.Count(node => node.Recovered);
        var externalCount = edgeList.Count(edge => edge.Scope == "external");

        Directory.CreateDirectory(Path.Combine(captureRoot, "offline"));
        var graph = new
        {
            version = "0.8.0",
            rootUrl = targetUri.AbsoluteUri,
            nodeCount = nodeList.Length,
            edgeCount = edgeList.Length,
            nodes = nodeList,
            edges = edgeList
        };
        await File.WriteAllTextAsync(graphPath, JsonSerializer.Serialize(graph, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);

        var report = new
        {
            version = "0.8.0",
            result = complete ? "PASS" : "INCOMPLETE",
            rootUrl = targetUri.AbsoluteUri,
            capturedResources = nodeList.Count(node => node.Status == "captured"),
            recoveredResources = recoveredCount,
            externalDependencies = externalCount,
            sameOriginDependencies = sameOriginEdges.Length,
            resolvedDependencies = resolvedEdges,
            missingDependencies = missingEdges,
            completenessScore = score,
            weightedCompletenessScore = weightedScore,
            weights = new { document = 5, stylesheet = 4, script = 4, fetch = 3, font = 2, image = 1, other = 1 },
            criticalMissing = edgeList
                .Where(edge => edge.Scope == "same-origin" && !edge.Resolved && edge.Weight >= 3)
                .Select(edge => new { edge.SourceUrl, edge.TargetUrl, edge.Kind, edge.Weight })
        };
        await File.WriteAllTextAsync(completenessPath, JsonSerializer.Serialize(report, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(dotPath, BuildDot(nodeList, edgeList), new UTF8Encoding(false), cancellationToken);

        return new(true, complete,
            complete ? "Dependency graph is complete for all discovered same-origin references." : "Dependency graph contains unresolved same-origin references.",
            graphPath, completenessPath, dotPath, nodeList.Length, edgeList.Length, missingEdges, score, weightedScore);
    }

    private static List<BodyEntry> ReadBodies(string path)
    {
        var result = new List<BodyEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var url = GetString(root, "url") ?? string.Empty;
                var file = GetString(root, "file") ?? string.Empty;
                if (!Uri.TryCreate(url, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(file)) continue;
                result.Add(new BodyEntry(
                    url,
                    NormalizeMime(GetString(root, "mimeType")),
                    GetString(root, "resourceType") ?? string.Empty,
                    file,
                    root.TryGetProperty("recovered", out var recovered) && recovered.ValueKind == JsonValueKind.True));
            }
            catch { }
        }
        return result;
    }

    private static List<ManifestEntry> ReadManifest(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("mappings").EnumerateArray()
            .Select(root => new ManifestEntry(
                GetString(root, "url") ?? string.Empty,
                NormalizeMime(GetString(root, "mimeType")),
                GetString(root, "resourceType") ?? string.Empty,
                GetString(root, "localPath") ?? string.Empty))
            .Where(entry => Uri.TryCreate(entry.Url, UriKind.Absolute, out _))
            .ToList();
    }

    private static IEnumerable<Reference> ExtractReferences(string text, string mime)
    {
        if (mime == "text/html")
        {
            foreach (Match match in HtmlReferenceRegex.Matches(text))
                yield return new Reference(match.Groups["url"].Value, "html-" + match.Groups["attr"].Value.ToLowerInvariant());
        }
        else if (mime == "text/css")
        {
            foreach (Match match in CssUrlRegex.Matches(text)) yield return new Reference(match.Groups["url"].Value.Trim(), "css-url");
            foreach (Match match in CssImportRegex.Matches(text)) yield return new Reference(match.Groups["url"].Value, "css-import");
        }
        else if (IsJavaScript(mime))
        {
            foreach (Match match in JsFetchRegex.Matches(text)) yield return new Reference(match.Groups["url"].Value, "js-fetch");
            foreach (Match match in JsDynamicImportRegex.Matches(text)) yield return new Reference(match.Groups["url"].Value, "js-import");
        }
    }

    private static bool TryResolve(string sourceUrl, string reference, out Uri resolved)
    {
        resolved = null!;
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('#') ||
            reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(new Uri(sourceUrl), reference, out resolved)) return false;
        return resolved.Scheme is "http" or "https";
    }

    private static int DependencyWeight(string resourceType) => resourceType.ToLowerInvariant() switch
    {
        "document" => 5,
        "stylesheet" => 4,
        "script" => 4,
        "fetch" or "xhr" => 3,
        "font" => 2,
        "image" => 1,
        _ => 1
    };

    private static string InferResourceType(Uri uri, string kind)
    {
        var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (kind is "js-fetch") return "Fetch";
        if (kind is "js-import") return "Script";
        return ext switch
        {
            ".html" or ".htm" => "Document",
            ".css" => "Stylesheet",
            ".js" or ".mjs" => "Script",
            ".json" => "Fetch",
            ".woff" or ".woff2" or ".ttf" or ".otf" => "Font",
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg" => "Image",
            _ => "Other"
        };
    }

    private static string BuildDot(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
    {
        var sb = new StringBuilder("digraph TrueWebsiteCloner {\n  rankdir=LR;\n");
        foreach (var node in nodes)
            sb.Append("  \"").Append(EscapeDot(node.Url)).Append("\" [label=\"").Append(EscapeDot(node.ResourceType + "\\n" + node.Url)).Append("\"];\n");
        foreach (var edge in edges)
            sb.Append("  \"").Append(EscapeDot(edge.SourceUrl)).Append("\" -> \"").Append(EscapeDot(edge.TargetUrl)).Append("\" [label=\"").Append(EscapeDot(edge.Kind)).Append("\"];\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string EscapeDot(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static bool IsTextAnalyzable(string mime) => mime is "text/html" or "text/css" || IsJavaScript(mime);
    private static bool IsJavaScript(string mime) => mime is "text/javascript" or "application/javascript" or "application/x-javascript";
    private static bool SameOrigin(Uri a, Uri b) => string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
    private static string NormalizeMime(string? mime) => (mime ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
    private static string NormalizeUrl(string url) { var builder = new UriBuilder(new Uri(url)) { Fragment = string.Empty }; return builder.Uri.AbsoluteUri; }
    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static string? SafeCapturePath(string captureRoot, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(captureRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = captureRoot.EndsWith(Path.DirectorySeparatorChar) ? captureRoot : captureRoot + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
