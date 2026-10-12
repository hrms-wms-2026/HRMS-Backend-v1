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

    public static SnapshotTimeWindowResult<T> Partition<T>(
        IReadOnlyList<T> items, Func<T, DateTimeOffset> capturedAt, DateTimeOffset now)
    {
        var accepted = new List<T>(items.Count);
        var tooOld = 0;
        var future = 0;

        foreach (var item in items)
        {
            var at = capturedAt(item);
            if (at > now + MaxFutureSkew)
                future++;
            else if (at < now - MaxAge)
                tooOld++;
            else
                accepted.Add(item);
        }

        return new SnapshotTimeWindowResult<T>(accepted, tooOld, future);
    }
}

public sealed record SnapshotTimeWindowResult<T>(List<T> Accepted, int TooOldCount, int FutureCount)
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
}
