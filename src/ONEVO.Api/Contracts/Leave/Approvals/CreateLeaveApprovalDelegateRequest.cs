namespace ONEVO.Api.Contracts.Leave.Approvals;

public sealed record CreateLeaveApprovalDelegateRequest(
    Guid DelegateEmployeeId,
    DateOnly StartDate,
    DateOnly EndDate);
