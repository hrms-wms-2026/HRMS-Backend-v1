using System.Security.Cryptography;
using System.Text;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.Tokens;

public class SecureTokenGenerator : ISecureTokenGenerator
{
    public string GenerateOpaqueToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string GenerateUrlSafeOpaqueToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string GenerateCsrfToken()
    {
        // Hex, not Base64: the token is issued as a cookie value and echoed back in the
        // X-CSRF-Token header. Base64's '+', '/', '=' get URL-encoded in Set-Cookie, so a
        // client using the cookie value verbatim would never match the stored hash.
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
