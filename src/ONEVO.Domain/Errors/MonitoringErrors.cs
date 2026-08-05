namespace ONEVO.Domain.Errors;

/// <summary>
/// Stable monitoring error codes and human-readable messages.
/// Handlers map these to <c>Result.Failure(message, statusCode)</c>.
/// </summary>
public static class MonitoringErrors
{
    public const string ActivityMonitoringDisabledCode = "monitoring.activity_monitoring_disabled";
    public const string SnapshotBatchTooLargeCode = "monitoring.snapshot_batch_too_large";
    public const string SnapshotTooOldCode = "monitoring.snapshot_too_old";
    public const string SnapshotFutureTimeCode = "monitoring.snapshot_future_time";

    public const string ActivityMonitoringDisabled =
        "Activity monitoring is not enabled for this employee.";

    public const string SnapshotBatchTooLarge =
        "Batch cannot exceed 200 snapshots.";

    public const string SnapshotTooOld =
        "Snapshot captured_at cannot be older than 24 hours.";

    public const string SnapshotFutureTime =
        "Snapshot captured_at cannot be in the future.";
}
