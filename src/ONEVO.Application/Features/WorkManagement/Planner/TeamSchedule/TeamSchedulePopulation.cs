using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>Who Today's Team Schedule and the team week cover, and how their zones read. Shared by both handlers.</summary>
internal static class TeamSchedulePopulation
{
    /// <summary>Everyone below the caller in the reporting line, one batched read per level. Through the common employee
    /// repository: WorkManagement may never reference the raw coverage tables (My Team spec section 10, enforced by
    /// MyTeamBoundaryArchitectureTests). The seen set stops a cycle in bad data from looping.</summary>
    public static async Task<List<Guid>> TransitiveReportsAsync(IEmployeeRepository employees, Guid tenantId, Guid callerId,
        CancellationToken ct)
    {
        var seen = new HashSet<Guid> { callerId };
        var result = new List<Guid>();
        IReadOnlyCollection<Guid> level = [callerId];
        while (level.Count > 0)
        {
            var next = (await employees.ListDirectReportEmployeeIdsAsync(tenantId, level, ct)).Where(seen.Add).ToList();
            result.AddRange(next);
            level = next;
        }
        return result;
    }

    public readonly record struct Zone(string Id, TimeZoneInfo Info);

    /// <summary>An unknown zone id reads in UTC instead of failing, like PlannerService.</summary>
    public static Zone ZoneOf(string? id)
        => id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var info) ? new Zone(id, info) : new Zone("UTC", TimeZoneInfo.Utc);

    public static DateOnly TodayIn(TimeZoneInfo zone, DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
}
