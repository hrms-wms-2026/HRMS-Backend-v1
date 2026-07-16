namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;

public record ModuleCatalogDetailDto(
    string ModuleKey,
    string Name,
    string Pillar,
    string Phase,
    string PricingUnit,
    string PricingReference,
    string StorageReference,
    string AiTokenReference,
    bool IsAiEnabled,
    bool IsStorageConsuming,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
