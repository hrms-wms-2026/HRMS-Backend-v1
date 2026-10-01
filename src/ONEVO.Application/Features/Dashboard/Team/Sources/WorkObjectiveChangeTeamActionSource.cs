using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: objective change requests (delete/edit/transfer/
/// achieve/unachieve/allocation-extension) pending the caller's decision as reporting manager (My
/// Team spec §8.2). Reuses IObjectiveChangeRequestRepository.ListPendingForApproverAsync verbatim
/// - the exact method ListMyObjectiveChangeRequestsQueryHandler already calls, which already
/// covers every ObjectiveChangeRequestTypes value including ExtendAllocation ("including
/// allocation extension" per spec) since they are all just rows of the one entity, not a separate
/// predicate branch.
///
/// V1 single-legal-entity assumption (clarification 5, same stance as the other work.* sources).</summary>
public sealed class WorkObjectiveChangeTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    IObjectiveChangeRequestRepository changeRequests,
    IObjectiveRepository objectives)
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

        var pending = await changeRequests.ListPendingForApproverAsync(currentUser.TenantId, callerEmployeeId.Value, ct);
        if (pending.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var names = await identity.ResolveDisplayNamesByEmployeeIdAsync(
            currentUser.TenantId, pending.Select(r => r.RequestedById).Distinct().ToList(), ct);
        var objectivesById = (await objectives.GetByIdsForTenantAsync(
                currentUser.TenantId, pending.Select(r => r.ObjectiveId).Distinct().ToList(), ct))
            .ToDictionary(o => o.Id);

        var ordered = pending.OrderBy(r => r.CreatedAt).ToList();
        var oldest = ordered[0].CreatedAt;

        var topItems = ordered.Take(top).Select(r =>
        {
            objectivesById.TryGetValue(r.ObjectiveId, out var objective);
            var requesterName = names.GetValueOrDefault(r.RequestedById);
            var title = objective is null
                ? DescribeRequestType(r.RequestType)
                : $"{DescribeRequestType(r.RequestType)} - {objective.Title}";
            return new ActionItem(
                Key,
                r.Id,
                title,
                r.RequestedById,
                requesterName,
                r.CreatedAt,
                null,
                new ActionItemLink(ActionItemLink.KindWorkRequest, new Dictionary<string, string>
                {
                    ["relatedEntityType"] = "objective_change_request",
                    ["relatedEntityId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }

    private static string DescribeRequestType(string requestType) => requestType switch
    {
        ObjectiveChangeRequestTypes.Delete => "Delete request",
        ObjectiveChangeRequestTypes.Edit => "Edit request",
        ObjectiveChangeRequestTypes.Transfer => "Transfer request",
        ObjectiveChangeRequestTypes.Achieve => "Mark achieved",
        ObjectiveChangeRequestTypes.Unachieve => "Mark not achieved",
        ObjectiveChangeRequestTypes.ExtendAllocation => "Allocation extension",
        _ => "Objective change",
    };
}
