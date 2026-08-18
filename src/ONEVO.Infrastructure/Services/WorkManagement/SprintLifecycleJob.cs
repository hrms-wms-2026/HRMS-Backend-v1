using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
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
                await using var scope = _services.CreateAsyncScope();
                var sprints = scope.ServiceProvider.GetRequiredService<ISprintRepository>();
                var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
                var statuses = scope.ServiceProvider.GetRequiredService<ITaskStatusRepository>();

                var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
                var candidates = (await sprints.GetByStatusAsync(SprintStatuses.Future, stoppingToken))
                    .Concat(await sprints.GetByStatusAsync(SprintStatuses.Active, stoppingToken));

                var advancedCount = 0;
                foreach (var sprint in candidates)
                {
                    var allTasksComplete = false;
                    if (sprint.Status == SprintStatuses.Active)
                    {
                        var sprintTasks = await tasks.GetBySprintIdAsync(sprint.TenantId, sprint.Id, stoppingToken);
                        allTasksComplete = sprintTasks.Count > 0;
                        foreach (var task in sprintTasks)
                        {
                            var status = await statuses.GetByIdForTenantAsync(sprint.TenantId, task.StatusId, stoppingToken);
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
                    advancedCount++;

                    if (next == SprintStatuses.Incomplete)
                    {
                        var members = scope.ServiceProvider.GetRequiredService<IProjectMemberRepository>();
                        var membership = scope.ServiceProvider.GetRequiredService<IMilestoneMembershipCoordinator>();
                        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                        var objectives = scope.ServiceProvider.GetRequiredService<IObjectiveRepository>();

                        var objective = await objectives.GetByIdForTenantAsync(sprint.TenantId, sprint.ObjectiveId, stoppingToken);
                        if (objective is not null)
                        {
                            var activeMembers = await members.ListActiveForObjectiveAsync(sprint.TenantId, sprint.ObjectiveId, stoppingToken);
                            foreach (var member in activeMembers)
                            {
                                var assignee = await membership.GetActiveAssigneeAsync(sprint.TenantId, member.EmployeeId, stoppingToken);
                                if (assignee is null) continue;

                                await notifications.SendTemplatedAsync(
                                    sprint.TenantId, assignee.UserId, "work_sprint_incomplete",
                                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                                    "sprint", sprint.Id, stoppingToken);
                            }
                        }
                    }
                }

                if (advancedCount > 0)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("SprintLifecycleJob advanced {Count} sprints.", advancedCount);
                }
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
