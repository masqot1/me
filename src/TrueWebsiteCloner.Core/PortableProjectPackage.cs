using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record PortableExportResult(
    bool Ok,
    string Message,
    string? PackagePath = null,
    string? PackageSha256 = null,
    string? ContentRootSha256 = null,
    int FileCount = 0,
    long TotalBytes = 0);

public sealed record PortableVerifyResult(
    bool Ok,
    string Message,
    string? PackageSha256 = null,
    string? ContentRootSha256 = null,
    int FileCount = 0,
    long TotalBytes = 0);

public sealed record PortableImportResult(
    bool Ok,
    string Message,
    string? DestinationPath = null,
    string? PackageSha256 = null,
    string? ContentRootSha256 = null,
    int FileCount = 0,
    long TotalBytes = 0);

public sealed class PortableProjectPackage
{
    public const string ManifestEntryPath = "_twc_package/project-export-manifest.json";
    public const int MaxFiles = 50_000;
    public const long MaxSingleFileBytes = 1L * 1024 * 1024 * 1024;
    public const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;

    private static readonly DateTimeOffset DeterministicTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record FileRecord(string Path, long ByteLength, string Sha256);
    private sealed record PackageManifest(
        string Format,
        string Version,
        string ProjectId,
        string ContentRootSha256,
        string? TargetUrl,
        int FileCount,
        long TotalBytes,
        FileRecord[] Files);

    public async Task<PortableExportResult> ExportAsync(
        string projectRoot,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            projectRoot = Path.GetFullPath(projectRoot);
            packagePath = Path.GetFullPath(packagePath);
            if (!Directory.Exists(projectRoot)) return new(false, "Project folder does not exist.");
            if (IsInside(projectRoot, packagePath))
                return new(false, "Portable package must be written outside the project folder.");
            if (HasReparsePoint(new DirectoryInfo(projectRoot)))
                return new(false, "Project root is a reparse point and cannot be exported.");

            var files = EnumerateSafeFiles(projectRoot).ToArray();
            if (files.Length == 0) return new(false, "Project folder contains no files.");
            if (files.Length > MaxFiles) return new(false, $"Project exceeds {MaxFiles} file limit.");

            var records = new List<FileRecord>(files.Length);
            long totalBytes = 0;
            foreach (var fullPath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeRelativePath(Path.GetRelativePath(projectRoot, fullPath));
                if (!IsSafeRelativePath(relative)) return new(false, $"Unsafe project path: {relative}");
                if (relative.StartsWith("_twc_package/", StringComparison.OrdinalIgnoreCase))
                    return new(false, "_twc_package is reserved for portable-package metadata.");

                var info = new FileInfo(fullPath);
                if (info.Length > MaxSingleFileBytes) return new(false, $"File exceeds 1 GiB package limit: {relative}");
                checked { totalBytes += info.Length; }
                if (totalBytes > MaxTotalBytes) return new(false, "Project exceeds 8 GiB package limit.");
                records.Add(new FileRecord(relative, info.Length, await HashFileAsync(fullPath, cancellationToken)));
            }

            records.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
            var contentRoot = ComputeContentRoot(records);
            var targetUrl = ReadTargetUrl(projectRoot);
            var manifest = new PackageManifest(
                "TrueWebsiteCloner.PortableProject",
                "0.11.0",
                contentRoot,
                contentRoot,
                targetUrl,
                records.Count,
                totalBytes,
                records.ToArray());
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptionsIndented);

            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            var tempPath = packagePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, useAsync: true))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8))
                {
                    foreach (var record in records)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(record.Path, CompressionLevel.NoCompression);
                        entry.LastWriteTime = DeterministicTimestamp;
                        entry.ExternalAttributes = 0;
                        await using var output = entry.Open();
                        await using var input = new FileStream(
                            Path.Combine(projectRoot, record.Path.Replace('/', Path.DirectorySeparatorChar)),
                            FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
                        await input.CopyToAsync(output, cancellationToken);
                    }

                    var manifestEntry = archive.CreateEntry(ManifestEntryPath, CompressionLevel.NoCompression);
                    manifestEntry.LastWriteTime = DeterministicTimestamp;
                    manifestEntry.ExternalAttributes = 0;
                    await using var manifestStream = manifestEntry.Open();
                    await manifestStream.WriteAsync(manifestBytes, cancellationToken);
                }

                var packageSha = await HashFileAsync(tempPath, cancellationToken);
                if (File.Exists(packagePath)) File.Delete(packagePath);
                File.Move(tempPath, packagePath);
                await File.WriteAllTextAsync(packagePath + ".sha256", packageSha + "  " + Path.GetFileName(packagePath) + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
                return new(true, "Portable project exported.", packagePath, packageSha, contentRoot, records.Count, totalBytes);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            return new(false, "Portable export failed: " + ex.Message);
        }
    }

    public async Task<PortableVerifyResult> VerifyAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            packagePath = Path.GetFullPath(packagePath);
            if (!File.Exists(packagePath)) return new(false, "Package file does not exist.");
            var packageSha = await HashFileAsync(packagePath, cancellationToken);

            await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
            if (archive.Entries.Count > MaxFiles + 128) return new(false, "Package contains too many archive entries.", packageSha);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ZipArchiveEntry? manifestEntry = null;
            long declaredArchiveBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateEntry(entry, out var normalized, out var error))
                    return new(false, error!, packageSha);
                if (normalized.EndsWith('/')) continue;
                if (!seen.Add(normalized)) return new(false, $"Duplicate archive path: {normalized}", packageSha);
                if (entry.Length > MaxSingleFileBytes) return new(false, $"Archive entry exceeds 1 GiB: {normalized}", packageSha);
                checked { declaredArchiveBytes += entry.Length; }
                if (declaredArchiveBytes > MaxTotalBytes + 16 * 1024 * 1024) return new(false, "Archive expands beyond allowed total size.", packageSha);
                if (string.Equals(normalized, ManifestEntryPath, StringComparison.Ordinal)) manifestEntry = entry;
            }

            if (manifestEntry is null) return new(false, "Portable package manifest is missing.", packageSha);
            PackageManifest? manifest;
            await using (var manifestStream = manifestEntry.Open())
                manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(manifestStream, JsonOptions, cancellationToken);
            if (manifest is null || manifest.Format != "TrueWebsiteCloner.PortableProject" || manifest.Version != "0.11.0")
                return new(false, "Portable package manifest is invalid or unsupported.", packageSha);
            if (manifest.FileCount != manifest.Files.Length || manifest.FileCount > MaxFiles)
                return new(false, "Manifest file count is invalid.", packageSha);

            var manifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            foreach (var record in manifest.Files.OrderBy(record => record.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeRelativePath(record.Path);
                if (!IsSafeRelativePath(relative) || relative.StartsWith("_twc_package/", StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Manifest contains unsafe/reserved path: {record.Path}", packageSha);
                if (!manifestPaths.Add(relative)) return new(false, $"Manifest contains duplicate path: {relative}", packageSha);
                if (record.ByteLength is < 0 or > MaxSingleFileBytes) return new(false, $"Manifest size invalid: {relative}", packageSha);
                checked { totalBytes += record.ByteLength; }
                if (totalBytes > MaxTotalBytes) return new(false, "Manifest exceeds total package size limit.", packageSha);

                var entry = archive.GetEntry(relative);
                if (entry is null) return new(false, $"Manifest file missing from archive: {relative}", packageSha);
                if (entry.Length != record.ByteLength) return new(false, $"Size mismatch: {relative}", packageSha);
                var actualHash = await HashEntryAsync(entry, cancellationToken);
                if (!string.Equals(actualHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"SHA-256 mismatch: {relative}", packageSha);
            }

            var archiveFiles = seen.Where(path => !string.Equals(path, ManifestEntryPath, StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!archiveFiles.SetEquals(manifestPaths)) return new(false, "Archive contains files not declared by the manifest.", packageSha);
            if (totalBytes != manifest.TotalBytes) return new(false, "Manifest total byte count does not match file records.", packageSha);
            var computedRoot = ComputeContentRoot(manifest.Files.OrderBy(record => record.Path, StringComparer.Ordinal).ToArray());
            if (!string.Equals(computedRoot, manifest.ContentRootSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(computedRoot, manifest.ProjectId, StringComparison.OrdinalIgnoreCase))
                return new(false, "Content-root integrity hash mismatch.", packageSha);

            return new(true, "Portable package integrity verified.", packageSha, computedRoot, manifest.FileCount, totalBytes);
        }
        catch (InvalidDataException ex)
        {
            return new(false, "Invalid ZIP package: " + ex.Message);
        }
        catch (Exception ex)
        {
            return new(false, "Package verification failed: " + ex.Message);
        }
    }

    public async Task<PortableImportResult> ImportAsync(
        string packagePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        destinationPath = Path.GetFullPath(destinationPath);
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            return new(false, "Import destination already exists; import will not overwrite existing projects.");

        var verified = await VerifyAsync(packagePath, cancellationToken);
        if (!verified.Ok) return new(false, verified.Message, null, verified.PackageSha256, verified.ContentRootSha256, verified.FileCount, verified.TotalBytes);

        var parent = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".twc-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
            var manifestEntry = archive.GetEntry(ManifestEntryPath)!;
            PackageManifest manifest;
            await using (var manifestStream = manifestEntry.Open())
                manifest = (await JsonSerializer.DeserializeAsync<PackageManifest>(manifestStream, JsonOptions, cancellationToken))!;

            foreach (var record in manifest.Files.OrderBy(record => record.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.GetEntry(record.Path)!;
                var target = SafeDestinationPath(staging, record.Path);
                if (target is null) throw new InvalidDataException("Unsafe import path: " + record.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > record.ByteLength || written > MaxSingleFileBytes) throw new InvalidDataException("Extracted file exceeds declared size: " + record.Path);
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (written != record.ByteLength || !string.Equals(actualHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Extracted file integrity mismatch: " + record.Path);
            }

            var metadataDir = Path.Combine(staging, "_twc_package");
            Directory.CreateDirectory(metadataDir);
            await File.WriteAllTextAsync(
                Path.Combine(metadataDir, "project-export-manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptionsIndented),
                new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(metadataDir, "import-verification.json"),
                JsonSerializer.Serialize(new
                {
                    version = "0.11.0",
                    result = "PASS",
                    packageSha256 = verified.PackageSha256,
                    contentRootSha256 = verified.ContentRootSha256,
                    fileCount = verified.FileCount,
                    totalBytes = verified.TotalBytes
                }, JsonOptionsIndented),
                new UTF8Encoding(false), cancellationToken);

            Directory.Move(staging, destinationPath);
            return new(true, "Portable project imported after full integrity verification.", destinationPath, verified.PackageSha256, verified.ContentRootSha256, verified.FileCount, verified.TotalBytes);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            return new(false, "Portable import failed: " + ex.Message, null, verified.PackageSha256, verified.ContentRootSha256, verified.FileCount, verified.TotalBytes);
        }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            var dirInfo = new DirectoryInfo(directory);
            if (HasReparsePoint(dirInfo)) throw new InvalidDataException("Reparse-point directory rejected: " + directory);
            foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.Ordinal).Reverse())
                stack.Push(child);
            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (HasReparsePoint(new FileInfo(file))) throw new InvalidDataException("Reparse-point file rejected: " + file);
                yield return file;
            }
        }
    }

    private static bool TryValidateEntry(ZipArchiveEntry entry, out string normalized, out string? error)
    {
        normalized = NormalizeRelativePath(entry.FullName);
        error = null;
        if (!IsSafeRelativePath(normalized)) { error = "Unsafe archive path: " + entry.FullName; return false; }
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType == 0xA000) { error = "Symlink archive entry rejected: " + entry.FullName; return false; }
        return true;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\')) return false;
        var raw = path.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var segments = raw.Replace('\\', '/').Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':'))) return false;
        var platform = raw.Replace('/', Path.DirectorySeparatorChar);
        return !Path.IsPathRooted(platform);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string? SafeDestinationPath(string root, string relative)
    {
        if (!IsSafeRelativePath(relative)) return null;
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.Equals(path, fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePoint(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static string ComputeContentRoot(IEnumerable<FileRecord> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records.OrderBy(record => record.Path, StringComparer.Ordinal))
            builder.Append(record.Path).Append('\0').Append(record.ByteLength).Append('\0').Append(record.Sha256.ToLowerInvariant()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        return await HashStreamAsync(stream, cancellationToken);
    }

    private static async Task<string> HashEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await HashStreamAsync(stream, cancellationToken);
    }

    private static async Task<string> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0) hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string? ReadTargetUrl(string root)
    {
        var path = Path.Combine(root, "_network", "session.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("targetUrl", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch { return null; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
