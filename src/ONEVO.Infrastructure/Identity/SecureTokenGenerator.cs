using System.Security.Cryptography;
using System.Text;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity;

public class SecureTokenGenerator : ISecureTokenGenerator
{
    public string GenerateOpaqueToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string GenerateCsrfToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
