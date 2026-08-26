using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;

public record ListBalanceAuditQuery(
    Guid? EmployeeId,
    Guid? LeaveTypeId,
    string? ChangeType,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page,
    int PageSize) : IRequest<Result<IReadOnlyList<LeaveBalanceAuditResponse>>>;
