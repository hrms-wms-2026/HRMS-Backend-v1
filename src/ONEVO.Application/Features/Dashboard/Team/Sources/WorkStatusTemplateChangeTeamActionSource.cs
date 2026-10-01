using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: task-status template change requests pending the
/// caller's decision as a project's root-module approver (My Team spec §8.2). No existing
/// endpoint lists these across every project at once - every caller today works one project at a
/// time (GetProjectTaskStatusChangeRequestsQueryHandler) - so this source adds
/// ITaskStatusChangeRequestRepository.ListAllPendingAsync (new, tenant-wide, mirrors
/// ListPendingForProjectAsync's own predicate minus the ProjectId filter) and then narrows to the
/// caller's actual approver scope by calling ITaskStatusChangeAccessService.ResolveAsync once per
/// DISTINCT project among the pending rows (never once per row) - the same CanEditDirectly gate
/// GetProjectTaskStatusChangeRequestsQueryHandler already uses, just batched over however many
/// projects actually have a pending request instead of a single one.
///
/// V1 single-legal-entity assumption (clarification 5, same stance as the other work.* sources).</summary>
public sealed class WorkStatusTemplateChangeTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    ITaskStatusChangeRequestRepository requests,
    ITaskStatusChangeAccessService access,
    IProjectRepository projects)
    : ITeamActionSource
{
    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public string Key => "work.status_template_change";
    public string Domain => ActionSourceSummary.DomainWork;

    public async Task<bool> IsGatedAsync(CancellationToken ct = default)
    {
        var activeModules = await modules.GetActiveModuleKeysForTenantAsync(currentUser.TenantId, ct);
        return WorkModuleKeys.Any(key => activeModules.Contains(key, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
    {
        var callerEmployeeId = await identity.ResolveCallerEmployeeIdAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var allPending = await requests.ListAllPendingAsync(currentUser.TenantId, ct);
        if (allPending.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var distinctProjectIds = allPending.Select(r => r.ProjectId).Distinct().ToList();
        var approverProjectIds = new HashSet<Guid>();
        foreach (var projectId in distinctProjectIds)
        {
            var projectAccess = await access.ResolveAsync(currentUser.TenantId, projectId, callerEmployeeId.Value, ct);
            if (projectAccess is { CanEditDirectly: true })
                approverProjectIds.Add(projectId);
        }

        var approvable = allPending.Where(r => approverProjectIds.Contains(r.ProjectId)).ToList();
        if (approvable.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var names = await identity.ResolveDisplayNamesByEmployeeIdAsync(
            currentUser.TenantId, approvable.Select(r => r.RequestedByEmployeeId).Distinct().ToList(), ct);
        var projectsById = (await projects.ListByIdsAsync(
                currentUser.TenantId, approvable.Select(r => r.ProjectId).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id);

        var ordered = approvable.OrderBy(r => r.CreatedAt).ToList();
        var oldest = ordered[0].CreatedAt;

        var topItems = ordered.Take(top).Select(r =>
        {
            projectsById.TryGetValue(r.ProjectId, out var project);
            var requesterName = names.GetValueOrDefault(r.RequestedByEmployeeId);
            var title = project is null
                ? "Task status template change"
                : $"Task status template change - {project.Name}";
            return new ActionItem(
                Key,
                r.Id,
                title,
                r.RequestedByEmployeeId,
                requesterName,
                r.CreatedAt,
                null,
                new ActionItemLink(ActionItemLink.KindWorkRequest, new Dictionary<string, string>
                {
                    ["relatedEntityType"] = "task_status_change_request",
                    ["relatedEntityId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }
}
