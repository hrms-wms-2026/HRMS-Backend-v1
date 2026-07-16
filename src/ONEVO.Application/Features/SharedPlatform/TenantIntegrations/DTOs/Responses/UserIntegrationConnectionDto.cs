namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;

public sealed record UserIntegrationConnectionDto(
    string IntegrationKey,
    string Status,
    string? ProviderUserId,
    string? ProviderUsername,
    string? ProviderEmail,
    DateTimeOffset? TokenExpiresAt,
    string[] ScopesGranted,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? LastSyncAt,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? DisconnectedAt);
