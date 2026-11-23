namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;

public sealed record GitHubTenantApprovalDto(
    bool IsEnabled,
    string Status,
    DateTimeOffset? EnabledAt,
    DateTimeOffset? DisabledAt);
