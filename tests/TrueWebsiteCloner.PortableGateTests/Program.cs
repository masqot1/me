using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_PORTABLE_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.11");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);
var project = Path.Combine(root, "source-project");
var packages = Path.Combine(root, "packages");
Directory.CreateDirectory(project);
Directory.CreateDirectory(packages);

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Write(string root, string relative, string content)
{
    var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
}

await Write(project, "_network/session.json", "{\"version\":\"0.10.0\",\"targetUrl\":\"http://127.0.0.1:7843/\"}");
await Write(project, "_network/network.jsonl", "{\"eventType\":\"capture.response\"}\n");
await Write(project, "_bodies/index.html", "<html><body>PORTABLE PROJECT</body></html>");
await Write(project, "_bodies/app.js", "console.log('portable');");
await Write(project, "offline/site/index.html", "<html><body>OFFLINE PORTABLE PROJECT</body></html>");
await Write(project, "offline/completeness-report.json", "{\"result\":\"PASS\",\"completenessScore\":100}");
await Write(project, "history/baseline/snapshot.json", "{\"version\":\"0.10.0\",\"label\":\"baseline\",\"snapshotId\":\"immutable-history-marker\",\"resources\":[]}");
var binaryPath = Path.Combine(project, "_bodies", "tiny.bin");
await File.WriteAllBytesAsync(binaryPath, Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());

var package1 = Path.Combine(packages, "project-a.twcproj");
var package2 = Path.Combine(packages, "project-b.twcproj");
var portable = new PortableProjectPackage();
var export1 = await portable.ExportAsync(project, package1);
Require(export1.Ok, export1.Message);
var export2 = await portable.ExportAsync(project, package2);
Require(export2.Ok, export2.Message);
Require(export1.ContentRootSha256 == export2.ContentRootSha256, "Content root is not deterministic");
Require((await File.ReadAllBytesAsync(package1)).SequenceEqual(await File.ReadAllBytesAsync(package2)), "Repeated export did not produce byte-identical package");
Require(export1.PackageSha256 == export2.PackageSha256, "Repeated export package SHA-256 differs");
Require(File.Exists(package1 + ".sha256"), "Package SHA-256 sidecar missing");

var verified = await portable.VerifyAsync(package1);
Require(verified.Ok, verified.Message);
Require(verified.ContentRootSha256 == export1.ContentRootSha256, "Verify content root differs from export");

var imported = Path.Combine(root, "imported-project");
var import = await portable.ImportAsync(package1, imported);
Require(import.Ok, import.Message);
Require(File.Exists(Path.Combine(imported, "history", "baseline", "snapshot.json")), "Snapshot history was not preserved");
Require(File.Exists(Path.Combine(imported, "_twc_package", "import-verification.json")), "Import verification metadata missing");

var originalFiles = Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories).OrderBy(path => Path.GetRelativePath(project, path), StringComparer.Ordinal).ToArray();
foreach (var original in originalFiles)
{
    var relative = Path.GetRelativePath(project, original);
    var importedFile = Path.Combine(imported, relative);
    Require(File.Exists(importedFile), "Imported file missing: " + relative);
    Require((await File.ReadAllBytesAsync(original)).SequenceEqual(await File.ReadAllBytesAsync(importedFile)), "Imported bytes differ: " + relative);
}

var overwriteAttempt = await portable.ImportAsync(package1, imported);
Require(!overwriteAttempt.Ok, "Import overwrote an existing destination");

var tampered = Path.Combine(packages, "tampered.twcproj");
File.Copy(package1, tampered);
using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update))
{
    var entry = zip.GetEntry("_bodies/app.js") ?? throw new Exception("Fixture app.js missing from package");
    entry.Delete();
    var replacement = zip.CreateEntry("_bodies/app.js", CompressionLevel.NoCompression);
    replacement.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    await using var stream = replacement.Open();
    await stream.WriteAsync(Encoding.UTF8.GetBytes("console.log('TAMPERED');"));
}
var tamperedVerify = await portable.VerifyAsync(tampered);
Require(!tamperedVerify.Ok && tamperedVerify.Message.Contains("SHA-256 mismatch", StringComparison.OrdinalIgnoreCase), "Tampered package was not rejected by SHA-256 verification");
var tamperedImportDestination = Path.Combine(root, "tampered-import");
var tamperedImport = await portable.ImportAsync(tampered, tamperedImportDestination);
Require(!tamperedImport.Ok && !Directory.Exists(tamperedImportDestination), "Tampered import materialized a destination");

var traversal = Path.Combine(packages, "path-traversal.twcproj");
using (var zip = ZipFile.Open(traversal, ZipArchiveMode.Create))
{
    var manifest = zip.CreateEntry(PortableProjectPackage.ManifestEntryPath, CompressionLevel.NoCompression);
    await using (var stream = manifest.Open())
    {
        var bytes = Encoding.UTF8.GetBytes("{\"format\":\"TrueWebsiteCloner.PortableProject\",\"version\":\"0.11.0\",\"projectId\":\"x\",\"contentRootSha256\":\"x\",\"targetUrl\":null,\"fileCount\":0,\"totalBytes\":0,\"files\":[]}");
        await stream.WriteAsync(bytes);
    }
    var evil = zip.CreateEntry("../escape.txt", CompressionLevel.NoCompression);
    await using var evilStream = evil.Open();
    await evilStream.WriteAsync(Encoding.UTF8.GetBytes("escape"));
}
var traversalVerify = await portable.VerifyAsync(traversal);
Require(!traversalVerify.Ok && traversalVerify.Message.Contains("Unsafe archive path", StringComparison.OrdinalIgnoreCase), "Path traversal archive was not rejected");
Require(!File.Exists(Path.Combine(root, "escape.txt")), "Path traversal wrote outside import destination");

var symlinkPackage = Path.Combine(packages, "symlink.twcproj");
using (var zip = ZipFile.Open(symlinkPackage, ZipArchiveMode.Create))
{
    var manifest = zip.CreateEntry(PortableProjectPackage.ManifestEntryPath, CompressionLevel.NoCompression);
    await using (var stream = manifest.Open())
        await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"format\":\"TrueWebsiteCloner.PortableProject\",\"version\":\"0.11.0\",\"projectId\":\"x\",\"contentRootSha256\":\"x\",\"targetUrl\":null,\"fileCount\":0,\"totalBytes\":0,\"files\":[]}"));
    var link = zip.CreateEntry("link-to-outside", CompressionLevel.NoCompression);
    link.ExternalAttributes = (0xA000 | 0x1FF) << 16;
    await using var stream = link.Open();
    await stream.WriteAsync(Encoding.UTF8.GetBytes("../../outside"));
}
var symlinkVerify = await portable.VerifyAsync(symlinkPackage);
Require(!symlinkVerify.Ok && symlinkVerify.Message.Contains("Symlink", StringComparison.OrdinalIgnoreCase), "Symlink archive entry was not rejected");

Console.WriteLine("PASS  deterministic byte-identical portable export");
Console.WriteLine("PASS  per-file and content-root SHA-256 verification");
Console.WriteLine("PASS  byte-identical import preserves snapshot history");
Console.WriteLine("PASS  existing destination is never overwritten");
Console.WriteLine("PASS  tampered package rejected before import");
Console.WriteLine("PASS  path traversal archive rejected");
Console.WriteLine("PASS  symlink archive entry rejected");
Console.WriteLine("RESULT: GATE 0.11 PASS");
