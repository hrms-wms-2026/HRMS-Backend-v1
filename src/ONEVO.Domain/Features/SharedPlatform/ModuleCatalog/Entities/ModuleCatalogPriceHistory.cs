namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class ModuleCatalogPriceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ModuleKey { get; set; } = string.Empty;
    public string? OldPricingReference { get; set; }
    public string? NewPricingReference { get; set; }
    public string? OldStorageReference { get; set; }
    public string? NewStorageReference { get; set; }
    public string? OldAiTokenReference { get; set; }
    public string? NewAiTokenReference { get; set; }
    public string? OldPricingUnit { get; set; }
    public string? NewPricingUnit { get; set; }
    public Guid ChangedById { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public ModuleCatalogItem Module { get; set; } = null!;
}
