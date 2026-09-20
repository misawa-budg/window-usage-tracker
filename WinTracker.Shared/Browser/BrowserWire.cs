using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTracker.Shared.Browser;

// Both native messaging and the local pipe use bounded, length-prefixed UTF-8 JSON.
public sealed record BrowserMessage(string Kind, string? Browser = null, string? RequestId = null,
    bool Focused = false, string? ServiceId = null);

public static class BrowserWire
{
    public const string HostName = "com.wintracker.browser";
    public const int MaximumMessageBytes = 4096;
    public static string PipeName => $"WinTracker.Browser.v1.{Process.GetCurrentProcess().SessionId}";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<BrowserMessage?> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Invalid browser frame size.");
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<BrowserMessage>(body, Options)
            ?? throw new InvalidDataException("Missing browser message.");
    }

    public static async Task WriteAsync(Stream stream, BrowserMessage message, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (body.Length > MaximumMessageBytes) throw new InvalidDataException("Browser frame too large.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }
}
