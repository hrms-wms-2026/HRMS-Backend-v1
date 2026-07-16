using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Tenant-scoped authentication policy for invite completion and login methods.
/// One row per tenant (unique tenant_id).
/// </summary>
public class TenantAuthPolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public bool PasswordCompletionAllowed { get; set; } = true;
    public bool GoogleCompletionAllowed { get; set; } = true;
    public bool GoogleEmailMismatchDefault { get; set; } = false;

    /// <summary>JSON array of allowed email domain suffixes for Google login.</summary>
    public string? AllowedLoginDomainsJson { get; set; }

    public bool MfaRequired { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
