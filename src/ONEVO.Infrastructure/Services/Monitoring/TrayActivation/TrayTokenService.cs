using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

public class TrayTokenService : ITrayTokenService
{
    private const int AccessTokenExpiryHours = 1;

    private readonly ITrayJwtKeyProvider _keys;
    private readonly string _issuer;
    private readonly string _audience;

    public TrayTokenService(ITrayJwtKeyProvider keys, IConfiguration configuration)
    {
        var section = configuration.GetSection("TrayApp:Jwt");
        _keys = keys;
        _issuer = section["Issuer"] ?? "crownus-tray";
        _audience = section["Audience"] ?? "crownus-tray-app";
    }

    public string GenerateAccessToken(
        Guid deviceRegistrationId, Guid userId, Guid tenantId, Guid? legalEntityId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceRegistrationId.ToString()),
            new("user_id", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("token_type", "tray_device")
        };

        if (legalEntityId.HasValue)
            claims.Add(new Claim("legal_entity_id", legalEntityId.Value.ToString()));

        // ES256 with the active key; its KeyId is written to the header as "kid" (SEC-15).
        // The claims and the 1-hour lifetime are unchanged: the tray app reads them.
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(AccessTokenExpiryHours),
            signingCredentials: _keys.SigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateOpaqueToken(int byteLength)
    {
        if (byteLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteLength));
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
