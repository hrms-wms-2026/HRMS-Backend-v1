using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Computes the canonical content_hash: SHA-256 over the trimmed content_html, lowercase hex.
/// The frontend never supplies this value - it is always recomputed server-side.
/// </summary>
public static class LegalContentHasher
{
    public static string ComputeHash(string html)
    {
        var normalized = html.Trim();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
