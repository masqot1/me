using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record ProjectCatalogEntry(
    string ProjectId,
    string Name,
    string FullPath,
    string RelativePath,
    string TargetUrl,
    DateTimeOffset? StartedAtUtc,
    int EventCount,
    int BodyCount,
    bool OfflineReady,
    int MissingResources,
    double? CompletenessScore,
    double? WeightedCompletenessScore,
    double? VisualMismatchPercent,
    int SnapshotCount,
    bool ImportIntegrityVerified,
    bool VerificationPassed,
    string Status)
{
    public string CompletenessDisplay => CompletenessScore.HasValue ? $"{CompletenessScore.Value:0.##}%" : "—";
    public string VisualDisplay => VisualMismatchPercent.HasValue ? $"{VisualMismatchPercent.Value:0.####}%" : "—";
}

public sealed record ProjectCatalogResult(
    bool Ok,
    string Message,
    string WorkspaceRoot,
    string CatalogPath,
    IReadOnlyList<ProjectCatalogEntry> Projects,
    int ScannedDirectories,
    int SkippedReparsePoints,
    int SkippedDepthLimit);

public sealed class ProjectCatalogService
{
    public const int MaxScanDepth = 8;
    public const int MaxScanDirectories = 10_000;
    public const string CatalogDirectoryName = "_twc_catalog";

    public async Task<ProjectCatalogResult> RefreshAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            workspaceRoot = Path.GetFullPath(workspaceRoot);
            var catalogPath = Path.Combine(workspaceRoot, CatalogDirectoryName, "catalog.json");
            if (!Directory.Exists(workspaceRoot))
                return new(false, "Workspace folder does not exist.", workspaceRoot, catalogPath, [], 0, 0, 0);
            if (IsReparsePoint(new DirectoryInfo(workspaceRoot)))
                return new(false, "Workspace root cannot be a reparse point.", workspaceRoot, catalogPath, [], 0, 1, 0);

            var projects = new List<ProjectCatalogEntry>();
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((workspaceRoot, 0));
            var scanned = 0;
            var skippedReparse = 0;
            var skippedDepth = 0;

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = stack.Pop();
                if (++scanned > MaxScanDirectories)
                    return new(false, $"Workspace scan exceeded {MaxScanDirectories} directories.", workspaceRoot, catalogPath, projects, scanned, skippedReparse, skippedDepth);

                if (!IsInsideRoot(workspaceRoot, directory)) continue;
                var info = new DirectoryInfo(directory);
                if (IsReparsePoint(info)) { skippedReparse++; continue; }
                if (string.Equals(info.Name, CatalogDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;

                var sessionPath = Path.Combine(directory, "_network", "session.json");
                if (File.Exists(sessionPath))
                {
                    var project = await ReadProjectAsync(workspaceRoot, directory, sessionPath, cancellationToken);
                    if (project is not null) projects.Add(project);
                    continue;
                }

                if (depth >= MaxScanDepth)
                {
                    skippedDepth++;
                    continue;
                }

                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(); }
                catch { continue; }

                foreach (var child in children.Reverse())
                {
                    if (!IsInsideRoot(workspaceRoot, child)) continue;
                    try
                    {
                        var childInfo = new DirectoryInfo(child);
                        if (string.Equals(childInfo.Name, CatalogDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsReparsePoint(childInfo)) { skippedReparse++; continue; }
                        stack.Push((child, depth + 1));
                    }
                    catch { }
                }
            }

            projects = projects
                .OrderByDescending(project => project.StartedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(project => project.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            var persisted = new
            {
                version = "0.12.0",
                projectCount = projects.Count,
                projects = projects.Select(project => new
                {
                    project.ProjectId,
                    project.Name,
                    project.RelativePath,
                    project.TargetUrl,
                    project.StartedAtUtc,
                    project.EventCount,
                    project.BodyCount,
                    project.OfflineReady,
                    project.MissingResources,
                    project.CompletenessScore,
                    project.WeightedCompletenessScore,
                    project.VisualMismatchPercent,
                    project.SnapshotCount,
                    project.ImportIntegrityVerified,
                    project.VerificationPassed,
                    project.Status
                })
            };
            await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(persisted, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);

            return new(true, $"Indexed {projects.Count} project(s).", workspaceRoot, catalogPath, projects, scanned, skippedReparse, skippedDepth);
        }
        catch (Exception ex)
        {
            var root = Path.GetFullPath(workspaceRoot);
            return new(false, "Catalog refresh failed: " + ex.Message, root, Path.Combine(root, CatalogDirectoryName, "catalog.json"), [], 0, 0, 0);
        }
    }

    private static async Task<ProjectCatalogEntry?> ReadProjectAsync(
        string workspaceRoot,
        string projectRoot,
        string sessionPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var sessionDoc = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath, cancellationToken));
            var session = sessionDoc.RootElement;
            var targetUrl = GetString(session, "targetUrl") ?? string.Empty;
            var started = GetDate(session, "startedAtUtc") ?? GetDate(session, "startedAt");
            var relative = NormalizeRelative(Path.GetRelativePath(workspaceRoot, projectRoot));
            var name = new DirectoryInfo(projectRoot).Name;

            var summaryPath = Path.Combine(projectRoot, "_network", "summary.json");
            var eventCount = ReadInt(summaryPath, "eventCount");
            var bodyCount = ReadInt(summaryPath, "bodyCount");
            var offlineReady = File.Exists(Path.Combine(projectRoot, "offline", "offline-manifest.json")) &&
                               Directory.Exists(Path.Combine(projectRoot, "offline", "site"));
            var missingResources = ReadArrayCount(Path.Combine(projectRoot, "offline", "missing-resources.json"));
            var completeness = ReadDouble(Path.Combine(projectRoot, "offline", "completeness-report.json"), "completenessScore");
            var weighted = ReadDouble(Path.Combine(projectRoot, "offline", "completeness-report.json"), "weightedCompletenessScore");
            var visual = ReadDouble(Path.Combine(projectRoot, "offline", "visual-comparison", "visual-report.json"), "mismatchPercent");
            var snapshotCount = CountSnapshots(projectRoot);
            var imported = ReadString(Path.Combine(projectRoot, "_twc_package", "import-verification.json"), "result") == "PASS";
            var verificationPassed = ReadString(Path.Combine(projectRoot, "offline", "verification-report.json"), "result") == "PASS";
            var projectId = ReadString(Path.Combine(projectRoot, "_twc_package", "import-verification.json"), "contentRootSha256");
            if (string.IsNullOrWhiteSpace(projectId)) projectId = StableId(relative + "\n" + targetUrl);

            var status = DetermineStatus(bodyCount, offlineReady, missingResources, completeness, visual, verificationPassed);
            return new ProjectCatalogEntry(
                projectId!, name, projectRoot, relative, targetUrl, started, eventCount, bodyCount, offlineReady,
                missingResources, completeness, weighted, visual, snapshotCount, imported, verificationPassed, status);
        }
        catch
        {
            return null;
        }
    }

    private static string DetermineStatus(int bodyCount, bool offlineReady, int missing, double? completeness, double? visual, bool verificationPassed)
    {
        if (bodyCount <= 0) return "Metadata only";
        if (!offlineReady) return "Captured";
        if (missing > 0) return "Incomplete";
        if (completeness.HasValue && completeness.Value < 100) return "Incomplete";
        if (verificationPassed && completeness is >= 100 && visual.HasValue) return "Verified";
        if (completeness is >= 100) return "Complete";
        return "Offline ready";
    }

    private static int CountSnapshots(string projectRoot)
    {
        var history = Path.Combine(projectRoot, "history");
        if (!Directory.Exists(history)) return 0;
        try
        {
            var count = 0;
            foreach (var directory in Directory.EnumerateDirectories(history))
            {
                if (IsReparsePoint(new DirectoryInfo(directory))) continue;
                if (File.Exists(Path.Combine(directory, "snapshot.json"))) count++;
            }
            return count;
        }
        catch { return 0; }
    }

    private static bool IsInsideRoot(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullCandidate = Path.GetFullPath(candidate);
        if (string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;
    private static string NormalizeRelative(string path) => path.Replace('\\', '/');
    private static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var result) ? result : null;

    private static int ReadInt(string path, string property)
    {
        if (!File.Exists(path)) return 0;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0; }
        catch { return 0; }
    }

    private static double? ReadDouble(string path, string property)
    {
        if (!File.Exists(path)) return null;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : null; }
        catch { return null; }
    }

    private static string? ReadString(string path, string property)
    {
        if (!File.Exists(path)) return null;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return GetString(doc.RootElement, property); }
        catch { return null; }
    }

    private static int ReadArrayCount(string path)
    {
        if (!File.Exists(path)) return 0;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0; }
        catch { return 0; }
    }

    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
