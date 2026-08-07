using System.Text.Json;
using TrueWebsiteCloner.Core;

var root = Environment.GetEnvironmentVariable("TWC_OFFLINE_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.4");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(Path.Combine(root, "_network"));
Directory.CreateDirectory(Path.Combine(root, "_bodies"));

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

await File.WriteAllTextAsync(Path.Combine(root, "_network", "session.json"), """
{
  "version": "0.3.0",
  "targetUrl": "http://127.0.0.1:7843/",
  "targetOrigin": "http://127.0.0.1:7843"
}
""");

var bodies = new[]
{
    ("doc", "http://127.0.0.1:7843/", "text/html", "Document", "_bodies/0001-doc.html", """<!doctype html><html><head><link rel="stylesheet" href="/styles.css"></head><body><img src="/images/logo.svg"><a href="/missing.html">Missing</a><script src="/app.js"></script></body></html>"""),
    ("css", "http://127.0.0.1:7843/styles.css", "text/css", "Stylesheet", "_bodies/0002-css.css", "body{background-image:url('/images/logo.svg')}"),
    ("js", "http://127.0.0.1:7843/app.js", "text/javascript", "Script", "_bodies/0003-js.js", "async function run(){return fetch('/api/sample')}"),
    ("svg", "http://127.0.0.1:7843/images/logo.svg", "image/svg+xml", "Image", "_bodies/0004-logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>LOGO</text></svg>"),
    ("api", "http://127.0.0.1:7843/api/sample", "application/json", "Fetch", "_bodies/0005-api.json", "{\"source\":\"test-lab\"}")
};

var lines = new List<string>();
foreach (var body in bodies)
{
    var filePath = Path.Combine(root, body.Item5.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    await File.WriteAllTextAsync(filePath, body.Item6);
    lines.Add(JsonSerializer.Serialize(new
    {
        requestId = body.Item1,
        url = body.Item2,
        mimeType = body.Item3,
        resourceType = body.Item4,
        status = 200,
        base64Encoded = false,
        byteLength = body.Item6.Length,
        file = body.Item5
    }));
}
await File.WriteAllLinesAsync(Path.Combine(root, "_bodies", "bodies.jsonl"), lines);

var builder = new OfflineSiteBuilder();
var result = await builder.BuildAsync(root);
Require(result.Ok, result.Message);
Require(result.ResourceCount == 5, $"Expected 5 resources, got {result.ResourceCount}");
Require(result.RewrittenReferences == 4, $"Expected 4 rewritten references, got {result.RewrittenReferences}");
Require(result.MissingReferences == 1, $"Expected 1 missing reference, got {result.MissingReferences}");

var site = Path.Combine(root, "offline", "site");
foreach (var file in new[] { "index.html", "styles.css", "app.js", "images/logo.svg", "api/sample.json" })
    Require(File.Exists(Path.Combine(site, file.Replace('/', Path.DirectorySeparatorChar))), $"Offline file missing: {file}");

var html = await File.ReadAllTextAsync(Path.Combine(site, "index.html"));
Require(html.Contains("href=\"styles.css\""), "HTML stylesheet path was not rewritten");
Require(html.Contains("src=\"images/logo.svg\""), "HTML image path was not rewritten");
Require(html.Contains("src=\"app.js\""), "HTML script path was not rewritten");
Require(html.Contains("href=\"/missing.html\""), "Missing same-origin reference should remain unchanged");

var css = await File.ReadAllTextAsync(Path.Combine(site, "styles.css"));
Require(css.Contains("url('images/logo.svg')"), "CSS url() path was not rewritten");

var js = await File.ReadAllTextAsync(Path.Combine(site, "app.js"));
Require(js.Contains("fetch('/api/sample')"), "JavaScript must remain untouched in Gate 0.4");

var missing = await File.ReadAllTextAsync(Path.Combine(root, "offline", "missing-resources.json"));
Require(missing.Contains("/missing.html"), "Missing-resource report did not contain /missing.html");

var manifestPath = Path.Combine(root, "offline", "offline-manifest.json");
var manifestFirst = await File.ReadAllTextAsync(manifestPath);
var second = await builder.BuildAsync(root);
Require(second.Ok, second.Message);
var manifestSecond = await File.ReadAllTextAsync(manifestPath);
Require(manifestFirst == manifestSecond, "Offline manifest is not deterministic across repeated builds");

Console.WriteLine("PASS  deterministic URL-to-local-path mapping");
Console.WriteLine("PASS  HTML src/href rewriting");
Console.WriteLine("PASS  CSS url() rewriting");
Console.WriteLine("PASS  missing same-origin resource reporting");
Console.WriteLine("PASS  JavaScript left unchanged for next-stage API replay");
Console.WriteLine("PASS  deterministic repeated build");
Console.WriteLine("RESULT: GATE 0.4 PASS");
