using System.IO;

namespace TrueWebsiteCloner.Desktop;

public static class DevelopmentLocator
{
    public static string? FindExtensionFolder()
    {
        var artifacts = FindArtifactsRoot();
        var root = artifacts is null ? null : Directory.GetParent(artifacts)?.FullName;
        var candidate = root is null ? null : Path.Combine(root, "chrome-extension");
        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }

    public static string? FindTestLabExe()
    {
        var artifacts = FindArtifactsRoot();
        var candidate = artifacts is null ? null : Path.Combine(artifacts, "testlab", "TrueWebsiteCloner.TestLab.exe");
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    public static string? FindLocalRuntimeExe()
    {
        var artifacts = FindArtifactsRoot();
        var candidate = artifacts is null ? null : Path.Combine(artifacts, "local-runtime", "TrueWebsiteCloner.LocalRuntime.exe");
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    private static string? FindArtifactsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
        {
            if (string.Equals(dir.Name, "artifacts", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
        }
        return null;
    }
}
