using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrueWebsiteCloner.Core;

public sealed record ReleaseSealEvidence(string Code, string Path, string Sha256);
public sealed record ReleaseSealPayload(string ToolVersion, string ReadinessFingerprintSha256, string PayloadContentRootSha256,
    int PayloadFileCount, long PayloadTotalBytes, string PrimarySnapshotId, string[] SnapshotIds,
    double CompletenessScore, double WeightedCompletenessScore, double VisualMismatchPercent, ReleaseSealEvidence[] Evidence);
public sealed record ReleaseSealDocument(string Format, string Version, string SealPayloadSha256, ReleaseSealPayload Payload);
public sealed record ReleaseSealResult(bool Ok, string Message, string? SealPath = null, string? SealPayloadSha256 = null,
    string? PayloadContentRootSha256 = null, int PayloadFileCount = 0, long PayloadTotalBytes = 0);

public sealed class ReleaseSealService
{
    public const string FormatName = "TrueWebsiteCloner.ReleaseSeal";
    public const string Version = "0.15.0";
    public const string ReleaseDirectoryName = "_release";
    public const string SealFileName = "release-seal.json";
    public const int MaxPayloadFiles = 50_000;
    public const long MaxPayloadBytes = 8L * 1024 * 1024 * 1024;
    private sealed record FileDigest(string Path, long ByteLength, string Sha256);

    public async Task<ReleaseSealResult> CreateAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            projectRoot = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(projectRoot)) return new(false, "Project folder does not exist.");
            var sealPath = Path.Combine(projectRoot, ReleaseDirectoryName, SealFileName);
            if (File.Exists(sealPath)) return new(false, "Release seal already exists; seals are immutable.", sealPath);

            var readiness = await new ReleaseReadinessService().ValidateAsync(projectRoot, cancellationToken);
            if (!readiness.Ok || readiness.Result != "READY") return new(false, "Project is not READY: " + readiness.NextAction, sealPath);
            var snapshots = ReadSnapshotIds(projectRoot);
            if (snapshots.Length == 0) return new(false, "At least one immutable snapshot is required before sealing.", sealPath);

            var completenessPath = Path.Combine(projectRoot, "offline", "completeness-report.json");
            var verificationPath = Path.Combine(projectRoot, "offline", "verification-report.json");
            var visualPath = Path.Combine(projectRoot, "offline", "visual-comparison", "visual-report.json");
            var graphPath = Path.Combine(projectRoot, "offline", "dependency-graph.json");
            var missingPath = Path.Combine(projectRoot, "offline", "missing-resources.json");
            var completeness = ReadDouble(completenessPath, "completenessScore") ?? -1;
            var weighted = ReadDouble(completenessPath, "weightedCompletenessScore") ?? -1;
            var visual = ReadDouble(visualPath, "mismatchPercent") ?? -1;
            if (completeness < 100 || weighted < 100 || visual < 0) return new(false, "Required release metrics are missing or incomplete.", sealPath);

            var evidenceInputs = new[] { ("READINESS", readiness.ReportPath), ("COMPLETENESS", completenessPath),
                ("DEPENDENCY_GRAPH", graphPath), ("VERIFICATION", verificationPath), ("VISUAL", visualPath), ("MISSING_RESOURCES", missingPath) };
            var evidence = new List<ReleaseSealEvidence>();
            foreach (var (code, path) in evidenceInputs)
            {
                if (!File.Exists(path)) return new(false, $"Required seal evidence is missing: {code}.", sealPath);
                evidence.Add(new(code, NormalizeRelative(Path.GetRelativePath(projectRoot, path)), await HashFileAsync(path, cancellationToken)));
            }
            evidence.Sort((a, b) => StringComparer.Ordinal.Compare(a.Code, b.Code));

            var payloadFiles = await DigestPayloadAsync(projectRoot, cancellationToken);
            if (!payloadFiles.Ok) return new(false, payloadFiles.Message, sealPath);
            var payload = new ReleaseSealPayload(Version, readiness.ReleaseFingerprintSha256, payloadFiles.ContentRootSha256!, payloadFiles.FileCount,
                payloadFiles.TotalBytes, snapshots[^1], snapshots, completeness, weighted, visual, evidence.ToArray());
            var payloadHash = HashBytes(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
            var document = new ReleaseSealDocument(FormatName, Version, payloadHash, payload);
            Directory.CreateDirectory(Path.GetDirectoryName(sealPath)!);
            await File.WriteAllTextAsync(sealPath, JsonSerializer.Serialize(document, JsonOptionsIndented), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(sealPath + ".sha256", HashFile(sealPath) + "  " + SealFileName + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
            return new(true, "Verified release seal created.", sealPath, payloadHash, payload.PayloadContentRootSha256, payload.PayloadFileCount, payload.PayloadTotalBytes);
        }
        catch (Exception ex) { return new(false, "Release sealing failed: " + ex.Message); }
    }

    public async Task<ReleaseSealResult> VerifyAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            projectRoot = Path.GetFullPath(projectRoot);
            var sealPath = Path.Combine(projectRoot, ReleaseDirectoryName, SealFileName);
            if (!File.Exists(sealPath)) return new(false, "Release seal does not exist.", sealPath);
            ReleaseSealDocument? document;
            await using (var stream = File.OpenRead(sealPath)) document = await JsonSerializer.DeserializeAsync<ReleaseSealDocument>(stream, JsonOptions, cancellationToken);
            if (document is null || document.Format != FormatName || document.Version != Version) return new(false, "Release seal format/version is invalid.", sealPath);
            var payloadHash = HashBytes(JsonSerializer.SerializeToUtf8Bytes(document.Payload, JsonOptions));
            if (!string.Equals(payloadHash, document.SealPayloadSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "Release seal payload SHA-256 mismatch.", sealPath);

            var readinessPath = Path.Combine(projectRoot, ProjectDiagnosticsService.DiagnosticsDirectoryName, ReleaseReadinessService.ReportFileName);
            if (!File.Exists(readinessPath)) return new(false, "Release-readiness evidence is missing.", sealPath);
            using (var readinessDoc = JsonDocument.Parse(await File.ReadAllTextAsync(readinessPath, cancellationToken)))
            {
                var result = GetString(readinessDoc.RootElement, "result");
                var fingerprint = GetString(readinessDoc.RootElement, "releaseFingerprintSha256");
                if (result != "READY" || !string.Equals(fingerprint, document.Payload.ReadinessFingerprintSha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, "Current readiness evidence does not match the sealed READY fingerprint.", sealPath);
            }

            foreach (var evidence in document.Payload.Evidence)
            {
                var path = SafeProjectPath(projectRoot, evidence.Path);
                if (path is null || !File.Exists(path)) return new(false, "Sealed evidence file is missing: " + evidence.Path, sealPath);
                if (!string.Equals(await HashFileAsync(path, cancellationToken), evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, "Sealed evidence SHA-256 mismatch: " + evidence.Path, sealPath);
            }

            var snapshots = ReadSnapshotIds(projectRoot);
            if (!snapshots.SequenceEqual(document.Payload.SnapshotIds, StringComparer.Ordinal) || snapshots.Length == 0 || snapshots[^1] != document.Payload.PrimarySnapshotId)
                return new(false, "Immutable snapshot set no longer matches the release seal.", sealPath);

            var payloadFiles = await DigestPayloadAsync(projectRoot, cancellationToken);
            if (!payloadFiles.Ok) return new(false, payloadFiles.Message, sealPath);
            if (!string.Equals(payloadFiles.ContentRootSha256, document.Payload.PayloadContentRootSha256, StringComparison.OrdinalIgnoreCase)
                || payloadFiles.FileCount != document.Payload.PayloadFileCount || payloadFiles.TotalBytes != document.Payload.PayloadTotalBytes)
                return new(false, "Project payload content root no longer matches the release seal.", sealPath);

            var packageDir = Path.Combine(projectRoot, "_twc_package");
            if (Directory.Exists(packageDir))
            {
                var importVerification = Path.Combine(packageDir, "import-verification.json");
                if (!File.Exists(importVerification) || !string.Equals(ReadString(importVerification, "result"), "PASS", StringComparison.OrdinalIgnoreCase))
                    return new(false, "Imported sealed project does not have PASS import-integrity evidence.", sealPath);
            }
            return new(true, "Release seal verified.", sealPath, document.SealPayloadSha256, document.Payload.PayloadContentRootSha256,
                document.Payload.PayloadFileCount, document.Payload.PayloadTotalBytes);
        }
        catch (Exception ex) { return new(false, "Release seal verification failed: " + ex.Message); }
    }

    private static async Task<(bool Ok, string Message, string? ContentRootSha256, int FileCount, long TotalBytes)> DigestPayloadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var records = new List<FileDigest>(); long total = 0; var stack = new Stack<string>(); stack.Push(projectRoot);
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop(); var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return (false, "Release payload contains a reparse-point directory.", null, 0, 0);
            foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.Ordinal).Reverse())
            {
                var relative = NormalizeRelative(Path.GetRelativePath(projectRoot, child));
                if (IsExcludedRoot(relative)) continue;
                if ((new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) != 0) return (false, "Release payload contains a reparse-point directory.", null, 0, 0);
                stack.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(projectRoot, file));
                if (IsExcludedRoot(relative)) continue;
                var fileInfo = new FileInfo(file);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0) return (false, "Release payload contains a reparse-point file.", null, 0, 0);
                checked { total += fileInfo.Length; }
                if (total > MaxPayloadBytes) return (false, "Release payload exceeds 8 GiB.", null, 0, 0);
                records.Add(new(relative, fileInfo.Length, await HashFileAsync(file, cancellationToken)));
                if (records.Count > MaxPayloadFiles) return (false, "Release payload exceeds 50,000 files.", null, 0, 0);
            }
        }
        records.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
        var builder = new StringBuilder();
        foreach (var record in records) builder.Append(record.Path).Append('\0').Append(record.ByteLength).Append('\0').Append(record.Sha256).Append('\n');
        return (true, "Payload digested.", HashBytes(Encoding.UTF8.GetBytes(builder.ToString())), records.Count, total);
    }

    private static bool IsExcludedRoot(string relative) { var first = relative.Split('/', 2)[0]; return first.Equals(ReleaseDirectoryName, StringComparison.OrdinalIgnoreCase) || first.Equals("_twc_package", StringComparison.OrdinalIgnoreCase) || first.Equals(ProjectCatalogService.CatalogDirectoryName, StringComparison.OrdinalIgnoreCase); }
    private static string[] ReadSnapshotIds(string projectRoot)
    {
        var history = Path.Combine(projectRoot, "history"); if (!Directory.Exists(history)) return [];
        var result = new List<(string Label, string Id)>();
        foreach (var directory in Directory.EnumerateDirectories(history).OrderBy(path => path, StringComparer.Ordinal))
        {
            if ((new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var path = Path.Combine(directory, "snapshot.json"); if (!File.Exists(path)) continue;
            var id = ReadString(path, "snapshotId"); if (!string.IsNullOrWhiteSpace(id)) result.Add((new DirectoryInfo(directory).Name, id!));
        }
        return result.OrderBy(item => item.Label, StringComparer.Ordinal).Select(item => item.Id).ToArray();
    }
    private static string? SafeProjectPath(string root, string relative) { if (!IsSafeRelative(relative)) return null; var fullRoot = Path.GetFullPath(root); var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar))); var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar; return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null; }
    private static bool IsSafeRelative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && path.Replace('\\', '/').Split('/').All(segment => !string.IsNullOrWhiteSpace(segment) && segment is not "." and not ".." && !segment.Contains(':'));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken) { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true); return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant(); }
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static double? ReadDouble(string path, string property) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : null; } catch { return null; } }
    private static string? ReadString(string path, string property) { try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return GetString(doc.RootElement, property); } catch { return null; } }
    private static string? GetString(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string NormalizeRelative(string path) => path.Replace('\\', '/');
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions JsonOptionsIndented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
