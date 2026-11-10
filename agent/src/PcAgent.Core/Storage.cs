using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace PcAgent.Core;

/// <summary>Último <c>data</c> e instante de coleta por fonte. Alimenta os <c>snapshot</c>.</summary>
public sealed class StateCache
{
    public sealed record Entry(JsonElement Data, long Ts);

    private readonly ConcurrentDictionary<string, Entry> _items = new();

    public void Set(string source, JsonElement data, long ts) => _items[source] = new Entry(data, ts);

    public bool TryGet(string source, out Entry entry) => _items.TryGetValue(source, out entry!);
}

/// <summary>Guarda em memória as 8 entradas mais recentes. Hash = 16 primeiros hex do SHA-256.</summary>
public sealed class BlobStore : IBlobStore
{
    private const int Capacity = 8;
    private readonly object _lock = new();
    private readonly LinkedList<(string Hash, byte[] Bytes)> _items = new();   // mais recente na frente

    public string Put(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        lock (_lock)
        {
            for (var n = _items.First; n is not null; n = n.Next)
                if (n.Value.Hash == hash) { _items.Remove(n); break; }
            _items.AddFirst((hash, bytes));
            while (_items.Count > Capacity) _items.RemoveLast();
        }
        return hash;
    }

    public byte[]? Get(string hash)
    {
        lock (_lock)
        {
            foreach (var (h, b) in _items) if (h == hash) return b;
            return null;
        }
    }
}
