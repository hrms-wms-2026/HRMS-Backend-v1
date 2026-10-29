using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

/// <summary>
/// Server-clock freshness window shared by every tray snapshot ingest (activity, app usage,
/// device state, meeting signals). Items are judged one by one: a tray that was offline for a
/// weekend flushes batches that straddle the 24h edge, and rejecting the whole batch for one
/// stale item silently discards every still-valid item alongside it.
/// </summary>
public static class SnapshotTimeWindow
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// PostgreSQL timestamptz keeps microseconds; the tray sends 100ns ticks. Every ingest compares and
    /// stores the truncated value, so a replayed batch (lost 202) matches the rows it already wrote.
    /// </summary>
    public static DateTimeOffset Normalize(DateTimeOffset at)
    {
        var ticks = at.UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    public static SnapshotTimeWindowResult<T> Partition<T>(
        IReadOnlyList<T> items, Func<T, DateTimeOffset> capturedAt, DateTimeOffset now)
    {
        var accepted = new List<T>(items.Count);
        var rejected = new List<SnapshotRejection>();
        var tooOld = 0;
        var future = 0;

        foreach (var item in items)
        {
            var at = capturedAt(item);
            if (at > now + MaxFutureSkew)
            {
                future++;
                rejected.Add(new SnapshotRejection(Normalize(at), MonitoringErrors.SnapshotFutureTimeCode));
            }
            else if (at < now - MaxAge)
            {
                tooOld++;
                rejected.Add(new SnapshotRejection(Normalize(at), MonitoringErrors.SnapshotTooOldCode));
            }
            else
                accepted.Add(item);
        }

        return new SnapshotTimeWindowResult<T>(accepted, tooOld, future, rejected);
    }

    /// <summary>
    /// Drops items this device already reported (in an earlier, possibly unacknowledged request) and
    /// repeats inside the same batch. Items must already be <see cref="Normalize"/>d.
    /// </summary>
    public static List<T> WithoutDuplicates<T>(
        IEnumerable<T> items, Func<T, DateTimeOffset> capturedAt, IReadOnlySet<DateTimeOffset> alreadyStored)
    {
        var seen = new HashSet<DateTimeOffset>(alreadyStored.Select(Normalize));
        return items.Where(item => seen.Add(capturedAt(item))).ToList();
    }
}

public sealed record SnapshotTimeWindowResult<T>(
    List<T> Accepted, int TooOldCount, int FutureCount, IReadOnlyList<SnapshotRejection> Rejected)
{
    public int RejectedCount => TooOldCount + FutureCount;

    /// <summary>
    /// True only when the batch had items and none of them fit the window. A mixed batch is
    /// accepted (the in-window items are stored); only an all-out-of-window batch is a 400.
    /// </summary>
    public bool AllRejected => Accepted.Count == 0 && RejectedCount > 0;

    public string RejectionError => TooOldCount > 0
        ? MonitoringErrors.SnapshotTooOld
        : MonitoringErrors.SnapshotFutureTime;

    public string RejectionErrorCode => TooOldCount > 0
        ? MonitoringErrors.SnapshotTooOldCode
        : MonitoringErrors.SnapshotFutureTimeCode;

    public SnapshotIngestOutcome ToOutcome(int stored) =>
        new(stored, Accepted.Count - stored, Rejected);
}

/// <summary>An item the server will never store, keyed by its normalized captured_at.</summary>
public sealed record SnapshotRejection(DateTimeOffset CapturedAt, string Code);

/// <summary>
/// 202 body for tray snapshot ingest. <c>Duplicates</c> were already stored by an earlier request
/// (safe to acknowledge); <c>Rejected</c> items are permanently refused and must not be retried as-is.
/// </summary>
public sealed record SnapshotIngestOutcome(int Stored, int Duplicates, IReadOnlyList<SnapshotRejection> Rejected);
