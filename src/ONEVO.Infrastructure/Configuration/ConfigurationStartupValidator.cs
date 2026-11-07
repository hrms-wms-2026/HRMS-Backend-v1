using System.Text;
using Microsoft.Data.SqlClient;
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

    public const string TrayJwtSecretKey = "TrayApp:Jwt:Secret";

    /// <summary>HS256 (the tray token algorithm) needs a key of at least 256 bits.</summary>
    public const int MinimumTrayJwtSecretBytes = 32;

    /// <summary>
    /// The tray device-token signing/validation key, checked for presence and HS256 key size.
    /// Used by both consumers (token issue + "TrayDeviceScheme" validation). Messages never
    /// contain the value.
    /// </summary>
    public static string RequireTrayJwtSecret(IConfiguration configuration)
    {
        var secret = configuration[TrayJwtSecretKey];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "TrayApp:Jwt:Secret is required. Set the TrayApp__Jwt__Secret environment variable.");
        }

        if (Encoding.UTF8.GetByteCount(secret) < MinimumTrayJwtSecretBytes)
        {
            throw new InvalidOperationException(
                $"TrayApp:Jwt:Secret must be at least {MinimumTrayJwtSecretBytes} bytes for HS256 token signing.");
        }

        return secret;
    }

    /// <summary>
    /// SEC-01 startup check. Outside Development/Test the API refuses to boot when the tray secret is
    /// missing, too short for HS256, or a development placeholder — appsettings.Development.json ships a
    /// self-labeled dev value anyone with the repo can sign tray tokens with. (Before this, a missing
    /// secret only surfaced on the first tray request.) Development/Test keep the lazy per-consumer check.
    /// </summary>
    public static void ValidateTrayJwtSecret(
        IConfiguration configuration,
        string environmentName)
    {
        if (IsLocalValidationEnvironment(environmentName))
        {
            return;
        }

        var secret = RequireTrayJwtSecret(configuration);
        if (secret.StartsWith("dev-only", StringComparison.OrdinalIgnoreCase) || IsMissingOrPlaceholder(secret))
        {
            throw new InvalidOperationException(
                $"TrayApp:Jwt:Secret is a development placeholder in the '{environmentName}' environment. " +
                "Set TrayApp__Jwt__Secret to a real random value (32+ bytes) from your secret store.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var checks = new (string Key, string Purpose, bool RequiredInProd)[]
        {
            ("ConnectionStrings:DefaultConnection", "SQL Server connection string", true),
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

        if (UsesAdminLogin(connectionString))
        {
            _logger.LogWarning(
                "[CONFIG] ConnectionStrings:DefaultConnection uses the sa login. The runtime app " +
                "connection must use the restricted onevo_app login, because sa can drop the " +
                "security policies. Use ConnectionStrings:MigrationConnection for schema migrations.");
        }
    }

    public static bool UsesAdminLogin(string connectionString)
    {
        try
        {
            return string.Equals(new SqlConnectionStringBuilder(connectionString).UserID, "sa", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // Not a SQL Server connection string. Connecting reports that; a warning must not.
            return false;
        }
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
