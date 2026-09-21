using ONEVO.Domain.Common;

// Namespace deliberately stops at the feature segment: a ".ClockInPolicy" segment would
// collide with the ClockInPolicy entity type (same convention as Department/LegalEntity).
namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class ClockInPolicy : ITenantOwnedEntity
{
    public const string ScopeFullCompany = "full_company";
    public const string ScopeDepartment = "department";
    public const string ScopePosition = "position";
    public const string ScopeEmployee = "employee";

    public const string NotificationManagementCoverageOwner = "management_coverage_owner";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ScopeType { get; set; } = ScopeFullCompany;
    public Guid[]? DepartmentIds { get; set; }
    public Guid[]? PositionIds { get; set; }
    public Guid[]? EmployeeIds { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public bool CorrectionRequiresApproval { get; set; }
    public string NotificationRecipientResolver { get; set; } = NotificationManagementCoverageOwner;
    public bool IsActive { get; set; } = true;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ClockInLateDeductionRule> LateDeductionRules { get; set; }
        = new List<ClockInLateDeductionRule>();
}
