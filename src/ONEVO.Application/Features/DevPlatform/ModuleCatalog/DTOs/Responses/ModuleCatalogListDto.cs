namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;

public record ModuleCatalogListDto(
    string ModuleKey,
    string Name,
    string Pillar,
    string Phase,
    string PricingUnit,
    bool IsActive);
