using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Queries.GetLeaveType;

public record GetLeaveTypeQuery(Guid LeaveTypeId) : IRequest<Result<LeaveTypeResponse>>;
