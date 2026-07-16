namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class ModuleCatalogItem
{
    public string ModuleKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Pillar { get; set; } = string.Empty;
    public string Phase { get; set; } = "phase_1";
    public string PricingUnit { get; set; } = "per_employee";
    public string PricingReference { get; set; } = "[]";
    public string StorageReference { get; set; } = "[]";
    public string AiTokenReference { get; set; } = "[]";
    public bool IsAiEnabled { get; set; }
    public bool IsStorageConsuming { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ModuleFeature> Features { get; set; } = new List<ModuleFeature>();
    public ICollection<ModulePermissionOwnership> PermissionOwnerships { get; set; } = new List<ModulePermissionOwnership>();
    public ICollection<ModuleCatalogPriceHistory> PriceHistories { get; set; } = new List<ModuleCatalogPriceHistory>();
}
