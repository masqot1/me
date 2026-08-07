using System.Text;
using System.Text.Json;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_DIAGNOSTICS_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.13");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);
var healthy = Path.Combine(root, "healthy");
var broken = Path.Combine(root, "broken");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Write(string project, string relative, string content)
{
    var path = Path.Combine(project, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
}

await Write(healthy, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:7843/\"}");
await Write(healthy, "_network/summary.json", "{\"eventCount\":30,\"bodyCount\":8}");
await Write(healthy, "offline/offline-manifest.json", "{\"mappings\":[]}");
await Write(healthy, "offline/site/index.html", "<html>healthy</html>");
await Write(healthy, "offline/missing-resources.json", "[]");
await Write(healthy, "offline/recovery-report.json", "{\"result\":\"PASS\",\"finalMissing\":0}");
await Write(healthy, "offline/completeness-report.json", "{\"result\":\"PASS\",\"completenessScore\":100,\"weightedCompletenessScore\":100}");
await Write(healthy, "offline/dependency-graph.json", "{\"nodes\":[],\"edges\":[]}");
await Write(healthy, "offline/verification-report.json", "{\"result\":\"PASS\",\"unexpectedDivergences\":0}");
await Write(healthy, "offline/visual-comparison/visual-report.json", "{\"result\":\"PASS\",\"mismatchPercent\":0.02,\"maxMismatchPercent\":0.15}");
await Write(healthy, "history/baseline/snapshot.json", "{\"snapshotId\":\"baseline\"}");

await Write(broken, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:7843/\"}");
await Write(broken, "_network/summary.json", "{\"eventCount\":12,\"bodyCount\":4}");
await Write(broken, "offline/offline-manifest.json", "{\"mappings\":[]}");
await Write(broken, "offline/site/index.html", "<html>broken</html>");
await Write(broken, "offline/missing-resources.json", "[{\"resolvedUrl\":\"/missing-a\"},{\"resolvedUrl\":\"/missing-b\"}]");
await Write(broken, "offline/recovery-report.json", "{\"result\":\"PARTIAL\",\"finalMissing\":2}");
await Write(broken, "offline/completeness-report.json", "{\"result\":\"INCOMPLETE\",\"completenessScore\":70,\"weightedCompletenessScore\":61}");
await Write(broken, "offline/verification-report.json", "{\"result\":\"FAIL\",\"unexpectedDivergences\":3}");
await Write(broken, "offline/visual-comparison/visual-report.json", "{\"result\":\"FAIL\",\"mismatchPercent\":2.5,\"maxMismatchPercent\":0.15}");
await Write(broken, "_twc_package/import-verification.json", "{\"result\":\"FAIL\"}");

var service = new ProjectDiagnosticsService();
var healthyResult = await service.RunAsync(healthy);
Require(healthyResult.Ok, "Healthy diagnostics failed to run");
Require(healthyResult.OverallStatus == "PASS", $"Healthy project should PASS, got {healthyResult.OverallStatus}");
Require(healthyResult.Readiness == "READY", "Healthy project should be READY");
Require(healthyResult.FailCount == 0 && healthyResult.WarningCount == 0, "Healthy project should have no failures or warnings");
Require(healthyResult.Checks.Any(check => check.Code == "VISUAL_COMPARISON" && check.Status == "PASS"), "Healthy visual check missing");
Require(healthyResult.Checks.Any(check => check.Code == "SNAPSHOT_HISTORY" && check.Status == "PASS"), "Healthy snapshot check missing");

var healthyBytes1 = await File.ReadAllBytesAsync(healthyResult.ReportPath);
var healthyAgain = await service.RunAsync(healthy);
Require(healthyAgain.OverallStatus == "PASS", "Repeated healthy diagnostics changed status");
var healthyBytes2 = await File.ReadAllBytesAsync(healthyAgain.ReportPath);
Require(healthyBytes1.SequenceEqual(healthyBytes2), "Diagnostics report is not deterministic");
var healthyReportText = await File.ReadAllTextAsync(healthyResult.ReportPath);
Require(!healthyReportText.Contains(Path.GetFullPath(healthy), StringComparison.OrdinalIgnoreCase), "Diagnostics report leaked absolute project path");

var brokenResult = await service.RunAsync(broken);
Require(brokenResult.Ok, "Broken diagnostics failed to run");
Require(brokenResult.OverallStatus == "FAIL" && brokenResult.Readiness == "NOT_READY", "Broken project should be NOT_READY/FAIL");
foreach (var code in new[] { "MISSING_RESOURCES", "RECOVERY", "COMPLETENESS", "DEPENDENCY_GRAPH", "OFFLINE_VERIFICATION", "VISUAL_COMPARISON", "IMPORT_INTEGRITY" })
    Require(brokenResult.Checks.Any(check => check.Code == code && check.Status == "FAIL"), $"Expected FAIL diagnostic missing: {code}");
Require(brokenResult.NextAction.Contains("missing-resource recovery", StringComparison.OrdinalIgnoreCase), "Broken project next action should prioritize missing-resource recovery");
Require(brokenResult.Checks.All(check => !Path.IsPathRooted(check.EvidencePath)), "Diagnostics evidence path must be relative");

using var brokenReport = JsonDocument.Parse(await File.ReadAllTextAsync(brokenResult.ReportPath));
Require(brokenReport.RootElement.GetProperty("overallStatus").GetString() == "FAIL", "Persisted broken health status incorrect");
Require(brokenReport.RootElement.GetProperty("counts").GetProperty("fail").GetInt32() >= 7, "Persisted fail count too low");

Console.WriteLine("PASS  healthy project normalized to READY/PASS");
Console.WriteLine("PASS  broken project exposes actionable FAIL checks");
Console.WriteLine("PASS  missing-resource recovery is prioritized as next action");
Console.WriteLine("PASS  evidence paths are relative only");
Console.WriteLine("PASS  repeated health report is byte-identical");
Console.WriteLine("RESULT: GATE 0.13 CORE PASS");
