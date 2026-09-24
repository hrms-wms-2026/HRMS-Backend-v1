namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class BillingAuditLog
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? ActorAdminUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
