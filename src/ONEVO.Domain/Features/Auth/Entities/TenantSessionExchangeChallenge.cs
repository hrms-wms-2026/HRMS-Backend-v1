using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Single-use, durable exchange challenge that lets a fully-authenticated base-domain login hand
/// off to the correct tenant host without ever setting a session cookie on the base host, and
/// without a shared parent-domain cookie. Stores only the SHA-256 hash of the opaque code; the raw
/// code exists only in the continue_url query string returned once to the browser and is never
/// persisted or logged.
/// </summary>
public class TenantSessionExchangeChallenge : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string AuthOrigin { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
