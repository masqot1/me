using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record ReleaseReadinessStage(
    string Code,
    string Status,
    string Message,
    string EvidencePath,
    string? EvidenceSha256,
    string RecommendedAction);

public sealed record ReleaseReadinessResult(
    bool Ok,
    string Result,
    string NextAction,
    string ReportPath,
    string ReleaseFingerprintSha256,
    int PassCount,
    int FailCount,
    int NotApplicableCount,
    IReadOnlyList<ReleaseReadinessStage> Stages);

public sealed class ReleaseReadinessService
{
    public const string ReportFileName = "release-readiness.json";

    public async Task<ReleaseReadinessResult> ValidateAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var reportPath = Path.Combine(projectRoot, ProjectDiagnosticsService.DiagnosticsDirectoryName, ReportFileName);
        var stages = new List<ReleaseReadinessStage>();

        if (!Directory.Exists(projectRoot))
        {
            Add(stages, "PROJECT_ROOT", "FAIL", "Project folder does not exist.", ".", null, "Choose an existing project folder.");
            return await WriteAsync(reportPath, stages, cancellationToken);
        }

        var diagnostics = await new ProjectDiagnosticsService().RunAsync(projectRoot, cancellationToken);
        var healthRelative = $"{ProjectDiagnosticsService.DiagnosticsDirectoryName}/{ProjectDiagnosticsService.DiagnosticsFileName}";
        var healthPath = Path.Combine(projectRoot, healthRelative.Replace('/', Path.DirectorySeparatorChar));
        var diagnosticsPass = diagnostics.OverallStatus == "PASS" && diagnostics.Readiness == "READY";
        Add(stages, "DIAGNOSTICS_HEALTH", diagnosticsPass ? "PASS" : "FAIL",
            diagnosticsPass ? "Project diagnostics report is PASS / READY." : $"Project diagnostics is {diagnostics.OverallStatus} / {diagnostics.Readiness}.",
            healthRelative, await HashIfExistsAsync(healthPath, cancellationToken), diagnosticsPass ? "No action required." : diagnostics.NextAction);

        AddJsonStage(stages, projectRoot, "CAPTURE_SUMMARY", "_network/summary.json",
            root => GetInt(root, "eventCount") > 0 && GetInt(root, "bodyCount") > 0,
            root => $"Capture summary has {GetInt(root, "eventCount")} event(s) and {GetInt(root, "bodyCount")} response body file(s).",
            "Run a complete response-body capture and stop it cleanly.");

        var offlineManifest = Path.Combine(projectRoot, "offline", "offline-manifest.json");
        var offlineReady = File.Exists(offlineManifest) && Directory.Exists(Path.Combine(projectRoot, "offline", "site"));
        Add(stages, "OFFLINE_BUILD", offlineReady ? "PASS" : "FAIL",
            offlineReady ? "Offline build manifest and site tree are present." : "Offline build evidence is missing.",
            "offline/offline-manifest.json", await HashIfExistsAsync(offlineManifest, cancellationToken),
            offlineReady ? "No action required." : "Run Build Offline Site.");

        var missingPath = Path.Combine(projectRoot, "offline", "missing-resources.json");
        var missingCount = ReadArrayCount(missingPath);
        var missingPass = File.Exists(missingPath) && missingCount == 0;
        Add(stages, "MISSING_RESOURCES", missingPass ? "PASS" : "FAIL",
            File.Exists(missingPath) ? $"Missing-resource report contains {missingCount} unresolved resource(s)." : "Missing-resource report is absent.",
            "offline/missing-resources.json", await HashIfExistsAsync(missingPath, cancellationToken),
            missingPass ? "No action required." : "Resolve missing resources and rebuild the offline site.");

        AddJsonStage(stages, projectRoot, "COMPLETENESS", "offline/completeness-report.json",
            root => string.Equals(GetString(root, "result"), "PASS", StringComparison.OrdinalIgnoreCase)
                    && GetDouble(root, "completenessScore") is >= 100
                    && GetDouble(root, "weightedCompletenessScore") is >= 100,
            root => $"Completeness is {Display(GetDouble(root, "completenessScore"))} raw / {Display(GetDouble(root, "weightedCompletenessScore"))} weighted.",
            "Resolve dependency-graph gaps until raw and weighted completeness reach 100%.");

        var graphPath = Path.Combine(projectRoot, "offline", "dependency-graph.json");
        Add(stages, "DEPENDENCY_GRAPH", File.Exists(graphPath) ? "PASS" : "FAIL",
            File.Exists(graphPath) ? "Dependency graph evidence is present." : "Dependency graph evidence is missing.",
            "offline/dependency-graph.json", await HashIfExistsAsync(graphPath, cancellationToken),
            File.Exists(graphPath) ? "No action required." : "Run dependency graph/completeness analysis.");

        AddJsonStage(stages, projectRoot, "OFFLINE_VERIFICATION", "offline/verification-report.json",
            root => string.Equals(GetString(root, "result"), "PASS", StringComparison.OrdinalIgnoreCase)
                    && GetInt(root, "unexpectedDivergences") == 0,
            root => $"Offline verification reports {GetInt(root, "unexpectedDivergences")} unexpected divergence(s).",
            "Fix unexpected source-vs-replay divergences and re-run verification.");

        AddJsonStage(stages, projectRoot, "VISUAL_COMPARISON", "offline/visual-comparison/visual-report.json",
            root => string.Equals(GetString(root, "result"), "PASS", StringComparison.OrdinalIgnoreCase)
                    && GetDouble(root, "mismatchPercent") is { } mismatch
                    && mismatch <= (GetDouble(root, "maxMismatchPercent") ?? 0.15),
            root => $"Visual mismatch is {Display(GetDouble(root, "mismatchPercent"))}; limit is {Display(GetDouble(root, "maxMismatchPercent") ?? 0.15)}.",
            "Review source/offline/diff screenshots and fix render differences.");

        var snapshotFiles = EnumerateSnapshotFiles(projectRoot).ToArray();
        var snapshotHash = snapshotFiles.Length == 0 ? null : ComputeAggregateHash(snapshotFiles.Select(path =>
            (NormalizeRelative(Path.GetRelativePath(projectRoot, path)), HashFile(path))));
        Add(stages, "SNAPSHOT_HISTORY", snapshotFiles.Length > 0 ? "PASS" : "FAIL",
            snapshotFiles.Length > 0 ? $"{snapshotFiles.Length} immutable snapshot(s) are available." : "No immutable snapshot exists.",
            "history", snapshotHash, snapshotFiles.Length > 0 ? "No action required." : "Create a baseline snapshot before release.");

        var packageDir = Path.Combine(projectRoot, "_twc_package");
        var importVerify = Path.Combine(packageDir, "import-verification.json");
        if (!Directory.Exists(packageDir))
        {
            Add(stages, "IMPORT_INTEGRITY", "NOT_APPLICABLE", "Project was created locally and has no portable-import requirement.",
                "_twc_package/import-verification.json", null, "No action required.");
        }
        else
        {
            var pass = File.Exists(importVerify) && string.Equals(ReadString(importVerify, "result"), "PASS", StringComparison.OrdinalIgnoreCase);
            Add(stages, "IMPORT_INTEGRITY", pass ? "PASS" : "FAIL",
                pass ? "Portable import integrity is verified." : "Portable import metadata exists without a PASS verification result.",
                "_twc_package/import-verification.json", await HashIfExistsAsync(importVerify, cancellationToken),
                pass ? "No action required." : "Re-import from a package that passes V0.11 integrity verification.");
        }

        return await WriteAsync(reportPath, stages, cancellationToken);
    }

    private static void AddJsonStage(List<ReleaseReadinessStage> stages, string projectRoot, string code, string relativePath,
        Func<JsonElement, bool> predicate, Func<JsonElement, string> message, string failAction)
    {
        var fullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            Add(stages, code, "FAIL", $"Required evidence is missing: {relativePath}.", relativePath, null, failAction);
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            var pass = predicate(doc.RootElement);
            Add(stages, code, pass ? "PASS" : "FAIL", message(doc.RootElement), relativePath, HashFile(fullPath), pass ? "No action required." : failAction);
        }
        catch (Exception ex)
        {
            Add(stages, code, "FAIL", "Evidence could not be parsed: " + ex.Message, relativePath, HashFile(fullPath), failAction);
        }
    }

    private static async Task<ReleaseReadinessResult> WriteAsync(string reportPath, List<ReleaseReadinessStage> stages, CancellationToken cancellationToken)
    {
        var ordered = stages.OrderBy(stage => stage.Code, StringComparer.Ordinal).ToArray();
        var fail = ordered.Count(stage => stage.Status == "FAIL");
        var pass = ordered.Count(stage => stage.Status == "PASS");
        var na = ordered.Count(stage => stage.Status == "NOT_APPLICABLE");
        var result = fail == 0 ? "READY" : "BLOCKED";
        var nextAction = stages.FirstOrDefault(stage => stage.Status == "FAIL")?.RecommendedAction ?? "No action required.";
        var fingerprint = ComputeFingerprint(ordered);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new { version = "0.14.0", result, releaseFingerprintSha256 = fingerprint, nextAction,
            counts = new { pass, fail, notApplicable = na }, stages = ordered };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
        return new(true, result, nextAction, reportPath, fingerprint, pass, fail, na, ordered);
    }

    private static string ComputeFingerprint(IEnumerable<ReleaseReadinessStage> stages)
    {
        var builder = new StringBuilder();
        foreach (var stage in stages.OrderBy(stage => stage.Code, StringComparer.Ordinal))
            builder.Append(stage.Code).Append('|').Append(stage.Status).Append('|').Append(stage.EvidencePath).Append('|').Append(stage.EvidenceSha256 ?? "-").Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateSnapshotFiles(string projectRoot)
    {
        var history = Path.Combine(projectRoot, "history");
        if (!Directory.Exists(history)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(history).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var snapshot = Path.Combine(directory, "snapshot.json");
            if (File.Exists(snapshot)) yield return snapshot;
        }
    }

    private static string ComputeAggregateHash(IEnumerable<(string Path, string Hash)> files)
    {
        var builder = new StringBuilder();
        foreach (var item in files.OrderBy(item => item.Path, StringComparer.Ordinal)) builder.Append(item.Path).Append('|').Append(item.Hash).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Add(List<ReleaseReadinessStage> stages, string code, string status, string message, string evidencePath, string? evidenceHash, string action) =>
        stages.Add(new(code, status, message, NormalizeRelative(evidencePath), evidenceHash, action));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static async Task<string?> HashIfExistsAsync(string path, CancellationToken cancellationToken) { if (!File.Exists(path)) return null; await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true); return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant(); }
    private static int ReadArrayCount(string path) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0; } catch { return -1; } }
    private static string? ReadString(string path, string property) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return GetString(doc.RootElement, property); } catch { return null; } }
    private static int GetInt(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static double? GetDouble(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : null;
    private static string? GetString(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Display(double? value) => value.HasValue ? $"{value.Value:0.####}%" : "not available";
    private static string NormalizeRelative(string value) => value.Replace('\\', '/');
    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
