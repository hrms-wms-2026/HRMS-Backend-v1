using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed record LeaveTeamAbsenceWarning(decimal TeamAbsencePercent, int AbsentCount, string Message);

public interface ILeaveTeamAbsenceWarningService
{
    Task<LeaveTeamAbsenceWarning?> BuildWarningAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        decimal? maxTeamAbsencePercent,
        CancellationToken ct = default);
}

public sealed class LeaveTeamAbsenceWarningService : ILeaveTeamAbsenceWarningService
{
    private readonly IEmployeeHierarchyClosureRepository _hierarchy;
    private readonly ILeaveRequestRepository _requests;

    public LeaveTeamAbsenceWarningService(
        IEmployeeHierarchyClosureRepository hierarchy,
        ILeaveRequestRepository requests)
    {
        _hierarchy = hierarchy;
        _requests = requests;
    }

    public async Task<LeaveTeamAbsenceWarning?> BuildWarningAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        decimal? maxTeamAbsencePercent,
        CancellationToken ct = default)
    {
        var managerId = await _hierarchy.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, ct);
        if (managerId is null)
            return null;

        var teamMemberIds = (await _hierarchy.GetDescendantEmployeeIdsAsync(tenantId, managerId.Value, ct))
            .Where(id => id != employeeId)
            .Distinct()
            .ToArray();
        if (teamMemberIds.Length == 0)
            return null;

        var absentCount = await _requests.CountDistinctEmployeesPendingOrApprovedInRangeAsync(
            tenantId, teamMemberIds, startDate, endDate, ct);
        if (absentCount == 0)
            return null;

        var percent = Math.Round(absentCount * 100m / teamMemberIds.Length, 2);
        _ = maxTeamAbsencePercent;

        return new LeaveTeamAbsenceWarning(
            percent,
            absentCount,
            LeaveRequestMessages.TeamAbsence(absentCount));
    }
}
