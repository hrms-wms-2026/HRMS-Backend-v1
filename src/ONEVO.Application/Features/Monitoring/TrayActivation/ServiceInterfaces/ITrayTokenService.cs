namespace ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

public interface ITrayTokenService
{
    string GenerateAccessToken(Guid deviceRegistrationId, Guid userId, Guid tenantId);
    string GenerateRawRefreshToken();
    string HashToken(string rawToken);
}
