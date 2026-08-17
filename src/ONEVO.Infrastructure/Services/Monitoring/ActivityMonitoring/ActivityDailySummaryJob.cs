using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

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
        var appUsage = scope.ServiceProvider.GetRequiredService<IAppUsageSnapshotRepository>();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingSignalRepository>();
        var summaries = scope.ServiceProvider.GetRequiredService<IActivityDailySummaryRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
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

            var dayAppUsage = await appUsage.GetAllByEmployeeDateAsync(tenantId, employeeId, date, ct);
            var dayMeetings = await meetings.GetAllByEmployeeDateAsync(tenantId, employeeId, date, ct);

            var summary = ActivityDailySummaryAggregator.Aggregate(
                tenantId, employeeId, date, daySnapshots, now,
                appUsageSnapshots: dayAppUsage,
                meetingSignals: dayMeetings);

            await summaries.UpsertAsync(summary, ct);

            const decimal LowActivityThreshold = 40m;
            const int FocusNudgeMinutesThreshold = 120;
            const int FocusNudgeSessionsThreshold = 2;

            if (summary.ActivityScore < LowActivityThreshold)
            {
                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                    Type = NotificationType.LowActivityAlert,
                    Title = "Lower activity today",
                    Message = $"Your activity score for {date:yyyy-MM-dd} was {summary.ActivityScore}.",
                    MetadataJson = $$"""{"activityScore":{{summary.ActivityScore}},"date":"{{date:yyyy-MM-dd}}"}""",
                    CreatedAt = now
                }, ct);
            }

            if (summary.FocusMinutes >= FocusNudgeMinutesThreshold || summary.DeepFocusSessionsCount >= FocusNudgeSessionsThreshold)
            {
                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                    Type = NotificationType.FocusNudge,
                    Title = "Great focus today",
                    Message = $"You had {summary.FocusMinutes} minutes of deep focus across {summary.DeepFocusSessionsCount} sessions.",
                    MetadataJson = $$"""{"focusMinutes":{{summary.FocusMinutes}},"sessions":{{summary.DeepFocusSessionsCount}}}""",
                    CreatedAt = now
                }, ct);
            }

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
