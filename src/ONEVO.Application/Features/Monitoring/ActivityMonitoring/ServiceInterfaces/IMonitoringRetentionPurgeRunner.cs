namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

public interface IMonitoringRetentionPurgeRunner
{
    /// <summary>
    /// Purges expired monitoring data in one pass across every tenant: raw ingest buffers
    /// older than the configured retention window, and screenshot evidence (plus its backing
    /// file) past its own window, skipping any row placed under legal hold.
    /// </summary>
    Task<MonitoringRetentionPurgeResult> RunOnceAsync(CancellationToken ct = default);
}

public sealed record MonitoringRetentionPurgeResult(int RawBuffersDeleted, int ScreenshotsDeleted);
