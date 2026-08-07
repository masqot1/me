using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json;

static string? Option(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

var captureArg = Option(args, "--capture") ?? Environment.GetEnvironmentVariable("TWC_CAPTURE_ROOT");
if (string.IsNullOrWhiteSpace(captureArg))
    throw new InvalidOperationException("Use --capture <capture-root>.");

var captureRoot = Path.GetFullPath(captureArg);
var offlineRoot = Path.Combine(captureRoot, "offline");
var siteRoot = Path.Combine(offlineRoot, "site");
var manifestPath = Path.Combine(offlineRoot, "offline-manifest.json");
if (!Directory.Exists(siteRoot) || !File.Exists(manifestPath))
    throw new InvalidOperationException("Offline build is missing. Run the V0.4 Offline Builder first.");

var port = 7850;
var portText = Option(args, "--port");
if (!string.IsNullOrWhiteSpace(portText) && (!int.TryParse(portText, out port) || port is < 1024 or > 65535))
    throw new InvalidOperationException("--port must be between 1024 and 65535.");

using var manifestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
var manifestRoot = manifestDoc.RootElement;
var targetUrl = manifestRoot.GetProperty("targetUrl").GetString() ?? string.Empty;
var targetOrigin = manifestRoot.GetProperty("targetOrigin").GetString() ?? string.Empty;

var replay = new Dictionary<string, ReplayEntry>(StringComparer.Ordinal);
ReplayEntry? documentEntry = null;
foreach (var mapping in manifestRoot.GetProperty("mappings").EnumerateArray())
{
    var url = mapping.GetProperty("url").GetString() ?? string.Empty;
    var mimeType = mapping.GetProperty("mimeType").GetString() ?? "application/octet-stream";
    var resourceType = mapping.GetProperty("resourceType").GetString() ?? string.Empty;
    var localPath = mapping.GetProperty("localPath").GetString() ?? string.Empty;
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(localPath)) continue;
    var entry = new ReplayEntry(uri.PathAndQuery, mimeType, resourceType, localPath);
    replay.TryAdd(entry.RequestKey, entry);
    if (documentEntry is null && string.Equals(resourceType, "Document", StringComparison.OrdinalIgnoreCase)) documentEntry = entry;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var app = builder.Build();
var contentTypes = new FileExtensionContentTypeProvider();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-TrueWebsiteCloner-Replay"] = "offline";
    context.Response.Headers["Cache-Control"] = "no-store";
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { ok = false, error = "method_not_replayed", allowed = new[] { "GET", "HEAD" } });
        return;
    }
    await next();
});

app.MapGet("/__twc/health", () => Results.Json(new
{
    ok = true,
    service = "TrueWebsiteCloner.LocalRuntime",
    version = "0.5.0",
    mode = "recorded-get-replay",
    bind = "127.0.0.1",
    targetUrl,
    targetOrigin,
    routeCount = replay.Count,
    outboundProxy = false,
    cookiesReplayed = false,
    authorizationReplayed = false
}));

app.MapMethods("/{**path}", new[] { "GET", "HEAD" }, async (HttpContext context) =>
{
    var requestKey = context.Request.Path.Value + context.Request.QueryString.Value;
    ReplayEntry? entry = null;
    if (!string.IsNullOrEmpty(requestKey)) replay.TryGetValue(requestKey, out entry);
    if (entry is null && context.Request.Path == "/") entry = documentEntry;

    string? filePath = null;
    string? mimeType = null;
    if (entry is not null)
    {
        filePath = SafeSitePath(siteRoot, entry.LocalPath);
        mimeType = entry.MimeType;
    }
    else
    {
        var relative = context.Request.Path == "/" ? "index.html" : (context.Request.Path.Value ?? string.Empty).TrimStart('/');
        filePath = SafeSitePath(siteRoot, relative);
        if (filePath is not null) contentTypes.TryGetContentType(filePath, out mimeType);
    }

    if (filePath is null || !File.Exists(filePath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { ok = false, error = "replay_miss", path = context.Request.Path.Value, query = context.Request.QueryString.Value });
        return;
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
    context.Response.ContentLength = new FileInfo(filePath).Length;
    if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.SendFileAsync(filePath);
});

Console.WriteLine($"TrueWebsiteCloner Local Runtime v0.5 listening on http://127.0.0.1:{port}");
Console.WriteLine($"Capture: {captureRoot}");
Console.WriteLine("Policy: recorded GET/HEAD only; no outbound proxy; no cookie/auth replay.");
await app.RunAsync();

static string? SafeSitePath(string siteRoot, string relative)
{
    if (string.IsNullOrWhiteSpace(relative)) return null;
    var root = Path.GetFullPath(siteRoot);
    var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
    return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
}

sealed record ReplayEntry(string RequestKey, string MimeType, string ResourceType, string LocalPath);
