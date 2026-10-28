using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;

namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>The Team Status population and its leave masking (My Team spec §8.1.2 / D6), shared by GetCoveredTeamTodayQuery
/// and GetTeamWorkingHoursQuery so the two can never disagree on who is in the team or whose leave the caller may see.</summary>
public static class CoveredTeam
{
    public const string AttendanceReadPermission = "attendance:read";

    /// <summary>attendance:read TimeTrackingRead coverage, the caller stripped: IncludeSelf:false stops the self channel, but
    /// company-wide or department coverage can still re-introduce the caller.</summary>
    public static async Task<List<Guid>> CoveredEmployeeIdsAsync(IEmployeeAuthorityResolver authority, Guid userId, Guid legalEntityId,
        Guid actorEmployeeId, CancellationToken ct)
    {
        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                userId,
                legalEntityId,
                AttendanceReadPermission,
                IncludeSelf: false,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);
        return visibility.EmployeeIds.Where(id => id != actorEmployeeId).Distinct().ToList();
    }

    /// <summary>The subjects whose leave the caller may see. Without a leave visibility scope nobody is leave-authorized,
    /// and their leave reads as absent.</summary>
    public static async Task<HashSet<Guid>> LeaveAuthorizedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        IEmployeeRepository employees, IPositionAssignmentRepository? positionAssignments,
        ILeaveVisibilityScopeProvider? leaveVisibilityScope, CancellationToken ct)
    {
        var authorized = new HashSet<Guid>();
        if (leaveVisibilityScope is null || employeeIds.Count == 0)
            return authorized;
        var leaveScope = (await leaveVisibilityScope.ResolveForCurrentUserAsync(ct)).Scope;
        if (leaveScope is null)
            return authorized;

        var employeesById = await employees.ListByIdsAsync(tenantId, employeeIds, ct);
        var primaryPositionByEmployee = positionAssignments is null
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>()
            : await positionAssignments.GetActivePrimaryByEmployeeIdsAsync(tenantId, employeeIds, ct);
        foreach (var employeeId in employeeIds)
        {
            employeesById.TryGetValue(employeeId, out var employeeRow);
            primaryPositionByEmployee.TryGetValue(employeeId, out var primaryPosition);
            if (EmployeeVisibilityScopeMatcher.Includes(
                    leaveScope, employeeId, primaryPosition?.PositionId, employeeRow?.DepartmentId, employeeRow?.LegalEntityId))
                authorized.Add(employeeId);
        }
        return authorized;
    }
}
