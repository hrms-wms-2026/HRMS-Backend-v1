namespace ONEVO.Api.Contracts.Leave.Requests;

public sealed record SubmitLeaveRequestRequest(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);

public sealed record SubmitLeaveRequestOnBehalfRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);
