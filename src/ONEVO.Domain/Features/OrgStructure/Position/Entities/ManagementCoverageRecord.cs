using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.OrgStructure.Entities;

public class ManagementCoverageRecord : ITenantOwnedEntity
{
    public const string TargetPosition = "Position";
    public const string TargetDepartment = "Department";
    public const string TargetCompany = "Company";

    public const string SourceReportingStructure = "ReportingStructure";
    public const string SourceManual = "Manual";

    public const string StatusActive = "active";
    public const string StatusInactive = "inactive";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid OwnerPositionId { get; set; }
    public string CoveredTargetType { get; set; } = TargetPosition;
    public Guid? CoveredPositionId { get; set; }
    public Guid? CoveredDepartmentId { get; set; }
    public int OwnerOrder { get; set; } = 1;
    public string Source { get; set; } = SourceReportingStructure;
    public bool IsLocked { get; set; } = true;
    public string Status { get; set; } = StatusActive;
    public Guid? ResponsibleEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
