using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class ClockInPolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>full_company | department | position | employee</summary>
    public string ScopeType { get; set; } = "full_company";

    public Guid[]? DepartmentIds { get; set; }
    public Guid[]? PositionIds { get; set; }
    public Guid[]? EmployeeIds { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool LocationVerificationRequired { get; set; }
    public int? AllowedRadiusMeters { get; set; }
    public bool OnsiteBiometricEnabled { get; set; }
    public bool OnsiteWebEnabled { get; set; }
    public bool OnsiteTrayEnabled { get; set; } = true;
    public bool OnsitePhotoRequired { get; set; }
    public bool RemoteBiometricEnabled { get; set; }
    public bool RemoteWebEnabled { get; set; }
    public bool RemoteTrayEnabled { get; set; } = true;
    public bool RemotePhotoRequired { get; set; }
    public bool EitherBiometricEnabled { get; set; }
    public bool EitherWebEnabled { get; set; }
    public bool EitherTrayEnabled { get; set; } = true;
    public bool EitherPhotoRequired { get; set; }
    public bool EitherLocationCheckRequired { get; set; }

    /// <summary>onsite | remote | employee_choice</summary>
    public string EitherSourceRule { get; set; } = "employee_choice";

    public bool FieldBiometricEnabled { get; set; }
    public bool FieldWebEnabled { get; set; }
    public bool FieldTrayEnabled { get; set; } = true;

    /// <summary>off | optional | required</summary>
    public string FieldPhotoRequirement { get; set; } = "off";

    public bool CorrectionRequiresApproval { get; set; } = true;
    public string NotificationRecipientResolver { get; set; } = "management_coverage_owner";
    public bool IsActive { get; set; } = true;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
