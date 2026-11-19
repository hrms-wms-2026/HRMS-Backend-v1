using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: module (objective) change requests - delete/edit/
/// transfer/achieve/unachieve/allocation-extension - pending the caller's decision (My Team spec
/// §8.2). Ported onto the unified Work Approvals model (backend merge plan §3/§7): the 6 legacy
/// ObjectiveChangeRequestTypes values map 1:1 onto MyTeamApprovalActionTypes.ObjectiveChange (the
/// 6 WorkActionTypes.Module* values), kept as one merged source/widget key for continuity. Reads
/// TargetTitle directly (ModuleActionSubmitter stamps it as module.Title at submit time) instead
/// of the old IObjectiveRepository.GetByIdsForTenantAsync enrichment join - a simplification, not
/// a behavior change.
///
/// V1 single-legal-entity assumption (clarification 5, same stance as the other work.* sources).</summary>
public sealed class WorkObjectiveChangeTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    IWorkApprovalEligibility eligibility)
    : ITeamActionSource
{
    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public string Key => "work.objective_change";
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
            currentUser.TenantId, callerEmployeeId.Value, legalEntityId, MyTeamApprovalActionTypes.ObjectiveChange, ct);
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
                ? DescribeActionType(r.ActionType)
                : $"{DescribeActionType(r.ActionType)} - {r.TargetTitle}";
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

    private static string DescribeActionType(string actionType) => actionType switch
    {
        WorkActionTypes.ModuleDelete => "Delete request",
        WorkActionTypes.ModuleEdit => "Edit request",
        WorkActionTypes.ModuleTransfer => "Transfer request",
        WorkActionTypes.ModuleAchieve => "Mark achieved",
        WorkActionTypes.ModuleUnachieve => "Mark not achieved",
        WorkActionTypes.ModuleAllocationExtend => "Allocation extension",
        _ => "Objective change",
    };
}
