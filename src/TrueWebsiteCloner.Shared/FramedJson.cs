using System.Buffers.Binary;
using System.Text.Json;

namespace TrueWebsiteCloner.Shared;

public static class FramedJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<JsonDocument?> ReadDocumentAsync(Stream stream, int maxBytes, CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        var got = await ReadExactlyOrEofAsync(stream, header, cancellationToken);
        if (!got)
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes)
            throw new InvalidDataException($"Invalid framed message length: {length}");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonDocument.Parse(payload);
    }

    public static async Task WriteAsync<T>(Stream stream, T value, int maxBytes, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length > maxBytes)
            throw new InvalidDataException($"Outgoing message too large: {payload.Length}");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                if (offset == 0)
                    return false;
                throw new EndOfStreamException("Unexpected EOF while reading frame header.");
            }
            offset += read;
        }
        return true;
    }
}
