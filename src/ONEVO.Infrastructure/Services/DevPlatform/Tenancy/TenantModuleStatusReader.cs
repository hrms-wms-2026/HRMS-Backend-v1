using System.Text.Json;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

/// <summary>
/// Reads module entitlements from the tenant's subscription snapshot
/// (tenant_subscriptions.selected_modules_json), the same source
/// CreateTenantCommandHandler seeds from the plan's included modules. This
/// reads the row directly rather than through IModuleEntitlementService,
/// which additionally filters by active subscription status
/// (SubscriptionStatusRules.ActiveStatuses) - a concern this provisioning
/// check doesn't need, since it only asks whether module entitlements were
/// ever configured, not whether the subscription is currently billable.
/// </summary>
public sealed class TenantModuleStatusReader : ITenantModuleStatusReader
{
    private readonly ITenantSubscriptionRepository _subscriptions;

    public TenantModuleStatusReader(ITenantSubscriptionRepository subscriptions) =>
        _subscriptions = subscriptions;

    public async Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByTenantIdAsync(tenantId, ct);
        var moduleKeys = ParseModuleKeys(subscription?.SelectedModulesJson);

        if (moduleKeys.Count == 0)
        {
            return NotConfiguredYetReaders.Build(
                section: "modules",
                code: "modules_not_configured",
                message: "no module entitlements have been configured for this tenant yet.");
        }

        return new ProvisioningSectionStatus(
            Complete: true,
            Summary: new Dictionary<string, object?>
            {
                ["module_count"] = moduleKeys.Count,
                ["modules"] = moduleKeys
            },
            MissingFields: Array.Empty<string>(),
            BlockingErrors: Array.Empty<ProvisioningIssue>(),
            Warnings: Array.Empty<ProvisioningIssue>());
    }

    private static IReadOnlyList<string> ParseModuleKeys(string? selectedModulesJson)
    {
        if (string.IsNullOrWhiteSpace(selectedModulesJson))
            return Array.Empty<string>();

        try
        {
            var modules = JsonSerializer.Deserialize<List<string>>(selectedModulesJson);
            if (modules is null)
                return Array.Empty<string>();

            return modules
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
