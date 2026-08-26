using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Queries.PreviewSubmitLeaveRequest;

public sealed record PreviewSubmitLeaveRequestQuery(
    Guid? EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    string? Reason,
    IReadOnlyList<Guid> FileRecordIds,
    bool IsOnBehalfRequest) : IRequest<Result<LeaveRequestResponse>>;
