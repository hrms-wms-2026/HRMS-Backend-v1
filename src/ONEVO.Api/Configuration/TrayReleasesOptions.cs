using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Api.Configuration;

public sealed class TrayReleasesOptions
{
    public const string SectionName = "TrayReleases";

    /// <summary>Shared secret the release pipeline sends in X-Release-Token. Empty = ingest disabled.</summary>
    public string IngestToken { get; set; } = string.Empty;
}

public static class IngestTokenValidator
{
    public static bool IsValid(string? configured, string? presented)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrEmpty(presented))
            return false;

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
