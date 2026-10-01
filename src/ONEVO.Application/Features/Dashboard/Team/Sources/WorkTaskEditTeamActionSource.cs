using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: task-edit requests pending the caller's decision
/// as the objective owner (My Team spec §8.2). Reuses
/// ITaskEditRequestRepository.GetPendingForOwnerEmployeeIdAsync verbatim - the exact method
/// GetMyTaskEditRequestsQueryHandler already calls.
///
/// V1 single-legal-entity assumption (clarification 5, same stance as WorkTaskCreationTeamActionSource).</summary>
public sealed class WorkTaskEditTeamActionSource(
    ICurrentUser currentUser,
    IModuleEntitlementService modules,
    ICallerIdentityResolver identity,
    ITaskEditRequestRepository requests)
    : ITeamActionSource
{
    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public string Key => "work.task_edit";
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
                title is null ? "Task edit request" : $"Task edit - {title}",
                r.RequestedByEmployeeId,
                requesterName,
                r.CreatedAt,
                null,
                new ActionItemLink(ActionItemLink.KindWorkRequest, new Dictionary<string, string>
                {
                    ["relatedEntityType"] = "task_edit_request",
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
            return JsonSerializer.Deserialize<TaskEditRequestPayload>(payloadJson)?.Title;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
