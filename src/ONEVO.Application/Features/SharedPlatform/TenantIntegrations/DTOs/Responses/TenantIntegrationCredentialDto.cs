namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;

public sealed record TenantIntegrationCredentialDto(
    Guid Id,
    Guid TenantId,
    string IntegrationKey,
    string Status,
    string[] ScopesGranted,
    string? ExternalAccountId,
    string? ExternalAccountName,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? LastSyncAt,
    DateTimeOffset ConnectedAt,
    Guid ConnectedByUserId,
    DateTimeOffset? DisconnectedAt,
    string? ErrorMessage,
    bool HasAccessToken,
    bool HasRefreshToken);
