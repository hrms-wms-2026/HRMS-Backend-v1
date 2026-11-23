using FluentAssertions;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Jobs;

public class ExclusiveJobRunnerTests
{
    /// <summary>job_leases in memory, shared by every "instance" built from one store.</summary>
    private sealed class LeaseStore
    {
        public readonly Dictionary<string, (string Owner, DateTimeOffset Until)> Leases = new();
        public IJobLeaseService For(string instance) => new Lease(this, instance);
    }

    private sealed class Lease(LeaseStore store, string instance) : IJobLeaseService
    {
        public string InstanceId => instance;

        public Task<bool> TryAcquireAsync(string jobName, TimeSpan ttl, CancellationToken ct)
        {
            lock (store)
            {
                if (store.Leases.TryGetValue(jobName, out var held) && held.Owner != instance && held.Until > DateTimeOffset.UtcNow)
                    return Task.FromResult(false);
                store.Leases[jobName] = (instance, DateTimeOffset.UtcNow + ttl);
                return Task.FromResult(true);
            }
        }

        public Task<bool> RenewAsync(string jobName, TimeSpan ttl, CancellationToken ct)
        {
            lock (store)
            {
                if (!store.Leases.TryGetValue(jobName, out var held) || held.Owner != instance || held.Until < DateTimeOffset.UtcNow)
                    return Task.FromResult(false);
                store.Leases[jobName] = (instance, DateTimeOffset.UtcNow + ttl);
                return Task.FromResult(true);
            }
        }

        public Task ReleaseAsync(string jobName, CancellationToken ct)
        {
            lock (store)
            {
                if (store.Leases.TryGetValue(jobName, out var held) && held.Owner == instance)
                    store.Leases[jobName] = (instance, DateTimeOffset.MinValue);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ListLogger : ILogger<ExclusiveJobRunner>
    {
        public readonly List<(LogLevel Level, string Message)> Lines = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }

    private readonly LeaseStore _store = new();
    private readonly ListLogger _log = new();

    private ExclusiveJobRunner Runner(string instance) => new(_store.For(instance), _log);

    private bool IsHeld(string job)
        => _store.Leases.TryGetValue(job, out var held) && held.Until > DateTimeOffset.UtcNow;

    [Fact]
    public async Task SecondInstance_WhileTheFirstRuns_IsSkipped_AndTheWorkRunsOnce()
    {
        var schedule = new JobSchedule("job", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        var runs = 0;
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var first = Runner("a").RunAsync(schedule, async _ => { runs++; started.SetResult(); await release.Task; }, CancellationToken.None);
        await started.Task;
        var second = await Runner("b").RunAsync(schedule, _ => { runs++; return Task.CompletedTask; }, CancellationToken.None);
        release.SetResult();

        second.Should().Be(JobRunOutcome.SkippedHeldElsewhere);
        (await first).Should().Be(JobRunOutcome.Completed);
        runs.Should().Be(1);
    }

    [Fact]
    public async Task AfterSuccess_TheLeaseIsKeptForTheMinimumGap()
    {
        var schedule = JobSchedule.Every("job", TimeSpan.FromMinutes(15));

        (await Runner("a").RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None)).Should().Be(JobRunOutcome.Completed);

        IsHeld("job").Should().BeTrue();
        (await Runner("b").RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(JobRunOutcome.SkippedHeldElsewhere);
    }

    [Fact]
    public async Task NoGap_ReleasesAfterSuccess()
    {
        var schedule = new JobSchedule("job", TimeSpan.FromMinutes(1), TimeSpan.Zero);

        await Runner("a").RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None);

        IsHeld("job").Should().BeFalse();
    }

    [Fact]
    public async Task TimeLimit_CancelsTheWork_ReturnsTimedOut_AndReleases()
    {
        var schedule = new JobSchedule("job", TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1));

        var outcome = await Runner("a").RunAsync(schedule, ct => Task.Delay(Timeout.Infinite, ct), CancellationToken.None);

        outcome.Should().Be(JobRunOutcome.TimedOut);
        IsHeld("job").Should().BeFalse();
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("time limit"));
    }

    [Fact]
    public async Task LeaseOutlastsTheTimeLimit()
    {
        var schedule = new JobSchedule("job", TimeSpan.FromMinutes(10), TimeSpan.Zero);
        DateTimeOffset until = default;

        await Runner("a").RunAsync(schedule, _ => { until = _store.Leases["job"].Until; return Task.CompletedTask; }, CancellationToken.None);

        until.Should().BeAfter(DateTimeOffset.UtcNow + schedule.TimeLimit + ExclusiveJobRunner.LeaseMargin - TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Failure_IsRethrown_Logged_AndReleases()
    {
        var schedule = JobSchedule.Daily("job", TimeSpan.FromMinutes(1));

        var act = () => Runner("a").RunAsync(schedule, _ => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        IsHeld("job").Should().BeFalse();
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("failed after"));
    }

    [Fact]
    public async Task HostShutdown_IsNotATimeout()
    {
        using var stopping = new CancellationTokenSource();
        var schedule = new JobSchedule("job", TimeSpan.FromMinutes(1), TimeSpan.Zero);

        var run = Runner("a").RunAsync(schedule, ct => Task.Delay(Timeout.Infinite, ct), stopping.Token);
        stopping.Cancel();

        await run.Invoking(r => r).Should().ThrowAsync<OperationCanceledException>();
        IsHeld("job").Should().BeFalse();
    }

    [Fact]
    public async Task CompletionLine_CarriesDurationAndRows_ZeroRowsAtDebug()
    {
        var schedule = JobSchedule.Every("job", TimeSpan.FromMinutes(1));

        await Runner("a").RunAsync(schedule, _ => Task.FromResult<int?>(7), CancellationToken.None);
        await Runner("a").RunAsync(schedule, _ => Task.FromResult<int?>(0), CancellationToken.None);

        _log.Lines.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("Job job completed in")
            && l.Message.EndsWith("7 rows processed."));
        _log.Lines.Should().Contain(l => l.Level == LogLevel.Debug && l.Message.EndsWith("0 rows processed."));
    }

    [Fact]
    public void Schedules_KeepTheLeaseShorterThanTheirInterval()
    {
        var every = JobSchedule.Every("job", TimeSpan.FromMinutes(15));
        every.MinimumGap.Should().BeLessThan(TimeSpan.FromMinutes(15));
        every.TimeLimit.Should().Be(TimeSpan.FromMinutes(15));

        JobSchedule.Daily("job", TimeSpan.FromMinutes(30)).MinimumGap.Should().BeLessThan(TimeSpan.FromDays(1));
    }
}
