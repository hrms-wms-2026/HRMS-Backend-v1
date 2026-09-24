using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>Lightweight approval record for sensitive position-based access.</summary>
public class AccessGrantRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Null until the underlying employee/user are actually created. Onboarding
    /// finalization defers employee/user creation until this request is approved, so a
    /// pending request is correlated by <see cref="OnboardingDraftId"/> instead.</summary>
    public Guid? EmployeeId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OnboardingDraftId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public Guid TargetPositionId { get; set; }
    public Guid TargetDepartmentId { get; set; }
    public Guid PositionAccessTemplateId { get; set; }
    public Guid RequestedRoleId { get; set; }
    public string ApprovalStatus { get; set; } = "Pending";
    public Guid RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public Guid? ReservedPositionAssignmentId { get; set; }
    public string? ChangeReason { get; set; }
    public string? DecisionNote { get; set; }
}

public static class AccessGrantActionType
{
    // ActionType is character varying(30); this is 26 characters.
    public const string EmployeeOnboarding = "onboarding_position_access";
    // 22 characters.
    public const string PositionChange = "position_change_access";
}
