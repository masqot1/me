using TrueWebsiteCloner.Desktop;

var projectRoot = Environment.GetEnvironmentVariable("TWC_PROJECT_ROOT");
if (string.IsNullOrWhiteSpace(projectRoot))
    projectRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-BridgeHarness");

Directory.CreateDirectory(projectRoot);
await using var bridge = new BridgeServer();
bridge.SetProjectRoot(projectRoot);
await bridge.StartAsync();

Console.WriteLine($"BRIDGE_READY port={bridge.Port} root={projectRoot}");
Console.Out.Flush();

await Task.Delay(Timeout.InfiniteTimeSpan);
