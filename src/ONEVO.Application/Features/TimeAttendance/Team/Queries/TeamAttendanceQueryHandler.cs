using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;
using ONEVO.Application.Features.TimeAttendance.Team.Services;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>
/// The My Team "Team attendance" widget. Scope is exactly GetCoveredTeamTodayQuery's (attendance:read, TimeTrackingRead
/// coverage, the actor stripped); a department filter only narrows it. Past days come from ITeamAttendancePeriodReader
/// (Classify); today comes from GetCoveredTeamTodayQuery itself, so this card never disagrees with Team Status.
/// </summary>
public sealed class TeamAttendanceQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IEmployeeAuthorityResolver authority,
    IMediator mediator,
    ITeamAttendancePeriodReader reader,
    IAttendanceReadRepository attendance,
    IDateTimeProvider clock)
    : IRequestHandler<GetTeamAttendanceSummaryQuery, Result<TeamAttendanceSummaryResponse>>,
      IRequestHandler<GetTeamAttendanceEmployeesQuery, Result<PagedResult<TeamAttendanceEmployeeRow>>>
{
    private const string AttendanceReadPermission = "attendance:read";
    /// <summary>GetCoveredTeamTodayQuery's own maximum; employees past it keep the reader's bucket for today.</summary>
    private const int TodayLimit = 200;
    private const int PreviewLimit = 6;
    private const int MaxPageSize = 50;

    public async Task<Result<TeamAttendanceSummaryResponse>> Handle(GetTeamAttendanceSummaryQuery query, CancellationToken ct)
    {
        var loaded = await LoadAsync(query.Mode, query.Anchor, query.DepartmentId, ct);
        if (loaded.Error is { } error)
            return Result<TeamAttendanceSummaryResponse>.Failure(error.Message, error.StatusCode);
        var ctx = loaded.Context!;
        var range = ctx.Range;

        var trend = new List<TeamAttendanceTrendPoint>();
        for (var d = range.From; d <= range.To; d = d.AddDays(1))
        {
            var c = Count(ctx.Rows, d, d);
            trend.Add(new TeamAttendanceTrendPoint(d, c.Present, c.Late, c.Absent, c.OnLeave, c.Pending, RateOf(c)));
        }

        var period = Count(ctx.Rows, range.From, range.To);
        var previous = Count(ctx.Rows, range.PrevFrom, range.PrevTo);
        var periodRate = RateOf(period);
        var previousRate = RateOf(previous);

        // Unfiltered, today's counts are Team Status's own (uncapped) summary; filtered, they come from the rows.
        TeamAttendanceCounts todayCounts;
        int eligibleToday;
        if (query.DepartmentId is null)
        {
            var s = ctx.TodayResponse.Summary;
            todayCounts = new TeamAttendanceCounts(
                s.Working + s.OnBreak + s.ClockedOut - s.Late, s.Late, s.Absent, s.OnLeave, s.NotStarted);
            eligibleToday = s.Total - s.NotScheduled;
        }
        else
        {
            var buckets = ctx.Rows.Select(r => r.TodayBucket).ToList();
            todayCounts = new TeamAttendanceCounts(
                buckets.Count(b => b == TeamAttendanceBuckets.Present), buckets.Count(b => b == TeamAttendanceBuckets.Late),
                buckets.Count(b => b == TeamAttendanceBuckets.Absent), buckets.Count(b => b == TeamAttendanceBuckets.Leave),
                buckets.Count(b => b == TeamAttendanceBuckets.Pending));
            eligibleToday = buckets.Count(b => b != TeamAttendanceBuckets.Off);
        }

        var preview = ctx.Rows
            .Where(r => r.TodayBucket != TeamAttendanceBuckets.Off)
            .OrderBy(r => PreviewRank(r.TodayBucket))
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PreviewLimit)
            .Select(r => new TeamAttendancePreviewRow(r.EmployeeId, r.Name, r.AvatarFileId, r.Department, r.TodayBucket, r.ClockInAt))
            .ToList();

        return Result<TeamAttendanceSummaryResponse>.Success(new TeamAttendanceSummaryResponse(
            ctx.Mode == TeamAttendanceMode.Week ? "week" : "month",
            range.From,
            range.To,
            ctx.TodayResponse.Timezone,
            ctx.Today,
            eligibleToday,
            todayCounts,
            RateOf(todayCounts),
            period,
            periodRate,
            previousRate,
            periodRate is decimal cur && previousRate is decimal prev ? Math.Round(cur - prev, 1, MidpointRounding.AwayFromZero) : null,
            trend,
            preview,
            ctx.Departments,
            ctx.Rows.Count,
            clock.UtcNow,
            range.InProgress));
    }

    public async Task<Result<PagedResult<TeamAttendanceEmployeeRow>>> Handle(GetTeamAttendanceEmployeesQuery query, CancellationToken ct)
    {
        var loaded = await LoadAsync(query.Mode, query.Anchor, query.DepartmentId, ct);
        if (loaded.Error is { } error)
            return Result<PagedResult<TeamAttendanceEmployeeRow>>.Failure(error.Message, error.StatusCode);
        var ctx = loaded.Context!;
        var range = ctx.Range;

        var search = query.Search?.Trim();
        var matching = ctx.Rows
            .Where(r => string.IsNullOrEmpty(search) || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, MaxPageSize);
        var page = query.Page < 1 ? 1 : query.Page;
        var items = matching
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r =>
            {
                var cells = new List<TeamAttendanceDayCell>();
                for (var d = range.From; d <= range.To; d = d.AddDays(1))
                    cells.Add(new TeamAttendanceDayCell(d, r.BucketOn(d)));
                var c = Count([r], range.From, range.To);
                return new TeamAttendanceEmployeeRow(
                    r.EmployeeId, r.Name, r.AvatarFileId, r.Department, cells,
                    c.Present + c.Late, c.Present + c.Late + c.Absent, RateOf(c));
            })
            .ToList();

        return Result<PagedResult<TeamAttendanceEmployeeRow>>.Success(
            new PagedResult<TeamAttendanceEmployeeRow>(items, page, pageSize, matching.Count));
    }

    private async Task<LoadResult> LoadAsync(string mode, DateOnly? anchor, Guid? departmentId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return LoadResult.Fail("Access denied.", 403);
        if (!TeamAttendanceMath.TryParseMode(mode, out var parsedMode))
            return LoadResult.Fail("Mode must be 'week' or 'month'.", 400);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return LoadResult.Fail("Current employee record was not found.", 404);

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: false,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);
        // Same defensive strip as GetCoveredTeamTodayQuery: company-wide coverage can re-introduce the actor.
        var covered = visibility.EmployeeIds.Where(id => id != actor.Id).Distinct().ToList();

        var todayResult = await mediator.Send(new GetCoveredTeamTodayQuery(TodayLimit), ct);
        if (!todayResult.IsSuccess)
            return LoadResult.Fail(todayResult.Error ?? "Couldn't load today's team status.", todayResult.StatusCode ?? 400);
        var todayResponse = todayResult.Value!;
        var today = todayResponse.WorkDate;
        var range = TeamAttendanceMath.Resolve(parsedMode, anchor ?? today, today);

        var identities = covered.Count == 0
            ? new Dictionary<Guid, AttendanceHistoryEmployee>()
            : await attendance.ListEmployeeIdentitiesAsync(currentUser.TenantId, actor.LegalEntityId.Value, covered, ct);
        var days = covered.Count == 0
            ? []
            : await reader.LoadAsync(currentUser.TenantId, covered, range.PrevFrom, range.To, ct);
        var members = todayResponse.Members.ToDictionary(m => m.EmployeeId);

        var departments = days
            .Where(d => d.DepartmentId is not null && identities.GetValueOrDefault(d.EmployeeId)?.Department is not null)
            .GroupBy(d => d.DepartmentId!.Value)
            .Select(g => new TeamAttendanceDepartment(g.Key, identities[g.First().EmployeeId].Department!))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = days
            .Where(d => departmentId is null || d.DepartmentId == departmentId)
            .Select(d =>
            {
                identities.TryGetValue(d.EmployeeId, out var identity);
                members.TryGetValue(d.EmployeeId, out var member);
                var todayBucket = member is not null
                    ? TodayBucket(member)
                    : d.BucketByDate.GetValueOrDefault(today, TeamAttendanceBuckets.Off);
                return new Row(
                    d.EmployeeId,
                    identity?.DisplayName ?? member?.DisplayName ?? "Unknown",
                    identity?.AvatarFileId ?? member?.AvatarFileId,
                    identity?.Department,
                    d.BucketByDate,
                    today,
                    todayBucket,
                    member?.ClockInAt);
            })
            .ToList();

        return new LoadResult(new LoadContext(parsedMode, range, today, todayResponse, rows, departments), null);
    }

    /// <summary>Team Status's vocabulary as a widget bucket.</summary>
    private static string TodayBucket(TeamTodayMember member) => member.Status switch
    {
        "working" or "on_break" or "clocked_out" => member.IsLate ? TeamAttendanceBuckets.Late : TeamAttendanceBuckets.Present,
        "absent" => TeamAttendanceBuckets.Absent,
        "not_started" => TeamAttendanceBuckets.Pending,
        "on_leave" => TeamAttendanceBuckets.Leave,
        _ => TeamAttendanceBuckets.Off,
    };

    /// <summary>Problems first: late, absent, not in yet, then present and leave.</summary>
    private static int PreviewRank(string bucket) => bucket switch
    {
        TeamAttendanceBuckets.Late => 0,
        TeamAttendanceBuckets.Absent => 1,
        TeamAttendanceBuckets.Pending => 2,
        TeamAttendanceBuckets.Present => 3,
        _ => 4,
    };

    private static TeamAttendanceCounts Count(IEnumerable<Row> rows, DateOnly from, DateOnly to)
    {
        int present = 0, late = 0, absent = 0, leave = 0, pending = 0;
        foreach (var row in rows)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                switch (row.BucketOn(d))
                {
                    case TeamAttendanceBuckets.Present: present++; break;
                    case TeamAttendanceBuckets.Late: late++; break;
                    case TeamAttendanceBuckets.Absent: absent++; break;
                    case TeamAttendanceBuckets.Leave: leave++; break;
                    case TeamAttendanceBuckets.Pending: pending++; break;
                }
            }
        }

        return new TeamAttendanceCounts(present, late, absent, leave, pending);
    }

    private static decimal? RateOf(TeamAttendanceCounts c) =>
        TeamAttendanceMath.Rate(c.Present + c.Late, c.Present + c.Late + c.Absent);

    private sealed record Row(
        Guid EmployeeId,
        string Name,
        Guid? AvatarFileId,
        string? Department,
        IReadOnlyDictionary<DateOnly, string> Buckets,
        DateOnly Today,
        string TodayBucket,
        DateTimeOffset? ClockInAt)
    {
        public string BucketOn(DateOnly date) =>
            date == Today ? TodayBucket : Buckets.GetValueOrDefault(date, TeamAttendanceBuckets.Off);
    }

    private sealed record LoadContext(
        TeamAttendanceMode Mode,
        TeamAttendanceRange Range,
        DateOnly Today,
        TeamTodayResponse TodayResponse,
        IReadOnlyList<Row> Rows,
        IReadOnlyList<TeamAttendanceDepartment> Departments);

    private sealed record LoadError(string Message, int StatusCode);

    private sealed record LoadResult(LoadContext? Context, LoadError? Error)
    {
        public static LoadResult Fail(string message, int statusCode) => new(null, new LoadError(message, statusCode));
    }
}
