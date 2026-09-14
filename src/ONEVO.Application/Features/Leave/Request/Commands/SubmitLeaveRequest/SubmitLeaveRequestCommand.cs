using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Commands.SubmitLeaveRequest;

public sealed record SubmitLeaveRequestCommand(
    Guid? EmployeeId,
    Guid LeaveTypeId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    IReadOnlyList<Guid> FileRecordIds,
    bool IsOnBehalfRequest) : IRequest<Result<LeaveRequestResponse>>;
