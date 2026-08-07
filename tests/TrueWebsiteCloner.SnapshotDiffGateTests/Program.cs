using System.Text.Json;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_SNAPSHOT_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.10");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);
var captureA = Path.Combine(root, "capture-a");
var captureB = Path.Combine(root, "capture-b");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

await WriteCapture(captureA, new[]
{
    new Fixture("http://127.0.0.1:7843/", "text/html", "Document", "_bodies/index.html", "<html><script src='/app.js'></script><link href='/styles.css'></html>", false, "index.html"),
    new Fixture("http://127.0.0.1:7843/app.js", "text/javascript", "Script", "_bodies/app.js", "console.log('v1')", false, "app.js"),
    new Fixture("http://127.0.0.1:7843/styles.css", "text/css", "Stylesheet", "_bodies/styles.css", "body{margin:0}", false, "styles.css")
}, 80, 82, 0.10);

await WriteCapture(captureB, new[]
{
    new Fixture("http://127.0.0.1:7843/", "text/html", "Document", "_bodies/index.html", "<html><script src='/app.js'></script><link href='/styles.css'></html>", false, "index.html"),
    new Fixture("http://127.0.0.1:7843/app.js", "text/javascript", "Script", "_bodies/app.js", "console.log('v2')", true, "app.js"),
    new Fixture("http://127.0.0.1:7843/api/sample", "application/json", "Fetch", "_bodies/api.json", "{\"source\":\"test-lab\"}", false, "api/sample.json")
}, 100, 100, 0.02);

var engine = new SnapshotDiffEngine();
var baseline = await engine.CreateSnapshotAsync(captureA, "baseline");
Require(baseline.Ok && baseline.SnapshotPath is not null, "Baseline snapshot creation failed");
var baselineBytesBefore = await File.ReadAllBytesAsync(baseline.SnapshotPath!);
var duplicate = await engine.CreateSnapshotAsync(captureA, "baseline");
Require(!duplicate.Ok, "Immutable history allowed an existing snapshot label to be overwritten");

var candidate = await engine.CreateSnapshotAsync(captureB, "candidate");
Require(candidate.Ok && candidate.SnapshotPath is not null, "Candidate snapshot creation failed");
var candidateBytesBefore = await File.ReadAllBytesAsync(candidate.SnapshotPath!);
Require(baseline.SnapshotId != candidate.SnapshotId, "Different snapshots produced the same snapshot ID");

var diffPath = Path.Combine(root, "diff-report.json");
var diff = await engine.CompareAsync(baseline.SnapshotPath!, candidate.SnapshotPath!, diffPath);
Require(diff.Ok, diff.Message);
Require(diff.Added == 1, $"Expected 1 added resource, got {diff.Added}");
Require(diff.Removed == 1, $"Expected 1 removed resource, got {diff.Removed}");
Require(diff.Changed == 1, $"Expected 1 changed resource, got {diff.Changed}");
Require(diff.Unchanged == 1, $"Expected 1 unchanged resource, got {diff.Unchanged}");
Require((await File.ReadAllBytesAsync(baseline.SnapshotPath!)).SequenceEqual(baselineBytesBefore), "Baseline snapshot changed during diff");
Require((await File.ReadAllBytesAsync(candidate.SnapshotPath!)).SequenceEqual(candidateBytesBefore), "Candidate snapshot changed during diff");

using var report = JsonDocument.Parse(await File.ReadAllTextAsync(diffPath));
var rootJson = report.RootElement;
Require(rootJson.GetProperty("result").GetString() == "CHANGED", "Diff result should be CHANGED");
Require(rootJson.GetProperty("added")[0].GetProperty("url").GetString()!.Contains("/api/sample"), "Added API resource not identified");
Require(rootJson.GetProperty("removed")[0].GetProperty("url").GetString()!.Contains("/styles.css"), "Removed stylesheet not identified");
var changed = rootJson.GetProperty("changed")[0];
Require(changed.GetProperty("url").GetString()!.Contains("/app.js"), "Changed JavaScript resource not identified");
Require(changed.GetProperty("hashChanged").GetBoolean(), "Changed resource hash delta missing");
Require(changed.GetProperty("recoveryStateChanged").GetBoolean(), "Recovery-state change missing");
Require(Math.Abs(rootJson.GetProperty("metrics").GetProperty("completenessDelta").GetDouble() - 20) < 0.001, "Completeness delta incorrect");
Require(Math.Abs(rootJson.GetProperty("metrics").GetProperty("visualMismatchDelta").GetDouble() - (-0.08)) < 0.001, "Visual mismatch delta incorrect");

Console.WriteLine("PASS  immutable snapshot history");
Console.WriteLine("PASS  added/removed/changed/unchanged classification");
Console.WriteLine("PASS  content-hash and recovery-state change detection");
Console.WriteLine("PASS  completeness and visual metric deltas");
Console.WriteLine("PASS  diff does not mutate either snapshot");
Console.WriteLine("RESULT: GATE 0.10 PASS");

static async Task WriteCapture(string capture, IEnumerable<Fixture> resources, double completeness, double weighted, double visual)
{
    Directory.CreateDirectory(Path.Combine(capture, "_network"));
    Directory.CreateDirectory(Path.Combine(capture, "_bodies"));
    Directory.CreateDirectory(Path.Combine(capture, "offline", "visual-comparison"));
    await File.WriteAllTextAsync(Path.Combine(capture, "_network", "session.json"), "{\"targetUrl\":\"http://127.0.0.1:7843/\"}");

    var bodyLines = new List<string>();
    var mappings = new List<object>();
    foreach (var resource in resources)
    {
        var file = Path.Combine(capture, resource.File.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, resource.Content);
        bodyLines.Add(JsonSerializer.Serialize(new { url = resource.Url, mimeType = resource.Mime, resourceType = resource.Type, file = resource.File, recovered = resource.Recovered }));
        mappings.Add(new { url = resource.Url, mimeType = resource.Mime, resourceType = resource.Type, localPath = resource.LocalPath });
    }
    await File.WriteAllLinesAsync(Path.Combine(capture, "_bodies", "bodies.jsonl"), bodyLines);
    await File.WriteAllTextAsync(Path.Combine(capture, "offline", "offline-manifest.json"), JsonSerializer.Serialize(new { mappings }));
    await File.WriteAllTextAsync(Path.Combine(capture, "offline", "completeness-report.json"), JsonSerializer.Serialize(new { completenessScore = completeness, weightedCompletenessScore = weighted }));
    await File.WriteAllTextAsync(Path.Combine(capture, "offline", "visual-comparison", "visual-report.json"), JsonSerializer.Serialize(new { mismatchPercent = visual }));
}

sealed record Fixture(string Url, string Mime, string Type, string File, string Content, bool Recovered, string LocalPath);
