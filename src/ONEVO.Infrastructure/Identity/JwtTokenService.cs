using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity;

/// <summary>
/// Implementation of IJwtTokenService for device/agent APIs.
/// Browser authentication uses opaque server-side state instead of JWTs.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly string? _secret;
    private readonly string _tenantIssuer;
    private readonly string _tenantAudience;

    public JwtTokenService(IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        _secret = jwt["Secret"];
        _tenantIssuer = jwt["TenantIssuer"] ?? "onevo-api";
        _tenantAudience = jwt["TenantAudience"] ?? "onevo-api";
    }

    private string GetRequiredSecret()
    {
        if (string.IsNullOrWhiteSpace(_secret))
            throw new InvalidOperationException("Jwt:Secret is required to generate or validate JWTs.");
        return _secret;
    }

    public string GenerateDeviceToken(Guid deviceId, Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("type", "agent")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetRequiredSecret()));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _tenantIssuer,
            audience: "onevo-agent",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}
