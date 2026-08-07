using System.Text;
using System.Text.Json;
using TrueWebsiteCloner.Core;

var output = Environment.GetEnvironmentVariable("TWC_BODY_GATE_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-Gate-0.3");
if (Directory.Exists(output)) Directory.Delete(output, true);
Directory.CreateDirectory(output);

var manager = new CaptureSessionManager();
manager.SetProjectRoot(output);

static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

var start = await manager.HandleAsync("capture.start", Json(new { tabId = 88, targetUrl = "http://127.0.0.1:7843/", title = "Body Gate" }));
Require(start.Ok && start.SessionPath is not null, "capture.start failed");

const string jsonBody = "{\"source\":\"test-lab\",\"message\":\"body-gate-marker\"}";
var textBody = await manager.HandleAsync("capture.body", Json(new
{
    tabId = 88,
    requestId = "json-1",
    url = "http://127.0.0.1:7843/api/sample",
    mimeType = "application/json",
    resourceType = "Fetch",
    status = 200,
    base64Encoded = false,
    byteLength = Encoding.UTF8.GetByteCount(jsonBody),
    body = jsonBody,
    headers = new { Authorization = "Bearer SHOULD-NOT-BE-LOGGED" }
}));
Require(textBody.Ok, "same-origin JSON body was rejected");

var binaryBytes = new byte[] { 1, 2, 3, 4, 5, 6 };
var binaryBody = await manager.HandleAsync("capture.body", Json(new
{
    tabId = 88,
    requestId = "image-1",
    url = "http://127.0.0.1:7843/tiny.png",
    mimeType = "image/png",
    resourceType = "Image",
    status = 200,
    base64Encoded = true,
    byteLength = binaryBytes.Length,
    body = Convert.ToBase64String(binaryBytes)
}));
Require(binaryBody.Ok, "same-origin base64 body was rejected");

var crossOrigin = await manager.HandleAsync("capture.body", Json(new
{
    tabId = 88,
    requestId = "cross-origin",
    url = "https://example.com/private.json",
    mimeType = "application/json",
    resourceType = "Fetch",
    status = 200,
    base64Encoded = false,
    body = "CROSS-ORIGIN-MUST-NOT-BE-SAVED"
}));
Require(!crossOrigin.Ok, "cross-origin body should have been rejected");

var oversized = new string('A', CaptureSessionManager.MaxBodyBytes + 1);
var tooLarge = await manager.HandleAsync("capture.body", Json(new
{
    tabId = 88,
    requestId = "oversized",
    url = "http://127.0.0.1:7843/oversized.txt",
    mimeType = "text/plain",
    resourceType = "Fetch",
    status = 200,
    base64Encoded = false,
    body = oversized
}));
Require(!tooLarge.Ok, "oversized body should have been rejected");

var stop = await manager.HandleAsync("capture.stop", Json(new { tabId = 88, reason = "body-gate" }));
Require(stop.Ok, "capture.stop failed");

var bodiesDir = Path.Combine(start.SessionPath!, "_bodies");
var bodyFiles = Directory.GetFiles(bodiesDir).Where(path => !path.EndsWith("bodies.jsonl", StringComparison.OrdinalIgnoreCase)).ToArray();
Require(bodyFiles.Length == 2, $"Expected exactly 2 body files, got {bodyFiles.Length}");
var jsonFile = bodyFiles.Single(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
var pngFile = bodyFiles.Single(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
Require(await File.ReadAllTextAsync(jsonFile) == jsonBody, "JSON body content mismatch");
Require((await File.ReadAllBytesAsync(pngFile)).SequenceEqual(binaryBytes), "Base64 binary body decode mismatch");

var networkLog = await File.ReadAllTextAsync(Path.Combine(start.SessionPath!, "_network", "network.jsonl"));
Require(!networkLog.Contains("body-gate-marker", StringComparison.Ordinal), "Response body leaked into network.jsonl");
Require(!networkLog.Contains("SHOULD-NOT-BE-LOGGED", StringComparison.Ordinal), "Authorization test value leaked into network.jsonl");
Require(!networkLog.Contains("CROSS-ORIGIN-MUST-NOT-BE-SAVED", StringComparison.Ordinal), "Cross-origin body leaked into network.jsonl");

using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(start.SessionPath!, "_network", "summary.json")));
Require(summary.RootElement.GetProperty("bodyCount").GetInt32() == 2, "summary bodyCount must equal 2");
Require(summary.RootElement.GetProperty("bodyBytes").GetInt64() > 0, "summary bodyBytes must be positive");

Console.WriteLine("PASS  same-origin text response body");
Console.WriteLine("PASS  base64 binary response body");
Console.WriteLine("PASS  cross-origin body rejected");
Console.WriteLine("PASS  512 KiB body limit enforced");
Console.WriteLine("PASS  body content excluded from network.jsonl");
Console.WriteLine("RESULT: GATE 0.3 CORE PASS");
