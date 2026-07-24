using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.ActivityMonitoring.Entities;

public class ApplicationCategory : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ApplicationNamePattern { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool? IsProductive { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
