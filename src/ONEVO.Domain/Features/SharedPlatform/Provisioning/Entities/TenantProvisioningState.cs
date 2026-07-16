using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class TenantProvisioningState : ITenantOwnedEntity
{
    public Guid TenantId { get; set; }
    public string CurrentStep { get; set; } = "tenant_details";
    public DateTimeOffset? TenantDetailsCompletedAt { get; set; }
    public DateTimeOffset? SubscriptionCompletedAt { get; set; }
    public DateTimeOffset? ModulesCompletedAt { get; set; }
    public DateTimeOffset? RolesCompletedAt { get; set; }
    public DateTimeOffset? SettingsCompletedAt { get; set; }
    public DateTimeOffset? OwnerInviteCompletedAt { get; set; }
    public bool ActivationReady { get; set; } = false;
    public DateTimeOffset? ActivatedAt { get; set; }
    public Guid? LastUpdatedById { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
