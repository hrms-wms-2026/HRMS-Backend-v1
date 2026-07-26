namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;

/// <summary>
/// Allowlist of supported platform service key slugs.
/// Provider-card identity and family are owned by platform_providers. These constants
/// remain only for provider-specific runtime adapters and verification rules.
/// </summary>
public static class PlatformServiceKeyCatalog
{
    public const string Resend = "resend";
    public const string Sendgrid = "sendgrid";
    public const string Cloudflare = "cloudflare";
    public const string CloudflareR2 = "cloudflare_r2";
    public const string AwsRekognition = "aws_rekognition";

    public static readonly IReadOnlyList<string> SupportedServiceKeys =
        new[] { Resend, Sendgrid, Cloudflare, CloudflareR2, AwsRekognition };

    /// <summary>
    /// Service keys that represent transactional email providers. This is a Phase 1
    /// classification bridge living in code, not a new database column:
    /// platform_service_keys has no provider_type/category column in the approved
    /// schema, so "which rows are email providers" is answered here.
    /// </summary>
    public static readonly IReadOnlyList<string> TransactionalEmailProviders =
        new[] { Sendgrid, Resend };

    public static bool IsSupported(string serviceKey)
    {
        foreach (var supported in SupportedServiceKeys)
        {
            if (string.Equals(supported, serviceKey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool IsTransactionalEmailProvider(string serviceKey)
    {
        foreach (var provider in TransactionalEmailProviders)
        {
            if (string.Equals(provider, serviceKey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Lowercase slug: starts with a letter; letters/digits/underscores only; max 50 chars.</summary>
    public static bool IsValidSlug(string serviceKey)
    {
        if (string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Length > 50)
            return false;

        if (serviceKey[0] < 'a' || serviceKey[0] > 'z')
            return false;

        foreach (var ch in serviceKey)
        {
            var ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_';
            if (!ok)
                return false;
        }
        return true;
    }
}
