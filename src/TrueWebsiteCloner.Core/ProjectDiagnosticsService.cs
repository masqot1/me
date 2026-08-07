using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record DiagnosticsCheck(
    string Code,
    string Status,
    string Message,
    string EvidencePath,
    string RecommendedAction);

public sealed record ProjectDiagnosticsResult(
    bool Ok,
    string OverallStatus,
    string Readiness,
    string NextAction,
    string ReportPath,
    int PassCount,
    int WarningCount,
    int FailCount,
    int NotRunCount,
    IReadOnlyList<DiagnosticsCheck> Checks);

public sealed class ProjectDiagnosticsService
{
    public const string DiagnosticsDirectoryName = "_diagnostics";
    public const string DiagnosticsFileName = "project-health.json";

    public async Task<ProjectDiagnosticsResult> RunAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var reportPath = Path.Combine(projectRoot, DiagnosticsDirectoryName, DiagnosticsFileName);
        var checks = new List<DiagnosticsCheck>();

        if (!Directory.Exists(projectRoot))
            return new(false, "FAIL", "NOT_READY", "Choose an existing project folder.", reportPath, 0, 0, 1, 0,
                [new("PROJECT_ROOT", "FAIL", "Project folder does not exist.", ".", "Choose an existing project folder.")]);

        var sessionPath = Path.Combine(projectRoot, "_network", "session.json");
        if (!File.Exists(sessionPath))
        {
            Add(checks, "CAPTURE_SESSION", "FAIL", "Capture session metadata is missing.", "_network/session.json", "Run a new capture for this project.");
            return await WriteResultAsync(projectRoot, reportPath, checks, cancellationToken);
        }
        Add(checks, "CAPTURE_SESSION", "PASS", "Capture session metadata is present.", "_network/session.json", "No action required.");

        var summaryPath = Path.Combine(projectRoot, "_network", "summary.json");
        if (!File.Exists(summaryPath))
        {
            Add(checks, "CAPTURE_SUMMARY", "FAIL", "Capture has no completion summary.", "_network/summary.json", "Stop the capture cleanly and capture again if needed.");
        }
        else
        {
            var eventCount = ReadInt(summaryPath, "eventCount");
            var bodyCount = ReadInt(summaryPath, "bodyCount");
            Add(checks, "CAPTURE_SUMMARY", eventCount > 0 ? "PASS" : "FAIL",
                eventCount > 0 ? $"Capture completed with {eventCount} recorded event(s)." : "Capture summary contains no events.",
                "_network/summary.json", eventCount > 0 ? "No action required." : "Run the capture again and confirm network events are recorded.");
            Add(checks, "RESPONSE_BODIES", bodyCount > 0 ? "PASS" : "WARNING",
                bodyCount > 0 ? $"{bodyCount} response body file(s) were recorded." : "No response bodies were recorded.",
                "_network/summary.json", bodyCount > 0 ? "No action required." : "Run response-body capture before building an offline site.");
        }

        var offlineManifest = Path.Combine(projectRoot, "offline", "offline-manifest.json");
        var offlineSite = Path.Combine(projectRoot, "offline", "site");
        var offlineReady = File.Exists(offlineManifest) && Directory.Exists(offlineSite);
        Add(checks, "OFFLINE_BUILD", offlineReady ? "PASS" : "NOT_RUN",
            offlineReady ? "Offline resource tree is available." : "Offline resource tree has not been built.",
            "offline/offline-manifest.json", offlineReady ? "No action required." : "Run Build Offline Site.");

        var missingPath = Path.Combine(projectRoot, "offline", "missing-resources.json");
        if (!File.Exists(missingPath))
            Add(checks, "MISSING_RESOURCES", "NOT_RUN", "Missing-resource analysis has not run.", "offline/missing-resources.json", "Build the offline site to generate missing-resource analysis.");
        else
        {
            var count = ReadArrayCount(missingPath);
            Add(checks, "MISSING_RESOURCES", count == 0 ? "PASS" : "FAIL",
                count == 0 ? "No same-origin resources are reported missing." : $"{count} same-origin resource(s) are still missing.",
                "offline/missing-resources.json", count == 0 ? "No action required." : "Run missing-resource recovery and rebuild the offline site.");
        }

        var recoveryPath = Path.Combine(projectRoot, "offline", "recovery-report.json");
        if (!File.Exists(recoveryPath))
            Add(checks, "RECOVERY", "NOT_RUN", "No recovery report exists.", "offline/recovery-report.json", "Run recovery only if missing resources are reported.");
        else
        {
            var result = ReadString(recoveryPath, "result");
            var finalMissing = ReadInt(recoveryPath, "finalMissing");
            var pass = string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) && finalMissing == 0;
            Add(checks, "RECOVERY", pass ? "PASS" : "FAIL",
                pass ? "Recovery completed with zero unresolved resources." : $"Recovery is incomplete; {finalMissing} resource(s) remain unresolved.",
                "offline/recovery-report.json", pass ? "No action required." : "Review the recovery report and resolve remaining resources.");
        }

        var completenessPath = Path.Combine(projectRoot, "offline", "completeness-report.json");
        if (!File.Exists(completenessPath))
            Add(checks, "COMPLETENESS", "NOT_RUN", "Dependency completeness has not been calculated.", "offline/completeness-report.json", "Run dependency graph/completeness analysis.");
        else
        {
            var raw = ReadDouble(completenessPath, "completenessScore");
            var weighted = ReadDouble(completenessPath, "weightedCompletenessScore");
            var pass = raw is >= 100 && weighted is >= 100;
            Add(checks, "COMPLETENESS", pass ? "PASS" : "FAIL",
                pass ? "Raw and weighted completeness are 100%." : $"Completeness is {Display(raw)} raw / {Display(weighted)} weighted.",
                "offline/completeness-report.json", pass ? "No action required." : "Review the dependency graph and resolve missing dependencies.");
        }

        var graphPath = Path.Combine(projectRoot, "offline", "dependency-graph.json");
        if (File.Exists(graphPath))
            Add(checks, "DEPENDENCY_GRAPH", "PASS", "Dependency graph evidence is available.", "offline/dependency-graph.json", "No action required.");
        else
            Add(checks, "DEPENDENCY_GRAPH", File.Exists(completenessPath) ? "FAIL" : "NOT_RUN",
                "Dependency graph evidence is not available.", "offline/dependency-graph.json", "Run dependency graph/completeness analysis.");

        var verificationPath = Path.Combine(projectRoot, "offline", "verification-report.json");
        if (!File.Exists(verificationPath))
            Add(checks, "OFFLINE_VERIFICATION", "NOT_RUN", "Offline source-vs-replay verification has not run.", "offline/verification-report.json", "Run offline verification before considering the project verified.");
        else
        {
            var result = ReadString(verificationPath, "result");
            var divergences = ReadInt(verificationPath, "unexpectedDivergences");
            var pass = string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) && divergences == 0;
            Add(checks, "OFFLINE_VERIFICATION", pass ? "PASS" : "FAIL",
                pass ? "Offline verification passed with zero unexpected divergences." : $"Offline verification reports {divergences} unexpected divergence(s).",
                "offline/verification-report.json", pass ? "No action required." : "Open the verification report and fix unexpected route/content differences.");
        }

        var visualPath = Path.Combine(projectRoot, "offline", "visual-comparison", "visual-report.json");
        if (!File.Exists(visualPath))
            Add(checks, "VISUAL_COMPARISON", "NOT_RUN", "Visual comparison has not run.", "offline/visual-comparison/visual-report.json", "Run visual comparison after offline verification.");
        else
        {
            var result = ReadString(visualPath, "result");
            var mismatch = ReadDouble(visualPath, "mismatchPercent");
            var max = ReadDouble(visualPath, "maxMismatchPercent") ?? 0.15;
            var pass = string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) && mismatch.HasValue && mismatch.Value <= max;
            Add(checks, "VISUAL_COMPARISON", pass ? "PASS" : "FAIL",
                pass ? $"Visual mismatch {Display(mismatch)} is within the {max:0.####}% limit." : $"Visual mismatch {Display(mismatch)} exceeds or does not satisfy the {max:0.####}% limit.",
                "offline/visual-comparison/visual-report.json", pass ? "No action required." : "Review source/offline/diff screenshots and fix render differences.");
        }

        var snapshots = CountSnapshots(projectRoot);
        Add(checks, "SNAPSHOT_HISTORY", snapshots > 0 ? "PASS" : "WARNING",
            snapshots > 0 ? $"{snapshots} immutable snapshot(s) are available." : "No immutable snapshot has been created yet.",
            "history", snapshots > 0 ? "No action required." : "Create a baseline snapshot before future update comparisons.");

        var packageDir = Path.Combine(projectRoot, "_twc_package");
        var importVerification = Path.Combine(packageDir, "import-verification.json");
        if (!Directory.Exists(packageDir))
            Add(checks, "IMPORT_INTEGRITY", "NOT_RUN", "Project was not imported from a portable package.", "_twc_package/import-verification.json", "No action required for locally created projects.");
        else if (!File.Exists(importVerification))
            Add(checks, "IMPORT_INTEGRITY", "FAIL", "Portable-package metadata exists without import verification evidence.", "_twc_package/import-verification.json", "Re-import the project from a verified portable package.");
        else
        {
            var pass = string.Equals(ReadString(importVerification, "result"), "PASS", StringComparison.OrdinalIgnoreCase);
            Add(checks, "IMPORT_INTEGRITY", pass ? "PASS" : "FAIL",
                pass ? "Portable import integrity is verified." : "Portable import integrity is not verified.",
                "_twc_package/import-verification.json", pass ? "No action required." : "Re-import the project from a package that passes integrity verification.");
        }

        return await WriteResultAsync(projectRoot, reportPath, checks, cancellationToken);
    }

    private static async Task<ProjectDiagnosticsResult> WriteResultAsync(string projectRoot, string reportPath, List<DiagnosticsCheck> checks, CancellationToken cancellationToken)
    {
        var pass = checks.Count(check => check.Status == "PASS");
        var warning = checks.Count(check => check.Status == "WARNING");
        var fail = checks.Count(check => check.Status == "FAIL");
        var notRun = checks.Count(check => check.Status == "NOT_RUN");
        var overall = fail > 0 ? "FAIL" : warning > 0 ? "WARNING" : "PASS";
        var readiness = overall == "PASS" ? "READY" : overall == "WARNING" ? "NEEDS_REVIEW" : "NOT_READY";
        var actionable = checks.FirstOrDefault(check => check.Status == "FAIL")
                         ?? checks.FirstOrDefault(check => check.Status == "WARNING")
                         ?? checks.FirstOrDefault(check => check.Status == "NOT_RUN" && check.RecommendedAction != "No action required for locally created projects.");
        var nextAction = actionable?.RecommendedAction ?? "No action required.";

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new
        {
            version = "0.13.0",
            overallStatus = overall,
            readiness,
            nextAction,
            counts = new { pass, warning, fail, notRun },
            checks
        };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
        return new(true, overall, readiness, nextAction, reportPath, pass, warning, fail, notRun, checks);
    }

    private static void Add(List<DiagnosticsCheck> checks, string code, string status, string message, string evidencePath, string action) =>
        checks.Add(new(code, status, message, evidencePath.Replace('\\', '/'), action));

    private static int CountSnapshots(string projectRoot)
    {
        var history = Path.Combine(projectRoot, "history");
        if (!Directory.Exists(history)) return 0;
        try { return Directory.EnumerateDirectories(history).Count(directory => File.Exists(Path.Combine(directory, "snapshot.json"))); }
        catch { return 0; }
    }

    private static string Display(double? value) => value.HasValue ? $"{value.Value:0.####}%" : "not available";
    private static int ReadArrayCount(string path) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0; } catch { return 0; } }
    private static int ReadInt(string path, string property) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0; } catch { return 0; } }
    private static double? ReadDouble(string path, string property) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : null; } catch { return null; } }
    private static string? ReadString(string path, string property) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null; } catch { return null; } }
    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
