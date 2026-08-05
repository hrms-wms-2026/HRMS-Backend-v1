using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.OrgStructure.Entities;

public class Position : BaseEntity, ITenantOwnedEntity
{
    public const string TypeUnique = "unique";
    public const string TypePooled = "pooled";

    // Transitional nullable fields for migration safety on existing databases with legacy rows
    public Guid? LegalEntityId { get; set; }
    public Guid? DepartmentId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string PositionType { get; set; } = TypeUnique;
    public int MaxOccupancy { get; set; } = 1;
    public Guid? ReportsToPositionId { get; set; }
    public bool IsActive { get; set; } = true;

    // Preserved legacy fields
    public Guid? DefaultRoleId { get; set; }
}
