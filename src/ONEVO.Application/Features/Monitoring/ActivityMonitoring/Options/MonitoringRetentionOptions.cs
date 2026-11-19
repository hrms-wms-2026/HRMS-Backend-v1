namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Options;

/// <summary>
/// Retention windows for monitoring data that has no purge job otherwise: raw ingest
/// buffers and screenshot evidence. Bound from configuration section "Monitoring:Retention".
/// </summary>
public sealed class MonitoringRetentionOptions
{
    public const string SectionName = "MonitoringRetention";

    /// <summary>
    /// activity_raw_buffer rows are the entire raw ingest batch payload, kept only as a
    /// write-only debug trail - no query handler reads them back. Safe to purge quickly.
    /// </summary>
    public int RawBufferRetentionDays { get; set; } = 14;

    /// <summary>
    /// How long screenshot evidence (monitoring_evidence_assets + its backing file) is kept
    /// after capture. A row with an active LegalHoldId is never purged, regardless of age.
    /// </summary>
    public int ScreenshotRetentionDays { get; set; } = 90;
}
