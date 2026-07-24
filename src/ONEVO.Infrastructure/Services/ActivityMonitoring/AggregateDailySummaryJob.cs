using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Persistence;
using System.Text.Json;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

public sealed class AggregateDailySummaryJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceProvider _services;
    private readonly ILogger<AggregateDailySummaryJob> _logger;

    public AggregateDailySummaryJob(IServiceProvider services, ILogger<AggregateDailySummaryJob> logger)
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
                await Task.Delay(Interval, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AggregateDailySummaryJob failed; will retry next interval.");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IActivityMonitoringRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = today.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var activeEmployees = await db.ActivitySnapshots
            .Where(s => s.CapturedAt >= start && s.CapturedAt <= end)
            .GroupBy(s => new { s.TenantId, s.EmployeeId })
            .Select(g => new { g.Key.TenantId, g.Key.EmployeeId })
            .ToListAsync(ct);

        foreach (var emp in activeEmployees)
        {
            var snapshots = await repo.GetSnapshotsForDayAsync(emp.EmployeeId, today, ct);
            var appUsage = await repo.GetAppUsageForDayAsync(emp.EmployeeId, today, ct);
            var meetings = await repo.GetMeetingsForDayAsync(emp.EmployeeId, today, ct);

            var totalActiveMin = snapshots.Sum(s => s.ActiveSeconds) / 60;
            var totalIdleMin = snapshots.Sum(s => s.IdleSeconds) / 60;
            var totalMeetingMin = meetings.Sum(m => m.DurationMinutes);
            var keyboardTotal = snapshots.Sum(s => s.KeyboardEventsCount);
            var mouseTotal = snapshots.Sum(s => s.MouseEventsCount);
            var intensityAvg = snapshots.Count > 0
                ? snapshots.Average(s => (double)s.IntensityScore)
                : 0;

            var productiveMin = appUsage.Where(u => u.IsProductive == true).Sum(u => u.TotalSeconds) / 60;
            var personalMin = appUsage.Where(u => u.IsProductive == false).Sum(u => u.TotalSeconds) / 60;
            var unknownMin = appUsage.Where(u => u.IsProductive is null).Sum(u => u.TotalSeconds) / 60;

            var totalMin = totalActiveMin + totalIdleMin;
            var activePercent = totalMin > 0 ? (decimal)totalActiveMin / totalMin * 100 : 0;
            var activityScore = Math.Min((decimal)intensityAvg, 100);

            var topApps = appUsage
                .GroupBy(u => u.ApplicationName)
                .Select(g => new { app = g.Key, seconds = g.Sum(u => u.TotalSeconds) })
                .OrderByDescending(x => x.seconds)
                .Take(5)
                .ToList();
            var topAppsJson = JsonSerializer.Serialize(topApps);

            var summary = new ActivityDailySummary
            {
                Id = Guid.NewGuid(),
                TenantId = emp.TenantId,
                EmployeeId = emp.EmployeeId,
                Date = today,
                TotalActiveMinutes = totalActiveMin,
                TotalIdleMinutes = totalIdleMin,
                TotalMeetingMinutes = totalMeetingMin,
                ActivePercentage = Math.Round(activePercent, 2),
                ProductiveAppMinutes = productiveMin,
                PersonalAppMinutes = personalMin,
                UnknownAppMinutes = unknownMin,
                FocusMinutes = 0,
                ActivityScore = Math.Round(activityScore, 2),
                DataCoveragePercentage = 100,
                TopAppsJson = topAppsJson,
                IntensityAvg = Math.Round((decimal)intensityAvg, 2),
                KeyboardTotal = keyboardTotal,
                MouseTotal = mouseTotal
            };

            await repo.UpsertDailySummaryAsync(summary, ct);
        }

        if (activeEmployees.Count > 0)
            await uow.SaveChangesAsync(ct);

        _logger.LogInformation("AggregateDailySummaryJob: aggregated {Count} employee summaries for {Date}.",
            activeEmployees.Count, today);
    }
}
