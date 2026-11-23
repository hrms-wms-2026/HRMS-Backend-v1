namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>
/// Ledger of permission catalogue changes applied to this database (rename, default grant,
/// soft-retire, remove). Global, like <see cref="Permission"/>: it is written only by
/// PermissionCatalogSynchronizer and read by no tenant API. The unique <see cref="ChangeKey"/> makes
/// each change apply once.
/// </summary>
public class PermissionChange
{
    public Guid Id { get; set; }

    /// <summary>For example "rename:a->b", "default-grant:x", "soft-retire:x", "remove:x".</summary>
    public string ChangeKey { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? PreviousCode { get; set; }

    /// <summary>JSON: the tenants and roles touched and the grant counts, for audit.</summary>
    public string Detail { get; set; } = "{}";

    public DateTimeOffset AppliedAt { get; set; }
}
