// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Atalaia.Agent.Core;

/// <summary>Último <c>data</c> e instante de coleta por fonte. Alimenta os <c>snapshot</c>.</summary>
public sealed class StateCache
{
    public sealed record Entry(JsonElement Data, long Ts);

    private readonly ConcurrentDictionary<string, Entry> _items = new();

    public void Set(string source, JsonElement data, long ts) => _items[source] = new Entry(data, ts);

    public bool TryGet(string source, out Entry entry) => _items.TryGetValue(source, out entry!);
}

/// <summary>Guarda em memória as 8 capas e os 32 avatares mais recentes (reservas separadas). Hash = 16 primeiros hex do SHA-256; a leitura vale para as duas.</summary>
public sealed class BlobStore : IBlobStore
{
    private const int Capacity = 8;
    private const int AvatarCapacity = 32;
    private readonly object _lock = new();
    private readonly LinkedList<(string Hash, byte[] Bytes)> _items = new();     // capas; mais recente na frente
    private readonly LinkedList<(string Hash, byte[] Bytes)> _avatars = new();   // avatares

    public string Put(byte[] bytes) => Put(_items, Capacity, bytes);

    public string PutAvatar(byte[] bytes) => Put(_avatars, AvatarCapacity, bytes);

    private string Put(LinkedList<(string Hash, byte[] Bytes)> list, int capacity, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        lock (_lock)
        {
            for (var n = list.First; n is not null; n = n.Next)
                if (n.Value.Hash == hash) { list.Remove(n); break; }
            list.AddFirst((hash, bytes));
            while (list.Count > capacity) list.RemoveLast();
        }
        return hash;
    }

    public byte[]? Get(string hash)
    {
        lock (_lock)
        {
            foreach (var (h, b) in _items) if (h == hash) return b;
            foreach (var (h, b) in _avatars) if (h == hash) return b;
            return null;
        }
    }
}
