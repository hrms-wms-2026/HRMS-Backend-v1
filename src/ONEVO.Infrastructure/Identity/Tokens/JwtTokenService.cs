using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.Tokens;

/// <summary>
/// Issues 90-day Device JWTs for enrolled desktop agents.
/// Uses Jwt:AgentSecret — independent of any user-session key.
/// Claims: sub=deviceId, tenant_id, type="agent", iss="onevo", aud="onevo-agent".
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly string _agentSecret;
    private readonly string _issuer;

    public JwtTokenService(IConfiguration configuration)
    {
        _agentSecret = configuration["Jwt:AgentSecret"]
            ?? throw new InvalidOperationException("Jwt:AgentSecret is required.");
        _issuer = configuration["Jwt:AgentIssuer"] ?? "onevo";
    }

    public string GenerateAgentToken(Guid deviceId, Guid tenantId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, deviceId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("type", "agent")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_agentSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: "onevo-agent",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(90),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
