using TrueWebsiteCloner.Core;

static string? Option(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

var capture = Option(args, "--capture");
if (string.IsNullOrWhiteSpace(capture))
{
    Console.Error.WriteLine("Usage: TrueWebsiteCloner.GraphTool --capture <capture-folder>");
    return 2;
}

var result = await new DependencyGraphBuilder().BuildAsync(capture);
Console.WriteLine($"Nodes: {result.NodeCount}");
Console.WriteLine($"Edges: {result.EdgeCount}");
Console.WriteLine($"Missing dependencies: {result.MissingDependencies}");
Console.WriteLine($"Completeness: {result.CompletenessScore:0.00}%");
Console.WriteLine($"Weighted completeness: {result.WeightedCompletenessScore:0.00}%");
Console.WriteLine("Graph: " + result.GraphPath);
Console.WriteLine("Completeness report: " + result.CompletenessPath);
Console.WriteLine(result.Ok && result.Complete ? "RESULT: GATE 0.8 GRAPH PASS" : "RESULT: GATE 0.8 GRAPH INCOMPLETE");
return result.Ok && result.Complete ? 0 : 1;
