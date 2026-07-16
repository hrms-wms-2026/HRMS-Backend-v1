using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;

public static class UserIntegrationConnectionMapper
{
    public static UserIntegrationConnectionDto ToSafeDto(UserIntegrationConnection connection)
    {
        return new UserIntegrationConnectionDto(
            connection.IntegrationKey,
            connection.Status,
            connection.ProviderUserId,
            connection.ProviderUsername,
            connection.ProviderEmail,
            connection.TokenExpiresAt,
            connection.ScopesGranted ?? [],
            connection.LastUsedAt,
            connection.LastSyncAt,
            connection.ConnectedAt,
            connection.DisconnectedAt);
    }

    public static UserIntegrationConnectionDto Disconnected(string integrationKey)
    {
        return new UserIntegrationConnectionDto(
            integrationKey,
            "disconnected",
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null);
    }
}
