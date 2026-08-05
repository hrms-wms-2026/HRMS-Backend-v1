using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Nightly job that aggregates activity_snapshots into activity_daily_summary.
/// Phase 1: runs at ~23:00 UTC once per day.
/// </summary>
public sealed class ActivityDailySummaryJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeOnly TargetUtcTime = new(23, 0);

    private readonly IServiceProvider _services;
    private readonly ILogger<ActivityDailySummaryJob> _logger;
    private DateOnly? _lastRunDateUtc;

    public ActivityDailySummaryJob(
        IServiceProvider services,
        ILogger<ActivityDailySummaryJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime);

                if (now.TimeOfDay >= TargetUtcTime.ToTimeSpan()
                    && _lastRunDateUtc != today)
                {
                    // Aggregate previous calendar day (complete data)
                    var targetDate = today.AddDays(-1);
                    await RunAggregationAsync(targetDate, stoppingToken);
                    _lastRunDateUtc = today;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Activity daily summary job iteration failed; will retry.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Public entry for tests / manual triggers.
    /// </summary>
    public async Task RunAggregationAsync(DateOnly date, CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var snapshots = scope.ServiceProvider.GetRequiredService<IActivitySnapshotRepository>();
        var summaries = scope.ServiceProvider.GetRequiredService<IActivityDailySummaryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var keys = await snapshots.GetEmployeeKeysForDateAsync(date, ct);
        _logger.LogInformation(
            "Activity daily summary job started. Date={Date} EmployeeCount={Count}",
            date,
            keys.Count);

        var now = clock.UtcNow;
        var processed = 0;

        foreach (var (tenantId, employeeId) in keys)
        {
            ct.ThrowIfCancellationRequested();

            var daySnapshots = await snapshots.GetAllByEmployeeDateAsync(
                tenantId, employeeId, date, ct);

            if (daySnapshots.Count == 0)
                continue;

            var summary = ActivityDailySummaryAggregator.Aggregate(
                tenantId, employeeId, date, daySnapshots, now);

            await summaries.UpsertAsync(summary, ct);
            processed++;
        }

        if (processed > 0)
            await unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Activity daily summary job finished. Date={Date} Processed={Processed}",
            date,
            processed);
    }
}
