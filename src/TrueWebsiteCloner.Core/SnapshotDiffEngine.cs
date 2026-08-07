using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record SnapshotCreateResult(bool Ok, string Message, string? SnapshotPath = null, string? SnapshotId = null);
public sealed record SnapshotDiffResult(bool Ok, string Message, string? ReportPath = null, int Added = 0, int Removed = 0, int Changed = 0, int Unchanged = 0);

public sealed class SnapshotDiffEngine
{
    private sealed record ResourceState(
        string Url,
        string MimeType,
        string ResourceType,
        string Sha256,
        long ByteLength,
        bool Recovered,
        string? LocalPath);

    private sealed record SnapshotData(
        string Label,
        string SnapshotId,
        string TargetUrl,
        double? CompletenessScore,
        double? WeightedCompletenessScore,
        double? VisualMismatchPercent,
        Dictionary<string, ResourceState> Resources);

    public async Task<SnapshotCreateResult> CreateSnapshotAsync(string captureRoot, string label, CancellationToken cancellationToken = default)
    {
        captureRoot = Path.GetFullPath(captureRoot);
        var safeLabel = SafeLabel(label);
        if (string.IsNullOrWhiteSpace(safeLabel)) return new(false, "Snapshot label is invalid.");

        var sessionPath = Path.Combine(captureRoot, "_network", "session.json");
        var bodiesPath = Path.Combine(captureRoot, "_bodies", "bodies.jsonl");
        var manifestPath = Path.Combine(captureRoot, "offline", "offline-manifest.json");
        if (!File.Exists(sessionPath) || !File.Exists(bodiesPath) || !File.Exists(manifestPath))
            return new(false, "Snapshot requires session.json, bodies.jsonl and offline-manifest.json.");

        var snapshotDir = Path.Combine(captureRoot, "history", safeLabel);
        var snapshotPath = Path.Combine(snapshotDir, "snapshot.json");
        if (Directory.Exists(snapshotDir) || File.Exists(snapshotPath))
            return new(false, "Snapshot label already exists; history is immutable.");

        using var sessionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath, cancellationToken));
        var targetUrl = GetString(sessionDoc.RootElement, "targetUrl") ?? string.Empty;
        var localPaths = ReadLocalPaths(manifestPath);
        var resources = new Dictionary<string, ResourceState>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in await File.ReadAllLinesAsync(bodiesPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var url = GetString(root, "url") ?? string.Empty;
                var file = GetString(root, "file") ?? string.Empty;
                if (!Uri.TryCreate(url, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(file)) continue;
                var bodyPath = SafeCapturePath(captureRoot, file);
                if (bodyPath is null || !File.Exists(bodyPath)) continue;

                var bytes = await File.ReadAllBytesAsync(bodyPath, cancellationToken);
                var key = NormalizeUrl(url);
                localPaths.TryGetValue(key, out var localPath);
                resources[key] = new ResourceState(
                    url,
                    NormalizeMime(GetString(root, "mimeType")),
                    GetString(root, "resourceType") ?? string.Empty,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    bytes.LongLength,
                    root.TryGetProperty("recovered", out var recovered) && recovered.ValueKind == JsonValueKind.True,
                    localPath);
            }
            catch { }
        }

        var completeness = ReadMetric(Path.Combine(captureRoot, "offline", "completeness-report.json"), "completenessScore");
        var weighted = ReadMetric(Path.Combine(captureRoot, "offline", "completeness-report.json"), "weightedCompletenessScore");
        var visual = ReadMetric(Path.Combine(captureRoot, "offline", "visual-comparison", "visual-report.json"), "mismatchPercent");
        var snapshotId = ComputeSnapshotId(targetUrl, resources.Values, completeness, weighted, visual);

        Directory.CreateDirectory(snapshotDir);
        var snapshot = new
        {
            version = "0.10.0",
            label = safeLabel,
            snapshotId,
            createdAtUtc = DateTimeOffset.UtcNow,
            targetUrl,
            resourceCount = resources.Count,
            completenessScore = completeness,
            weightedCompletenessScore = weighted,
            visualMismatchPercent = visual,
            resources = resources.Values.OrderBy(resource => resource.Url, StringComparer.OrdinalIgnoreCase)
        };
        await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
        return new(true, "Immutable snapshot created.", snapshotPath, snapshotId);
    }

    public async Task<SnapshotDiffResult> CompareAsync(string beforeSnapshotPath, string afterSnapshotPath, string outputPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(beforeSnapshotPath) || !File.Exists(afterSnapshotPath)) return new(false, "Both snapshot files are required.");
        var before = await ReadSnapshotAsync(beforeSnapshotPath, cancellationToken);
        var after = await ReadSnapshotAsync(afterSnapshotPath, cancellationToken);
        if (before is null || after is null) return new(false, "Snapshot file is invalid.");

        var beforeKeys = before.Resources.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterKeys = after.Resources.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedKeys = afterKeys.Except(beforeKeys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var removedKeys = beforeKeys.Except(afterKeys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var commonKeys = beforeKeys.Intersect(afterKeys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        var changed = new List<object>();
        var unchanged = new List<string>();
        foreach (var key in commonKeys)
        {
            var left = before.Resources[key];
            var right = after.Resources[key];
            if (SameResource(left, right))
            {
                unchanged.Add(right.Url);
                continue;
            }
            changed.Add(new
            {
                url = right.Url,
                before = left,
                after = right,
                hashChanged = !string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase),
                mimeChanged = !string.Equals(left.MimeType, right.MimeType, StringComparison.OrdinalIgnoreCase),
                recoveryStateChanged = left.Recovered != right.Recovered,
                localPathChanged = !string.Equals(left.LocalPath, right.LocalPath, StringComparison.OrdinalIgnoreCase)
            });
        }

        var report = new
        {
            version = "0.10.0",
            result = addedKeys.Length == 0 && removedKeys.Length == 0 && changed.Count == 0 ? "IDENTICAL" : "CHANGED",
            before = new { before.Label, before.SnapshotId, before.TargetUrl },
            after = new { after.Label, after.SnapshotId, after.TargetUrl },
            counts = new
            {
                added = addedKeys.Length,
                removed = removedKeys.Length,
                changed = changed.Count,
                unchanged = unchanged.Count
            },
            metrics = new
            {
                completenessBefore = before.CompletenessScore,
                completenessAfter = after.CompletenessScore,
                completenessDelta = Delta(before.CompletenessScore, after.CompletenessScore),
                weightedCompletenessBefore = before.WeightedCompletenessScore,
                weightedCompletenessAfter = after.WeightedCompletenessScore,
                weightedCompletenessDelta = Delta(before.WeightedCompletenessScore, after.WeightedCompletenessScore),
                visualMismatchBefore = before.VisualMismatchPercent,
                visualMismatchAfter = after.VisualMismatchPercent,
                visualMismatchDelta = Delta(before.VisualMismatchPercent, after.VisualMismatchPercent)
            },
            added = addedKeys.Select(key => after.Resources[key]),
            removed = removedKeys.Select(key => before.Resources[key]),
            changed,
            unchanged
        };

        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
        return new(true, "Snapshot diff created without modifying either snapshot.", outputPath, addedKeys.Length, removedKeys.Length, changed.Count, unchanged.Count);
    }

    private static async Task<SnapshotData?> ReadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            var root = doc.RootElement;
            var resources = new Dictionary<string, ResourceState>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in root.GetProperty("resources").EnumerateArray())
            {
                var url = GetString(item, "url") ?? string.Empty;
                if (!Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
                resources[NormalizeUrl(url)] = new ResourceState(
                    url,
                    GetString(item, "mimeType") ?? string.Empty,
                    GetString(item, "resourceType") ?? string.Empty,
                    GetString(item, "sha256") ?? string.Empty,
                    item.TryGetProperty("byteLength", out var length) && length.TryGetInt64(out var n) ? n : 0,
                    item.TryGetProperty("recovered", out var recovered) && recovered.ValueKind == JsonValueKind.True,
                    GetString(item, "localPath"));
            }
            return new SnapshotData(
                GetString(root, "label") ?? string.Empty,
                GetString(root, "snapshotId") ?? string.Empty,
                GetString(root, "targetUrl") ?? string.Empty,
                GetDouble(root, "completenessScore"),
                GetDouble(root, "weightedCompletenessScore"),
                GetDouble(root, "visualMismatchPercent"),
                resources);
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ReadLocalPaths(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.GetProperty("mappings").EnumerateArray())
        {
            var url = GetString(item, "url");
            var local = GetString(item, "localPath");
            if (Uri.TryCreate(url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(local)) result[NormalizeUrl(url!)] = local!;
        }
        return result;
    }

    private static string ComputeSnapshotId(string targetUrl, IEnumerable<ResourceState> resources, double? completeness, double? weighted, double? visual)
    {
        var sb = new StringBuilder(targetUrl).Append('\n');
        foreach (var resource in resources.OrderBy(resource => resource.Url, StringComparer.OrdinalIgnoreCase))
            sb.Append(NormalizeUrl(resource.Url)).Append('|').Append(resource.Sha256).Append('|').Append(resource.MimeType).Append('|').Append(resource.ResourceType).Append('|').Append(resource.Recovered).Append('|').Append(resource.LocalPath).Append('\n');
        sb.Append("c=").Append(completeness).Append("|w=").Append(weighted).Append("|v=").Append(visual);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static bool SameResource(ResourceState left, ResourceState right) =>
        string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.MimeType, right.MimeType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ResourceType, right.ResourceType, StringComparison.OrdinalIgnoreCase) &&
        left.ByteLength == right.ByteLength && left.Recovered == right.Recovered &&
        string.Equals(left.LocalPath, right.LocalPath, StringComparison.OrdinalIgnoreCase);

    private static double? ReadMetric(string path, string property)
    {
        if (!File.Exists(path)) return null;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return GetDouble(doc.RootElement, property); }
        catch { return null; }
    }

    private static double? GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    private static double? Delta(double? before, double? after) => before.HasValue && after.HasValue ? Math.Round(after.Value - before.Value, 6) : null;
    private static string SafeLabel(string value) => new(value.Trim().Take(80).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
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
