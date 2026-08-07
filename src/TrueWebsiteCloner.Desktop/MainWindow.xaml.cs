using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TrueWebsiteCloner.Core;
using TrueWebsiteCloner.Shared;

namespace TrueWebsiteCloner.Desktop;

public partial class MainWindow : Window
{
    private readonly BridgeServer _bridge = new();
    private readonly OfflineSiteBuilder _offlineBuilder = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _chromePath;
    private Process? _testLabProcess;
    private Process? _localRuntimeProcess;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        _bridge.StateChanged += (_, _) => Dispatcher.Invoke(UpdateStatuses);
        _statusTimer.Tick += (_, _) => UpdateStatuses();
        var configuredRoot = Environment.GetEnvironmentVariable("TWC_PROJECT_ROOT");
        ProjectFolderTextBox.Text = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TrueWebsiteClonerProjects")
            : Path.GetFullPath(configuredRoot);
        Directory.CreateDirectory(ProjectFolderTextBox.Text);
        _bridge.SetProjectRoot(ProjectFolderTextBox.Text);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _bridge.StartAsync();
            Log($"Desktop bridge listening on 127.0.0.1:{_bridge.Port}");
            Log("TrueWebsiteCloner v0.5: capture + offline builder + local replay runtime ready.");
        }
        catch (Exception ex) { Log("Bridge start failed: " + ex.Message); }

        _chromePath = ChromeLocator.FindChrome();
        ChromePathText.Text = _chromePath ?? "Google Chrome was not detected.";
        LaunchChromeButton.IsEnabled = _chromePath is not null;
        _statusTimer.Start();
        UpdateStatuses();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _statusTimer.Stop();
        if (_localRuntimeProcess is { HasExited: false }) { try { _localRuntimeProcess.Kill(entireProcessTree: true); } catch { } }
        if (_testLabProcess is { HasExited: false }) { try { _testLabProcess.Kill(entireProcessTree: true); } catch { } }
        await _bridge.DisposeAsync();
    }

    private void UpdateStatuses()
    {
        BridgeStatusText.Text = _bridge.Port > 0 ? $"READY : {_bridge.Port}" : "OFFLINE";
        BridgeStatusText.Foreground = _bridge.Port > 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.OrangeRed;
        var reg = NativeHostRegistration.IsRegistered();
        NativeHostStatusText.Text = reg ? "REGISTERED" : "NOT REGISTERED";
        NativeHostStatusText.Foreground = reg ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Orange;
        var seen = _bridge.WasExtensionSeenRecently(TimeSpan.FromSeconds(15));
        ExtensionStatusText.Text = seen ? "CONNECTED" : "WAITING";
        ExtensionStatusText.Foreground = seen ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Gray;
        LastMessageText.Text = _bridge.LastMessageSummary;
        FoundationStatusText.Text = _bridge.Port > 0 && reg && seen ? "CAPTURE READY" : "FOUNDATION";
    }

    private void LaunchChromeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_chromePath is not null) Process.Start(new ProcessStartInfo(_chromePath) { UseShellExecute = true });
    }

    private void OpenExtensionFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = DevelopmentLocator.FindExtensionFolder();
        if (folder is null) { MessageBox.Show("Could not locate the chrome-extension folder. Run Install-Dev.ps1 first.", "TrueWebsiteCloner"); return; }
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void ChooseProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose TrueWebsiteCloner projects folder", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectFolderTextBox.Text = dialog.FolderName;
            Directory.CreateDirectory(dialog.FolderName);
            _bridge.SetProjectRoot(dialog.FolderName);
            Log("Project folder: " + dialog.FolderName);
        }
    }

    private void StartTestLabButton_Click(object sender, RoutedEventArgs e)
    {
        var exe = DevelopmentLocator.FindTestLabExe();
        if (exe is null) { MessageBox.Show("Test Lab executable was not found. Run scripts\\Install-Dev.ps1 first.", "TrueWebsiteCloner"); return; }
        if (_testLabProcess is { HasExited: false }) { Log("Test Lab is already running."); return; }
        _testLabProcess = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true });
        Log("Test Lab started: http://127.0.0.1:7843");
    }

    private void OpenTestLabButton_Click(object sender, RoutedEventArgs e)
    {
        if (_chromePath is not null) Process.Start(new ProcessStartInfo(_chromePath, "http://127.0.0.1:7843") { UseShellExecute = true });
        else Process.Start(new ProcessStartInfo("http://127.0.0.1:7843") { UseShellExecute = true });
    }

    private async void BuildOfflineSiteButton_Click(object sender, RoutedEventArgs e)
    {
        var capture = FindLatestCompletedCapture(ProjectFolderTextBox.Text);
        if (capture is null) { Log("OFFLINE BUILD: no completed capture was found."); return; }
        await BuildOfflineAsync(capture, openFolder: true);
    }

    private async void StartOfflineRuntimeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var capture = FindLatestCompletedCapture(ProjectFolderTextBox.Text);
            if (capture is null) { Log("LOCAL RUNTIME: no completed capture was found."); return; }

            var build = await BuildOfflineAsync(capture, openFolder: false);
            if (!build) return;

            var exe = DevelopmentLocator.FindLocalRuntimeExe();
            if (exe is null) { Log("LOCAL RUNTIME: executable not found. Run Install-Dev.ps1 first."); return; }

            if (_localRuntimeProcess is { HasExited: false })
            {
                try { _localRuntimeProcess.Kill(entireProcessTree: true); } catch { }
                _localRuntimeProcess = null;
            }

            var startInfo = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("--capture");
            startInfo.ArgumentList.Add(capture);
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add("7850");
            _localRuntimeProcess = Process.Start(startInfo);
            if (_localRuntimeProcess is null) { Log("LOCAL RUNTIME: failed to start process."); return; }

            Log("LOCAL RUNTIME: http://127.0.0.1:7850");
            await Task.Delay(900);
            if (_chromePath is not null)
                Process.Start(new ProcessStartInfo(_chromePath, "http://127.0.0.1:7850/") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("http://127.0.0.1:7850/") { UseShellExecute = true });
        }
        catch (Exception ex) { Log("LOCAL RUNTIME FAIL: " + ex.Message); }
    }

    private async Task<bool> BuildOfflineAsync(string capture, bool openFolder)
    {
        try
        {
            Log("OFFLINE BUILD: " + capture);
            var result = await _offlineBuilder.BuildAsync(capture);
            if (!result.Ok) { Log("OFFLINE BUILD FAIL: " + result.Message); return false; }
            Log($"OFFLINE BUILD PASS: resources={result.ResourceCount}, rewritten={result.RewrittenReferences}, missing={result.MissingReferences}");
            Log("Offline output: " + result.OutputRoot);
            if (openFolder && result.OutputRoot is not null)
                Process.Start(new ProcessStartInfo("explorer.exe", result.OutputRoot) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { Log("OFFLINE BUILD FAIL: " + ex.Message); return false; }
    }

    private static string? FindLatestCompletedCapture(string projectRoot)
    {
        if (!Directory.Exists(projectRoot)) return null;
        return Directory.EnumerateFiles(projectRoot, "bodies.jsonl", SearchOption.AllDirectories)
            .Where(path => string.Equals(new DirectoryInfo(Path.GetDirectoryName(path)!).Name, "_bodies", StringComparison.OrdinalIgnoreCase))
            .Select(path => Directory.GetParent(Path.GetDirectoryName(path)!)?.FullName)
            .Where(path => path is not null && File.Exists(Path.Combine(path!, "_network", "summary.json")))
            .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path!))
            .FirstOrDefault();
    }

    private void RunFoundationCheckButton_Click(object sender, RoutedEventArgs e)
    {
        var reg = NativeHostRegistration.IsRegistered();
        var seen = _bridge.WasExtensionSeenRecently(TimeSpan.FromSeconds(15));
        var chrome = _chromePath is not null;
        Log("FOUNDATION / CAPTURE CORE CHECK");
        Log($"  Desktop bridge : {(_bridge.Port > 0 ? "PASS" : "FAIL")}");
        Log($"  Native host    : {(reg ? "PASS" : "FAIL")}");
        Log($"  Chrome         : {(chrome ? "PASS" : "FAIL")}");
        Log($"  Extension link : {(seen ? "PASS" : "FAIL - test the extension connection")}");
        Log($"  Project root   : {(Directory.Exists(ProjectFolderTextBox.Text) ? "PASS" : "FAIL")}");
        Log($"  RESULT         : {(_bridge.Port > 0 && reg && chrome && seen ? "PASS" : "NOT READY")}");
    }

    private void Log(string message) => ActivityLogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
}
