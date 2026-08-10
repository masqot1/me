using System.Text.Json;
using TrueWebsiteCloner.Core;

static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-HeaderGate-" + Guid.NewGuid().ToString("N"));
var captureRoot = Path.Combine(tempRoot, "example.test", "capture-gate-1.3");
Directory.CreateDirectory(Path.Combine(captureRoot, "_network"));

try
{
    var manager = new SafeHeaderCaptureManager();
    const int tabId = 103;
    var registered = await manager.RegisterAsync(Json(new
    {
        tabId,
        targetUrl = "https://example.test/app"
    }), captureRoot);
    Require(registered.Ok, "Header capture session did not register.");

    const string authSecret = "Bearer AUTHORIZATION-MUST-NOT-PERSIST";
    const string cookieSecret = "sid=COOKIE-MUST-NOT-PERSIST";
    const string apiKeySecret = "APIKEY-MUST-NOT-PERSIST";
    const string newlineSecret = "NEWLINE-MUST-NOT-PERSIST";
    var oversizedSecret = "OVERSIZED-MUST-NOT-PERSIST-" + new string('X', SafeHeaderCaptureManager.MaxHeaderValueBytes + 64);

    var request = await manager.HandleAsync("capture.request.headers", Json(new
    {
        tabId,
        requestId = "request-1",
        url = "https://example.test/api/echo",
        method = "POST",
        resourceType = "Fetch",
        timestamp = 1.25,
        headers = new Dictionary<string, object>
        {
            ["Accept"] = "application/json",
            ["CONTENT-TYPE"] = "application/json",
            ["Cache-Control"] = "no-cache",
            ["Authorization"] = authSecret,
            ["Cookie"] = cookieSecret,
            ["X-API-Key"] = apiKeySecret,
            ["Pragma"] = $"no-cache\r\n{newlineSecret}"
        }
    }));
    Require(request.Ok, "Safe request headers were rejected.");
    Require(request.AcceptedHeaderCount == 3, $"Expected 3 accepted request headers, got {request.AcceptedHeaderCount}.");
    Require(request.DroppedHeaderCount == 4, $"Expected 4 dropped request headers, got {request.DroppedHeaderCount}.");

    var response = await manager.HandleAsync("capture.response.headers", Json(new
    {
        tabId,
        requestId = "request-1",
        url = "https://example.test/api/echo",
        status = 200,
        resourceType = "Fetch",
        timestamp = 1.75,
        headers = new Dictionary<string, object>
        {
            ["Content-Type"] = "application/json; charset=utf-8",
            ["ETag"] = "\"gate-1.3\"",
            ["Content-Language"] = "en",
            ["Set-Cookie"] = "twc=SET-COOKIE-MUST-NOT-PERSIST",
            ["X-Auth-Token"] = "TOKEN-MUST-NOT-PERSIST",
            ["Server"] = "SERVER-MUST-NOT-PERSIST"
        }
    }));
    Require(response.Ok, "Safe response headers were rejected.");
    Require(response.AcceptedHeaderCount == 3, $"Expected 3 accepted response headers, got {response.AcceptedHeaderCount}.");
    Require(response.DroppedHeaderCount == 3, $"Expected 3 dropped response headers, got {response.DroppedHeaderCount}.");

    var oversized = await manager.HandleAsync("capture.request.headers", Json(new
    {
        tabId,
        requestId = "request-oversized",
        url = "https://example.test/api/large",
        method = "GET",
        resourceType = "Fetch",
        timestamp = 2.0,
        headers = new Dictionary<string, object>
        {
            ["Accept"] = oversizedSecret,
            ["Accept-Language"] = "en-US"
        }
    }));
    Require(oversized.Ok, "Bounded-header event was rejected instead of dropping oversized value.");
    Require(oversized.AcceptedHeaderCount == 1, "Oversized header value was not dropped independently.");
    Require(oversized.DroppedHeaderCount == 1, "Oversized header drop was not counted.");

    var crossOrigin = await manager.HandleAsync("capture.request.headers", Json(new
    {
        tabId,
        requestId = "cross-origin",
        url = "https://other.test/api",
        method = "GET",
        resourceType = "Fetch",
        headers = new Dictionary<string, object> { ["Accept"] = "application/json" }
    }));
    Require(!crossOrigin.Ok, "Cross-origin header event was not rejected.");

    var missingHeaders = await manager.HandleAsync("capture.request.headers", Json(new
    {
        tabId,
        requestId = "missing-headers",
        url = "https://example.test/api",
        method = "GET",
        resourceType = "Fetch"
    }));
    Require(!missingHeaders.Ok, "Header event without headers object was not rejected.");

    var networkPath = Path.Combine(captureRoot, "_network", "network.jsonl");
    var policyPath = Path.Combine(captureRoot, "_network", "header-policy.json");
    Require(File.Exists(networkPath), "network.jsonl was not created.");
    Require(File.Exists(policyPath), "header-policy.json was not created.");

    var networkText = await File.ReadAllTextAsync(networkPath);
    var policyText = await File.ReadAllTextAsync(policyPath);
    Require(networkText.Contains("capture.request.headers", StringComparison.Ordinal), "Request header event missing from network log.");
    Require(networkText.Contains("capture.response.headers", StringComparison.Ordinal), "Response header event missing from network log.");
    Require(networkText.Contains("\"content-type\"", StringComparison.Ordinal), "Allowed content-type header missing.");
    Require(networkText.Contains("\"etag\"", StringComparison.Ordinal), "Allowed etag header missing.");
    Require(networkText.Contains("\"accept-language\"", StringComparison.Ordinal), "Allowed accept-language header missing.");

    foreach (var forbidden in new[]
    {
        authSecret,
        cookieSecret,
        apiKeySecret,
        newlineSecret,
        oversizedSecret,
        "SET-COOKIE-MUST-NOT-PERSIST",
        "TOKEN-MUST-NOT-PERSIST",
        "SERVER-MUST-NOT-PERSIST",
        "https://other.test/api"
    })
    {
        Require(!networkText.Contains(forbidden, StringComparison.Ordinal), $"Forbidden header data leaked into network.jsonl: {forbidden[..Math.Min(forbidden.Length, 40)]}");
    }

    Require(!networkText.Contains("\"authorization\"", StringComparison.OrdinalIgnoreCase), "Authorization header name leaked.");
    Require(!networkText.Contains("\"cookie\"", StringComparison.OrdinalIgnoreCase), "Cookie header name leaked.");
    Require(!networkText.Contains("\"set-cookie\"", StringComparison.OrdinalIgnoreCase), "Set-Cookie header name leaked.");
    Require(!networkText.Contains("\"x-api-key\"", StringComparison.OrdinalIgnoreCase), "API key header name leaked.");

    using var policy = JsonDocument.Parse(policyText);
    var root = policy.RootElement;
    Require(root.GetProperty("sameOriginOnly").GetBoolean(), "Header policy same-origin enforcement missing.");
    Require(!root.GetProperty("unknownHeadersPersisted").GetBoolean(), "Unknown headers must not persist.");
    Require(!root.GetProperty("sensitiveHeadersPersisted").GetBoolean(), "Sensitive headers must not persist.");
    Require(!root.GetProperty("rawHeaderBlocksPersisted").GetBoolean(), "Raw header blocks must not persist.");
    Require(root.GetProperty("maxHeaderValueBytes").GetInt32() == SafeHeaderCaptureManager.MaxHeaderValueBytes, "Header value limit missing from policy.");
    Require(root.GetProperty("maxHeaderTotalBytes").GetInt32() == SafeHeaderCaptureManager.MaxHeaderTotalBytes, "Header total limit missing from policy.");

    manager.Unregister(Json(new { tabId }));
    var afterStop = await manager.HandleAsync("capture.request.headers", Json(new
    {
        tabId,
        requestId = "after-stop",
        url = "https://example.test/api",
        method = "GET",
        resourceType = "Fetch",
        headers = new Dictionary<string, object> { ["Accept"] = "application/json" }
    }));
    Require(!afterStop.Ok, "Header event was accepted after session unregister.");

    Console.WriteLine("PASS  exact request/response header allowlists");
    Console.WriteLine("PASS  Authorization/Cookie/Set-Cookie/API-key exclusion");
    Console.WriteLine("PASS  per-value and aggregate-safe bounded storage");
    Console.WriteLine("PASS  cross-origin header rejection");
    Console.WriteLine("PASS  normalized safe headers persisted without raw blocks");
    Console.WriteLine("RESULT: TrueWebsiteCloner Safe HTTP Header Gate 1.3 PASS");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
