namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Bundle of cross-cutting platform options that are wired today as
/// configuration sections. Keeping each concern in its own POCO avoids one
/// god-config and lets the startup validator log a clean per-section warning
/// when a key is missing.
/// </summary>
public class PlatformUrlsOptions
{
    public const string SectionName = "Urls";
    public string AppBaseUrl { get; set; } = string.Empty;
    public string AdminConsoleBaseUrl { get; set; } = string.Empty;
    public string WebhookBaseUrl { get; set; } = string.Empty;
}

public class EncryptionOptions
{
    public const string SectionName = "Encryption";
    public string MasterKey { get; set; } = string.Empty;
}
