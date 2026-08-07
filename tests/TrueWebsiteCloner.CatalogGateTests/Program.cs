using System.Text;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_CATALOG_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.12");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);
var workspace = Path.Combine(root, "workspace");
var outside = Path.Combine(root, "outside");
Directory.CreateDirectory(workspace);
Directory.CreateDirectory(outside);

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Write(string root, string relative, string content)
{
    var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
}

var projectA = Path.Combine(workspace, "testlab", "capture-a");
await Write(projectA, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:7843/\",\"startedAtUtc\":\"2026-08-07T20:00:00Z\"}");
await Write(projectA, "_network/summary.json", "{\"eventCount\":30,\"bodyCount\":8}");
await Write(projectA, "_bodies/bodies.jsonl", "{}\n");
await Write(projectA, "offline/offline-manifest.json", "{\"mappings\":[]}");
await Write(projectA, "offline/site/index.html", "<html>A</html>");
await Write(projectA, "offline/missing-resources.json", "[]");
await Write(projectA, "offline/completeness-report.json", "{\"result\":\"PASS\",\"completenessScore\":100,\"weightedCompletenessScore\":100}");
await Write(projectA, "offline/verification-report.json", "{\"result\":\"PASS\"}");
await Write(projectA, "offline/visual-comparison/visual-report.json", "{\"result\":\"PASS\",\"mismatchPercent\":0.02}");
await Write(projectA, "history/baseline/snapshot.json", "{\"snapshotId\":\"one\"}");
await Write(projectA, "history/current/snapshot.json", "{\"snapshotId\":\"two\"}");
await Write(projectA, "_twc_package/import-verification.json", "{\"result\":\"PASS\",\"contentRootSha256\":\"catalog-import-content-root\"}");
await Write(projectA, "history/nested/fake/_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:9999/\"}");

var projectB = Path.Combine(workspace, "testlab", "capture-b");
await Write(projectB, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:7843/\",\"startedAtUtc\":\"2026-08-07T21:00:00Z\"}");
await Write(projectB, "_network/summary.json", "{\"eventCount\":14,\"bodyCount\":4}");
await Write(projectB, "_bodies/bodies.jsonl", "{}\n");
await Write(projectB, "offline/offline-manifest.json", "{\"mappings\":[]}");
await Write(projectB, "offline/site/index.html", "<html>B</html>");
await Write(projectB, "offline/missing-resources.json", "[{\"resolvedUrl\":\"http://127.0.0.1:7843/a\"},{\"resolvedUrl\":\"http://127.0.0.1:7843/b\"}]");
await Write(projectB, "offline/completeness-report.json", "{\"result\":\"INCOMPLETE\",\"completenessScore\":70,\"weightedCompletenessScore\":62.5}");

var outsideProject = Path.Combine(outside, "capture-outside");
await Write(outsideProject, "_network/session.json", "{\"targetUrl\":\"http://127.0.0.1:9000/\"}");
await Write(outsideProject, "_network/summary.json", "{\"eventCount\":999,\"bodyCount\":999}");

var service = new ProjectCatalogService();
var first = await service.RefreshAsync(workspace);
Require(first.Ok, first.Message);
Require(first.Projects.Count == 2, $"Expected exactly 2 workspace projects, got {first.Projects.Count}");
Require(first.Projects.All(project => Path.GetFullPath(project.FullPath).StartsWith(Path.GetFullPath(workspace), StringComparison.OrdinalIgnoreCase)), "Catalog escaped configured workspace root");
Require(first.Projects.All(project => !project.TargetUrl.Contains(":9999") && !project.TargetUrl.Contains(":9000")), "Catalog indexed nested/outside fake project");

var a = first.Projects.Single(project => project.RelativePath.EndsWith("capture-a", StringComparison.OrdinalIgnoreCase));
var b = first.Projects.Single(project => project.RelativePath.EndsWith("capture-b", StringComparison.OrdinalIgnoreCase));
Require(a.Status == "Verified", $"Project A status should be Verified, got {a.Status}");
Require(a.CompletenessScore == 100 && a.WeightedCompletenessScore == 100, "Project A completeness metrics incorrect");
Require(a.VisualMismatchPercent == 0.02, "Project A visual metric incorrect");
Require(a.SnapshotCount == 2, $"Project A snapshot count should be 2, got {a.SnapshotCount}");
Require(a.ImportIntegrityVerified, "Project A import integrity should be verified");
Require(a.ProjectId == "catalog-import-content-root", "Imported project should use content-root ID");
Require(b.Status == "Incomplete" && b.MissingResources == 2, "Project B incomplete state not detected");
Require(first.Projects[0].RelativePath.EndsWith("capture-b", StringComparison.OrdinalIgnoreCase), "Catalog should sort newest project first");

var catalogBytes1 = await File.ReadAllBytesAsync(first.CatalogPath);
var second = await service.RefreshAsync(workspace);
Require(second.Ok && second.Projects.Count == 2, "Second catalog refresh failed");
var catalogBytes2 = await File.ReadAllBytesAsync(second.CatalogPath);
Require(catalogBytes1.SequenceEqual(catalogBytes2), "Catalog output is not deterministic");
var persisted = await File.ReadAllTextAsync(second.CatalogPath);
Require(!persisted.Contains(Path.GetFullPath(workspace), StringComparison.OrdinalIgnoreCase), "Persisted catalog leaked absolute workspace path");
Require(persisted.Contains("testlab/capture-a", StringComparison.Ordinal), "Persisted catalog is missing relative path");

Console.WriteLine("PASS  indexes captures only inside configured workspace");
Console.WriteLine("PASS  stops recursion at capture roots");
Console.WriteLine("PASS  status/completeness/visual/history/import-integrity summary");
Console.WriteLine("PASS  newest-first deterministic catalog");
Console.WriteLine("PASS  persisted catalog uses relative paths only");
Console.WriteLine("RESULT: GATE 0.12 CORE PASS");
