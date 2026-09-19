using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.WorkManagement;

/// <summary>
/// Sends a one-time "sprint overdue" notification when an Active sprint's end date passes with
/// unfinished tasks. Never mutates Sprint.Status - completion is always a manual owner action
/// (CompleteSprintCommand), and there is no more Future/Incomplete auto-advance since Draft sprints
/// have no dates to watch and overdue is a purely computed display state on the frontend.
/// </summary>
public sealed class SprintLifecycleJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<SprintLifecycleJob> _logger;

    public SprintLifecycleJob(IServiceProvider services, ILogger<SprintLifecycleJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
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
                _logger.LogError(ex, "SprintLifecycleJob encountered an error.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var sprints = scope.ServiceProvider.GetRequiredService<ISprintRepository>();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
        var statuses = scope.ServiceProvider.GetRequiredService<ITaskStatusRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var objectives = scope.ServiceProvider.GetRequiredService<IObjectiveRepository>();
        var members = scope.ServiceProvider.GetRequiredService<IProjectMemberRepository>();
        var membership = scope.ServiceProvider.GetRequiredService<IMilestoneMembershipCoordinator>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // sprints and their related tables are under FORCE row-level security. A background
        // scope defaults to system mode, which the tenant_isolation policy admits for neither -
        // so the cross-tenant sweep needs admin mode, and each tenant's rows need that tenant's
        // context established first. Mirrors WellnessRuleEvaluatorJob/LocationRuleEvaluatorJob.
        tenantContext.SetAdminMode();

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var activeSprints = await sprints.GetByStatusAsync(SprintStatuses.Active, ct);

        var notifiedCount = 0;

        foreach (var tenantGroup in activeSprints.GroupBy(s => s.TenantId))
        {
            ct.ThrowIfCancellationRequested();

            var tenantId = tenantGroup.Key;
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null) continue;
            await tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

            var tenantNotified = 0;

            foreach (var sprint in tenantGroup)
            {
                ct.ThrowIfCancellationRequested();
                if (sprint.EndDate is null) continue;

                var sprintTasks = await tasks.GetBySprintIdAsync(sprint.TenantId, sprint.Id, ct);
                var allTasksComplete = sprintTasks.Count > 0;
                foreach (var task in sprintTasks)
                {
                    var status = await statuses.GetByIdForTenantAsync(sprint.TenantId, task.StatusId, ct);
                    if (status is null || !status.MarksTaskComplete) { allTasksComplete = false; break; }
                }

                if (!ShouldNotifyOverdue(sprint.EndDate.Value, today, allTasksComplete, sprint.OverdueNotifiedAt is not null))
                    continue;

                sprint.OverdueNotifiedAt = DateTimeOffset.UtcNow;
                sprints.Update(sprint);
                tenantNotified++;

                var objective = await objectives.GetByIdForTenantAsync(sprint.TenantId, sprint.ObjectiveId, ct);
                if (objective is null) continue;

                var objectiveMembers = await members.ListActiveForObjectiveAsync(tenantId, objective.Id, ct);
                foreach (var member in objectiveMembers)
                {
                    var assignee = await membership.GetActiveAssigneeAsync(tenantId, member.EmployeeId, ct);
                    if (assignee is null) continue;

                    await notifications.SendTemplatedAsync(
                        tenantId, assignee.UserId, "work_sprint_overdue",
                        new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                        "sprint", sprint.Id, ct);
                }
            }

            if (tenantNotified > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("SprintLifecycleJob notified {Count} overdue sprints for tenant {TenantId}.", tenantNotified, tenantId);
            }

            notifiedCount += tenantNotified;
        }
    }

    /// <summary>Pure decision function, unit-tested directly without the BackgroundService/DI machinery.</summary>
    public static bool ShouldNotifyOverdue(DateOnly endDate, DateOnly today, bool allTasksComplete, bool alreadyNotified)
        => today > endDate && !allTasksComplete && !alreadyNotified;
}
