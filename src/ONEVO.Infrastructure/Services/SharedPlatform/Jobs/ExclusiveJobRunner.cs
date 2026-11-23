using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Observability;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Jobs;

/// <summary>How one background job may run across all API instances (task 13).</summary>
/// <param name="Name">Lease name in <c>job_leases</c>; stable, one per job.</param>
/// <param name="TimeLimit">The run is cancelled after this long. The lease lasts <see cref="ExclusiveJobRunner.LeaseMargin"/>
/// longer, so it never expires while the run is still allowed to work and a second instance cannot start the same work.</param>
/// <param name="MinimumGap">After a successful run the lease is kept until the run's start plus this gap, so another
/// instance whose timer fires in between skips. Zero releases at once (only concurrent runs are prevented).</param>
/// <param name="CompletionLogLevel">Level of the "completed" line. Debug for pollers that run every few seconds; a run
/// that processed 0 rows is always logged at Debug. Timeouts and failures are always warnings.</param>
public sealed record JobSchedule(string Name, TimeSpan TimeLimit, TimeSpan MinimumGap, LogLevel CompletionLogLevel = LogLevel.Information)
{
    /// <summary>A job on a fixed interval: at most one run per interval across all instances, limited to the interval.</summary>
    public static JobSchedule Every(string name, TimeSpan interval, TimeSpan? timeLimit = null)
        => new(name, timeLimit ?? interval, interval * 0.9);

    /// <summary>A job that runs once per day: the lease is kept for most of a day after a successful run.</summary>
    public static JobSchedule Daily(string name, TimeSpan timeLimit)
        => new(name, timeLimit, TimeSpan.FromHours(20));
}

public enum JobRunOutcome
{
    /// <summary>The work ran to the end.</summary>
    Completed,

    /// <summary>Another instance holds the lease (running now, or ran within the minimum gap). Nothing was done.</summary>
    SkippedHeldElsewhere,

    /// <summary>The work was cancelled at its time limit. The next run continues where the job's own logic allows.</summary>
    TimedOut,
}

/// <summary>
/// Runs a background job's work only on the instance that holds the job's lease, with a time limit, and writes one
/// log line per run with the duration and, when the job counts them, the rows it processed. Exceptions from the work
/// are logged with the duration and rethrown, so each job keeps its own retry handling.
/// </summary>
public sealed class ExclusiveJobRunner
{
    /// <summary>Lease time beyond the time limit, covering the cancellation and the database round trips around it.</summary>
    public static readonly TimeSpan LeaseMargin = TimeSpan.FromMinutes(2);

    private readonly IJobLeaseService _leases;
    private readonly ILogger<ExclusiveJobRunner> _logger;
    private readonly TimeProvider _time;

    public ExclusiveJobRunner(IJobLeaseService leases, ILogger<ExclusiveJobRunner> logger, TimeProvider? time = null)
    {
        _leases = leases;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary><paramref name="work"/> returns the number of rows it processed, or null when it does not count them.</summary>
    public async Task<JobRunOutcome> RunAsync(
        JobSchedule schedule, Func<CancellationToken, Task<int?>> work, CancellationToken stoppingToken)
    {
        if (!await _leases.TryAcquireAsync(schedule.Name, schedule.TimeLimit + LeaseMargin, stoppingToken))
        {
            _logger.LogDebug("Job {Job} skipped: another instance holds its lease.", schedule.Name);
            Record(schedule, "skipped", null);
            return JobRunOutcome.SkippedHeldElsewhere;
        }

        var started = _time.GetTimestamp();
        var keepLease = false;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        limit.CancelAfter(schedule.TimeLimit);
        try
        {
            var rows = await work(limit.Token);
            var elapsed = _time.GetElapsedTime(started);
            Record(schedule, "completed", elapsed);
            var level = rows == 0 ? LogLevel.Debug : schedule.CompletionLogLevel;
            if (rows is { } count)
                _logger.Log(level, "Job {Job} completed in {ElapsedMs} ms, {Rows} rows processed.",
                    schedule.Name, (long)elapsed.TotalMilliseconds, count);
            else
                _logger.Log(level, "Job {Job} completed in {ElapsedMs} ms.", schedule.Name, (long)elapsed.TotalMilliseconds);

            var hold = schedule.MinimumGap - elapsed;
            keepLease = hold > TimeSpan.Zero
                && await _leases.RenewAsync(schedule.Name, hold, CancellationToken.None);
            return JobRunOutcome.Completed;
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            var elapsed = _time.GetElapsedTime(started);
            Record(schedule, "timed_out", elapsed);
            _logger.LogWarning("Job {Job} stopped at its time limit of {Limit} after {ElapsedMs} ms.",
                schedule.Name, schedule.TimeLimit, (long)elapsed.TotalMilliseconds);
            return JobRunOutcome.TimedOut;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var elapsed = _time.GetElapsedTime(started);
            Record(schedule, "failed", elapsed);
            _logger.LogWarning("Job {Job} failed after {ElapsedMs} ms.", schedule.Name, (long)elapsed.TotalMilliseconds);
            throw;
        }
        finally
        {
            if (!keepLease)
                await _leases.ReleaseAsync(schedule.Name, CancellationToken.None);
        }
    }

    /// <summary>Task 17 metrics: crownus.jobs.runs for every outcome, crownus.jobs.duration for runs that did work.</summary>
    private static void Record(JobSchedule schedule, string outcome, TimeSpan? elapsed)
    {
        var tags = new TagList { { "job", schedule.Name }, { "outcome", outcome } };
        CrownusMetrics.JobRuns.Add(1, tags);
        if (elapsed is { } duration)
            CrownusMetrics.JobDuration.Record(duration.TotalMilliseconds, tags);
    }

    /// <summary>For work that does not count rows.</summary>
    public Task<JobRunOutcome> RunAsync(JobSchedule schedule, Func<CancellationToken, Task> work, CancellationToken stoppingToken)
        => RunAsync(schedule, async ct =>
        {
            await work(ct);
            return (int?)null;
        }, stoppingToken);
}

public static class ExclusiveJobRunnerExtensions
{
    /// <summary>Resolves the runner from a job's root service provider and runs <paramref name="work"/> under it.</summary>
    public static Task<JobRunOutcome> RunExclusiveAsync(
        this IServiceProvider services, JobSchedule schedule, Func<CancellationToken, Task> work, CancellationToken stoppingToken)
        => services.GetRequiredService<ExclusiveJobRunner>().RunAsync(schedule, work, stoppingToken);

    public static Task<JobRunOutcome> RunExclusiveAsync(
        this IServiceProvider services, JobSchedule schedule, Func<CancellationToken, Task<int?>> work, CancellationToken stoppingToken)
        => services.GetRequiredService<ExclusiveJobRunner>().RunAsync(schedule, work, stoppingToken);
}
