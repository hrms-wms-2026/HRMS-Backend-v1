namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

/// <summary>
/// Links an integration catalog entry to a ONEVO product module. Controls which
/// integrations become visible/connectable when a tenant has the related module
/// entitlement. Visibility linkage only — no credentials or tokens.
/// Phase 1 canonical table: module_integration_links.
/// </summary>
public class ModuleIntegrationLink
{
    /// <summary>FK -> module_catalog(module_key).</summary>
    public string ModuleKey { get; set; } = string.Empty;

    /// <summary>FK -> integration_catalog(integration_key).</summary>
    public string IntegrationKey { get; set; } = string.Empty;

    /// <summary>Platform user who created this link. FK -> platform_users.</summary>
    public Guid LinkedById { get; set; }

    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}
