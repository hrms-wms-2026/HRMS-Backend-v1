using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedTimeProgress;

/// <summary>Same gate and scope as GetLedPeriodProgressQueryHandler: the window comes from today's UTC date, the filters
/// only narrow the led scope, and a caller who leads nothing (or a filter outside the scope) gets an empty response with
/// From/To filled in. Time figures cover the blocks and sessions on the scope's tasks (any depth, meetings excluded); people
/// = assignees of those tasks plus anyone with blocks or sessions on them. Every source is read once for the whole set.</summary>
public sealed class GetLedTimeProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    ITeamTimeReadRepository teamTime,
    ILegalEntityRepository legalEntities,
    ICalendarEventRepository calendarEvents,
    ILeaveRequestReadRepository leaveRequests,
    IOptions<PlannerOptions> plannerOptions)
    : IRequestHandler<GetLedTimeProgressQuery, Result<LedTimeProgressResponse>>
{
    public async Task<Result<LedTimeProgressResponse>> Handle(GetLedTimeProgressQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedTimeProgressResponse>.Forbidden("Authentication required.");

        var period = LedProgressQueryOptions.Parse(request.Period, LedProgressPeriod.WindowedPeriods, LedPeriod.Week);
        var now = clock.UtcNow;
        var today = LedProgressPeriod.UtcToday(now);
        var window = LedProgressPeriod.Window(period, today)!;
        var plannerEnabled = plannerOptions.Value.Enabled;
        LedTimeProgressResponse Empty() =>
            LedTimeProgressCalculator.Build(period, window.From, window.To, today, now, plannerEnabled, [], new Dictionary<Guid, TeamTaskRow>());

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<LedTimeProgressResponse>.Success(Empty());

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        var objectiveIds = LedScopeFilter.Resolve(scope, request.ProjectId, request.ObjectiveId);
        if (objectiveIds.Count == 0)
            return Result<LedTimeProgressResponse>.Success(Empty());

        // Every person's local days of the previous and current windows, whatever their zone (UTC-12 .. UTC+14).
        var (prevFrom, _) = LedTimeProgressCalculator.PreviousSameElapsed(period, window.From, today);
        var rangeStart = new DateTimeOffset(prevFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-14);
        var rangeEnd = new DateTimeOffset(window.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(12);

        var blocks = plannerEnabled ? await teamTime.ListBlocksForTasksAsync(tenantId, objectiveIds, rangeStart, rangeEnd, ct) : [];
        var sessions = await teamTime.ListSessionsForTasksAsync(tenantId, objectiveIds, rangeStart, rangeEnd, ct);
        var assignees = await teamTime.ListAssigneesForTasksAsync(tenantId, objectiveIds, ct);
        var personIds = assignees.Concat(blocks.Select(b => b.EmployeeId)).Concat(sessions.Select(s => s.EmployeeId)).Distinct().ToList();
        if (personIds.Count == 0)
            return Result<LedTimeProgressResponse>.Success(Empty());

        var people = await teamTime.GetPeopleAsync(tenantId, personIds, ct);
        var zones = await teamTime.GetTimezonesAsync(tenantId, personIds, ct);
        var tasks = await teamTime.GetTasksAsync(tenantId, sessions.Select(s => s.TaskId).Distinct().ToList(), ct);
        var expected = await ExpectedMinutesAsync(tenantId, personIds, window, ct);

        var blocksBy = blocks.ToLookup(b => b.EmployeeId);
        var sessionsBy = sessions.ToLookup(s => s.EmployeeId);
        var inputs = personIds.Select(id => new LedTimePerson(
            id,
            people.GetValueOrDefault(id)?.DisplayName ?? "Unknown",
            TimeZoneInfo.TryFindSystemTimeZoneById(zones.GetValueOrDefault(id) ?? "UTC", out var zone) ? zone : TimeZoneInfo.Utc,
            expected.GetValueOrDefault(id),
            blocksBy[id].Select(b => new AttributionBlock(b.TaskId, b.Start, b.End)).ToList(),
            sessionsBy[id].Select(s => new AttributionSession(s.TaskId, s.ClockIn, s.ClockOut)).ToList())).ToList();

        return Result<LedTimeProgressResponse>.Success(
            LedTimeProgressCalculator.Build(period, window.From, window.To, today, now, plannerEnabled, inputs, tasks));
    }

    /// <summary>Capacity per person for the whole window: their legal entity's working weekdays and hours, holidays, hire
    /// and termination, less approved leave (the Weekly Work Progress sources). Missing = no working hours.</summary>
    private async Task<Dictionary<Guid, int?>> ExpectedMinutesAsync(Guid tenantId, IReadOnlyCollection<Guid> personIds,
        LedDueWindow window, CancellationToken ct)
    {
        var rows = await employees.ListByIdsAsync(tenantId, personIds, ct);
        var entities = new Dictionary<Guid, LegalEntity?>();
        foreach (var entityId in rows.Values.Select(e => e.LegalEntityId).OfType<Guid>().Distinct())
            entities[entityId] = await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct);
        var holidays = (await calendarEvents.ListHolidayDatesAsync(tenantId, window.From, window.To, ct)).ToHashSet();
        var leaves = (await leaveRequests.ListApprovedCoveringAsync(tenantId, personIds, window.From, window.To, ct))
            .ToLookup(l => l.EmployeeId);

        var result = new Dictionary<Guid, int?>();
        foreach (var (id, employee) in rows)
        {
            var entity = employee.LegalEntityId is Guid entityId ? entities.GetValueOrDefault(entityId) : null;
            if (entity is null || !AttendanceScheduleResolver.IsScheduleConfigured(entity)
                || !TimeZoneInfo.TryFindSystemTimeZoneById(entity.Timezone!, out var zone))
            {
                result[id] = null;
                continue;
            }
            var leaveHours = LeaveWorkWindow.HoursByDate(leaves[id], window.From, window.To, zone, entity.WorkStartTime, entity.WorkEndTime);
            result[id] = LedTimeProgressCalculator.ExpectedMinutes(
                AttendanceScheduleResolver.ParseWorkingDays(entity.StandardWorkingDays), entity.WorkStartTime, entity.WorkEndTime,
                holidays, window.From, window.To, employee.HireDate, employee.TerminationDate, leaveHours);
        }
        return result;
    }
}
