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
    Console.Error.WriteLine("Usage: TrueWebsiteCloner.RecoveryTool --capture <capture-folder>");
    return 2;
}

var result = await new MissingResourceRecovery().RecoverAsync(capture);
Console.WriteLine($"Initial missing: {result.InitialMissing}");
Console.WriteLine($"Attempted: {result.Attempted}");
Console.WriteLine($"Recovered: {result.Recovered}");
Console.WriteLine($"Skipped: {result.Skipped}");
Console.WriteLine($"Failed: {result.Failed}");
Console.WriteLine($"Final missing: {result.FinalMissing}");
Console.WriteLine("Report: " + result.ReportPath);
Console.WriteLine(result.Ok && result.Complete ? "RESULT: GATE 0.7 RECOVERY PASS" : "RESULT: GATE 0.7 RECOVERY INCOMPLETE");
return result.Ok && result.Complete ? 0 : 1;
