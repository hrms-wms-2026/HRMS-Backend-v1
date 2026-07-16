namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;

/// <summary>
/// Allowlist of supported platform service key slugs.
/// Phase 1 supports only ONEVO-owned Resend/SendGrid (transactional email) and
/// Cloudflare/Cloudflare R2 (DNS/WAF, object storage) credentials.
/// </summary>
public static class PlatformServiceKeyCatalog
{
    public const string Resend = "resend";
    public const string Sendgrid = "sendgrid";
    public const string Cloudflare = "cloudflare";
    public const string CloudflareR2 = "cloudflare_r2";

    public static readonly IReadOnlyList<string> SupportedServiceKeys =
        new[] { Resend, Sendgrid, Cloudflare, CloudflareR2 };

    public static bool IsSupported(string serviceKey)
    {
        foreach (var supported in SupportedServiceKeys)
        {
            if (string.Equals(supported, serviceKey, StringComparison.Ordinal))
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
