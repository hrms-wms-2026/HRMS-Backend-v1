namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;

public record ModuleFeatureDto(
    string FeatureKey,
    string Name,
    string? Description,
    bool IsDefaultIncluded,
    bool IsActive);
