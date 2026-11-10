using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Atalaia.Agent.Plugins.Discord;

/// <summary>Quadro do RPC local do Discord: 4 bytes de opcode, 4 de tamanho (little-endian) e o JSON em UTF-8.</summary>
public static class DiscordIpc
{
    public const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
    private const int MaxFrameBytes = 1024 * 1024;

    public static async Task WriteAsync(Stream s, int op, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];   // um único Write: o pipe não mistura quadros
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await s.WriteAsync(frame, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Lê um quadro. <c>EndOfStreamException</c> se o Discord fechou o pipe.</summary>
    public static async Task<(int Op, JsonElement Json)> ReadAsync(Stream s, CancellationToken ct)
    {
        var header = new byte[8];
        await s.ReadExactlyAsync(header, ct);
        var op = BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > MaxFrameBytes) throw new InvalidDataException("Quadro do Discord com tamanho inválido");
        var body = new byte[length];
        await s.ReadExactlyAsync(body, ct);
        using var doc = JsonDocument.Parse(body);
        return (op, doc.RootElement.Clone());
    }
}
