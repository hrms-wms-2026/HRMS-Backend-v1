using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

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

    /// <summary>HS256 (the legacy tray token algorithm) needs a key of at least 256 bits.</summary>
    public const int MinimumTrayJwtSecretBytes = 32;

    /// <summary>
    /// The legacy HS256 tray secret, checked for presence and key size. Since SEC-15 it is read only
    /// while <c>TrayApp:Jwt:AcceptLegacyHs256Until</c> is set, to validate tokens issued before the
    /// switch to ES256. Messages never contain the value.
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
    /// SEC-15 startup check (replaces the SEC-01 HS256 secret check). Loads the tray device-token
    /// signing keys once before the host is built, so a missing, unparseable or ambiguous key set
    /// stops startup instead of surfacing on the first tray request. Outside Development/Test a key
    /// set is required; locally, with none configured, a throwaway key is used (see
    /// <see cref="TrayJwtKeyLoader"/>). Messages never contain key material.
    /// </summary>
    public static void ValidateTrayJwtSigningKeys(
        IConfiguration configuration,
        string environmentName)
    {
        TrayJwtKeyLoader.Load(configuration, environmentName);
    }

    /// <summary>A tray legacy secret may not be a development value outside Development/Test.</summary>
    internal static bool IsDevelopmentTraySecret(string secret) =>
        secret.StartsWith("dev-only", StringComparison.OrdinalIgnoreCase) || IsMissingOrPlaceholder(secret);

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
                "connection must use the restricted crownus_app login, because sa can drop the " +
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

    internal static bool IsMissingOrPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.Equals("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith('<') && value.EndsWith('>')) return true;
        foreach (var marker in PlaceholderMarkers)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    internal static bool IsLocalValidationEnvironment(string environmentName)
    {
        return environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);
    }
}
