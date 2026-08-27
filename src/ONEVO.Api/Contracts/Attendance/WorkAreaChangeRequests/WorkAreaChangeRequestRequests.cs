namespace ONEVO.Api.Contracts.Attendance.WorkAreaChangeRequests;

public sealed record WorkAreaChangeRequestRequest(
    DateOnly Date,
    string RequestedWorkArea,
    string Reason);

public sealed record ReviewWorkAreaChangeRequestRequest(string? ReviewComment);
