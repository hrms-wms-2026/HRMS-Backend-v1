using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: work-area change requests pending the caller's
/// approval (My Team spec §8.2). Reuses the exact gate+list pair
/// WorkAreaChangeRequestWorkflow.ListApprovalsAsync already calls -
/// IWorkAreaChangeRequestRepository.ListPendingEmployeeIdsAsync (candidates) then
/// IEmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync (narrows to the caller's exact
/// approver scope) then ListApprovalInboxAsync - so the count/top-N here can never drift from
/// that screen. The entity has no CreatedAt field, only RequestedAt; ordering and oldest-first
/// use that field.</summary>
public sealed class AttendanceWorkAreaChangeTeamActionSource(
    ICurrentUser currentUser,
    IEmployeeAuthorityResolver authority,
    IWorkAreaChangeRequestRepository requests,
    IAttendanceReadRepository attendance)
    : ITeamActionSource
{
    private const string Permission = "attendance:approve";

    public string Key => "attendance.work_area";
    public string Domain => ActionSourceSummary.DomainPeople;

    public Task<bool> IsGatedAsync(CancellationToken ct = default)
        => Task.FromResult(currentUser.HasPermission(Permission));

    public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
    {
        var candidateEmployeeIds = await requests.ListPendingEmployeeIdsAsync(
            currentUser.TenantId, legalEntityId, from: null, to: null, ct);
        var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(
                legalEntityId, Permission, EmployeeAuthorityPurpose.WorkAreaChangeApproval, candidateEmployeeIds), ct);

        var (rows, _) = await requests.ListApprovalInboxAsync(
            currentUser.TenantId, legalEntityId, eligibleEmployeeIds,
            from: null, to: null, skip: 0, take: int.MaxValue, ct);

        if (rows.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var identities = await attendance.ListEmployeeIdentitiesAsync(
            currentUser.TenantId, legalEntityId, rows.Select(r => r.EmployeeId).Distinct().ToArray(), ct);

        var ordered = rows.OrderBy(r => r.RequestedAt).ToList();
        var oldest = ordered[0].RequestedAt;

        var topItems = ordered.Take(top).Select(r =>
        {
            identities.TryGetValue(r.EmployeeId, out var identity);
            var name = identity?.DisplayName;
            return new ActionItem(
                Key,
                r.Id,
                $"Work-area change - {name ?? "Unknown"}",
                r.EmployeeId,
                name,
                r.RequestedAt,
                null,
                new ActionItemLink(ActionItemLink.KindAttendanceApproval, new Dictionary<string, string>
                {
                    ["type"] = "work-area",
                    ["requestId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }
}
