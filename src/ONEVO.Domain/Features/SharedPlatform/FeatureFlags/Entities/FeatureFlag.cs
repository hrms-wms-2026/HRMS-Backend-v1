namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class FeatureFlag
{
    public string Key { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool DefaultValue { get; set; }
    public int RolloutPercentage { get; set; }
    public string? ModuleKey { get; set; }
    public string? FeatureKey { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ModuleCatalogItem? Module { get; set; }
    public ModuleFeature? Feature { get; set; }
    public ICollection<FeatureFlagOverride> Overrides { get; set; } = new List<FeatureFlagOverride>();
}
