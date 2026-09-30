using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>One active violation. Rank (1 = most important) is the only ordering; Severity is
/// critical | warning | info and drives colour only. Unit is days | count | minutes.</summary>
public sealed record EmployeeSignal(
    string Key,
    int Rank,
    string Severity,
    string Category,
    int Value,
    string Unit,
    int? Denominator,
    string Label,
    string Detail);

public sealed record EmployeeOverviewSignalsResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeSignal> Signals);

/// <summary>Exceptions is null when the viewer may not see this employee's exception cases.</summary>
public sealed record EmployeeMonitoringSignalCounts(int IdleAlerts, int? Exceptions);

/// <summary>Per-source inputs for <see cref="Helpers.EmployeeSignalCatalogue"/>. A null source
/// (module off, not visible to the viewer, or failed) omits its signals.</summary>
public sealed record EmployeeSignalInputs(
    EmployeeAttendanceOverviewResponse? Attendance,
    EmployeeAttendanceDisciplineResponse? Discipline,
    EmployeeWorkOverviewResponse? Work,
    EmployeeApprovalActivityResponse? Approvals,
    EmployeeMonitoringSignalCounts? Monitoring);
