using System.Text.Json;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});

builder.WebHost.UseUrls("http://127.0.0.1:7843");
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    service = "TrueWebsiteCloner.TestLab",
    version = "0.7.0",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/sample", () => Results.Json(new
{
    source = "test-lab",
    message = "Network Capture Core API response",
    values = new[] { 10, 20, 30 }
}));

app.MapGet("/recover/help.html", () => Results.Content(
    "<!doctype html><html><head><meta charset=\"utf-8\"><title>Recovered Help</title></head><body><h1>RECOVERED HELP RESOURCE</h1><p>This page exists in Test Lab but is not requested during the initial capture.</p></body></html>",
    "text/html"));

app.MapPost("/api/echo", async (HttpRequest request, HttpResponse response) =>
{
    response.Headers["Cache-Control"] = "no-store";
    response.Headers["ETag"] = "\"gate-1.3-echo\"";
    response.Headers["Content-Language"] = "en";
    response.Headers["Set-Cookie"] = "twc_gate13=RUNTIME-SET-COOKIE-MUST-NOT-PERSIST; Path=/; HttpOnly; SameSite=Strict";
    response.Headers["X-API-Key"] = "RUNTIME-RESPONSE-APIKEY-MUST-NOT-PERSIST";

    using var doc = await JsonDocument.ParseAsync(request.Body);
    return Results.Json(new { received = doc.RootElement.Clone(), at = DateTimeOffset.UtcNow });
});

app.Run();
