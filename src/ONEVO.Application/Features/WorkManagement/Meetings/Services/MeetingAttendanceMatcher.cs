namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The pure halves of per-occurrence attendance (spec 5.8): which occurrence a provider session belongs to, and how long a
/// person was there.
/// </summary>
public static class MeetingAttendanceMatcher
{
    /// <summary>Each session goes to the occurrence whose window [start - <paramref name="before"/>, end] contains the
    /// session's start; when several do, the nearest start wins and the earlier occurrence on a tie. A session that fits no
    /// window is left out (unassigned).</summary>
    public static IReadOnlyDictionary<string, Guid> Assign(IReadOnlyList<(string SessionId, DateTimeOffset Start)> unassigned,
        IReadOnlyList<(Guid TaskId, DateTimeOffset Start, DateTimeOffset End)> occurrences, TimeSpan before)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (sessionId, start) in unassigned)
        {
            var match = occurrences
                .Where(o => start >= o.Start - before && start <= o.End)
                .OrderBy(o => (start - o.Start).Duration())
                .ThenBy(o => o.Start)
                .Select(o => (Guid?)o.TaskId)
                .FirstOrDefault();
            if (match is Guid taskId)
                result[sessionId] = taskId;
        }
        return result;
    }

    /// <summary>The length of the union of the intervals: overlaps and touching rejoins count once. An interval still open
    /// (no leave time) ends at <paramref name="openEndAt"/>, the occurrence's end; nothing counts past it for an open one.</summary>
    public static TimeSpan Union(IEnumerable<(DateTimeOffset Joined, DateTimeOffset? Left)> intervals, DateTimeOffset openEndAt)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? from = null, to = null;
        foreach (var (start, end) in intervals
                     .Select(i => (Start: i.Joined, End: i.Left ?? (openEndAt > i.Joined ? openEndAt : i.Joined)))
                     .Where(i => i.End > i.Start)
                     .OrderBy(i => i.Start))
        {
            if (to is { } currentEnd && start <= currentEnd)
            {
                if (end > currentEnd)
                    to = end;
                continue;
            }
            if (from is { } f && to is { } t)
                total += t - f;
            (from, to) = (start, end);
        }
        if (from is { } lastFrom && to is { } lastTo)
            total += lastTo - lastFrom;
        return total;
    }
}
