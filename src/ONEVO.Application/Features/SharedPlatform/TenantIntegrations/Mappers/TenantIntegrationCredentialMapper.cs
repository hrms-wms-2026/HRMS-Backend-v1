using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;

public static class TenantIntegrationCredentialMapper
{
    public static TenantIntegrationCredentialDto ToSafeDto(TenantIntegrationCredential value)
    {
        return new TenantIntegrationCredentialDto(
            value.Id, value.TenantId, value.IntegrationKey, value.Status,
            value.ScopesGranted, value.ExternalAccountId, value.ExternalAccountName,
            value.TokenExpiresAt, value.LastSyncAt, value.ConnectedAt,
            value.ConnectedByUserId, value.DisconnectedAt, value.ErrorMessage,
            !string.IsNullOrEmpty(value.AccessTokenEncrypted),
            !string.IsNullOrEmpty(value.RefreshTokenEncrypted));
    }
}
