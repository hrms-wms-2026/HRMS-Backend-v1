namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class FeatureFlagOverride
{
    public Guid Id { get; set; }
    public string FlagKey { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public bool Value { get; set; }
    public Guid GrantedById { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reason { get; set; }

    public FeatureFlag Flag { get; set; } = null!;
}
