using ONEVO.Domain.Common;

// Namespace deliberately stops at the feature segment: a ".Department" segment would
// collide with the Department entity type and force using-aliases everywhere (same
// convention as LegalEntity/Position).
namespace ONEVO.Domain.Features.OrgStructure.Entities;

public class Department : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public Guid? HeadPositionId { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
