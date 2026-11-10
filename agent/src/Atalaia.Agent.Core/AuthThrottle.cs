namespace Atalaia.Agent.Core;

/// <summary>
/// Limita tentativas erradas de token vindas da rede. Um código de 6 dígitos tem só 1 milhão de combinações: sem limite, quem
/// está na rede acerta em minutos. Aqui, cada endereço tem 5 erros e depois fica bloqueado 1 min, e cada novo bloqueio dobra
/// a espera (até 15 min, e o histórico só some depois de 1 h sem erros); somando todos os endereços, 20 erros em 10 min bloqueiam a rede inteira por 10 min (cobre quem troca
/// de IP). O bloqueio não é avaliado contra o token: bloqueado é bloqueado, mesmo com o código certo.
/// </summary>
public sealed class AuthThrottle(Func<DateTimeOffset>? now = null, int maxFailures = 5, int globalMaxFailures = 20)
{
    public static readonly TimeSpan FirstLock = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxLock = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan GlobalLock = TimeSpan.FromMinutes(10);
    // Erros antigos deixam de contar. Tem que ser MAIOR que o bloqueio máximo: se fosse igual, esperar o bloqueio acabar
    // zeraria o histórico e o escalonamento nunca passaria de 1 minuto.
    private static readonly TimeSpan Forget = TimeSpan.FromHours(1);
    private const int MaxTracked = 1024;

    private sealed class Entry { public int Failures; public int Lockouts; public DateTimeOffset LockedUntil; public DateTimeOffset LastFailure; }

    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<string, Entry> _byKey = [];
    private readonly Queue<DateTimeOffset> _global = new();
    private DateTimeOffset _globalLockedUntil;
    private readonly object _lock = new();

    public bool IsLocked(string key)
    {
        lock (_lock)
        {
            var t = _now();
            return t < _globalLockedUntil || (_byKey.TryGetValue(key, out var e) && t < e.LockedUntil);
        }
    }

    /// <summary>Registra um erro. Devolve a duração do bloqueio se este erro iniciou um (senão <c>null</c>).</summary>
    public TimeSpan? Failure(string key)
    {
        lock (_lock)
        {
            var t = _now();
            if (_byKey.Count >= MaxTracked) Prune(t);
            if (!_byKey.TryGetValue(key, out var e)) _byKey[key] = e = new Entry();
            if (t - e.LastFailure > Forget) { e.Failures = 0; e.Lockouts = 0; }
            e.LastFailure = t;

            TimeSpan? started = null;
            if (++e.Failures >= maxFailures)
            {
                e.Failures = 0;
                var seconds = Math.Min(FirstLock.TotalSeconds * Math.Pow(2, e.Lockouts++), MaxLock.TotalSeconds);
                started = TimeSpan.FromSeconds(seconds);
                e.LockedUntil = t + started.Value;
            }

            _global.Enqueue(t);
            while (_global.Count > 0 && t - _global.Peek() > GlobalWindow) _global.Dequeue();
            if (_global.Count >= globalMaxFailures)
            {
                _global.Clear();
                _globalLockedUntil = t + GlobalLock;
                started ??= GlobalLock;
            }
            return started;
        }
    }

    /// <summary>Quem acertou o código começa do zero.</summary>
    public void Success(string key)
    {
        lock (_lock) _byKey.Remove(key);
    }

    private void Prune(DateTimeOffset t)
    {
        foreach (var k in _byKey.Where(p => t - p.Value.LastFailure > Forget && t >= p.Value.LockedUntil).Select(p => p.Key).ToList())
            _byKey.Remove(k);
        if (_byKey.Count >= MaxTracked) _byKey.Clear();   // enxurrada de endereços: recomeça (o limite global já valeu)
    }
}
