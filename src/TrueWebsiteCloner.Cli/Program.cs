using TrueWebsiteCloner.Core;

static string? Option(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static int Usage()
{
    Console.Error.WriteLine("TrueWebsiteCloner.Cli 1.0.0");
    Console.Error.WriteLine("Commands: offline-build, recover, graph, snapshot-create, snapshot-diff, portable-export, portable-verify, portable-import, readiness, seal-create, seal-verify, bundle-create, bundle-verify, bundle-import");
    return 2;
}

if (args.Length == 0) return Usage();
var command = args[0].ToLowerInvariant();

switch (command)
{
    case "offline-build":
    {
        var capture = Option(args, "--capture");
        if (string.IsNullOrWhiteSpace(capture)) return Usage();
        var result = await new OfflineSiteBuilder().BuildAsync(capture);
        Console.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }
    case "recover":
    {
        var capture = Option(args, "--capture");
        if (string.IsNullOrWhiteSpace(capture)) return Usage();
        var result = await new MissingResourceRecovery().RecoverAsync(capture);
        Console.WriteLine(result.Message);
        return result.Ok && result.Complete ? 0 : 1;
    }
    case "graph":
    {
        var capture = Option(args, "--capture");
        if (string.IsNullOrWhiteSpace(capture)) return Usage();
        var result = await new DependencyGraphBuilder().BuildAsync(capture);
        Console.WriteLine(result.Message);
        return result.Ok && result.Complete ? 0 : 1;
    }
    case "snapshot-create":
    {
        var capture = Option(args, "--capture");
        var label = Option(args, "--label");
        if (string.IsNullOrWhiteSpace(capture) || string.IsNullOrWhiteSpace(label)) return Usage();
        var result = await new SnapshotDiffEngine().CreateSnapshotAsync(capture, label);
        Console.WriteLine(result.Message);
        if (result.SnapshotPath is not null) Console.WriteLine("Snapshot: " + result.SnapshotPath);
        return result.Ok ? 0 : 1;
    }
    case "snapshot-diff":
    {
        var before = Option(args, "--before");
        var after = Option(args, "--after");
        var output = Option(args, "--output");
        if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after) || string.IsNullOrWhiteSpace(output)) return Usage();
        var result = await new SnapshotDiffEngine().CompareAsync(before, after, output);
        Console.WriteLine(result.Message);
        Console.WriteLine($"Added={result.Added} Removed={result.Removed} Changed={result.Changed} Unchanged={result.Unchanged}");
        return result.Ok ? 0 : 1;
    }
    case "portable-export":
    {
        var project = Option(args, "--project");
        var output = Option(args, "--output");
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(output)) return Usage();
        var result = await new WorkspacePortableOperations().ExportAsync(project, output);
        Console.WriteLine(result.Message);
        Console.WriteLine("Package SHA-256: " + result.PackageSha256);
        return result.Ok ? 0 : 1;
    }
    case "portable-verify":
    {
        var package = Option(args, "--package");
        if (string.IsNullOrWhiteSpace(package)) return Usage();
        var result = await new PortableProjectPackage().VerifyAsync(package);
        Console.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }
    case "portable-import":
    {
        var package = Option(args, "--package");
        var workspace = Option(args, "--workspace");
        var name = Option(args, "--name");
        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(workspace)) return Usage();
        var result = await new WorkspacePortableOperations().ImportIntoWorkspaceAsync(package, workspace, name);
        Console.WriteLine(result.Message);
        Console.WriteLine("Destination: " + result.DestinationPath);
        return result.Ok ? 0 : 1;
    }
    case "readiness":
    {
        var project = Option(args, "--project");
        if (string.IsNullOrWhiteSpace(project)) return Usage();
        var result = await new ReleaseReadinessService().ValidateAsync(project);
        Console.WriteLine($"Result: {result.Result}");
        Console.WriteLine("Fingerprint: " + result.ReleaseFingerprintSha256);
        Console.WriteLine("Next: " + result.NextAction);
        return result.Result == "READY" ? 0 : 1;
    }
    case "seal-create":
    case "seal-verify":
    {
        var project = Option(args, "--project");
        if (string.IsNullOrWhiteSpace(project)) return Usage();
        var service = new ReleaseSealService();
        var result = command == "seal-create" ? await service.CreateAsync(project) : await service.VerifyAsync(project);
        Console.WriteLine(result.Message);
        Console.WriteLine("Seal SHA-256: " + result.SealPayloadSha256);
        return result.Ok ? 0 : 1;
    }
    case "bundle-create":
    {
        var project = Option(args, "--project");
        var output = Option(args, "--output");
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(output)) return Usage();
        var result = await new ReleaseBundleService().CreateAsync(project, output);
        Console.WriteLine(result.Message);
        Console.WriteLine("Bundle SHA-256: " + result.BundleSha256);
        return result.Ok ? 0 : 1;
    }
    case "bundle-verify":
    {
        var bundle = Option(args, "--bundle");
        if (string.IsNullOrWhiteSpace(bundle)) return Usage();
        var result = await new ReleaseBundleService().VerifyAsync(bundle);
        Console.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }
    case "bundle-import":
    {
        var bundle = Option(args, "--bundle");
        var workspace = Option(args, "--workspace");
        var name = Option(args, "--name");
        if (string.IsNullOrWhiteSpace(bundle) || string.IsNullOrWhiteSpace(workspace)) return Usage();
        var result = await new ReleaseBundleService().ImportAsync(bundle, workspace, name);
        Console.WriteLine(result.Message);
        Console.WriteLine("Destination: " + result.DestinationPath);
        return result.Ok ? 0 : 1;
    }
    default:
        return Usage();
}
