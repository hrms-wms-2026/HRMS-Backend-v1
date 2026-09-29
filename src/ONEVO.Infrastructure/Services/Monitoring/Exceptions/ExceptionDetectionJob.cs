using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Helpers;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Infrastructure.Services.Monitoring.Exceptions;

/// <summary>Nightly (23:30 UTC, 30 min after ActivityDailySummaryJob) multi-day pattern detection and escalation sweep.</summary>
public sealed class ExceptionDetectionJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeOnly TargetUtcTime = new(23, 30);
    private static readonly TimeSpan EscalationThreshold = TimeSpan.FromDays(3);

    private readonly IServiceProvider _services;
    private readonly ILogger<ExceptionDetectionJob> _logger;
    private DateOnly? _lastRunDateUtc;

    public ExceptionDetectionJob(IServiceProvider services, ILogger<ExceptionDetectionJob> logger)
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

                if (now.TimeOfDay >= TargetUtcTime.ToTimeSpan() && _lastRunDateUtc != today)
                {
                    await RunDetectionAsync(today.AddDays(-1), stoppingToken);
                    _lastRunDateUtc = today;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Exception detection job iteration failed; will retry.");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RunDetectionAsync(DateOnly targetDate, CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var exceptions = scope.ServiceProvider.GetRequiredService<IExceptionRepository>();
        var summaries = scope.ServiceProvider.GetRequiredService<IActivityDailySummaryRepository>();
        var reports = scope.ServiceProvider.GetRequiredService<IProductivityReportRepository>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var alertRouters = scope.ServiceProvider.GetRequiredService<IExceptionAlertRouterFactory>();

        // exceptions and activity_daily_summary are under FORCE row-level security. A
        // background scope defaults to system mode, which the tenant_isolation policy
        // admits for neither - so the cross-tenant discovery sweep below needs admin mode
        // before it can see any row. Mirrors LocationRuleEvaluatorJob/ActivityDailySummaryJob.
        tenantContext.SetAdminMode();

        var now = clock.UtcNow;
        var keys = await exceptions.GetActiveTenantEmployeeKeysAsync(
            targetDate.ToDateTime(TimeOnly.MinValue), ct);

        var createdCount = 0;
        var escalatedCount = 0;

        // Grouped tenant-major so each tenant's creates and escalation sweep run - and save -
        // while that tenant's context is active on the connection, before switching to the
        // next tenant. A single batched SaveChangesAsync across tenants would flush every
        // tenant's changes under whichever tenant was switched to last, and every earlier
        // tenant's rows would fail the RLS WITH CHECK constraint.
        foreach (var tenantGroup in keys.GroupBy(k => k.TenantId))
        {
            ct.ThrowIfCancellationRequested();

            var tenantId = tenantGroup.Key;
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null) continue;
            await tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

            // IEmployeeAuthorityResolver reads the tenant from ICurrentUser, which a background
            // scope doesn't have - the factory pins the router to this tenant instead.
            var alerts = alertRouters.CreateForTenant(tenantId);
            var created = new List<DomainException>();
            var tenantEscalated = 0;

            foreach (var (_, employeeId) in tenantGroup)
            {
                ct.ThrowIfCancellationRequested();

                var last3Days = await summaries.GetRangeAsync(tenantId, employeeId, targetDate.AddDays(-2), targetDate, ct);
                if (ExceptionDetectionRules.IsSustainedLowActivity(last3Days, targetDate)
                    && !await exceptions.HasUnresolvedAsync(tenantId, employeeId, ExceptionType.SustainedLowActivity, ct))
                {
                    created.Add(new DomainException
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                        Type = ExceptionType.SustainedLowActivity, Status = ExceptionStatus.Open,
                        Title = "Sustained low activity",
                        Description = $"Activity score below {ExceptionDetectionRules.SustainedLowActivityScoreThreshold} for {ExceptionDetectionRules.SustainedLowActivityConsecutiveDays} consecutive days.",
                        DetectedAt = now,
                        MetadataJson = new ExceptionMetadata
                        {
                            WorkDate = targetDate,
                            Measures = ExceptionPatternMeasures.SustainedLowActivity(last3Days.Select(d => d.ActivityScore).ToList())
                        }.ToJson()
                    });
                }

                var thisWeek = await reports.GetEmployeeAggregateAsync(tenantId, employeeId, targetDate.AddDays(-6), targetDate, ct);
                var trailingFourWeeks = await reports.GetEmployeeAggregateAsync(tenantId, employeeId, targetDate.AddDays(-34), targetDate.AddDays(-7), ct);
                var trailingAvgPerWeek = trailingFourWeeks.DayCount > 0 ? trailingFourWeeks.TotalWorkedMinutes / 4 : 0;
                if (ExceptionDetectionRules.IsAttendanceIrregularity(thisWeek.TotalWorkedMinutes, trailingAvgPerWeek)
                    && !await exceptions.HasUnresolvedAsync(tenantId, employeeId, ExceptionType.AttendanceIrregularity, ct))
                {
                    created.Add(new DomainException
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                        Type = ExceptionType.AttendanceIrregularity, Status = ExceptionStatus.Open,
                        Title = "Attendance irregularity",
                        Description = $"This week's worked time ({thisWeek.TotalWorkedMinutes} min) is well below the trailing 4-week average ({trailingAvgPerWeek} min/week).",
                        DetectedAt = now,
                        MetadataJson = new ExceptionMetadata
                        {
                            WorkDate = targetDate,
                            Measures = ExceptionPatternMeasures.AttendanceIrregularity(thisWeek.TotalWorkedMinutes, trailingAvgPerWeek)
                        }.ToJson()
                    });
                }

                var todaySummary = last3Days.FirstOrDefault(s => s.Date == targetDate);
                var trailing30Days = await reports.GetEmployeeAggregateAsync(tenantId, employeeId, targetDate.AddDays(-30), targetDate.AddDays(-1), ct);
                if (todaySummary is not null && trailing30Days.DayCount > 0
                    && ExceptionDetectionRules.IsUnusualActivityPattern(todaySummary.ActivityScore, trailing30Days.AverageActivityScore)
                    && !await exceptions.HasUnresolvedAsync(tenantId, employeeId, ExceptionType.UnusualActivityPattern, ct))
                {
                    created.Add(new DomainException
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                        Type = ExceptionType.UnusualActivityPattern, Status = ExceptionStatus.Open,
                        Title = "Unusual activity pattern",
                        Description = $"Today's activity score ({todaySummary.ActivityScore}) deviates sharply from the 30-day average ({trailing30Days.AverageActivityScore}).",
                        DetectedAt = now,
                        MetadataJson = new ExceptionMetadata
                        {
                            WorkDate = targetDate,
                            Measures = ExceptionPatternMeasures.UnusualActivityPattern(todaySummary.ActivityScore, trailing30Days.AverageActivityScore)
                        }.ToJson()
                    });
                }
            }

            // Cases and their in-app alerts go into the same SaveChanges, per tenant, while that
            // tenant's context is still active on the connection.
            foreach (var exception in created)
            {
                await exceptions.AddAsync(exception, ct);
                await NotifySafelyAsync(() => alerts.NotifyDetectedAsync(exception, ct), exception, ct);
            }

            var stale = await exceptions.GetStaleOpenAsync(tenantId, now - EscalationThreshold, ct);
            foreach (var open in stale)
            {
                open.Status = ExceptionStatus.Escalated;
                open.EscalatedAt = now;
                exceptions.Update(open);
                await NotifySafelyAsync(() => alerts.NotifyEscalatedAsync(open, ct), open, ct);
                tenantEscalated++;
            }

            var tenantCreated = created.Count;
            if (tenantCreated > 0 || tenantEscalated > 0)
                await exceptions.SaveChangesAsync(ct);

            createdCount += tenantCreated;
            escalatedCount += tenantEscalated;
        }

        _logger.LogInformation(
            "Exception detection finished. Date={Date} EmployeesScanned={Count} Created={Created} Escalated={Escalated}",
            targetDate, keys.Count, createdCount, escalatedCount);
    }

    /// <summary>A failure working out who to alert must not stop the case itself being saved, or
    /// the rest of the tenants being swept - the case still shows up in the reviewers' list.</summary>
    private async Task NotifySafelyAsync(Func<Task> notify, DomainException exception, CancellationToken ct)
    {
        try
        {
            await notify();
        }
        catch (System.Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Could not send the in-app alert for exception {ExceptionId} ({Type}) of employee {EmployeeId}; the case is still saved.",
                exception.Id, exception.Type, exception.EmployeeId);
        }
    }
}
