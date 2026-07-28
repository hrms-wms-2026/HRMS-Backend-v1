using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

/// <summary>
/// Shared builder for a provisioning section that has no data yet, so the
/// activation guard fails closed until the section is configured.
/// </summary>
internal static class NotConfiguredYetReaders
{
    public static ProvisioningSectionStatus Build(string section, string code, string message) =>
        new(
            Complete: false,
            Summary: new Dictionary<string, object?>
            {
                ["status"] = "not_configured_yet"
            },
            MissingFields: new[] { "*" },
            BlockingErrors: new[] { new ProvisioningIssue(code, message, section) },
            Warnings: Array.Empty<ProvisioningIssue>());
}
