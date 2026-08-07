namespace TrueWebsiteCloner.Shared;

public static class AppPaths
{
    public const string NativeHostName = "com.truewebsitecloner.host";
    public const string ProductName = "TrueWebsiteCloner";

    public static string LocalRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    public static string RuntimeDirectory => Path.Combine(LocalRoot, "runtime");
    public static string BridgeInfoPath => Path.Combine(RuntimeDirectory, "bridge-info.json");
    public static string LogsDirectory => Path.Combine(LocalRoot, "logs");
    public static string ProjectsDirectory => Path.Combine(LocalRoot, "projects");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(RuntimeDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
    }
}
