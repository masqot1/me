using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TrueWebsiteCloner.Core;
using TrueWebsiteCloner.Shared;

namespace TrueWebsiteCloner.Desktop;

public partial class MainWindow : Window
{
    private readonly BridgeServer _bridge = new();
    private readonly OfflineSiteBuilder _offlineBuilder = new();
    private readonly ProjectCatalogService _catalogService = new();
    private readonly ProjectDiagnosticsService _diagnosticsService = new();
    private readonly WorkspacePortableOperations _workspacePortable = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<ProjectCatalogEntry> _catalogEntries = [];
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
            Log("TrueWebsiteCloner v0.13: project diagnostics dashboard ready.");
        }
        catch (Exception ex) { Log("Bridge start failed: " + ex.Message); }

        _chromePath = ChromeLocator.FindChrome();
        ChromePathText.Text = _chromePath ?? "Google Chrome was not detected.";
        LaunchChromeButton.IsEnabled = _chromePath is not null;
        _statusTimer.Start();
        UpdateStatuses();
        await RefreshCatalogAsync();
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

    private async void ChooseProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose TrueWebsiteCloner workspace", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectFolderTextBox.Text = dialog.FolderName;
            Directory.CreateDirectory(dialog.FolderName);
            _bridge.SetProjectRoot(dialog.FolderName);
            Log("Workspace: " + dialog.FolderName);
            await RefreshCatalogAsync();
        }
    }

    private async void RefreshCatalogButton_Click(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();

    private async Task RefreshCatalogAsync()
    {
        var result = await _catalogService.RefreshAsync(ProjectFolderTextBox.Text);
        if (!result.Ok) { Log("CATALOG FAIL: " + result.Message); return; }
        _catalogEntries = result.Projects;
        ProjectCatalogGrid.ItemsSource = _catalogEntries;
        CatalogCountText.Text = $"{_catalogEntries.Count} project{(_catalogEntries.Count == 1 ? string.Empty : "s")}";
        Log($"CATALOG PASS: {_catalogEntries.Count} projects; scanned={result.ScannedDirectories}; skipped reparse={result.SkippedReparsePoints}");
        if (_catalogEntries.Count > 0 && ProjectCatalogGrid.SelectedItem is null) ProjectCatalogGrid.SelectedIndex = 0;
        ShowSelectedDiagnostics();
    }

    private ProjectCatalogEntry? SelectedProject => ProjectCatalogGrid.SelectedItem as ProjectCatalogEntry;
    private string? ActiveProjectPath => SelectedProject?.FullPath ?? _catalogEntries.FirstOrDefault()?.FullPath;

    private void ProjectCatalogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelectedDiagnostics();

    private void ShowSelectedDiagnostics()
    {
        var project = SelectedProject;
        if (project is null)
        {
            DiagnosticsStatusText.Text = "NOT_RUN";
            DiagnosticsNextActionText.Text = "Select a project and run diagnostics.";
            DiagnosticsStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        var path = Path.Combine(project.FullPath, ProjectDiagnosticsService.DiagnosticsDirectoryName, ProjectDiagnosticsService.DiagnosticsFileName);
        if (!File.Exists(path))
        {
            DiagnosticsStatusText.Text = "NOT_RUN";
            DiagnosticsNextActionText.Text = "Run diagnostics for the selected project.";
            DiagnosticsStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var status = root.TryGetProperty("overallStatus", out var statusValue) ? statusValue.GetString() ?? "NOT_RUN" : "NOT_RUN";
            var readiness = root.TryGetProperty("readiness", out var readinessValue) ? readinessValue.GetString() ?? string.Empty : string.Empty;
            var action = root.TryGetProperty("nextAction", out var actionValue) ? actionValue.GetString() ?? "Run diagnostics." : "Run diagnostics.";
            DiagnosticsStatusText.Text = string.IsNullOrWhiteSpace(readiness) ? status : $"{status} · {readiness}";
            DiagnosticsNextActionText.Text = action;
            DiagnosticsStatusText.Foreground = status switch
            {
                "PASS" => System.Windows.Media.Brushes.LightGreen,
                "WARNING" => System.Windows.Media.Brushes.Gold,
                "FAIL" => System.Windows.Media.Brushes.OrangeRed,
                _ => System.Windows.Media.Brushes.Gray
            };
        }
        catch
        {
            DiagnosticsStatusText.Text = "INVALID";
            DiagnosticsNextActionText.Text = "Run diagnostics again to regenerate the health report.";
            DiagnosticsStatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
    }

    private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null) { Log("DIAGNOSTICS: select a project first."); return; }
        var result = await _diagnosticsService.RunAsync(project.FullPath);
        DiagnosticsStatusText.Text = $"{result.OverallStatus} · {result.Readiness}";
        DiagnosticsNextActionText.Text = result.NextAction;
        DiagnosticsStatusText.Foreground = result.OverallStatus switch
        {
            "PASS" => System.Windows.Media.Brushes.LightGreen,
            "WARNING" => System.Windows.Media.Brushes.Gold,
            "FAIL" => System.Windows.Media.Brushes.OrangeRed,
            _ => System.Windows.Media.Brushes.Gray
        };
        Log($"DIAGNOSTICS {result.OverallStatus}: PASS={result.PassCount}, WARNING={result.WarningCount}, FAIL={result.FailCount}, NOT_RUN={result.NotRunCount}");
        foreach (var check in result.Checks.Where(check => check.Status is "FAIL" or "WARNING"))
            Log($"  {check.Status} {check.Code}: {check.Message} → {check.RecommendedAction}");
        Log("Diagnostics report: " + result.ReportPath);
    }

    private void OpenSelectedProjectButton_Click(object sender, RoutedEventArgs e) => OpenSelectedProject();
    private void ProjectCatalogGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedProject();

    private void OpenSelectedProject()
    {
        var project = SelectedProject;
        if (project is null) { Log("CATALOG: select a project first."); return; }
        Process.Start(new ProcessStartInfo("explorer.exe", project.FullPath) { UseShellExecute = true });
    }

    private async void ExportSelectedProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null) { Log("EXPORT: select a project first."); return; }
        var dialog = new SaveFileDialog
        {
            Title = "Export TrueWebsiteCloner portable project",
            Filter = "TrueWebsiteCloner Project (*.twcproj)|*.twcproj|All files (*.*)|*.*",
            FileName = project.Name + ".twcproj",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            AddExtension = true,
            DefaultExt = ".twcproj",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        var result = await _workspacePortable.ExportAsync(project.FullPath, dialog.FileName);
        Log(result.Ok ? $"EXPORT PASS: {result.FileCount} files; SHA-256={result.PackageSha256}; {dialog.FileName}" : "EXPORT FAIL: " + result.Message);
    }

    private async void ImportPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import TrueWebsiteCloner portable project",
            Filter = "TrueWebsiteCloner Project (*.twcproj)|*.twcproj|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        var result = await _workspacePortable.ImportIntoWorkspaceAsync(dialog.FileName, ProjectFolderTextBox.Text);
        if (!result.Ok) { Log("IMPORT FAIL: " + result.Message); return; }
        Log($"IMPORT PASS: integrity verified; destination={result.DestinationPath}");
        await RefreshCatalogAsync();
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
        var capture = ActiveProjectPath;
        if (capture is null) { Log("OFFLINE BUILD: no indexed project was found."); return; }
        await BuildOfflineAsync(capture, openFolder: true);
        await RefreshCatalogAsync();
    }

    private async void StartOfflineRuntimeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var capture = ActiveProjectPath;
            if (capture is null) { Log("LOCAL RUNTIME: no indexed project was found."); return; }
            var build = await BuildOfflineAsync(capture, openFolder: false);
            if (!build) return;
            var exe = DevelopmentLocator.FindLocalRuntimeExe();
            if (exe is null) { Log("LOCAL RUNTIME: executable not found. Run Install-Dev.ps1 first."); return; }
            if (_localRuntimeProcess is { HasExited: false }) { try { _localRuntimeProcess.Kill(entireProcessTree: true); } catch { } }
            var startInfo = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("--capture"); startInfo.ArgumentList.Add(capture);
            startInfo.ArgumentList.Add("--port"); startInfo.ArgumentList.Add("7850");
            _localRuntimeProcess = Process.Start(startInfo);
            if (_localRuntimeProcess is null) { Log("LOCAL RUNTIME: failed to start process."); return; }
            Log("LOCAL RUNTIME: http://127.0.0.1:7850");
            await Task.Delay(900);
            if (_chromePath is not null) Process.Start(new ProcessStartInfo(_chromePath, "http://127.0.0.1:7850/") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("http://127.0.0.1:7850/") { UseShellExecute = true });
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
            if (openFolder && result.OutputRoot is not null) Process.Start(new ProcessStartInfo("explorer.exe", result.OutputRoot) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { Log("OFFLINE BUILD FAIL: " + ex.Message); return false; }
    }

    private void RunFoundationCheckButton_Click(object sender, RoutedEventArgs e)
    {
        var reg = NativeHostRegistration.IsRegistered();
        var seen = _bridge.WasExtensionSeenRecently(TimeSpan.FromSeconds(15));
        var chrome = _chromePath is not null;
        Log("FOUNDATION / WORKSPACE CHECK");
        Log($"  Desktop bridge : {(_bridge.Port > 0 ? "PASS" : "FAIL")}");
        Log($"  Native host    : {(reg ? "PASS" : "FAIL")}");
        Log($"  Chrome         : {(chrome ? "PASS" : "FAIL")}");
        Log($"  Extension link : {(seen ? "PASS" : "FAIL - test the extension connection")}");
        Log($"  Workspace      : {(Directory.Exists(ProjectFolderTextBox.Text) ? "PASS" : "FAIL")}");
        Log($"  Catalog        : {_catalogEntries.Count} project(s)");
    }

    private void Log(string message) => ActivityLogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
}
