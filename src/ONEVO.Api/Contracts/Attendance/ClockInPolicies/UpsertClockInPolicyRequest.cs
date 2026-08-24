namespace ONEVO.Api.Contracts.Attendance.ClockInPolicies;

public record ClockInPolicyScopeRequest(
    string Type,
    IReadOnlyList<Guid>? DepartmentIds = null,
    IReadOnlyList<Guid>? PositionIds = null,
    IReadOnlyList<Guid>? EmployeeIds = null);

public record WorkAreaSourceRulesRequest(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired);

public record HybridWorkAreaRulesRequest(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired,
    bool LocationCheckRequired,
    string SourceRule);

public record FieldWorkAreaRulesRequest(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    string PhotoRequirement);

public record WorkAreaRulesRequest(
    WorkAreaSourceRulesRequest Onsite,
    WorkAreaSourceRulesRequest Remote,
    HybridWorkAreaRulesRequest Hybrid,
    FieldWorkAreaRulesRequest Field);

public record LateDeductionRuleRequest(
    int LateArrivalMinute,
    decimal Multiplier,
    Guid TimeOffTypeId);

public record UpsertClockInPolicyRequest(
    string Name,
    ClockInPolicyScopeRequest Scope,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool LocationVerificationRequired,
    int? AllowedRadiusMeters,
    WorkAreaRulesRequest WorkAreaRules,
    bool CorrectionRequiresApproval,
    string NotificationRecipientResolver,
    IReadOnlyList<LateDeductionRuleRequest>? LateDeductionRules,
    bool IsActive = true);
