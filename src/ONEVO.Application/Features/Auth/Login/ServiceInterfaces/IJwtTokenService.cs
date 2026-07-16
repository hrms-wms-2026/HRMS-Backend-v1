namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// Service for generating JWTs for device/agent APIs.
/// Browser authentication does not use this service.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Issues a 24-hour device JWT for a registered desktop agent.
    /// Contains sub=deviceId, tenant_id, type="agent". Used by Agent Gateway.</summary>
    string GenerateDeviceToken(Guid deviceId, Guid tenantId);
}
