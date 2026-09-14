namespace ONEVO.Api.Contracts.Attendance.WorkAreaChangeRequests;

public sealed record WorkAreaChangeRequestRequest(
    DateOnly Date,
    Guid RequestedWorkModeId,
    string Reason);

public sealed record ReviewWorkAreaChangeRequestRequest(string? ReviewComment);
