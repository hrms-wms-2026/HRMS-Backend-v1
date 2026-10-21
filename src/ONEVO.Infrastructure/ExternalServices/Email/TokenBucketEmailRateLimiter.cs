using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// A sliding-window rate limiter for outbound email, driven by <c>Email:MaxPerSecond</c> (Plan D Task 3).
/// <para>
/// At most <c>MaxPerSecond</c> sends may start within any rolling second. The limiter remembers when the
/// sends in the current window started; when the window is full, the next send waits until the oldest of
/// them is a second old. <c>MaxPerSecond = 0</c> means unlimited and never waits.
/// </para>
/// <para>
/// The window is per instance. With several instances behind one provider, set the limit to the provider's
/// limit divided by the instance count (see the Phase 2 release runbook).
/// </para>
/// </summary>
public sealed class TokenBucketEmailRateLimiter : IEmailRateLimiter
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly int _maxPerSecond;
    private readonly long _windowTicks;
    private readonly List<Entry> _window = new();
    private long _nextId;

    public TokenBucketEmailRateLimiter(IOptions<EmailRateOptions> options, TimeProvider clock)
    {
        // The startup validator rejects a negative limit, so it never reaches here. The clamp is a
        // belt-and-braces guard for a container built without that validation.
        _maxPerSecond = Math.Max(0, options.Value.MaxPerSecond);
        _clock = clock;
        // TimestampFrequency is ticks per second, so one rolling second is exactly that many ticks.
        _windowTicks = clock.TimestampFrequency;
    }

    public async ValueTask WaitAsync(CancellationToken ct)
    {
        if (_maxPerSecond == 0)
            return;

        ct.ThrowIfCancellationRequested();

        long dueAt;
        long id;
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            Prune(now);

            if (_window.Count < _maxPerSecond)
            {
                // There is room in the window: this send goes now.
                _window.Add(new Entry(++_nextId, now));
                return;
            }

            // The window is full: the oldest send has to be a second old before the next one may go.
            dueAt = _window[0].EffectiveAt + _windowTicks;
            if (dueAt < now)
                dueAt = now;

            // Reserve the slot now, so a second concurrent caller sees a full window too and waits behind
            // this one instead of being let through alongside it.
            id = ++_nextId;
            _window.Add(new Entry(id, dueAt));
        }

        var delay = ToTimeSpan(dueAt - _clock.GetTimestamp());
        if (delay <= TimeSpan.Zero)
            return;

        try
        {
            await Task.Delay(delay, _clock, ct);
        }
        catch (OperationCanceledException)
        {
            // The reserved slot is never used: give it back, or the window would be short by one send.
            lock (_gate)
                _window.RemoveAll(entry => entry.Id == id);
            throw;
        }
    }

    /// <summary>Converts a number of timestamp ticks into wall-clock time.</summary>
    private TimeSpan ToTimeSpan(long ticks)
        => TimeSpan.FromTicks((long)(ticks * (10_000_000.0 / _clock.TimestampFrequency)));

    /// <summary>Drops the sends that have fallen out of the rolling second.</summary>
    private void Prune(long now)
    {
        var cutoff = now - _windowTicks;
        _window.RemoveAll(entry => entry.EffectiveAt <= cutoff);
    }

    /// <summary>One send in the window: when it happened (or when it is reserved to happen).</summary>
    private readonly record struct Entry(long Id, long EffectiveAt);
}
