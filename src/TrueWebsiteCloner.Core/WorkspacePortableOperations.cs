namespace TrueWebsiteCloner.Core;

public sealed class WorkspacePortableOperations
{
    private readonly PortableProjectPackage _portable = new();

    public async Task<PortableExportResult> ExportAsync(string projectRoot, string packagePath, CancellationToken cancellationToken = default)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(Path.Combine(projectRoot, "_twc_package")))
            return await _portable.ExportAsync(projectRoot, packagePath, cancellationToken);

        var staging = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner", "portable-export", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            CopyProjectWithoutPackageMetadata(projectRoot, staging);
            return await _portable.ExportAsync(staging, packagePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new(false, "Workspace export failed: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    public async Task<PortableImportResult> ImportIntoWorkspaceAsync(
        string packagePath,
        string workspaceRoot,
        string? preferredName = null,
        CancellationToken cancellationToken = default)
    {
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(workspaceRoot)) Directory.CreateDirectory(workspaceRoot);
        if ((new DirectoryInfo(workspaceRoot).Attributes & FileAttributes.ReparsePoint) != 0)
            return new(false, "Workspace root cannot be a reparse point.");

        var rawName = string.IsNullOrWhiteSpace(preferredName) ? Path.GetFileNameWithoutExtension(packagePath) : preferredName;
        var safeName = SafeName(rawName!);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "imported-project";
        var destination = Path.Combine(workspaceRoot, safeName);
        var index = 2;
        while (Directory.Exists(destination) || File.Exists(destination))
            destination = Path.Combine(workspaceRoot, safeName + "-" + index++);

        return await _portable.ImportAsync(packagePath, destination, cancellationToken);
    }

    private static void CopyProjectWithoutPackageMetadata(string sourceRoot, string destinationRoot)
    {
        var sourceFull = Path.GetFullPath(sourceRoot);
        var stack = new Stack<(string Source, string Destination)>();
        stack.Push((sourceFull, destinationRoot));

        while (stack.Count > 0)
        {
            var (source, destination) = stack.Pop();
            var info = new DirectoryInfo(source);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse-point directory rejected: " + source);
            Directory.CreateDirectory(destination);

            foreach (var directory in Directory.EnumerateDirectories(source).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var childInfo = new DirectoryInfo(directory);
                if ((childInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse-point directory rejected: " + directory);
                if (string.Equals(source, sourceFull, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(childInfo.Name, "_twc_package", StringComparison.OrdinalIgnoreCase))
                    continue;
                stack.Push((directory, Path.Combine(destination, childInfo.Name)));
            }

            foreach (var file in Directory.EnumerateFiles(source).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(file);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse-point file rejected: " + file);
                File.Copy(file, Path.Combine(destination, fileInfo.Name), overwrite: false);
            }
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Trim().Take(100).Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        return new string(chars).Trim('.', ' ');
    }
}
