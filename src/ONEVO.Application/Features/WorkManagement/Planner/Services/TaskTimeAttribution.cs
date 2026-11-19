namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>A Planner block of one task (My Team widgets design 2026-10-29, section 5.2).</summary>
public sealed record AttributionBlock(Guid TaskId, DateTimeOffset Start, DateTimeOffset End);

/// <summary>A task clocking session; an open session has no clock-out.</summary>
public sealed record AttributionSession(Guid TaskId, DateTimeOffset ClockIn, DateTimeOffset? ClockOut);

public enum UnscheduledKind { NoPlan, OutsideSlot }

/// <summary>Clocked time with no matching block of the same task: the task has no block in the window (NoPlan) or was
/// clocked outside its blocks (OutsideSlot).</summary>
public sealed record UnscheduledSegment(Guid TaskId, DateTimeOffset Start, DateTimeOffset End, int Minutes, UnscheduledKind Kind);

public sealed record TaskAttribution(Guid TaskId, int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes,
    int NoPlanMinutes);

/// <summary>One employee's window. Invariant: Scheduled + OutsideSlot + NoPlan == Clocked.</summary>
public sealed record AttributionResult(int PlannedMinutes, int ClockedMinutes, int ScheduledMinutes, int OutsideSlotMinutes,
    int NoPlanMinutes, int ParallelClockMinutes, IReadOnlyList<TaskAttribution> Tasks, IReadOnlyList<UnscheduledSegment> Unscheduled);

/// <summary>Splits one employee's clocked task time in a window into scheduled (inside a block of the same task), outside
/// slot and no plan. Sessions of one task are merged so an instant counts once; overlap between different tasks is kept
/// and reported as ParallelClockMinutes. The Planner's own day totals (PlannerService) are not affected.</summary>
public static class TaskTimeAttribution
{
    public static AttributionResult Attribute(IEnumerable<AttributionBlock> blocks, IEnumerable<AttributionSession> sessions,
        DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var clockEnd = now < to ? now : to;

        var blocksByTask = blocks
            .Select(b => (b.TaskId, Interval: Clip(b.Start, b.End, from, to)))
            .Where(x => x.Interval is not null)
            .GroupBy(x => x.TaskId)
            .ToDictionary(g => g.Key, g => Merge(g.Select(x => x.Interval!.Value)));

        var sessionsByTask = sessions
            .Select(s => (s.TaskId, Interval: Clip(s.ClockIn, s.ClockOut ?? now, from, clockEnd)))
            .Where(x => x.Interval is not null)
            .GroupBy(x => x.TaskId)
            .ToDictionary(g => g.Key, g => Merge(g.Select(x => x.Interval!.Value)));

        var tasks = new List<TaskAttribution>();
        var unscheduled = new List<UnscheduledSegment>();
        var clockedRawTotal = 0.0;

        foreach (var taskId in blocksByTask.Keys.Union(sessionsByTask.Keys).OrderBy(id => id))
        {
            var taskBlocks = blocksByTask.GetValueOrDefault(taskId) ?? [];
            var taskSessions = sessionsByTask.GetValueOrDefault(taskId) ?? [];
            var hasPlan = taskBlocks.Count > 0;

            var clockedRaw = Length(taskSessions);
            clockedRawTotal += clockedRaw;
            var planned = Round(Length(taskBlocks));
            var clocked = Round(clockedRaw);
            var scheduled = Math.Min(clocked, Round(Length(Intersect(taskSessions, taskBlocks))));
            var remainder = clocked - scheduled;

            tasks.Add(new TaskAttribution(taskId, planned, clocked, scheduled, hasPlan ? remainder : 0, hasPlan ? 0 : remainder));

            var kind = hasPlan ? UnscheduledKind.OutsideSlot : UnscheduledKind.NoPlan;
            foreach (var (start, end) in Subtract(taskSessions, taskBlocks))
            {
                var minutes = Round((end - start).TotalMinutes);
                if (minutes > 0)
                    unscheduled.Add(new UnscheduledSegment(taskId, start, end, minutes, kind));
            }
        }

        var unionRaw = Length(Merge(sessionsByTask.Values.SelectMany(x => x)));
        var parallel = Math.Max(0, Round(clockedRawTotal - unionRaw));

        return new AttributionResult(
            tasks.Sum(t => t.PlannedMinutes),
            tasks.Sum(t => t.ClockedMinutes),
            tasks.Sum(t => t.ScheduledMinutes),
            tasks.Sum(t => t.OutsideSlotMinutes),
            tasks.Sum(t => t.NoPlanMinutes),
            parallel,
            tasks,
            unscheduled.OrderBy(s => s.Start).ThenBy(s => s.TaskId).ToList());
    }

    private static int Round(double minutes) => (int)Math.Round(minutes, MidpointRounding.AwayFromZero);

    private static (DateTimeOffset Start, DateTimeOffset End)? Clip(DateTimeOffset start, DateTimeOffset end, DateTimeOffset from,
        DateTimeOffset to)
    {
        var s = start > from ? start : from;
        var e = end < to ? end : to;
        return e > s ? (s, e) : null;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Merge(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var (start, end) in intervals.OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, end > last.End ? end : last.End);
            }
            else
            {
                merged.Add((start, end));
            }
        }
        return merged;
    }

    private static double Length(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals)
        => intervals.Sum(i => (i.End - i.Start).TotalMinutes);

    /// <summary>Intersection of two merged (sorted, disjoint) lists.</summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> Intersect(
        List<(DateTimeOffset Start, DateTimeOffset End)> a, List<(DateTimeOffset Start, DateTimeOffset End)> b)
    {
        var result = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            var start = a[i].Start > b[j].Start ? a[i].Start : b[j].Start;
            var end = a[i].End < b[j].End ? a[i].End : b[j].End;
            if (end > start)
                result.Add((start, end));
            if (a[i].End < b[j].End) i++; else j++;
        }
        return result;
    }

    /// <summary>The parts of merged list a not covered by merged list b.</summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> Subtract(
        List<(DateTimeOffset Start, DateTimeOffset End)> a, List<(DateTimeOffset Start, DateTimeOffset End)> b)
    {
        var result = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var (start, end) in a)
        {
            var cursor = start;
            foreach (var (bs, be) in b)
            {
                if (be <= cursor || bs >= end)
                    continue;
                if (bs > cursor)
                    result.Add((cursor, bs));
                cursor = be > cursor ? be : cursor;
                if (cursor >= end)
                    break;
            }
            if (cursor < end)
                result.Add((cursor, end));
        }
        return result;
    }
}
