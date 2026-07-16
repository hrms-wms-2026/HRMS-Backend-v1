using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// OAuth / OIDC external identity linked to a tenant user (e.g. Google subject).
/// </summary>
public class UserExternalIdentity : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>e.g. "google"</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Stable provider-specific subject (Google "sub").</summary>
    public string ProviderSubject { get; set; } = string.Empty;

    public string ProviderEmail { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }

    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}
