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

    public const string HybridSourceOnsite = "onsite";
    public const string HybridSourceRemote = "remote";
    public const string HybridSourceEmployeeChoice = "employee_choice";

    public const string FieldPhotoOff = "off";
    public const string FieldPhotoOptional = "optional";
    public const string FieldPhotoRequired = "required";

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
    public bool LocationVerificationRequired { get; set; }
    public int? AllowedRadiusMeters { get; set; }

    public bool OnsiteBiometricEnabled { get; set; }
    public bool OnsiteWebEnabled { get; set; }
    public bool OnsiteTrayEnabled { get; set; }
    public bool OnsitePhotoRequired { get; set; }

    public bool RemoteBiometricEnabled { get; set; }
    public bool RemoteWebEnabled { get; set; }
    public bool RemoteTrayEnabled { get; set; }
    public bool RemotePhotoRequired { get; set; }

    // Persisted as either_* columns (inventory). API/UI expose this work area as "hybrid".
    public bool EitherBiometricEnabled { get; set; }
    public bool EitherWebEnabled { get; set; }
    public bool EitherTrayEnabled { get; set; }
    public bool EitherPhotoRequired { get; set; }
    public bool EitherLocationCheckRequired { get; set; }
    public string EitherSourceRule { get; set; } = HybridSourceEmployeeChoice;

    public bool FieldBiometricEnabled { get; set; }
    public bool FieldWebEnabled { get; set; }
    public bool FieldTrayEnabled { get; set; }
    public string FieldPhotoRequirement { get; set; } = FieldPhotoOff;

    public bool CorrectionRequiresApproval { get; set; }
    public string NotificationRecipientResolver { get; set; } = NotificationManagementCoverageOwner;
    public bool IsActive { get; set; } = true;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ClockInLateDeductionRule> LateDeductionRules { get; set; }
        = new List<ClockInLateDeductionRule>();
}
