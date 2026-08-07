using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TrueWebsiteCloner.Shared;

namespace TrueWebsiteCloner.NativeHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        AppPaths.EnsureDirectories();
        var origin = args.FirstOrDefault(a => a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase));
        try
        {
            await using var stdin = Console.OpenStandardInput();
            await using var stdout = Console.OpenStandardOutput();
            while (true)
            {
                using var doc = await FramedJson.ReadDocumentAsync(stdin, ProtocolConstants.MaxChromeInboundBytes);
                if (doc is null) return 0;
                var reply = await ForwardToDesktopAsync(doc.RootElement.Clone(), origin);
                await FramedJson.WriteAsync(stdout, reply, ProtocolConstants.MaxChromeOutboundBytes);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TrueWebsiteCloner.NativeHost: {ex}");
            return 1;
        }
    }

    private static async Task<BridgeReply> ForwardToDesktopAsync(JsonElement payload, string? origin)
    {
        if (!File.Exists(AppPaths.BridgeInfoPath)) return Failure("desktop_not_running", "TrueWebsiteCloner Desktop is not running.");
        BridgeInfo? bridge;
        try { bridge = JsonSerializer.Deserialize<BridgeInfo>(await File.ReadAllTextAsync(AppPaths.BridgeInfoPath), JsonOptions); }
        catch (Exception ex) { return Failure("bridge_info_invalid", ex.Message); }
        if (bridge is null || bridge.Port is <= 0 or > 65535 || string.IsNullOrWhiteSpace(bridge.Token)) return Failure("bridge_info_invalid", "Desktop bridge information is incomplete.");

        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await client.ConnectAsync(IPAddress.Loopback, bridge.Port, timeout.Token);
            await using var stream = client.GetStream();
            await FramedJson.WriteAsync(stream, new NativeBridgeEnvelope(bridge.Token, origin, payload, DateTimeOffset.UtcNow), ProtocolConstants.MaxBridgeMessageBytes, timeout.Token);
            using var response = await FramedJson.ReadDocumentAsync(stream, ProtocolConstants.MaxBridgeMessageBytes, timeout.Token);
            if (response is null) return Failure("desktop_no_response", "Desktop bridge closed without a response.");
            return JsonSerializer.Deserialize<BridgeReply>(response.RootElement.GetRawText(), JsonOptions) ?? Failure("desktop_invalid_response", "Desktop bridge returned an invalid response.");
        }
        catch (Exception ex) { return Failure("desktop_unreachable", ex.Message); }
    }

    private static BridgeReply Failure(string type, string message) => new(false, type, message, "0.2.0", DateTimeOffset.UtcNow);
}
