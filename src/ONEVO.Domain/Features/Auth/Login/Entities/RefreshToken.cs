using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

public class RefreshToken : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;
}
