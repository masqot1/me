using System.Text;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_RELEASE_READINESS_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TWC-Gate-0.14");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);
var project = Path.Combine(root, "project");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Write(string project, string relative, string content)
{
    var file = Path.Combine(project, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    await File.WriteAllTextAsync(file, content, new UTF8Encoding(false));
}

await Write(project, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:7843/\"}");
await Write(project, "_network/summary.json", "{\"eventCount\":30,\"bodyCount\":8}");
await Write(project, "offline/offline-manifest.json", "{\"mappings\":[]}");
await Write(project, "offline/site/index.html", "<html/>");
await Write(project, "offline/missing-resources.json", "[]");
await Write(project, "offline/recovery-report.json", "{\"result\":\"PASS\",\"finalMissing\":0}");
await Write(project, "offline/completeness-report.json", "{\"result\":\"PASS\",\"completenessScore\":100,\"weightedCompletenessScore\":100}");
await Write(project, "offline/dependency-graph.json", "{\"nodes\":[],\"edges\":[]}");
await Write(project, "offline/verification-report.json", "{\"result\":\"PASS\",\"unexpectedDivergences\":0}");
await Write(project, "offline/visual-comparison/visual-report.json", "{\"result\":\"PASS\",\"mismatchPercent\":0.02,\"maxMismatchPercent\":0.15}");
await Write(project, "history/baseline/snapshot.json", "{\"snapshotId\":\"baseline\"}");

var service = new ReleaseReadinessService();
var first = await service.ValidateAsync(project);
Require(first.Result == "READY" && first.FailCount == 0, "Ready fixture blocked");
var firstReportBytes = await File.ReadAllBytesAsync(first.ReportPath);

var second = await service.ValidateAsync(project);
Require(second.ReleaseFingerprintSha256 == first.ReleaseFingerprintSha256, "Fingerprint unstable");
var secondReportBytes = await File.ReadAllBytesAsync(second.ReportPath);
Require(firstReportBytes.SequenceEqual(secondReportBytes), "Report not deterministic");

var summaryPath = Path.Combine(project, "_network", "summary.json");
var summaryBefore = await File.ReadAllTextAsync(summaryPath);
await Write(project, "offline/visual-comparison/visual-report.json", "{\"result\":\"FAIL\",\"mismatchPercent\":2.5,\"maxMismatchPercent\":0.15}");
var blocked = await service.ValidateAsync(project);
Require(blocked.Result == "BLOCKED" && blocked.Stages.Any(stage => stage.Code == "VISUAL_COMPARISON" && stage.Status == "FAIL"), "Visual failure did not block");
var summaryAfter = await File.ReadAllTextAsync(summaryPath);
Require(summaryBefore == summaryAfter, "Source capture evidence mutated");

Console.WriteLine("PASS  READY evidence produces stable release fingerprint");
Console.WriteLine("PASS  repeated readiness report is byte-identical");
Console.WriteLine("PASS  visual failure blocks release");
Console.WriteLine("PASS  readiness validation does not mutate source capture evidence");
Console.WriteLine("RESULT: GATE 0.14 PASS");
