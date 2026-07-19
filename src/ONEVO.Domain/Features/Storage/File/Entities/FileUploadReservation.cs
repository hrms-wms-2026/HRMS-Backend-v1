using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Storage.File.Entities;

public class FileUploadReservation : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public long ReservedBytes { get; set; }
    public string Status { get; set; } = FileUploadReservationStatus.Active;
    public Guid ReservedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? CompletedFileRecordId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
