namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Requests;

public record CreatePlatformFeatureFlagRequest(string Key, string Name, string? Description, bool IsEnabled);
