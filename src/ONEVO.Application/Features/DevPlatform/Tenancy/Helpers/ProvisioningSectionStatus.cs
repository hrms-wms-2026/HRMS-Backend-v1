namespace ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;

/// <summary>
/// Single provisioning section's completion snapshot returned by the
/// section-reader interfaces below. Aggregated by the provisioning summary
/// handler into the final ProvisioningSummaryDto.
/// </summary>
public sealed record ProvisioningSectionStatus(
    bool Complete,
    IReadOnlyDictionary<string, object?> Summary,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<ProvisioningIssue> BlockingErrors,
    IReadOnlyList<ProvisioningIssue> Warnings)
{
    public static ProvisioningSectionStatus Empty(string section, string code, string message) =>
        new(
            Complete: false,
            Summary: new Dictionary<string, object?>(),
            MissingFields: Array.Empty<string>(),
            BlockingErrors: new[] { new ProvisioningIssue(code, message, section) },
            Warnings: Array.Empty<ProvisioningIssue>());
}

public sealed record ProvisioningIssue(string Code, string Message, string Section);
