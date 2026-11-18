using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

/// <summary>The tray device-token keys loaded from configuration (SEC-15).</summary>
public sealed class TrayJwtKeySet
{
    /// <summary>Signs new access tokens. Its <c>KeyId</c> is written to the token header as <c>kid</c>.</summary>
    public required ECDsaSecurityKey ActiveKey { get; init; }

    /// <summary>Every key a token may name by <c>kid</c>, the active key included.</summary>
    public required IReadOnlyDictionary<string, ECDsaSecurityKey> ValidationKeys { get; init; }

    /// <summary>The pre-SEC-15 HS256 secret, set only while the legacy window is configured.</summary>
    public SymmetricSecurityKey? LegacyKey { get; init; }

    /// <summary>HS256 tokens are accepted until (not at) this instant.</summary>
    public DateTimeOffset? LegacyUntil { get; init; }

    /// <summary>True when Development/Test generated a throwaway key because none was configured.</summary>
    public bool IsEphemeral { get; init; }
}

/// <summary>
/// Reads and checks the tray device-token signing keys. Used by the startup check and by
/// <see cref="TrayJwtKeyProvider"/>, so both apply the same rules.
/// <code>
/// TrayApp:Jwt:SigningKeys:0:Kid            = "tray-2026-11"
/// TrayApp:Jwt:SigningKeys:0:PrivateKeyPem  = &lt;PKCS#8 or SEC1 PEM, ECDSA P-256, from the secret store&gt;
/// TrayApp:Jwt:ActiveKid                    = "tray-2026-11"
/// TrayApp:Jwt:AcceptLegacyHs256Until       = "2026-11-20T12:00:00Z"   (optional cut-over window)
/// </code>
/// No error message contains key material.
/// </summary>
public static class TrayJwtKeyLoader
{
    public const string SigningKeysSection = "TrayApp:Jwt:SigningKeys";
    public const string ActiveKidKey = "TrayApp:Jwt:ActiveKid";
    public const string LegacyUntilKey = "TrayApp:Jwt:AcceptLegacyHs256Until";
    public const string EphemeralKidPrefix = "dev-ephemeral-";

    private const string P256Oid = "1.2.840.10045.3.1.7";

    // An explicit instant: date, time and a Z or ±hh:mm offset.
    private static readonly Regex ExplicitInstant =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant);

    public static TrayJwtKeySet Load(IConfiguration configuration, string environmentName)
    {
        var local = ConfigurationStartupValidator.IsLocalValidationEnvironment(environmentName);
        var entries = configuration.GetSection(SigningKeysSection).GetChildren().ToList();

        var keys = new Dictionary<string, ECDsaSecurityKey>(StringComparer.Ordinal);
        string activeKid;
        var ephemeral = false;

        if (entries.Count == 0)
        {
            if (!local)
            {
                throw new InvalidOperationException(
                    "No tray device-token signing key is configured. Set TrayApp__Jwt__SigningKeys__0__Kid, " +
                    "TrayApp__Jwt__SigningKeys__0__PrivateKeyPem and TrayApp__Jwt__ActiveKid from the secret store " +
                    "(see docs/deployment/tray-device-jwt.md).");
            }

            activeKid = EphemeralKidPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            keys[activeKid] = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = activeKid };
            ephemeral = true;
        }
        else
        {
            foreach (var entry in entries)
            {
                var kid = entry["Kid"];
                if (string.IsNullOrWhiteSpace(kid))
                {
                    throw new InvalidOperationException(
                        $"{SigningKeysSection}:{entry.Key}:Kid is required: every tray signing key needs a key id.");
                }

                if (keys.ContainsKey(kid))
                {
                    throw new InvalidOperationException(
                        $"Tray signing key id '{kid}' is listed more than once under {SigningKeysSection}.");
                }

                keys[kid] = ParsePrivateKey(kid, entry["PrivateKeyPem"]);
            }

            var configuredActive = configuration[ActiveKidKey];
            if (string.IsNullOrWhiteSpace(configuredActive) || !keys.ContainsKey(configuredActive))
            {
                throw new InvalidOperationException(
                    $"{ActiveKidKey} must name one of the keys listed under {SigningKeysSection} " +
                    "(set TrayApp__Jwt__ActiveKid).");
            }

            activeKid = configuredActive;
        }

        var (legacyKey, legacyUntil) = LoadLegacyWindow(configuration, local);

        return new TrayJwtKeySet
        {
            ActiveKey = keys[activeKid],
            ValidationKeys = keys,
            LegacyKey = legacyKey,
            LegacyUntil = legacyUntil,
            IsEphemeral = ephemeral
        };
    }

    private static ECDsaSecurityKey ParsePrivateKey(string kid, string? pem)
    {
        if (ConfigurationStartupValidator.IsMissingOrPlaceholder(pem))
        {
            throw new InvalidOperationException(
                $"Tray signing key '{kid}' has no private key. Set its PrivateKeyPem from the secret store.");
        }

        // Environment variables and some secret stores flatten newlines to a literal "\n".
        var normalized = pem!.Replace("\\n", "\n", StringComparison.Ordinal);

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            ecdsa.Dispose();
            // No inner exception: the parser's message may quote the input.
            throw new InvalidOperationException(
                $"Tray signing key '{kid}' is not a valid PEM-encoded EC private key.");
        }

        ECParameters parameters;
        try
        {
            parameters = ecdsa.ExportParameters(includePrivateParameters: true);
        }
        catch (CryptographicException)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"Tray signing key '{kid}' is a public key. The private key is required to sign tokens.");
        }

        var isP256 = parameters.Curve.IsNamed && parameters.Curve.Oid.Value == P256Oid;
        CryptographicOperations.ZeroMemory(parameters.D);
        if (!isP256)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException(
                $"Tray signing key '{kid}' must be an ECDSA P-256 key (tray tokens are pinned to ES256).");
        }

        return new ECDsaSecurityKey(ecdsa) { KeyId = kid };
    }

    private static (SymmetricSecurityKey? Key, DateTimeOffset? Until) LoadLegacyWindow(
        IConfiguration configuration, bool local)
    {
        var raw = configuration[LegacyUntilKey];
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        if (!ExplicitInstant.IsMatch(raw.Trim())
            || !DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
        {
            throw new InvalidOperationException(
                $"{LegacyUntilKey} must be a date and time with an offset, for example 2026-11-20T12:00:00Z.");
        }

        var secret = ConfigurationStartupValidator.RequireTrayJwtSecret(configuration);
        if (!local && ConfigurationStartupValidator.IsDevelopmentTraySecret(secret))
        {
            throw new InvalidOperationException(
                $"{ConfigurationStartupValidator.TrayJwtSecretKey} is a development placeholder, so the legacy " +
                "HS256 window cannot be used. Set TrayApp__Jwt__Secret to the secret the running API signs with.");
        }

        return (new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), until.ToUniversalTime());
    }
}
