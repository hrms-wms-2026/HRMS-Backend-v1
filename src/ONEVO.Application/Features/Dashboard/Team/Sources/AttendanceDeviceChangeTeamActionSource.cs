using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: device change requests pending the caller's
/// approval (My Team spec §8.2). Reuses the exact gate+list pair
/// DeviceChangeRequestWorkflow.ListApprovalsAsync already calls -
/// IDeviceChangeRequestRepository.ListPendingEmployeeIdsAsync (candidates) then
/// IEmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync then ListApprovalInboxAsync. The
/// entity has no CreatedAt field, only RequestedAt.</summary>
public sealed class AttendanceDeviceChangeTeamActionSource(
    ICurrentUser currentUser,
    IEmployeeAuthorityResolver authority,
    IDeviceChangeRequestRepository requests,
    IAttendanceReadRepository attendance)
    : ITeamActionSource
{
    private const string Permission = "attendance:approve";

    public string Key => "attendance.device_change";
    public string Domain => ActionSourceSummary.DomainPeople;

    public Task<bool> IsGatedAsync(CancellationToken ct = default)
        => Task.FromResult(currentUser.HasPermission(Permission));

    public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
    {
        var candidateEmployeeIds = await requests.ListPendingEmployeeIdsAsync(currentUser.TenantId, legalEntityId, ct);
        var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(
                legalEntityId, Permission, EmployeeAuthorityPurpose.DeviceChangeApproval, candidateEmployeeIds), ct);

        var (rows, _) = await requests.ListApprovalInboxAsync(
            currentUser.TenantId, legalEntityId, eligibleEmployeeIds, skip: 0, take: int.MaxValue, ct);

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
                $"Device change - {name ?? "Unknown"}",
                r.EmployeeId,
                name,
                r.RequestedAt,
                null,
                new ActionItemLink(ActionItemLink.KindAttendanceApproval, new Dictionary<string, string>
                {
                    ["type"] = "device-change",
                    ["requestId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }
}
