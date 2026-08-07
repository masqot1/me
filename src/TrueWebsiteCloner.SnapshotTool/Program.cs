using TrueWebsiteCloner.Core;

static string? Option(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: SnapshotTool create|diff ...");
    return 2;
}

var engine = new SnapshotDiffEngine();
if (args[0].Equals("create", StringComparison.OrdinalIgnoreCase))
{
    var capture = Option(args, "--capture");
    var label = Option(args, "--label");
    if (string.IsNullOrWhiteSpace(capture) || string.IsNullOrWhiteSpace(label)) return 2;
    var result = await engine.CreateSnapshotAsync(capture, label);
    Console.WriteLine(result.Message);
    if (result.SnapshotPath is not null) Console.WriteLine("Snapshot: " + result.SnapshotPath);
    if (result.SnapshotId is not null) Console.WriteLine("ID: " + result.SnapshotId);
    return result.Ok ? 0 : 1;
}

if (args[0].Equals("diff", StringComparison.OrdinalIgnoreCase))
{
    var before = Option(args, "--before");
    var after = Option(args, "--after");
    var output = Option(args, "--output");
    if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after) || string.IsNullOrWhiteSpace(output)) return 2;
    var result = await engine.CompareAsync(before, after, output);
    Console.WriteLine($"Added={result.Added} Removed={result.Removed} Changed={result.Changed} Unchanged={result.Unchanged}");
    Console.WriteLine(result.Message);
    return result.Ok ? 0 : 1;
}

return 2;
