namespace ONEVO.Application.Features.Leave.Approval.DTOs.Responses;

public sealed record LeaveApprovalDelegateResponse(
    Guid Id,
    Guid DelegateEmployeeId,
    string DelegateName,
    DateOnly StartDate,
    DateOnly EndDate);
