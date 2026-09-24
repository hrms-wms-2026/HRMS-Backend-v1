namespace ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;

public sealed record LeaveCalendarMonthResponse(
    int Year,
    int Month,
    DateOnly MonthStart,
    DateOnly MonthEnd,
    bool IncludesTentativeBlocks,
    bool IsEmpty,
    IReadOnlyList<LeaveCalendarDayResponse> Days);

public sealed record LeaveCalendarDayResponse(
    DateOnly Date,
    string DayOfWeek,
    IReadOnlyList<LeaveCalendarAbsenceResponse> Absences,
    IReadOnlyList<LeaveCalendarHolidayResponse> Holidays);

public sealed record LeaveCalendarAbsenceResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    string LeaveTypeCategory,
    string? TypeColorHex,
    string Status,
    bool IsTentative,
    bool IsPartialCancellationHistory,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal TotalHours);

public sealed record LeaveCalendarHolidayResponse(
    DateOnly Date,
    string Name,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string Source);
