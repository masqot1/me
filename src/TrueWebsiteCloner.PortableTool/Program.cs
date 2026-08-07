using TrueWebsiteCloner.Core;

static string? Option(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: PortableTool export|verify|import ...");
    return 2;
}

var tool = new PortableProjectPackage();
var command = args[0].ToLowerInvariant();
if (command == "export")
{
    var project = Option(args, "--project");
    var output = Option(args, "--output");
    if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(output)) return 2;
    var result = await tool.ExportAsync(project, output);
    Console.WriteLine(result.Message);
    Console.WriteLine($"Files: {result.FileCount} Bytes: {result.TotalBytes}");
    Console.WriteLine("Package SHA-256: " + result.PackageSha256);
    Console.WriteLine("Content Root SHA-256: " + result.ContentRootSha256);
    return result.Ok ? 0 : 1;
}
if (command == "verify")
{
    var package = Option(args, "--package");
    if (string.IsNullOrWhiteSpace(package)) return 2;
    var result = await tool.VerifyAsync(package);
    Console.WriteLine(result.Message);
    Console.WriteLine($"Files: {result.FileCount} Bytes: {result.TotalBytes}");
    Console.WriteLine("Package SHA-256: " + result.PackageSha256);
    Console.WriteLine("Content Root SHA-256: " + result.ContentRootSha256);
    return result.Ok ? 0 : 1;
}
if (command == "import")
{
    var package = Option(args, "--package");
    var destination = Option(args, "--destination");
    if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(destination)) return 2;
    var result = await tool.ImportAsync(package, destination);
    Console.WriteLine(result.Message);
    if (result.DestinationPath is not null) Console.WriteLine("Destination: " + result.DestinationPath);
    return result.Ok ? 0 : 1;
}
return 2;
