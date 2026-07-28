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
    private const int MinimumEncryptionMasterKeyLength = 32;

    private static readonly string[] PlaceholderMarkers =
    {
        "CHANGE_ME",
        "CHANGE-ME",
        "__SET_ME__",
        "REPLACE_ME",
        "PLACEHOLDER"
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

    public static void ValidateRequiredLocalConfiguration(
        IConfiguration configuration,
        string environmentName)
    {
        if (!IsLocalValidationEnvironment(environmentName))
        {
            return;
        }

        var masterKey = configuration["Encryption:MasterKey"];
        if (IsMissingOrPlaceholder(masterKey))
        {
            throw new InvalidOperationException(
                "Encryption:MasterKey is required in Development/Test and must be supplied " +
                "through the repo-root .env as Encryption__MasterKey. Placeholder values are not allowed.");
        }

        if (masterKey!.Length < MinimumEncryptionMasterKeyLength)
        {
            throw new InvalidOperationException(
                $"Encryption:MasterKey must be at least {MinimumEncryptionMasterKeyLength} characters long.");
        }
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
            ("Email:FromAddress", "default From: address", true)
        };

        // Provider registrations, credentials, and provider SELECTION are intentionally
        // not configuration checks. OAuth apps resolve from
        // platform_oauth_apps/platform_oauth_app_credentials, the active SendGrid/Resend
        // transactional email provider and Cloudflare/Cloudflare R2 keys resolve from
        // platform_service_keys, and payment providers resolve from
        // payment_gateway_configs/payment_gateway_credentials. No provider secret and no
        // provider selector belongs in appsettings, .env, or startup warning guidance.

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

        WarnIfDefaultConnectionUsesSuperuserRoleName();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void WarnIfDefaultConnectionUsesSuperuserRoleName()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var username = ExtractUsername(connectionString);
        if (username is null)
            return;

        if (username.Contains("postgres", StringComparison.OrdinalIgnoreCase) ||
            username.Contains("superuser", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[CONFIG] ConnectionStrings:DefaultConnection username '{Username}' looks like a " +
                "superuser role. The runtime app connection must use a restricted, non-superuser, " +
                "non-BYPASSRLS role so PostgreSQL Row-Level Security is actually enforced. Use " +
                "ConnectionStrings:MigrationConnection for schema migrations instead.",
                username);
        }
    }

    private static string? ExtractUsername(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex < 0)
                continue;

            var key = part[..separatorIndex].Trim();
            if (key.Equals("Username", StringComparison.OrdinalIgnoreCase))
                return part[(separatorIndex + 1)..].Trim();
        }

        return null;
    }

    private static bool IsMissingOrPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.Equals("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith('<') && value.EndsWith('>')) return true;
        foreach (var marker in PlaceholderMarkers)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsLocalValidationEnvironment(string environmentName)
    {
        return environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);
    }
}
