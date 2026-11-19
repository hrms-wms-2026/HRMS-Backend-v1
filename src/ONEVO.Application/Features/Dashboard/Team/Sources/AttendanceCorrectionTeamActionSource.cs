using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: attendance corrections pending the caller's
/// approval (My Team spec §8.2). Reuses IEmployeeAuthorityResolver.ResolveVisibilityAsync +
/// IAttendanceCorrectionRepository.ListApprovalInboxAsync - the exact pair
/// AttendanceCorrectionWorkflow.ListApprovalsAsync already calls for the approvals-inbox screen -
/// so the count/top-N here can never drift from what that screen shows.</summary>
public sealed class AttendanceCorrectionTeamActionSource(
    ICurrentUser currentUser,
    IEmployeeAuthorityResolver authority,
    IAttendanceCorrectionRepository corrections,
    IAttendanceReadRepository attendance)
    : ITeamActionSource
{
    private const string Permission = "attendance:approve";

    public string Key => "attendance.correction";
    public string Domain => ActionSourceSummary.DomainPeople;

    public Task<bool> IsGatedAsync(CancellationToken ct = default)
        => Task.FromResult(currentUser.HasPermission(Permission));

    public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
    {
        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId, legalEntityId, Permission,
                IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval), ct);

        var rows = await corrections.ListApprovalInboxAsync(
            currentUser.TenantId, legalEntityId, visibility.EmployeeIds,
            from: null, to: null, status: AttendanceCorrection.StatusPending, ct);

        if (rows.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var identities = await attendance.ListEmployeeIdentitiesAsync(
            currentUser.TenantId, legalEntityId, rows.Select(r => r.EmployeeId).Distinct().ToArray(), ct);

        var ordered = rows.OrderBy(r => r.CreatedAt).ToList();
        var oldest = ordered[0].CreatedAt;

        var topItems = ordered.Take(top).Select(r =>
        {
            identities.TryGetValue(r.EmployeeId, out var identity);
            var name = identity?.DisplayName;
            return new ActionItem(
                Key,
                r.Id,
                $"Attendance correction - {name ?? "Unknown"}",
                r.EmployeeId,
                name,
                r.CreatedAt,
                null,
                new ActionItemLink(ActionItemLink.KindAttendanceApproval, new Dictionary<string, string>
                {
                    ["type"] = "corrections",
                    ["requestId"] = r.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }
}
