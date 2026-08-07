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
    Console.Error.WriteLine("Usage: TrueWebsiteCloner.OfflineTool --capture <capture-folder>");
    return 2;
}

var result = await new OfflineSiteBuilder().BuildAsync(capture);
Console.WriteLine(result.Ok ? "RESULT: OFFLINE BUILD PASS" : "RESULT: OFFLINE BUILD FAIL");
Console.WriteLine(result.Message);
if (result.OutputRoot is not null) Console.WriteLine("Output: " + result.OutputRoot);
return result.Ok ? 0 : 1;
