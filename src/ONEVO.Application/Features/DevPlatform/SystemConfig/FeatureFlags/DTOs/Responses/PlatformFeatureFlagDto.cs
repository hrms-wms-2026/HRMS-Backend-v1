namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Responses;

public record PlatformFeatureFlagDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
