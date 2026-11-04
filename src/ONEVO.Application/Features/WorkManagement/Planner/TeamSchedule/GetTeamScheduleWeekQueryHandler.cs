using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>
/// Team week planned vs actual (2026-11-02 design match, section 7.4). Same population and gate as Today's Team Schedule.
/// Each person's week is their own seven local days, composed with TeamScheduleComposer; blocks, sessions and meetings are read
/// once over the union of every person's week and the week before. Coverage uses the planned time elapsed so far, and the
/// previous figures cover the same elapsed span one week earlier. No presence: the week view shows no live status.
/// </summary>
public sealed class GetTeamScheduleWeekQueryHandler(
    ICurrentUser currentUser,
    ICallerIdentityResolver identity,
    IEmployeeRepository employees,
    ITeamTimeReadRepository teamTime,
    IMeetingRepository meetings,
    IDateTimeProvider clock,
    IOptions<PlannerOptions> options)
    : IRequestHandler<GetTeamScheduleWeekQuery, Result<TeamScheduleWeekResponse>>
{
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);
    private static readonly IReadOnlyDictionary<Guid, TeamTaskRow> NoTasks = new Dictionary<Guid, TeamTaskRow>();

    public async Task<Result<TeamScheduleWeekResponse>> Handle(GetTeamScheduleWeekQuery query, CancellationToken ct)
    {
        var caller = currentUser.IsAuthenticated
            ? await identity.ResolveCallerEmployeeIdAsync(currentUser.TenantId, currentUser.UserId, ct)
            : null;
        if (caller is not Guid callerId)
            return Result<TeamScheduleWeekResponse>.Failure("Team schedule not found.", 404, PlannerErrorCodes.NotFound);
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;
        var plannerEnabled = options.Value.Enabled;

        var callerZone = TeamSchedulePopulation.ZoneOf((await teamTime.GetTimezonesAsync(tenantId, [callerId], ct)).GetValueOrDefault(callerId));
        var callerToday = TeamSchedulePopulation.TodayIn(callerZone.Info, now);
        var weekStart = query.WeekStart ?? callerToday.AddDays(-(((int)callerToday.DayOfWeek + 6) % 7));
        if (weekStart > callerToday)
            return Result<TeamScheduleWeekResponse>.Failure("Only this week and earlier weeks can be shown.", 400, PlannerErrorCodes.NotToday);

        var descendants = await TeamSchedulePopulation.TransitiveReportsAsync(employees, tenantId, callerId, ct);
        var people = await teamTime.GetPeopleAsync(tenantId, descendants, ct);
        var reports = descendants.Where(id => people.GetValueOrDefault(id)?.IsActive == true).ToList();
        if (reports.Count == 0)
            return Result<TeamScheduleWeekResponse>.Success(TeamWeekComposer.Compose(weekStart, plannerEnabled, now, []));

        var zoneIds = await teamTime.GetTimezonesAsync(tenantId, reports, ct);
        var zones = reports.ToDictionary(id => id, id => TeamSchedulePopulation.ZoneOf(zoneIds.GetValueOrDefault(id)));
        var weeks = zones.ToDictionary(z => z.Key, z => (
            From: PlannerActuals.DayBounds(weekStart, z.Value.Info).Start,
            To: PlannerActuals.DayBounds(weekStart.AddDays(6), z.Value.Info).End));
        var from = weeks.Values.Min(w => w.From) - OneWeek;
        var to = weeks.Values.Max(w => w.To);

        var blocks = plannerEnabled ? await teamTime.ListBlocksForEmployeesAsync(tenantId, reports, from, to, ct) : [];
        var sessions = await teamTime.ListSessionsForEmployeesAsync(tenantId, reports, from, to, ct);
        var weekMeetings = await meetings.ListEmployeesDayMeetingsAsync(tenantId, reports, from, to, ct);

        var blocksBy = blocks.ToLookup(b => b.EmployeeId);
        var sessionsBy = sessions.ToLookup(s => s.EmployeeId);
        var inputs = reports.Select(id =>
        {
            var person = people[id];
            var zone = zones[id];
            var (weekFrom, weekTo) = weeks[id];
            var personBlocks = blocksBy[id].ToList();
            var personSessions = sessionsBy[id].ToList();
            var personMeetings = weekMeetings.GetValueOrDefault(id) ?? [];

            var days = Enumerable.Range(0, 7).Select(i =>
            {
                var date = weekStart.AddDays(i);
                var (dayStart, dayEnd) = PlannerActuals.DayBounds(date, zone.Info);
                var day = TeamScheduleComposer.Compose(new TeamScheduleInput(
                    id, person.DisplayName, person.AvatarFileId, zone.Id, dayStart, dayEnd, null,
                    personBlocks.Where(b => b.Start < dayEnd && b.End > dayStart).ToList(),
                    personSessions.Where(s => s.ClockIn < dayEnd && (s.ClockOut ?? now) > dayStart).ToList(),
                    personMeetings, NoTasks), now, plannerEnabled);
                return (date, day);
            }).ToList();

            var elapsed = Attribute(personBlocks, personSessions, weekFrom, weekTo, now);
            var previous = Attribute(personBlocks, personSessions, weekFrom - OneWeek, weekTo - OneWeek, now - OneWeek, now);
            return new TeamWeekPersonInput(id, person.DisplayName, person.AvatarFileId, days, elapsed.PlannedMinutes,
                previous.PlannedMinutes, previous.ScheduledMinutes, previous.ClockedMinutes,
                previous.OutsideSlotMinutes + previous.NoPlanMinutes);
        }).ToList();

        return Result<TeamScheduleWeekResponse>.Success(TeamWeekComposer.Compose(weekStart, plannerEnabled, now, inputs));
    }

    /// <summary>Attribution over [from, min(cutoff, to)); an empty span when the cutoff is before the window.</summary>
    private static AttributionResult Attribute(IReadOnlyList<TeamBlockRow> blocks, IReadOnlyList<TeamSessionRow> sessions,
        DateTimeOffset from, DateTimeOffset to, DateTimeOffset cutoff, DateTimeOffset? now = null)
    {
        var end = cutoff < to ? cutoff : to;
        if (end <= from)
            end = from;
        return TaskTimeAttribution.Attribute(
            blocks.Select(b => new AttributionBlock(b.TaskId, b.Start, b.End)),
            sessions.Select(s => new AttributionSession(s.TaskId, s.ClockIn, s.ClockOut)),
            from, end, now ?? cutoff);
    }
}
