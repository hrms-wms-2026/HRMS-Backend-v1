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
/// Advances Sprint.Status for the two date-driven transitions: Future->Active when the start date
/// arrives, and Active->Incomplete when the end date passes with unfinished tasks. Completion is
/// always a manual owner action (CompleteSprintCommand) - this job never sets Complete. Mirrors
/// AgentCommandExpiryJob's shape (PeriodicTimer, per-tick DI scope, catch-and-log).
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

    /// <summary>Public entry for tests / manual triggers - same precedent as
    /// ActivityDailySummaryJob.RunAggregationAsync.</summary>
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

        // sprints, work_tasks and related tables are under FORCE row-level security. A
        // background scope defaults to system mode, which the tenant_isolation policy
        // admits for neither - so the cross-tenant sweep below needs admin mode, and each
        // tenant's sprints need that tenant's context established before reading/writing
        // them. Mirrors LocationRuleEvaluatorJob/ActivityDailySummaryJob.
        tenantContext.SetAdminMode();

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var candidates = (await sprints.GetByStatusAsync(SprintStatuses.Future, ct))
            .Concat(await sprints.GetByStatusAsync(SprintStatuses.Active, ct))
            .ToList();

        var advancedCount = 0;

        // Grouped tenant-major so each tenant's sprint advances - and save - happen while
        // that tenant's context is active on the connection, before switching to the next
        // tenant. A single batched SaveChangesAsync across tenants would flush every
        // tenant's changes under whichever tenant was switched to last, and every earlier
        // tenant's rows would fail the RLS WITH CHECK constraint.
        foreach (var tenantGroup in candidates.GroupBy(s => s.TenantId))
        {
            ct.ThrowIfCancellationRequested();

            var tenantId = tenantGroup.Key;
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null) continue;
            await tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

            var tenantAdvanced = 0;

            foreach (var sprint in tenantGroup)
            {
                ct.ThrowIfCancellationRequested();

                var allTasksComplete = false;
                if (sprint.Status == SprintStatuses.Active)
                {
                    var sprintTasks = await tasks.GetBySprintIdAsync(sprint.TenantId, sprint.Id, ct);
                    allTasksComplete = sprintTasks.Count > 0;
                    foreach (var task in sprintTasks)
                    {
                        var status = await statuses.GetByIdForTenantAsync(sprint.TenantId, task.StatusId, ct);
                        if (status is null || !status.MarksTaskComplete)
                        {
                            allTasksComplete = false;
                            break;
                        }
                    }
                }

                var next = DetermineNextStatus(sprint.Status, sprint.StartDate, sprint.EndDate, today, allTasksComplete);
                if (next is null)
                    continue;

                sprint.Status = next;
                sprint.UpdatedAt = DateTimeOffset.UtcNow;
                sprints.Update(sprint);
                tenantAdvanced++;

                if (next == SprintStatuses.Incomplete)
                {
                    var members = scope.ServiceProvider.GetRequiredService<IProjectMemberRepository>();
                    var membership = scope.ServiceProvider.GetRequiredService<IMilestoneMembershipCoordinator>();
                    var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                    var objectives = scope.ServiceProvider.GetRequiredService<IObjectiveRepository>();

                    var objective = await objectives.GetByIdForTenantAsync(sprint.TenantId, sprint.ObjectiveId, ct);
                    if (objective is not null)
                    {
                        var activeMembers = await members.ListActiveForObjectiveAsync(sprint.TenantId, sprint.ObjectiveId, ct);
                        foreach (var member in activeMembers)
                        {
                            var assignee = await membership.GetActiveAssigneeAsync(sprint.TenantId, member.EmployeeId, ct);
                            if (assignee is null) continue;

                            await notifications.SendTemplatedAsync(
                                sprint.TenantId, assignee.UserId, "work_sprint_incomplete",
                                new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                                "sprint", sprint.Id, ct);
                        }
                    }
                }
            }

            if (tenantAdvanced > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("SprintLifecycleJob advanced {Count} sprints for tenant {TenantId}.", tenantAdvanced, tenantId);
            }

            advancedCount += tenantAdvanced;
        }
    }

    /// <summary>Pure decision function, extracted for direct unit testing without the BackgroundService/DI machinery. Returns null if no transition applies.</summary>
    public static string? DetermineNextStatus(string currentStatus, DateOnly startDate, DateOnly endDate, DateOnly today, bool allTasksComplete)
    {
        if (currentStatus == SprintStatuses.Future && today >= startDate)
            return SprintStatuses.Active;

        if (currentStatus == SprintStatuses.Active && today > endDate && !allTasksComplete)
            return SprintStatuses.Incomplete;

        return null;
    }
}
