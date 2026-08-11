using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>A checklist task instantiated for one employee.</summary>
public class EmployeeChecklistTask : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? TemplateId { get; set; }
    public string LifecycleType { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public int? Sequence { get; set; }
    public Guid AssignedToId { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? CompletedAt { get; set; }
}
