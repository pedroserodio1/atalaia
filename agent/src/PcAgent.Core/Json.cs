using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PcAgent.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    /// <summary>Serializa em camelCase e devolve um elemento independente (sem documento a descartar).</summary>
    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>ISO-8601 em hora local com offset, sem frações: <c>2026-11-08T20:00:00-03:00</c>.</summary>
    public static string Iso(DateTimeOffset t) =>
        t.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Envelope do protocolo v1: <c>{ v, type, source?, seq, ts, data }</c>.</summary>
public static class Envelope
{
    public const int Version = 1;

    public static byte[] Serialize(string type, string? source, long seq, long ts, JsonElement data)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = Json.Options.Encoder }))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteString("type", type);
            if (source is not null) w.WriteString("source", source);   // omitido, nunca null
            w.WriteNumber("seq", seq);
            w.WriteNumber("ts", ts);
            w.WritePropertyName("data");
            data.WriteTo(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
