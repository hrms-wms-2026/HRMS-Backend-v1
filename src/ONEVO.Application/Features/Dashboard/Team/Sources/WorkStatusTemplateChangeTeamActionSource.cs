using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: task-status template change requests - a project's
/// task-status set, not one task's status (My Team spec §8.2). Ported onto the unified Work
/// Approvals model (backend merge plan §3/§7): the old bespoke ITaskStatusChangeAccessService
/// project-root-approver check is now exactly WorkApprovalDecisionRules.CanDecide against the
/// request's PositionObjectiveId (the project's root module) - a refactor onto the shared rule,
/// not an intended behavior change. Reads TargetTitle directly (the project's name, stamped at
/// submit time) instead of the old IProjectRepository.ListByIdsAsync enrichment join.
///
/// V1 single-legal-entity assumption (clarification 5, same stance as the other work.* sources).</summary>
public sealed class WorkStatusTemplateChangeTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    IWorkApprovalEligibility eligibility)
    : ITeamActionSource
{
    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];
    private static readonly IReadOnlySet<string> ActionTypes = new HashSet<string> { WorkActionTypes.ProjectStatusTemplateChange };

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

        var pending = await eligibility.ListDecidableAcrossLedProjectsAsync(
            currentUser.TenantId, callerEmployeeId.Value, legalEntityId, ActionTypes, ct);
        if (pending.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var names = await identity.ResolveDisplayNamesByEmployeeIdAsync(
            currentUser.TenantId, pending.Select(r => r.RequestedByEmployeeId).Distinct().ToList(), ct);

        var ordered = pending.OrderBy(r => r.CreatedAt).ToList();
        var oldest = ordered[0].CreatedAt;

        var topItems = ordered.Take(top).Select(r =>
        {
            var requesterName = names.GetValueOrDefault(r.RequestedByEmployeeId);
            var title = string.IsNullOrWhiteSpace(r.TargetTitle)
                ? "Task status template change"
                : $"Task status template change - {r.TargetTitle}";
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
                    ["relatedEntityType"] = "work_approval_request",
                    ["relatedEntityId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }
}
