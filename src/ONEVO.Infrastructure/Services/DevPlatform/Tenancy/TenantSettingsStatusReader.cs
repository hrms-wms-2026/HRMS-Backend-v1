using System.Text.Json;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

/// <summary>
/// Reads tenant.settings_json, populated at tenant creation time by
/// CreateTenantCommandHandler (default_timezone at minimum). This is the only
/// Phase 1 tenant-settings source; there is no separate settings table.
/// </summary>
public sealed class TenantSettingsStatusReader : ITenantSettingsStatusReader
{
    private readonly ITenantRepository _tenants;

    public TenantSettingsStatusReader(ITenantRepository tenants) => _tenants = tenants;

    public async Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        var settings = ParseSettings(tenant?.SettingsJson);

        if (settings is null || settings.Count == 0)
        {
            return NotConfiguredYetReaders.Build(
                section: "settings",
                code: "settings_not_configured",
                message: "tenant initial settings have not been confirmed yet.");
        }

        return new ProvisioningSectionStatus(
            Complete: true,
            Summary: new Dictionary<string, object?>(settings),
            MissingFields: Array.Empty<string>(),
            BlockingErrors: Array.Empty<ProvisioningIssue>(),
            Warnings: Array.Empty<ProvisioningIssue>());
    }

    private static Dictionary<string, object?>? ParseSettings(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(settingsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
