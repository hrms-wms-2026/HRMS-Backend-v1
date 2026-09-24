namespace ONEVO.Api.Contracts.Leave.Requests;

public sealed record SubmitLeaveRequestRequest(
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);

public sealed record SubmitLeaveRequestOnBehalfRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);
