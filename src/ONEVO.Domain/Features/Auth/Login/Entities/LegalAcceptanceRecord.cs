using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class LegalAcceptanceRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentVersion { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public bool Required { get; set; }
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string Source { get; set; } = "web";
}
