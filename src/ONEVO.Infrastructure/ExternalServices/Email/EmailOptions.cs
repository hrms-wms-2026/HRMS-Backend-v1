namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Bound from the Email:* section of configuration. NON-SECRET settings only.
/// Provider API keys (SendGrid/Resend) are never read from configuration - they are
/// resolved at send time from platform_service_keys via IPlatformServiceKeyResolver.
/// There is no provider selector here: WHICH provider is active is a runtime fact
/// derived from active platform_providers rows joined to matching active
/// platform_service_keys credentials, not a config value. See
/// PlatformKeyTransactionalEmailSender + IPlatformServiceKeyResolver.
/// ResolveActiveTransactionalEmailProviderAsync.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "ONEVO";

    /// <summary>Optional Reply-To address; omitted from provider requests when empty.</summary>
    public string ReplyToEmail { get; set; } = string.Empty;

    /// <summary>App base URL (tenant-facing). Used to render invite/reset links.</summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>Admin console base URL. Used for admin-facing emails when needed.</summary>
    public string AdminConsoleBaseUrl { get; set; } = string.Empty;
}
