using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

public class MonitoringFaceScan : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CheckInId { get; set; }

    // R2 storage key: tenants/{tenantId}/monitoring/face-scans/{id}/{fileName}
    public string StorageKey { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;

    /// <summary>CompareFaces similarity 0-100 against the employee's enrolled reference photo. Null when there was no reference photo to compare against.</summary>
    public float? SimilarityScore { get; set; }

    public string Status { get; set; } = MonitoringFaceScanStatus.PendingScan;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
