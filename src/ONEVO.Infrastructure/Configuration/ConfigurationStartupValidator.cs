using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// At startup, scan every config key the platform expects and log a warning
/// (not an error) for each missing or placeholder value. Keeps boot non-fatal
/// while making it obvious in the console which integrations are inert until
/// real keys arrive.
/// </summary>
public class ConfigurationStartupValidator : IHostedService
{
    private static readonly string[] PlaceholderMarkers =
    {
        "CHANGE_ME",
        "__SET_ME__",
        "REPLACE_ME"
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationStartupValidator> _logger;

    public ConfigurationStartupValidator(
        IConfiguration configuration,
        ILogger<ConfigurationStartupValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var checks = new (string Key, string Purpose, bool RequiredInProd)[]
        {
            ("ConnectionStrings:DefaultConnection", "PostgreSQL connection string", true),
            ("Jwt:Secret", "MFA challenge/device JWT signing key", false),
            ("Jwt:TenantIssuer", "MFA challenge/device JWT issuer", false),
            ("Jwt:TenantAudience", "MFA challenge/device JWT audience", false),
            ("Encryption:MasterKey", "encryption master key for secrets at rest", true),
            ("Urls:AppBaseUrl", "tenant app base URL (used in invite/reset links)", true),
            ("Urls:AdminConsoleBaseUrl", "admin console base URL", true),
            ("Urls:WebhookBaseUrl", "public webhook callback base URL", false),
            ("Email:Provider", "email provider selector (sendgrid/resend, non-secret)", true),
            ("Email:FromAddress", "default From: address", true),
            ("GoogleAuth:Admin:ClientId", "Google OAuth client ID for admin login", false),
            ("GoogleAuth:TenantInvite:ClientId", "Google OAuth client ID for tenant invite Google accept", false),
            ("Stripe:PublishableKey", "Stripe publishable key (client-side, not secret)", false),
            ("PayHere:MerchantId", "PayHere merchant ID", false)
        };

        // Provider secrets (GoogleAuth:*:ClientSecret, Stripe:SecretKey, Stripe:WebhookSecret,
        // PayHere:MerchantSecret, PayHere:WebhookSecret) are intentionally NOT checked here and must
        // never live in appsettings.*.json. SendGrid/Resend transactional email API keys are stored
        // encrypted in platform_service_keys and resolved at send time via IPlatformServiceKeyResolver;
        // no email secret exists in configuration at all. Remaining payment credentials live in
        // payment_gateway_credentials; until fully wired, supply them only via environment
        // variables / user-secrets for local runs that need a real provider.

        var missing = 0;
        foreach (var check in checks)
        {
            var value = _configuration[check.Key];
            if (IsMissingOrPlaceholder(value))
            {
                missing++;
                _logger.LogWarning(
                    "[CONFIG] {Key} is not set ({Purpose}). Endpoints depending on this will be inert until you fill it in.",
                    check.Key,
                    check.Purpose);
            }
        }

        if (missing == 0)
        {
            _logger.LogInformation("[CONFIG] All known platform configuration keys are populated.");
        }
        else
        {
            _logger.LogWarning(
                "[CONFIG] {Missing} of {Total} platform configuration keys are missing or placeholder. " +
                "Fill them in via appsettings.Development.json, environment variables, or your secret store.",
                missing,
                checks.Length);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsMissingOrPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        foreach (var marker in PlaceholderMarkers)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
