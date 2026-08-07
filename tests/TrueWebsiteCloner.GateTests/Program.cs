using System.Text.Json;
using TrueWebsiteCloner.Core;

var output = Environment.GetEnvironmentVariable("TWC_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.2");
if (Directory.Exists(output)) Directory.Delete(output, true);
Directory.CreateDirectory(output);

var manager = new CaptureSessionManager();
manager.SetProjectRoot(output);

static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

var start = await manager.HandleAsync("capture.start", Json("""{"tabId":77,"targetUrl":"http://127.0.0.1:7843/","title":"Gate Test"}"""));
Require(start.Ok && start.SessionPath is not null, "capture.start failed");

await manager.HandleAsync("capture.request", Json("""{"tabId":77,"requestId":"1","url":"http://127.0.0.1:7843/api/sample","method":"GET","resourceType":"Fetch","timestamp":1.2,"headers":{"Authorization":"Bearer SECRET","Cookie":"SECRET"},"postData":"SECRET-BODY"}"""));
await manager.HandleAsync("capture.response", Json("""{"tabId":77,"requestId":"1","url":"http://127.0.0.1:7843/api/sample","status":200,"statusText":"OK","mimeType":"application/json","resourceType":"Fetch","protocol":"http/1.1","fromDiskCache":false,"fromServiceWorker":false,"encodedDataLength":321,"headers":{"Set-Cookie":"SECRET"}}"""));
await manager.HandleAsync("capture.finished", Json("""{"tabId":77,"requestId":"1","encodedDataLength":321,"timestamp":2.4}"""));
var stop = await manager.HandleAsync("capture.stop", Json("""{"tabId":77,"reason":"gate-test"}"""));

Require(stop.Ok, "capture.stop failed");
Require(stop.EventCount == 3, $"Expected 3 metadata events, got {stop.EventCount}");
var logPath = Path.Combine(start.SessionPath!, "_network", "network.jsonl");
var summaryPath = Path.Combine(start.SessionPath!, "_network", "summary.json");
Require(File.Exists(logPath), "network.jsonl missing");
Require(File.Exists(summaryPath), "summary.json missing");
var lines = await File.ReadAllLinesAsync(logPath);
Require(lines.Length == 3, $"Expected 3 JSONL lines, got {lines.Length}");
var text = await File.ReadAllTextAsync(logPath);
foreach (var forbidden in new[] { "Authorization", "Cookie", "Set-Cookie", "SECRET", "postData" })
    Require(!text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Sensitive/unapproved field leaked: {forbidden}");

Console.WriteLine("PASS  capture session creation");
Console.WriteLine("PASS  request metadata");
Console.WriteLine("PASS  response metadata");
Console.WriteLine("PASS  loading-finished metadata");
Console.WriteLine("PASS  metadata whitelist blocks secrets and bodies");
Console.WriteLine("RESULT: GATE 0.2 CORE PASS");
