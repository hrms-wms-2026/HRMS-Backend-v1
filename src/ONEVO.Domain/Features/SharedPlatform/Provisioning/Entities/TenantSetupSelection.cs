using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class TenantSetupSelection : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SetupOptionKey { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedById { get; set; }
}
