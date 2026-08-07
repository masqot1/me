using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:7843");
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    service = "TrueWebsiteCloner.TestLab",
    version = "0.1.0",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/sample", () => Results.Json(new
{
    source = "test-lab",
    message = "Foundation API response",
    values = new[] { 10, 20, 30 }
}));

app.MapPost("/api/echo", async (HttpRequest request) =>
{
    using var doc = await JsonDocument.ParseAsync(request.Body);
    return Results.Json(new { received = doc.RootElement.Clone(), at = DateTimeOffset.UtcNow });
});

app.Run();
