using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.InfrastructureModule.Entities;

public class TenantStatusHistory : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public TenantStatus FromStatus { get; set; }
    public TenantStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public Guid? ChangedById { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
