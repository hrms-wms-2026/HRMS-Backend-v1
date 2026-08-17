using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class Session : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public string CsrfTokenHash { get; set; } = string.Empty;
    public Guid? ActiveEmployeeId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
}
