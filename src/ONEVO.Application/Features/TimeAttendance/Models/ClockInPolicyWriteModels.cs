namespace ONEVO.Application.Features.TimeAttendance.Models;

public record ClockInPolicyScopeInput(
    string Type,
    IReadOnlyList<Guid>? DepartmentIds,
    IReadOnlyList<Guid>? PositionIds,
    IReadOnlyList<Guid>? EmployeeIds);

public record LateDeductionRuleInput(
    int LateArrivalMinute,
    decimal Multiplier,
    Guid TimeOffTypeId);
