using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class GdprConsentRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string ConsentType { get; set; } = string.Empty; // "data_processing" | "biometric" | "monitoring" | "marketing"
    public bool Consented { get; set; }
    public DateTimeOffset ConsentedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
}
