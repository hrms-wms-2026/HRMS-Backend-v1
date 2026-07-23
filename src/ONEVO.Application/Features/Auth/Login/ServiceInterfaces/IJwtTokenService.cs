namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// Issues Device JWTs for the desktop agent only.
/// Browser sessions use HttpOnly cookies — this service is never called for web auth.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a Device JWT per spec: sub=deviceId, tenant_id, type="agent", aud="onevo-agent".
    /// Signed with Jwt:AgentSecret — completely separate from user session signing.
    /// </summary>
    string GenerateAgentToken(Guid deviceId, Guid tenantId);
}
