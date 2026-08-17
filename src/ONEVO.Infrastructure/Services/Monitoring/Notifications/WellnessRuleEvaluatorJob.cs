using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.Monitoring.Notifications;

/// <summary>Every 5 minutes, evaluates break/idle continuity for every currently-monitored employee.</summary>
public sealed class WellnessRuleEvaluatorJob : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(3);
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BreakReminderCooldown = TimeSpan.FromHours(2);
    private static readonly TimeSpan LongIdleCooldown = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<WellnessRuleEvaluatorJob> _logger;

    public WellnessRuleEvaluatorJob(IServiceProvider services, ILogger<WellnessRuleEvaluatorJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Wellness rule evaluation iteration failed; will retry next cycle.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var deviceState = scope.ServiceProvider.GetRequiredService<IDeviceStateSnapshotRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var now = clock.UtcNow;
        var keys = await deviceState.GetActiveEmployeeKeysAsync(now - ActiveWindow, ct);
        var created = 0;

        foreach (var (tenantId, employeeId) in keys)
        {
            ct.ThrowIfCancellationRequested();

            var recent = await deviceState.GetRecentAsync(tenantId, employeeId, now - LookbackWindow, ct);
            var result = WellnessRuleEvaluator.Evaluate(recent, now);

            if (result.BreakReminderTriggered
                && !await notifications.ExistsRecentAsync(tenantId, employeeId, NotificationType.BreakReminder, now - BreakReminderCooldown, ct))
            {
                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                    Type = NotificationType.BreakReminder,
                    Title = "Time for a break",
                    Message = $"You've been active for {result.StreakMinutes} minutes straight. Consider taking a short break.",
                    MetadataJson = $$"""{"streakMinutes":{{result.StreakMinutes}}}""",
                    CreatedAt = now
                }, ct);
                created++;
            }

            if (result.LongIdleTriggered
                && !await notifications.ExistsRecentAsync(tenantId, employeeId, NotificationType.LongIdleAlert, now - LongIdleCooldown, ct))
            {
                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                    Type = NotificationType.LongIdleAlert,
                    Title = "Still there?",
                    Message = $"No activity detected for {result.StreakMinutes} minutes.",
                    MetadataJson = $$"""{"idleMinutes":{{result.StreakMinutes}}}""",
                    CreatedAt = now
                }, ct);
                created++;
            }
        }

        if (created > 0)
            await notifications.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Wellness rule evaluation finished. EmployeesScanned={Count} NotificationsCreated={Created}",
            keys.Count, created);
    }
}
