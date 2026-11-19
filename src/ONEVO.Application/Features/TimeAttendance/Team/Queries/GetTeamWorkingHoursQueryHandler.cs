using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>
/// Working / Break / Leave's week trend. Population and leave masking are exactly GetCoveredTeamTodayQuery's (CoveredTeam).
/// Worked and break minutes come from attendance_records; an open record counts up to now today, and up to that day's local
/// midnight on a past day (a forgotten clock-out never inflates a past day past 24 h).
/// </summary>
public sealed class GetTeamWorkingHoursQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    IEmployeeAuthorityResolver authority,
    IAttendanceReadRepository attendance,
    ILeaveRequestReadRepository leaveRequests,
    IPositionAssignmentRepository positionAssignments,
    ILeaveVisibilityScopeProvider leaveVisibilityScope,
    IDateTimeProvider clock)
    : IRequestHandler<GetTeamWorkingHoursQuery, Result<TeamWorkingHoursResponse>>
{
    public async Task<Result<TeamWorkingHoursResponse>> Handle(GetTeamWorkingHoursQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(CoveredTeam.AttendanceReadPermission))
            return Result<TeamWorkingHoursResponse>.Forbidden();

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<TeamWorkingHoursResponse>.NotFound("Current employee record was not found.");
        var legalEntity = await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, actor.LegalEntityId.Value, ct);
        if (legalEntity is null)
            return Result<TeamWorkingHoursResponse>.NotFound("Current employee record was not found.");

        var utcNow = clock.UtcNow;
        var schedule = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
        var today = schedule.WorkDate;
        var weekStart = query.WeekStart ?? today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(6);

        var covered = await CoveredTeam.CoveredEmployeeIdsAsync(
            authority, currentUser.UserId, actor.LegalEntityId.Value, actor.Id, ct);

        var records = covered.Count == 0
            ? []
            : (await attendance.ListRecordsAsync(currentUser.TenantId, covered, weekStart, weekEnd, 0, covered.Count * 7, ct)).Items;
        var leave = covered.Count == 0
            ? []
            : await leaveRequests.ListApprovedCoveringAsync(currentUser.TenantId, covered, weekStart, weekEnd, ct);
        var leaveAuthorized = leave.Count == 0
            ? []
            : await CoveredTeam.LeaveAuthorizedAsync(currentUser.TenantId, covered, employees, positionAssignments, leaveVisibilityScope, ct);

        var days = new List<TeamWorkingHoursDay>(7);
        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            if (date > today)
            {
                days.Add(new TeamWorkingHoursDay(date, 0, 0, 0, 0));
                continue;
            }

            var window = AttendanceTodayStateService.GetLocalDayWindow(date, schedule.TimeZone);
            var asOf = date == today ? utcNow : window.End;
            var dayRecords = records.Where(r => r.Date == date).ToList();
            var worked = dayRecords.Sum(r => PlannerActuals.AttendanceMinutes(r.ActualStart, r.ActualEnd, r.WorkedMinutes, r.BreakMinutes, asOf));
            var onLeave = leave
                .Where(l => leaveAuthorized.Contains(l.EmployeeId) && l.StartAt < window.End && l.EndAt > window.Start)
                .Select(l => l.EmployeeId)
                .Distinct()
                .Count();
            days.Add(new TeamWorkingHoursDay(
                date, worked, dayRecords.Sum(r => r.BreakMinutes), onLeave, dayRecords.Count(r => r.ActualStart is not null)));
        }

        return Result<TeamWorkingHoursResponse>.Success(new TeamWorkingHoursResponse(weekStart, weekEnd, days));
    }
}
