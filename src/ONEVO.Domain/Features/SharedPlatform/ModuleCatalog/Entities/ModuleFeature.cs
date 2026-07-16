namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class ModuleFeature
{
    public string FeatureKey { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefaultIncluded { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ModuleCatalogItem Module { get; set; } = null!;
}
