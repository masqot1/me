using System.Text.Json;

namespace TrueWebsiteCloner.Shared;

public sealed record BridgeInfo(int Port, string Token, int ProcessId, DateTimeOffset StartedAtUtc);

public sealed record NativeBridgeEnvelope(
    string Token,
    string? Origin,
    JsonElement Payload,
    DateTimeOffset SentAtUtc);

public sealed record BridgeReply(
    bool Ok,
    string Type,
    string Message,
    string AppVersion,
    DateTimeOffset AtUtc,
    object? Data = null);

public static class ProtocolConstants
{
    public const int MaxBridgeMessageBytes = 2 * 1024 * 1024;
    public const int MaxChromeInboundBytes = 64 * 1024 * 1024;
    public const int MaxChromeOutboundBytes = 1024 * 1024;
}
