using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record ReleaseBundleCreateResult(bool Ok, string Message, string? BundlePath = null, string? BundleSha256 = null,
    string? PortableContentRootSha256 = null, string? SealPayloadSha256 = null);
public sealed record ReleaseBundleVerifyResult(bool Ok, string Message, string? BundleSha256 = null,
    string? PortableContentRootSha256 = null, string? SealPayloadSha256 = null);
public sealed record ReleaseBundleImportResult(bool Ok, string Message, string? DestinationPath = null,
    string? BundleSha256 = null, string? PortableContentRootSha256 = null, string? SealPayloadSha256 = null);

public sealed class ReleaseBundleService
{
    public const string FormatName = "TrueWebsiteCloner.ReleaseBundle";
    public const string Version = "0.16.0";
    public const string DescriptorEntryPath = "release-descriptor.json";
    public const string PortableEntryPath = "project.twcproj";
    private static readonly DateTimeOffset DeterministicTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private sealed record Descriptor(string Format, string Version, string PortableEntryPath, string PortableSha256,
        string PortableContentRootSha256, string SealPayloadSha256, string PayloadContentRootSha256, string ReadinessFingerprintSha256);

    public async Task<ReleaseBundleCreateResult> CreateAsync(string projectRoot, string bundlePath, CancellationToken cancellationToken = default)
    {
        projectRoot = Path.GetFullPath(projectRoot); bundlePath = Path.GetFullPath(bundlePath);
        if (!Directory.Exists(projectRoot)) return new(false, "Project folder does not exist.");
        if (IsInside(projectRoot, bundlePath)) return new(false, "Release bundle must be written outside the project folder.");
        var seal = await new ReleaseSealService().VerifyAsync(projectRoot, cancellationToken);
        if (!seal.Ok) return new(false, "Release seal verification failed: " + seal.Message);
        var sealDoc = await ReadSealAsync(Path.Combine(projectRoot, ReleaseSealService.ReleaseDirectoryName, ReleaseSealService.SealFileName), cancellationToken);
        if (sealDoc is null) return new(false, "Release seal could not be read.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner", "release-bundle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var portablePath = Path.Combine(tempRoot, "project.twcproj");
        var tempBundle = Path.Combine(tempRoot, "bundle.twcrelease");
        try
        {
            var portable = await new WorkspacePortableOperations().ExportAsync(projectRoot, portablePath, cancellationToken);
            if (!portable.Ok || portable.PackageSha256 is null || portable.ContentRootSha256 is null)
                return new(false, "Portable payload export failed: " + portable.Message);
            var descriptor = new Descriptor(FormatName, Version, PortableEntryPath, portable.PackageSha256, portable.ContentRootSha256,
                sealDoc.SealPayloadSha256, sealDoc.Payload.PayloadContentRootSha256, sealDoc.Payload.ReadinessFingerprintSha256);
            var descriptorBytes = JsonSerializer.SerializeToUtf8Bytes(descriptor, JsonOptionsIndented);
            await using (var stream = new FileStream(tempBundle, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, true))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                var descriptorEntry = zip.CreateEntry(DescriptorEntryPath, CompressionLevel.NoCompression);
                descriptorEntry.LastWriteTime = DeterministicTimestamp; descriptorEntry.ExternalAttributes = 0;
                await using (var output = descriptorEntry.Open()) await output.WriteAsync(descriptorBytes, cancellationToken);
                var projectEntry = zip.CreateEntry(PortableEntryPath, CompressionLevel.NoCompression);
                projectEntry.LastWriteTime = DeterministicTimestamp; projectEntry.ExternalAttributes = 0;
                await using var outProject = projectEntry.Open(); await using var inProject = File.OpenRead(portablePath); await inProject.CopyToAsync(outProject, cancellationToken);
            }
            var bundleSha = await HashFileAsync(tempBundle, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
            File.Move(tempBundle, bundlePath);
            await File.WriteAllTextAsync(bundlePath + ".sha256", bundleSha + "  " + Path.GetFileName(bundlePath) + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
            return new(true, "Deterministic release bundle created.", bundlePath, bundleSha, portable.ContentRootSha256, sealDoc.SealPayloadSha256);
        }
        catch (Exception ex) { return new(false, "Release bundle creation failed: " + ex.Message); }
        finally { try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { } }
    }

    public async Task<ReleaseBundleVerifyResult> VerifyAsync(string bundlePath, CancellationToken cancellationToken = default)
    {
        bundlePath = Path.GetFullPath(bundlePath);
        if (!File.Exists(bundlePath)) return new(false, "Release bundle does not exist.");
        var bundleSha = await HashFileAsync(bundlePath, cancellationToken);
        var tempRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner", "release-verify", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var extracted = await ExtractPortableAsync(bundlePath, tempRoot, cancellationToken);
            if (!extracted.Ok || extracted.Descriptor is null || extracted.PortablePath is null) return new(false, extracted.Message, bundleSha);
            var descriptor = extracted.Descriptor;
            var portable = await new PortableProjectPackage().VerifyAsync(extracted.PortablePath, cancellationToken);
            if (!portable.Ok) return new(false, "Embedded portable package failed verification: " + portable.Message, bundleSha);
            if (!string.Equals(portable.PackageSha256, descriptor.PortableSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "Embedded portable package SHA-256 does not match descriptor.", bundleSha);
            if (!string.Equals(portable.ContentRootSha256, descriptor.PortableContentRootSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "Embedded portable content root does not match descriptor.", bundleSha);

            var tempProject = Path.Combine(tempRoot, "verified-project");
            var imported = await new PortableProjectPackage().ImportAsync(extracted.PortablePath, tempProject, cancellationToken);
            if (!imported.Ok) return new(false, "Embedded portable package could not be materialized for seal verification: " + imported.Message, bundleSha);
            var seal = await new ReleaseSealService().VerifyAsync(tempProject, cancellationToken);
            if (!seal.Ok) return new(false, "Embedded project seal failed verification: " + seal.Message, bundleSha);
            var sealDoc = await ReadSealAsync(Path.Combine(tempProject, ReleaseSealService.ReleaseDirectoryName, ReleaseSealService.SealFileName), cancellationToken);
            if (sealDoc is null) return new(false, "Embedded release seal could not be read.", bundleSha);
            if (!string.Equals(sealDoc.SealPayloadSha256, descriptor.SealPayloadSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "Embedded release seal hash does not match descriptor.", bundleSha);
            if (!string.Equals(sealDoc.Payload.PayloadContentRootSha256, descriptor.PayloadContentRootSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "Embedded sealed payload root does not match descriptor.", bundleSha);
            if (!string.Equals(sealDoc.Payload.ReadinessFingerprintSha256, descriptor.ReadinessFingerprintSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "Embedded readiness fingerprint does not match descriptor.", bundleSha);
            return new(true, "Release bundle chain verified.", bundleSha, portable.ContentRootSha256, sealDoc.SealPayloadSha256);
        }
        catch (Exception ex) { return new(false, "Release bundle verification failed: " + ex.Message, bundleSha); }
        finally { try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { } }
    }

    public async Task<ReleaseBundleImportResult> ImportAsync(string bundlePath, string workspaceRoot, string? preferredName = null, CancellationToken cancellationToken = default)
    {
        var verified = await VerifyAsync(bundlePath, cancellationToken);
        if (!verified.Ok) return new(false, verified.Message, null, verified.BundleSha256, verified.PortableContentRootSha256, verified.SealPayloadSha256);
        workspaceRoot = Path.GetFullPath(workspaceRoot); Directory.CreateDirectory(workspaceRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner", "release-import", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tempRoot);
        try
        {
            var extracted = await ExtractPortableAsync(Path.GetFullPath(bundlePath), tempRoot, cancellationToken);
            if (!extracted.Ok || extracted.PortablePath is null) return new(false, extracted.Message, null, verified.BundleSha256, verified.PortableContentRootSha256, verified.SealPayloadSha256);
            var imported = await new WorkspacePortableOperations().ImportIntoWorkspaceAsync(extracted.PortablePath, workspaceRoot, preferredName, cancellationToken);
            if (!imported.Ok || imported.DestinationPath is null) return new(false, imported.Message, null, verified.BundleSha256, verified.PortableContentRootSha256, verified.SealPayloadSha256);
            var seal = await new ReleaseSealService().VerifyAsync(imported.DestinationPath, cancellationToken);
            if (!seal.Ok)
            {
                try { Directory.Delete(imported.DestinationPath, true); } catch { }
                return new(false, "Imported release seal failed verification: " + seal.Message, null, verified.BundleSha256, verified.PortableContentRootSha256, verified.SealPayloadSha256);
            }
            return new(true, "Verified release bundle imported.", imported.DestinationPath, verified.BundleSha256, verified.PortableContentRootSha256, verified.SealPayloadSha256);
        }
        catch (Exception ex) { return new(false, "Release bundle import failed: " + ex.Message, null, verified.BundleSha256, verified.PortableContentRootSha256, verified.SealPayloadSha256); }
        finally { try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { } }
    }

    private static async Task<(bool Ok, string Message, Descriptor? Descriptor, string? PortablePath)> ExtractPortableAsync(string bundlePath, string tempRoot, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(bundlePath); using var zip = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8);
        var files = zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        if (files.Length != 2) return (false, "Release bundle must contain exactly descriptor and portable payload.", null, null);
        foreach (var entry in files) if (!IsSafeEntry(entry.FullName)) return (false, "Unsafe release bundle entry: " + entry.FullName, null, null);
        var descriptorEntry = zip.GetEntry(DescriptorEntryPath); var portableEntry = zip.GetEntry(PortableEntryPath);
        if (descriptorEntry is null || portableEntry is null) return (false, "Release bundle descriptor or portable payload is missing.", null, null);
        if (descriptorEntry.Length > 1024 * 1024) return (false, "Release descriptor is unexpectedly large.", null, null);
        Descriptor? descriptor; await using (var descriptorStream = descriptorEntry.Open()) descriptor = await JsonSerializer.DeserializeAsync<Descriptor>(descriptorStream, JsonOptions, cancellationToken);
        if (descriptor is null || descriptor.Format != FormatName || descriptor.Version != Version || descriptor.PortableEntryPath != PortableEntryPath)
            return (false, "Release bundle descriptor is invalid or unsupported.", null, null);
        var portablePath = Path.Combine(tempRoot, "project.twcproj");
        await using (var input = portableEntry.Open()) await using (var output = new FileStream(portablePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true)) await input.CopyToAsync(output, cancellationToken);
        var actualHash = await HashFileAsync(portablePath, cancellationToken);
        if (!string.Equals(actualHash, descriptor.PortableSha256, StringComparison.OrdinalIgnoreCase)) return (false, "Embedded portable package SHA-256 mismatch.", descriptor, portablePath);
        return (true, "Bundle payload extracted.", descriptor, portablePath);
    }

    private static async Task<ReleaseSealDocument?> ReadSealAsync(string path, CancellationToken cancellationToken) { try { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<ReleaseSealDocument>(stream, JsonOptions, cancellationToken); } catch { return null; } }
    private static bool IsSafeEntry(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && path.Replace('\\', '/').Split('/').All(segment => segment is not "." and not ".." && !segment.Contains(':'));
    private static bool IsInside(string root, string path) { var fullRoot = Path.GetFullPath(root); var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar; return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.Equals(path, fullRoot, StringComparison.OrdinalIgnoreCase); }
    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken) { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true); return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant(); }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
