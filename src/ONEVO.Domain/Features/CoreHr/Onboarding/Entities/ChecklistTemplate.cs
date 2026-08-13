using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>Tenant-owned reusable onboarding or offboarding task definition.</summary>
public class ChecklistTemplate : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string TasksJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
}
