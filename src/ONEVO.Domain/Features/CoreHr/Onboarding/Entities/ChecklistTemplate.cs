using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>Tenant-owned reusable onboarding or offboarding task definition.</summary>
public class ChecklistTemplate : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;

    // Transitional nullable fields (same convention as Position.LegalEntityId/DepartmentId):
    // every row created through the checklist-template API always sets LegalEntityId; the
    // column stays nullable at the schema level only so an ALTER TABLE ADD COLUMN on an
    // existing table never fails on pre-existing rows.
    public Guid? LegalEntityId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? DepartmentId { get; set; }

    public string TasksJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
}
