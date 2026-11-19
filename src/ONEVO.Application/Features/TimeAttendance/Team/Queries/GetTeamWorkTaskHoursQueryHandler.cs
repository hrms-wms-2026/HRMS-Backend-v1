using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>
/// Work vs task hours. Population is Team Status's (CoveredTeam: attendance:read TimeTrackingRead coverage, the actor
/// stripped). Working time comes from attendance_records exactly as GetTeamWorkingHoursQuery reads it (an open record
/// counts up to now today, up to that day's local midnight on a past day). Task time is the estimate of each top-level
/// task the employee completed, dated by its CompletedAt in the legal entity's zone (ITeamCompletedTaskReader).
/// </summary>
public sealed class GetTeamWorkTaskHoursQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    IEmployeeAuthorityResolver authority,
    IAttendanceReadRepository attendance,
    ITeamCompletedTaskReader completedTasks,
    IDateTimeProvider clock)
    : IRequestHandler<GetTeamWorkTaskHoursQuery, Result<TeamWorkTaskHoursResponse>>
{
    private const string NotFoundMessage = "Current employee record was not found.";
    /// <summary>The oldest anchor accepted: two years before today.</summary>
    public const int MaxYearsBack = 2;

    public async Task<Result<TeamWorkTaskHoursResponse>> Handle(GetTeamWorkTaskHoursQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(CoveredTeam.AttendanceReadPermission))
            return Result<TeamWorkTaskHoursResponse>.Forbidden();

        if (query.Period is not (TeamWorkTaskMath.Day or TeamWorkTaskMath.Week or TeamWorkTaskMath.Month))
            return Result<TeamWorkTaskHoursResponse>.UnprocessableEntity("period must be day, week or month.");

        var tenantId = currentUser.TenantId;
        var actor = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is not Guid legalEntityId)
            return Result<TeamWorkTaskHoursResponse>.NotFound(NotFoundMessage);
        var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
        if (legalEntity is null)
            return Result<TeamWorkTaskHoursResponse>.NotFound(NotFoundMessage);

        var utcNow = clock.UtcNow;
        var schedule = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
        var zone = schedule.TimeZone;
        var today = schedule.WorkDate;

        var anchor = query.Anchor is { } a && a < today ? a : today;
        if (anchor < today.AddYears(-MaxYearsBack))
            return Result<TeamWorkTaskHoursResponse>.UnprocessableEntity(
                $"anchor must be on or after {today.AddYears(-MaxYearsBack):yyyy-MM-dd}.");

        var period = query.Period;
        var (from, to) = TeamWorkTaskMath.RangeOf(period, anchor);
        var (prevFrom, prevTo) = TeamWorkTaskMath.PreviousOf(period, from);
        var (trendFrom, trendTo) = TeamWorkTaskMath.TrendOf(period, from, to);
        var loadFrom = prevFrom < trendFrom ? prevFrom : trendFrom;
        var loadTo = to > trendTo ? to : trendTo;
        if (loadTo > today) loadTo = today;

        var covered = await CoveredTeam.CoveredEmployeeIdsAsync(authority, currentUser.UserId, legalEntityId, actor.Id, ct);

        // Per employee per day: worked, break, task minutes and completed / unestimated task counts.
        var cells = new Dictionary<(Guid EmployeeId, DateOnly Date), Cell>();
        Cell CellOf(Guid employeeId, DateOnly date)
        {
            if (!cells.TryGetValue((employeeId, date), out var cell))
                cells[(employeeId, date)] = cell = new Cell();
            return cell;
        }

        IReadOnlyDictionary<Guid, AttendanceHistoryEmployee> identities = covered.Count == 0
            ? new Dictionary<Guid, AttendanceHistoryEmployee>()
            : await attendance.ListEmployeeIdentitiesAsync(tenantId, legalEntityId, covered, ct);

        if (covered.Count > 0 && loadFrom <= loadTo)
        {
            var days = loadTo.DayNumber - loadFrom.DayNumber + 1;
            var records = (await attendance.ListRecordsAsync(
                tenantId, covered, loadFrom, loadTo, 0, Math.Max(1, covered.Count * days * 2), ct)).Items;
            foreach (var r in records)
            {
                var window = AttendanceTodayStateService.GetLocalDayWindow(r.Date, zone);
                var asOf = r.Date == today ? utcNow : window.End;
                var cell = CellOf(r.EmployeeId, r.Date);
                cell.Worked += PlannerActuals.AttendanceMinutes(r.ActualStart, r.ActualEnd, r.WorkedMinutes, r.BreakMinutes, asOf);
                cell.Break += r.BreakMinutes;
            }

            var fromUtc = AttendanceTodayStateService.GetLocalDayWindow(loadFrom, zone).Start;
            var toUtc = AttendanceTodayStateService.GetLocalDayWindow(loadTo, zone).End;
            var tasks = await completedTasks.ListCompletedAsync(tenantId, covered, fromUtc, toUtc, ct);
            foreach (var t in tasks)
            {
                var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.CompletedAt, zone).DateTime);
                var cell = CellOf(t.EmployeeId, localDate);
                cell.Task += TeamWorkTaskMath.EstimateMinutes(t.EstimatedHours);
                cell.Completed++;
                if (t.EstimatedHours is null or <= 0) cell.Unestimated++;
            }
        }

        Cell Sum(IEnumerable<Guid> people, DateOnly start, DateOnly end)
        {
            var total = new Cell();
            foreach (var employeeId in people)
            {
                for (var d = start; d <= end; d = d.AddDays(1))
                {
                    if (cells.TryGetValue((employeeId, d), out var c)) total.Add(c);
                }
            }
            return total;
        }

        var trend = new List<TeamWorkTaskTrendPoint>();
        for (var d = trendFrom; d <= trendTo; d = d.AddDays(1))
        {
            var c = Sum(covered, d, d);
            trend.Add(new TeamWorkTaskTrendPoint(d, c.Worked, c.Break, c.Task, c.Completed,
                TeamWorkTaskMath.Utilization(c.Task, c.Worked)));
        }

        var perEmployee = covered
            .Select(id => (Id: id, Cell: Sum([id], from, to)))
            .ToList();
        var rates = perEmployee
            .Select(x => TeamWorkTaskMath.Utilization(x.Cell.Task, x.Cell.Worked))
            .OfType<decimal>()
            .ToList();
        var rows = perEmployee
            .Select(x =>
            {
                identities.TryGetValue(x.Id, out var identity);
                var utilization = TeamWorkTaskMath.Utilization(x.Cell.Task, x.Cell.Worked);
                return new TeamWorkTaskEmployeeRow(
                    x.Id,
                    identity?.DisplayName ?? "Unknown",
                    identity?.AvatarFileId,
                    identity?.Department,
                    x.Cell.Worked, x.Cell.Break, x.Cell.Task, x.Cell.Completed, x.Cell.Unestimated,
                    utilization,
                    TeamWorkTaskMath.RankOf(utilization, rates));
            })
            .OrderBy(r => r.Rank ?? int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<TeamWorkTaskHoursResponse>.Success(new TeamWorkTaskHoursResponse(
            period, from, to, today, from <= today && today <= to, covered.Count,
            Sum(covered, from, to).ToTotals(), Sum(covered, prevFrom, prevTo).ToTotals(),
            trendFrom, trendTo, trend, rows, utcNow));
    }

    private sealed class Cell
    {
        public int Worked;
        public int Break;
        public int Task;
        public int Completed;
        public int Unestimated;

        public void Add(Cell other)
        {
            Worked += other.Worked;
            Break += other.Break;
            Task += other.Task;
            Completed += other.Completed;
            Unestimated += other.Unestimated;
        }

        public TeamWorkTaskTotals ToTotals() =>
            new(Worked, Break, Task, Completed, Unestimated, TeamWorkTaskMath.Utilization(Task, Worked));
    }
}
