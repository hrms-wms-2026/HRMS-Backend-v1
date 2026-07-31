using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

public class TrayTokenService : ITrayTokenService
{
    private const int AccessTokenExpiryHours = 1;

    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;

    public TrayTokenService(IConfiguration configuration)
    {
        var section = configuration.GetSection("TrayApp:Jwt");
        _secret = section["Secret"]
            ?? throw new InvalidOperationException("TrayApp:Jwt:Secret is required.");
        _issuer = section["Issuer"] ?? "onevo-tray";
        _audience = section["Audience"] ?? "onevo-tray-app";
    }

    public string GenerateAccessToken(Guid deviceRegistrationId, Guid userId, Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceRegistrationId.ToString()),
            new("user_id", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("token_type", "tray_device")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(AccessTokenExpiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRawRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
