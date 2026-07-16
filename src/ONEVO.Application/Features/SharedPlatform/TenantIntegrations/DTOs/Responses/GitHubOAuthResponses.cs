namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;

public sealed record GitHubOAuthStartResponse(string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record GitHubOAuthCompleteResponse(
    string IntegrationKey,
    string Status,
    string? ProviderUsername,
    string? ReturnUrl);
