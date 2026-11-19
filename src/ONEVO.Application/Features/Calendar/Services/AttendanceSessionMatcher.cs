namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Picks the provider session (a Teams attendance report, a Zoom past-meeting instance) that
/// belongs to one scheduled occurrence. A reused link carries one session per occurrence, so the
/// newest session is not necessarily the one asked about (spec E6b/E6c).</summary>
public static class AttendanceSessionMatcher
{
    /// <summary>A session may start up to this long before the scheduled start (people join early).</summary>
    public static readonly TimeSpan EarlyStartAllowance = TimeSpan.FromMinutes(60);

    /// <summary>Index of the session whose start falls in [scheduledStart - allowance, scheduledEnd],
    /// nearest to the scheduled start (the earlier one on a tie); null when none does.</summary>
    public static int? PickIndex(IReadOnlyList<DateTimeOffset?> sessionStarts, DateTimeOffset scheduledStart, DateTimeOffset scheduledEnd)
        => sessionStarts
            .Select((start, index) => (start, index))
            .Where(x => x.start is { } s && s >= scheduledStart - EarlyStartAllowance && s <= scheduledEnd)
            .OrderBy(x => (x.start!.Value - scheduledStart).Duration())
            .ThenBy(x => x.start)
            .Select(x => (int?)x.index)
            .FirstOrDefault();
}
