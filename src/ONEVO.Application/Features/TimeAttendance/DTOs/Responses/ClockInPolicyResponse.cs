namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public record ClockInPolicyScopeResponse(
    string Type,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<Guid> PositionIds,
    IReadOnlyList<Guid> EmployeeIds);

public record WorkAreaSourceRulesResponse(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired);

public record HybridWorkAreaRulesResponse(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired,
    bool LocationCheckRequired,
    string SourceRule);

public record FieldWorkAreaRulesResponse(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    string PhotoRequirement);

public record WorkAreaRulesResponse(
    WorkAreaSourceRulesResponse Onsite,
    WorkAreaSourceRulesResponse Remote,
    HybridWorkAreaRulesResponse Hybrid,
    FieldWorkAreaRulesResponse Field);

public record LateDeductionRuleResponse(
    Guid Id,
    int LateArrivalMinute,
    decimal Multiplier,
    Guid TimeOffTypeId,
    bool IsActive);

public record ClockInPolicyResponse(
    Guid Id,
    Guid LegalEntityId,
    string Name,
    ClockInPolicyScopeResponse Scope,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool LocationVerificationRequired,
    int? AllowedRadiusMeters,
    WorkAreaRulesResponse WorkAreaRules,
    bool CorrectionRequiresApproval,
    string NotificationRecipientResolver,
    IReadOnlyList<LateDeductionRuleResponse> LateDeductionRules,
    bool IsActive,
    Guid CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ClockInPolicyListItemResponse(
    Guid Id,
    Guid LegalEntityId,
    string Name,
    string ScopeType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    int LateDeductionRuleCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
