namespace ONEVO.Application.Features.TimeAttendance.Models;

public record ClockInPolicyScopeInput(
    string Type,
    IReadOnlyList<Guid>? DepartmentIds,
    IReadOnlyList<Guid>? PositionIds,
    IReadOnlyList<Guid>? EmployeeIds);

public record WorkAreaSourceRulesInput(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired);

public record HybridWorkAreaRulesInput(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired,
    bool LocationCheckRequired,
    string SourceRule);

public record FieldWorkAreaRulesInput(
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    string PhotoRequirement);

public record WorkAreaRulesInput(
    WorkAreaSourceRulesInput Onsite,
    WorkAreaSourceRulesInput Remote,
    HybridWorkAreaRulesInput Hybrid,
    FieldWorkAreaRulesInput Field);

public record LateDeductionRuleInput(
    int LateArrivalMinute,
    decimal Multiplier,
    Guid TimeOffTypeId);
