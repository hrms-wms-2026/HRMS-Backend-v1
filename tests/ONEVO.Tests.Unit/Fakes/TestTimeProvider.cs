using System.Threading;

namespace ONEVO.Tests.Unit.Fakes;

/// <summary>A hand-written <see cref="TimeProvider"/> whose clock only moves when the test moves it. The project has
/// no TimeProvider testing package, so the timer queue is faked as well: a timer created on this provider fires
/// only when the clock passes its due time, and never on its own.</summary>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = new();
    private readonly List<(TaskCompletionSource Signal, int Target)> _timerWaiters = new();
    private DateTimeOffset _now;
    private int _timerCount;

    public TestTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override long GetTimestamp()
    {
        lock (_gate)
            return _now.Ticks;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, dueTime, period);
        List<(TaskCompletionSource Signal, int Target)> toSignal;
        lock (_gate)
        {
            _timers.Add(timer);
            _timerCount++;
            toSignal = _timerWaiters.Where(waiter => waiter.Target <= _timerCount).ToList();
            _timerWaiters.RemoveAll(waiter => waiter.Target <= _timerCount);
        }

        foreach (var waiter in toSignal)
            waiter.Signal.TrySetResult();

        return timer;
    }

    /// <summary>How many timers this provider has handed out - a limiter that never waits creates none.</summary>
    public int TimerCount
    {
        get
        {
            lock (_gate)
                return _timerCount;
        }
    }

    /// <summary>Completes once at least <paramref name="target"/> timers have been created. The test's cue that the
    /// limiter is really waiting: the timer exists before the clock is advanced past its deadline.</summary>
    public Task WaitForTimerCountAsync(int target)
    {
        lock (_gate)
        {
            if (_timerCount >= target)
                return Task.CompletedTask;

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _timerWaiters.Add((signal, target));
            return signal.Task;
        }
    }

    /// <summary>Moves the clock forward and fires every timer that has come due.</summary>
    public void Advance(TimeSpan delta)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _now += delta;
            due = _timers.Where(timer => timer.IsDue(_now)).ToList();
        }

        foreach (var timer in due)
            timer.Fire();
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly TestTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset _dueAt;
        private readonly TimeSpan _period;
        private bool _disposed;

        public FakeTimer(TestTimeProvider provider, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            _period = period;
            _dueAt = provider.GetUtcNow() + dueTime;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
                return false;

            _dueAt = _provider.GetUtcNow() + dueTime;
            return true;
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal bool IsDue(DateTimeOffset now) => !_disposed && now >= _dueAt;

        internal void Fire()
        {
            if (_disposed)
                return;

            _callback(_state);
        }
    }
}
