using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: task-creation requests pending the caller's
/// decision (My Team spec §8.2). Ported onto the unified Work Approvals model (backend merge plan
/// §3/§7): queries IWorkApprovalEligibility.ListDecidableAcrossLedProjectsAsync filtered to
/// WorkActionTypes.TaskCreate - the same shared eligibility call the capability gate
/// (WorkLeadershipService.HasPendingWorkApprovalsAsync) makes, so this source's content and that
/// gate can never disagree about what counts. Reads the new task's title straight from
/// WorkApprovalRequest.TargetTitle (CreateTaskCommandHandler stamps it at submit time) instead of
/// deserializing PayloadJson - a simplification the old per-type payload DTO no longer needs.
///
/// Eligibility is now CanDecide (own-or-ancestor of the request's PositionObjectiveId), a wider,
/// always-live-re-checked set than the old flat "I am the objective's current owner" check - an
/// intentional, already-shipped property of the new Work Approvals engine, not a porting
/// regression (backend merge plan §3).</summary>
public sealed class WorkTaskCreationTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    IWorkApprovalEligibility eligibility)
    : ITeamActionSource
{
    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];
    private static readonly IReadOnlySet<string> ActionTypes = new HashSet<string> { WorkActionTypes.TaskCreate };

    public string Key => "work.task_creation";
    public string Domain => ActionSourceSummary.DomainWork;

    public async Task<bool> IsGatedAsync(CancellationToken ct = default)
    {
        var active = await modules.GetActiveModuleKeysForTenantAsync(currentUser.TenantId, ct);
        return WorkModuleKeys.Any(key => active.Contains(key, StringComparer.OrdinalIgnoreCase));
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
            var title = string.IsNullOrWhiteSpace(r.TargetTitle) ? "New task request" : $"New task - {r.TargetTitle}";
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
