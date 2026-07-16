namespace ONEVO.Domain.Features.InfrastructureModule.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string IndustryProfile { get; set; } = "office_it";
    public string CompanySizeRange { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Provisioning;
    public Guid? SubscriptionPlanId { get; set; }
    public string? SettingsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
