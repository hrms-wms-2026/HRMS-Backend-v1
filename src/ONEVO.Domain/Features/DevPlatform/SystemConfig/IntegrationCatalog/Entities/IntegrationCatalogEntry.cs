namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

/// <summary>
/// Operator-managed catalog of connectable software integrations shown in the tenant app.
/// Stores metadata only — no provider secrets, no tenant/employee OAuth tokens.
/// Phase 1 canonical table: integration_catalog.
/// </summary>
public class IntegrationCatalogEntry
{
    /// <summary>Operator-set slug, stored lowercase and unique: github, zoom, google_calendar.</summary>
    public string IntegrationKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>"tenant", "user", or "both".</summary>
    public string ConnectionScope { get; set; } = string.Empty;

    /// <summary>FK -> platform_oauth_apps.provider; ONEVO OAuth app registration used for consent.</summary>
    public string OnevoAppProvider { get; set; } = string.Empty;

    public string? LogoUrl { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Platform user who created this catalog entry. FK -> platform_users.</summary>
    public Guid CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
