namespace ONEVO.Api.Contracts.Leave.Requests;

public sealed record CancelLeaveRequestRequest(
    string? Reason,
    DateOnly? EffectiveDate,
    string? ExpectedVersion);
