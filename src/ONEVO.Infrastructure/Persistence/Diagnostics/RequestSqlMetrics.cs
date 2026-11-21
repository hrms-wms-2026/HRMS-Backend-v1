using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Persistence.Diagnostics;

/// <summary>
/// Scoped counter of EF Core commands and their total time. Thread-safe, because one request can
/// run commands on more than one DbContext at a time.
/// </summary>
public sealed class RequestSqlMetrics : IRequestSqlMetrics
{
    private int _commandCount;
    private long _elapsedTicks;

    public int CommandCount => Volatile.Read(ref _commandCount);

    public TimeSpan TotalElapsed => TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks));

    public void Record(TimeSpan duration)
    {
        Interlocked.Increment(ref _commandCount);
        Interlocked.Add(ref _elapsedTicks, duration.Ticks);
    }
}
