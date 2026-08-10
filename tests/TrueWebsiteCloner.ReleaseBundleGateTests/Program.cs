using System.Text;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_RELEASE_BUNDLE_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TWC-Gate-0.16");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);
var project = Path.Combine(root, "project");
var bundleA = Path.Combine(root, "a.twcrelease");
var bundleB = Path.Combine(root, "b.twcrelease");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Write(string project, string relative, string content)
{
    var file = Path.Combine(project, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    await File.WriteAllTextAsync(file, content, new UTF8Encoding(false));
}

await Write(project, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:7843/\"}");
await Write(project, "_network/summary.json", "{\"eventCount\":30,\"bodyCount\":8}");
await Write(project, "_bodies/bodies.jsonl", "{}\n");
await Write(project, "offline/offline-manifest.json", "{\"mappings\":[]}");
await Write(project, "offline/site/index.html", "<html>bundle</html>");
await Write(project, "offline/missing-resources.json", "[]");
await Write(project, "offline/recovery-report.json", "{\"result\":\"PASS\",\"finalMissing\":0}");
await Write(project, "offline/completeness-report.json", "{\"result\":\"PASS\",\"completenessScore\":100,\"weightedCompletenessScore\":100}");
await Write(project, "offline/dependency-graph.json", "{\"nodes\":[],\"edges\":[]}");
await Write(project, "offline/verification-report.json", "{\"result\":\"PASS\",\"unexpectedDivergences\":0}");
await Write(project, "offline/visual-comparison/visual-report.json", "{\"result\":\"PASS\",\"mismatchPercent\":0.02,\"maxMismatchPercent\":0.15}");
await Write(project, "history/001/snapshot.json", "{\"snapshotId\":\"bundle-snapshot\"}");

var seal = await new ReleaseSealService().CreateAsync(project);
Require(seal.Ok, seal.Message);

var bundles = new ReleaseBundleService();
var first = await bundles.CreateAsync(project, bundleA);
Require(first.Ok, first.Message);
var second = await bundles.CreateAsync(project, bundleB);
Require(second.Ok, second.Message);

var firstBytes = await File.ReadAllBytesAsync(bundleA);
var secondBytes = await File.ReadAllBytesAsync(bundleB);
Require(firstBytes.SequenceEqual(secondBytes), "Bundle export not deterministic");
Require((await bundles.VerifyAsync(bundleA)).Ok, "Bundle verify failed");

var workspace = Path.Combine(root, "workspace");
var imported = await bundles.ImportAsync(bundleA, workspace, "release");
Require(imported.Ok && imported.DestinationPath is not null, "Bundle import failed");
Require((await new ReleaseSealService().VerifyAsync(imported.DestinationPath!)).Ok, "Imported seal failed");

var tamperedBytes = await File.ReadAllBytesAsync(bundleA);
tamperedBytes[^1] ^= 0x01;
var tampered = Path.Combine(root, "tampered.twcrelease");
await File.WriteAllBytesAsync(tampered, tamperedBytes);
Require(!(await bundles.VerifyAsync(tampered)).Ok, "Tampered bundle accepted");

Console.WriteLine("PASS  deterministic repeated release bundle");
Console.WriteLine("PASS  complete bundle verification chain");
Console.WriteLine("PASS  verified bundle import and embedded seal verification");
Console.WriteLine("PASS  tampered release bundle rejected");
Console.WriteLine("RESULT: GATE 0.16 PASS");
