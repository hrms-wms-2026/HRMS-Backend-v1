using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Storage.File.Entities;

public class FileRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string SafeFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? DetectedContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public string Status { get; set; } = FileRecordStatus.PendingScan;
    public string? ScanProvider { get; set; }
    public string? ScanResultCode { get; set; }
    public DateTimeOffset? ScanCompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? StorageDeletedAt { get; set; }
}
