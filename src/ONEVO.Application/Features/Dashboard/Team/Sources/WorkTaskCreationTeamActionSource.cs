using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: task-creation requests pending the caller's
/// decision as the objective owner (My Team spec §8.2). Reuses
/// ITaskCreationRequestRepository.GetPendingForOwnerEmployeeIdAsync verbatim - the exact method
/// GetMyTaskCreationRequestsQueryHandler already calls - so the count/top-N here can never drift
/// from that screen.
///
/// V1 single-legal-entity assumption (clarification 5, same stance as
/// WorkLeadershipService/GetLedWorkProgressQueryHandler): the caller's owned objectives are not
/// re-filtered by OwningLegalEntityId here because a tenant has exactly one legal entity in V1, so
/// every objective the caller owns already belongs to the active one. Revisit when multi-legal-
/// entity tenants ship.</summary>
public sealed class WorkTaskCreationTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    ITaskCreationRequestRepository requests)
    : ITeamActionSource
{
    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

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

        var pending = await requests.GetPendingForOwnerEmployeeIdAsync(currentUser.TenantId, callerEmployeeId.Value, ct);
        if (pending.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var names = await identity.ResolveDisplayNamesByEmployeeIdAsync(
            currentUser.TenantId, pending.Select(r => r.RequestedByEmployeeId).Distinct().ToList(), ct);

        var ordered = pending.OrderBy(r => r.CreatedAt).ToList();
        var oldest = ordered[0].CreatedAt;

        var topItems = ordered.Take(top).Select(r =>
        {
            var title = TryGetTitle(r.PayloadJson);
            var requesterName = names.GetValueOrDefault(r.RequestedByEmployeeId);
            return new ActionItem(
                Key,
                r.Id,
                title is null ? "New task request" : $"New task - {title}",
                r.RequestedByEmployeeId,
                requesterName,
                r.CreatedAt,
                null,
                new ActionItemLink(ActionItemLink.KindWorkRequest, new Dictionary<string, string>
                {
                    ["relatedEntityType"] = "task_creation_request",
                    ["relatedEntityId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }

    private static string? TryGetTitle(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<TaskCreationRequestPayload>(payloadJson)?.Title;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
