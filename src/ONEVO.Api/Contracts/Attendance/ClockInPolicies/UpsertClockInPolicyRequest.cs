namespace ONEVO.Api.Contracts.Attendance.ClockInPolicies;

public record ClockInPolicyScopeRequest(
    string Type,
    IReadOnlyList<Guid>? DepartmentIds = null,
    IReadOnlyList<Guid>? PositionIds = null,
    IReadOnlyList<Guid>? EmployeeIds = null);

public record LateDeductionRuleRequest(
    int LateArrivalMinute,
    decimal Multiplier,
    Guid TimeOffTypeId);

public record UpsertClockInPolicyRequest(
    string Name,
    ClockInPolicyScopeRequest Scope,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool CorrectionRequiresApproval,
    string NotificationRecipientResolver,
    IReadOnlyList<LateDeductionRuleRequest>? LateDeductionRules,
    bool IsActive = true);
