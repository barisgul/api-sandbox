using System.Collections.Concurrent;

namespace AuthSandbox.Infrastructure.Demo;

/// <summary>
/// In-memory state for one demo domain. Registered as a singleton; <see cref="Reset"/> restores the
/// deterministic seed so demos and recorded videos are repeatable. IDs are sequential per domain
/// (e.g. <c>cus_1001</c>) and restart after a reset.
/// </summary>
public abstract class SandboxState
{
    private int _sequence;
    private readonly ConcurrentDictionary<string, int> _counters = new();

    protected SandboxState(TimeProvider clock)
    {
        Clock = clock;
    }

    public TimeProvider Clock { get; }
    public object Sync { get; } = new();
    public DateTimeOffset Now => Clock.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref _sequence)}";

    /// <summary>Sequential number per series, e.g. invoice numbers per year.</summary>
    public int NextNumber(string series) => _counters.AddOrUpdate(series, 1, (_, n) => n + 1);

    public void Reset()
    {
        lock (Sync)
        {
            _sequence = 1000;
            _counters.Clear();
            Clear();
            Seed();
        }
    }

    protected abstract void Clear();
    protected abstract void Seed();

    /// <summary>Describes the seeded fixtures, returned by the reset endpoint.</summary>
    public abstract object Fixtures { get; }
}
