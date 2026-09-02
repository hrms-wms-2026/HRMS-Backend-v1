namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

public interface ITrayTokenService
{
    string GenerateAccessToken(Guid deviceRegistrationId, Guid userId, Guid tenantId, Guid? legalEntityId);
    string GenerateOpaqueToken(int byteLength);
    string GenerateRawRefreshToken();
    string HashToken(string rawToken);
}
