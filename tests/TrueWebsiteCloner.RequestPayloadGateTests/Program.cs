using System.Text;
using System.Text.Json;
using TrueWebsiteCloner.Core;

static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "TrueWebsiteCloner-RequestPayloadGate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var manager = new CaptureSessionManager();
    manager.SetProjectRoot(tempRoot);
    const int tabId = 91;

    var start = await manager.HandleAsync("capture.start", Json(new
    {
        tabId,
        targetUrl = "https://example.test/app",
        title = "Gate 1.2"
    }));
    Require(start.Ok && !string.IsNullOrWhiteSpace(start.SessionPath), "Capture session did not start.");

    const string passwordSecret = "super-secret-password";
    const string accessTokenSecret = "access-token-secret";
    const string clientSecret = "client-secret-value";
    var jsonBody = JsonSerializer.Serialize(new
    {
        username = "alice",
        password = passwordSecret,
        nested = new
        {
            access_token = accessTokenSecret,
            keep = "visible-value"
        },
        items = new[]
        {
            new { client_secret = clientSecret, value = 1 }
        }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    var jsonPayload = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "xhr-json-1",
        url = "https://example.test/api/login",
        method = "POST",
        resourceType = "Fetch",
        contentType = "application/json; charset=utf-8",
        payloadCaptured = true,
        byteLength = Encoding.UTF8.GetByteCount(jsonBody),
        body = jsonBody,
        timestamp = 1.5,
        headers = new
        {
            Authorization = "Bearer HEADER-MUST-NOT-PERSIST",
            Cookie = "sid=COOKIE-MUST-NOT-PERSIST"
        }
    }));
    Require(jsonPayload.Ok, "Valid JSON Fetch payload was rejected.");

    const string formPassword = "form-password-secret";
    const string formApiKey = "form-api-key-secret";
    var formBody = $"email=alice%40example.test&password={Uri.EscapeDataString(formPassword)}&api_key={Uri.EscapeDataString(formApiKey)}&note=keep-me";
    var formPayload = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "xhr-form-1",
        url = "https://example.test/api/profile",
        method = "PATCH",
        resourceType = "XHR",
        contentType = "application/x-www-form-urlencoded",
        payloadCaptured = true,
        byteLength = Encoding.UTF8.GetByteCount(formBody),
        body = formBody,
        timestamp = 2.0
    }));
    Require(formPayload.Ok, "Valid form XHR payload was rejected.");

    var unsupportedMetadata = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "multipart-1",
        url = "https://example.test/api/upload",
        method = "POST",
        resourceType = "Fetch",
        contentType = "multipart/form-data",
        payloadCaptured = false,
        byteLength = 2048,
        body = "RAW-FILE-SECRET-MUST-NOT-PERSIST",
        reason = "unsupported-content-type",
        timestamp = 2.5
    }));
    Require(unsupportedMetadata.Ok, "Unsupported-content metadata should be retained without body content.");

    var oversizedMetadata = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "oversized-meta-1",
        url = "https://example.test/api/bulk",
        method = "PUT",
        resourceType = "Fetch",
        contentType = "application/json",
        payloadCaptured = false,
        byteLength = CaptureSessionManager.MaxRequestPayloadBytes + 4096,
        body = "OVERSIZED-METADATA-BODY-MUST-NOT-PERSIST",
        reason = "payload-too-large",
        timestamp = 3.0
    }));
    Require(oversizedMetadata.Ok, "Oversized payload metadata should be retained without body content.");

    var crossOrigin = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "cross-origin-1",
        url = "https://other.test/api",
        method = "POST",
        resourceType = "Fetch",
        contentType = "application/json",
        payloadCaptured = true,
        byteLength = 2,
        body = "{}"
    }));
    Require(!crossOrigin.Ok, "Cross-origin request payload was not rejected.");

    var unsupportedCaptured = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "plain-1",
        url = "https://example.test/api/plain",
        method = "POST",
        resourceType = "Fetch",
        contentType = "text/plain",
        payloadCaptured = true,
        byteLength = 5,
        body = "hello"
    }));
    Require(!unsupportedCaptured.Ok, "Unsupported captured content type was not rejected.");

    var wrongResourceType = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "document-1",
        url = "https://example.test/form",
        method = "POST",
        resourceType = "Document",
        contentType = "application/json",
        payloadCaptured = true,
        byteLength = 2,
        body = "{}"
    }));
    Require(!wrongResourceType.Ok, "Non-XHR/Fetch request payload was not rejected.");

    var wrongMethod = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "get-1",
        url = "https://example.test/api/query",
        method = "GET",
        resourceType = "Fetch",
        contentType = "application/json",
        payloadCaptured = true,
        byteLength = 2,
        body = "{}"
    }));
    Require(!wrongMethod.Ok, "GET request payload was not rejected.");

    const string malformedJson = "{not-valid-json}";
    var malformed = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "malformed-json-1",
        url = "https://example.test/api/malformed",
        method = "POST",
        resourceType = "Fetch",
        contentType = "application/json",
        payloadCaptured = true,
        byteLength = Encoding.UTF8.GetByteCount(malformedJson),
        body = malformedJson
    }));
    Require(!malformed.Ok, "Malformed JSON payload was persisted instead of rejected.");

    var oversizedBody = "{\"data\":\"" + new string('A', CaptureSessionManager.MaxRequestPayloadBytes) + "\"}";
    var oversizedCaptured = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "oversized-captured-1",
        url = "https://example.test/api/huge",
        method = "POST",
        resourceType = "Fetch",
        contentType = "application/json",
        payloadCaptured = true,
        byteLength = Encoding.UTF8.GetByteCount(oversizedBody),
        body = oversizedBody
    }));
    Require(!oversizedCaptured.Ok, "Oversized captured request payload was not rejected.");

    const string mismatchBody = "{\"safe\":true}";
    var mismatch = await manager.HandleAsync("capture.request.payload", Json(new
    {
        tabId,
        requestId = "length-mismatch-1",
        url = "https://example.test/api/mismatch",
        method = "POST",
        resourceType = "Fetch",
        contentType = "application/json",
        payloadCaptured = true,
        byteLength = Encoding.UTF8.GetByteCount(mismatchBody) + 1,
        body = mismatchBody
    }));
    Require(!mismatch.Ok, "Request payload byteLength mismatch was not rejected.");

    var stop = await manager.HandleAsync("capture.stop", Json(new
    {
        tabId,
        reason = "gate-1.2"
    }));
    Require(stop.Ok, "Capture session did not stop cleanly.");

    var requestDir = Path.Combine(start.SessionPath!, "_requests");
    var manifestPath = Path.Combine(requestDir, "request-payloads.jsonl");
    var networkPath = Path.Combine(start.SessionPath!, "_network", "network.jsonl");
    var summaryPath = Path.Combine(start.SessionPath!, "_network", "summary.json");
    var sessionPath = Path.Combine(start.SessionPath!, "_network", "session.json");

    Require(File.Exists(manifestPath), "request-payloads.jsonl was not created.");
    Require(File.Exists(networkPath), "network.jsonl was not created.");
    Require(File.Exists(summaryPath), "summary.json was not created.");
    Require(File.Exists(sessionPath), "session.json was not created.");

    var payloadFiles = Directory.GetFiles(requestDir)
        .Where(path => !path.EndsWith("request-payloads.jsonl", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    Require(payloadFiles.Length == 2, $"Expected exactly 2 persisted request payload files, got {payloadFiles.Length}.");

    var jsonFile = payloadFiles.Single(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    var formFile = payloadFiles.Single(path => path.EndsWith(".form", StringComparison.OrdinalIgnoreCase));
    var persistedJson = await File.ReadAllTextAsync(jsonFile);
    var persistedForm = await File.ReadAllTextAsync(formFile);

    Require(persistedJson.Contains("alice", StringComparison.Ordinal), "Non-sensitive JSON field was lost.");
    Require(persistedJson.Contains("visible-value", StringComparison.Ordinal), "Nested non-sensitive JSON field was lost.");
    Require(persistedJson.Contains("[REDACTED]", StringComparison.Ordinal), "JSON sensitive fields were not redacted.");
    Require(!persistedJson.Contains(passwordSecret, StringComparison.Ordinal), "Password leaked into persisted JSON payload.");
    Require(!persistedJson.Contains(accessTokenSecret, StringComparison.Ordinal), "Access token leaked into persisted JSON payload.");
    Require(!persistedJson.Contains(clientSecret, StringComparison.Ordinal), "Client secret leaked into persisted JSON payload.");

    Require(persistedForm.Contains("email=alice%40example.test", StringComparison.Ordinal), "Non-sensitive form field was lost.");
    Require(persistedForm.Contains("note=keep-me", StringComparison.Ordinal), "Non-sensitive form note was lost.");
    Require(persistedForm.Contains("password=%5BREDACTED%5D", StringComparison.Ordinal), "Form password was not redacted.");
    Require(persistedForm.Contains("api_key=%5BREDACTED%5D", StringComparison.Ordinal), "Form API key was not redacted.");
    Require(!persistedForm.Contains(formPassword, StringComparison.Ordinal), "Form password leaked into persisted payload.");
    Require(!persistedForm.Contains(formApiKey, StringComparison.Ordinal), "Form API key leaked into persisted payload.");

    var manifestText = await File.ReadAllTextAsync(manifestPath);
    var networkText = await File.ReadAllTextAsync(networkPath);
    var persistedText = manifestText + "\n" + networkText + "\n" + persistedJson + "\n" + persistedForm;
    foreach (var forbidden in new[]
    {
        passwordSecret,
        accessTokenSecret,
        clientSecret,
        formPassword,
        formApiKey,
        "HEADER-MUST-NOT-PERSIST",
        "COOKIE-MUST-NOT-PERSIST",
        "RAW-FILE-SECRET-MUST-NOT-PERSIST",
        "OVERSIZED-METADATA-BODY-MUST-NOT-PERSIST",
        malformedJson
    })
    {
        Require(!persistedText.Contains(forbidden, StringComparison.Ordinal), $"Sensitive or rejected value leaked: {forbidden}");
    }
    Require(!networkText.Contains("\"body\"", StringComparison.Ordinal), "Request body content leaked into network.jsonl.");

    using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath));
    var summaryRoot = summary.RootElement;
    Require(summaryRoot.GetProperty("requestPayloadCount").GetInt32() == 2, "Unexpected request payload count.");
    Require(summaryRoot.GetProperty("requestPayloadMetadataOnlyCount").GetInt32() == 2, "Unexpected metadata-only request payload count.");
    Require(summaryRoot.GetProperty("requestPayloadRedactedFieldCount").GetInt32() == 5, "Unexpected sensitive-field redaction count.");
    Require(summaryRoot.GetProperty("requestPayloadBytes").GetInt64() > 0, "Request payload byte count must be positive.");
    Require(summaryRoot.GetProperty("maxRequestPayloadBytes").GetInt32() == CaptureSessionManager.MaxRequestPayloadBytes, "Request payload size limit missing from summary.");

    using var session = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath));
    var policy = session.RootElement.GetProperty("requestPayloadPolicy");
    Require(policy.GetProperty("sameOriginOnly").GetBoolean(), "Request payload same-origin policy is not enabled.");
    Require(policy.GetProperty("sensitiveFieldsRedacted").GetBoolean(), "Sensitive-field redaction policy is not enabled.");
    Require(policy.GetProperty("unsupportedPayloadsMetadataOnly").GetBoolean(), "Metadata-only fallback policy is not enabled.");
    Require(!policy.GetProperty("rawHeadersSaved").GetBoolean(), "Raw headers must remain disabled.");
    Require(!policy.GetProperty("cookiesSaved").GetBoolean(), "Cookie capture must remain disabled.");
    Require(!policy.GetProperty("authorizationHeadersSaved").GetBoolean(), "Authorization header capture must remain disabled.");

    Console.WriteLine("PASS  same-origin Fetch JSON payload capture");
    Console.WriteLine("PASS  same-origin XHR form payload capture");
    Console.WriteLine("PASS  JSON and form sensitive-field redaction");
    Console.WriteLine("PASS  unsupported and oversized metadata-only fallback");
    Console.WriteLine("PASS  cross-origin/type/method/size/format rejection");
    Console.WriteLine("PASS  raw request payload excluded from network.jsonl");
    Console.WriteLine("RESULT: TrueWebsiteCloner Request Payload Gate 1.2 PASS");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
