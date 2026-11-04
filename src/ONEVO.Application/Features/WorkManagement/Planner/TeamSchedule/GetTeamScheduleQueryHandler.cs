using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>
/// Today's Team Schedule. Population = the caller's active transitive reports (the Planner's manager rule, read level by level
/// from the reporting line), each on their own
/// local day; every read is batched over the union of those days. Presence comes from GetCoveredTeamTodayQuery (Team Status,
/// with its coverage and leave masking) and only for today; a person the caller does not cover gets none. With the Planner
/// off the response still carries meetings and clocked minutes, flagged PlannerEnabled = false.
/// </summary>
public sealed class GetTeamScheduleQueryHandler(
    ICurrentUser currentUser,
    ICallerIdentityResolver identity,
    IEmployeeRepository employees,
    ITeamTimeReadRepository teamTime,
    IMeetingRepository meetings,
    IMediator mediator,
    IDateTimeProvider clock,
    IOptions<PlannerOptions> options)
    : IRequestHandler<GetTeamScheduleQuery, Result<TeamScheduleResponse>>
{
    /// <summary>GetCoveredTeamTodayQuery's own maximum.</summary>
    private const int PresenceLimit = 200;

    public async Task<Result<TeamScheduleResponse>> Handle(GetTeamScheduleQuery query, CancellationToken ct)
    {
        var caller = currentUser.IsAuthenticated
            ? await identity.ResolveCallerEmployeeIdAsync(currentUser.TenantId, currentUser.UserId, ct)
            : null;
        if (caller is not Guid callerId)
            return Result<TeamScheduleResponse>.Failure("Team schedule not found.", 404, PlannerErrorCodes.NotFound);
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;
        var plannerEnabled = options.Value.Enabled;

        var callerZone = TeamSchedulePopulation.ZoneOf((await teamTime.GetTimezonesAsync(tenantId, [callerId], ct)).GetValueOrDefault(callerId));
        var callerToday = TeamSchedulePopulation.TodayIn(callerZone.Info, now);
        if (query.Date is { } requested && requested > callerToday)
            return Result<TeamScheduleResponse>.Failure("Only today and earlier days can be shown.", 400, PlannerErrorCodes.NotToday);

        var descendants = await TeamSchedulePopulation.TransitiveReportsAsync(employees, tenantId, callerId, ct);
        var people = await teamTime.GetPeopleAsync(tenantId, descendants, ct);
        var reports = descendants.Where(id => people.GetValueOrDefault(id)?.IsActive == true).ToList();
        if (reports.Count == 0)
        {
            return Result<TeamScheduleResponse>.Success(new TeamScheduleResponse(query.Date ?? callerToday, plannerEnabled, now,
                TeamScheduleComposer.Summarize([]), []));
        }

        var zoneIds = await teamTime.GetTimezonesAsync(tenantId, reports, ct);
        var days = reports.ToDictionary(id => id, id =>
        {
            var zone = TeamSchedulePopulation.ZoneOf(zoneIds.GetValueOrDefault(id));
            var bounds = PlannerActuals.DayBounds(query.Date ?? TeamSchedulePopulation.TodayIn(zone.Info, now), zone.Info);
            return (Zone: zone, bounds.Start, bounds.End);
        });
        var from = days.Values.Min(d => d.Start);
        var to = days.Values.Max(d => d.End);

        var blocks = plannerEnabled ? await teamTime.ListBlocksForEmployeesAsync(tenantId, reports, from, to, ct) : [];
        var sessions = await teamTime.ListSessionsForEmployeesAsync(tenantId, reports, from, to, ct);
        var dayMeetings = await meetings.ListEmployeesDayMeetingsAsync(tenantId, reports, from, to, ct);
        var tasks = await teamTime.GetTasksAsync(tenantId,
            blocks.Select(b => b.TaskId).Concat(sessions.Select(s => s.TaskId)).Distinct().ToList(), ct);
        var presence = await PresenceAsync(query.Date is null || query.Date == callerToday, ct);

        var blocksBy = blocks.ToLookup(b => b.EmployeeId);
        var sessionsBy = sessions.ToLookup(s => s.EmployeeId);
        var members = reports.Select(id =>
        {
            var day = days[id];
            var person = people[id];
            return TeamScheduleComposer.Compose(new TeamScheduleInput(
                id, person.DisplayName, person.AvatarFileId, day.Zone.Id, day.Start, day.End, null,
                blocksBy[id].ToList(), sessionsBy[id].ToList(), dayMeetings.GetValueOrDefault(id) ?? [], tasks), now, plannerEnabled,
                presence.GetValueOrDefault(id));
        }).ToList();

        var ranked = TeamScheduleComposer.Rank(members);
        return Result<TeamScheduleResponse>.Success(new TeamScheduleResponse(query.Date ?? callerToday, plannerEnabled, now,
            TeamScheduleComposer.Summarize(ranked), ranked));
    }

    /// <summary>Team Status per covered person (status, work window, breaks), today only; nothing without attendance:read.</summary>
    private async Task<IReadOnlyDictionary<Guid, TeamPresence>> PresenceAsync(bool today, CancellationToken ct)
    {
        if (!today || !currentUser.HasPermission(CoveredTeam.AttendanceReadPermission))
            return new Dictionary<Guid, TeamPresence>();
        var result = await mediator.Send(new GetCoveredTeamTodayQuery(PresenceLimit), ct);
        return result.IsSuccess
            ? result.Value!.Members.ToDictionary(m => m.EmployeeId, m => new TeamPresence(m.Status, m.ExpectedMinutes,
                (m.Breaks ?? []).Select(b => new TeamScheduleBreak(b.Start, b.End)).ToList()))
            : new Dictionary<Guid, TeamPresence>();
    }
}
