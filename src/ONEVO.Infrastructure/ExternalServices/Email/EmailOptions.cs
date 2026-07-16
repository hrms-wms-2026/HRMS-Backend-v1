namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Bound from the Email:* section of configuration. NON-SECRET settings only.
/// Provider API keys (SendGrid/Resend) are never read from configuration — they are
/// resolved at send time from platform_service_keys via IPlatformServiceKeyResolver.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Non-secret provider selector: "sendgrid" or "resend".
    /// Empty defaults to "sendgrid". This selects WHICH platform service key row is
    /// resolved; it never carries credential material itself.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "ONEVO";

    /// <summary>Optional Reply-To address; omitted from provider requests when empty.</summary>
    public string ReplyToEmail { get; set; } = string.Empty;

    /// <summary>App base URL (tenant-facing). Used to render invite/reset links.</summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>Admin console base URL. Used for admin-facing emails when needed.</summary>
    public string AdminConsoleBaseUrl { get; set; } = string.Empty;
}
