using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

/// <summary>
/// Stub implementations of the Member-2-owned provisioning section readers.
/// They always return Complete=false so the activation guard fails closed
/// until subscription/modules/roles/settings tracks ship real implementations.
/// Replace registrations in DI when real services are introduced.
/// </summary>
internal static class NotConfiguredYetReaders
{
    public static ProvisioningSectionStatus Build(string section, string code, string message) =>
        new(
            Complete: false,
            Summary: new Dictionary<string, object?>
            {
                ["status"] = "not_configured_yet",
                ["owner"] = "member_2"
            },
            MissingFields: new[] { "*" },
            BlockingErrors: new[] { new ProvisioningIssue(code, message, section) },
            Warnings: Array.Empty<ProvisioningIssue>());
}

public class NotConfiguredSubscriptionStatusReader : ITenantSubscriptionStatusReader
{
    public Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(NotConfiguredYetReaders.Build(
            section: "subscription",
            code: "subscription_not_configured",
            message: "subscription/commercial terms have not been configured for this tenant yet."));
}

public class NotConfiguredModuleStatusReader : ITenantModuleStatusReader
{
    public Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(NotConfiguredYetReaders.Build(
            section: "modules",
            code: "modules_not_configured",
            message: "no module entitlements have been configured for this tenant yet."));
}

public class NotConfiguredSettingsStatusReader : ITenantSettingsStatusReader
{
    public Task<ProvisioningSectionStatus> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(NotConfiguredYetReaders.Build(
            section: "settings",
            code: "settings_not_configured",
            message: "tenant initial settings have not been confirmed yet."));
}
