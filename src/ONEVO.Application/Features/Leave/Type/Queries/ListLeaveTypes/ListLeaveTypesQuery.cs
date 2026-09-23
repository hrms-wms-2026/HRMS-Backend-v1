using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;

public record ListLeaveTypesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<LeaveTypeResponse>>>;
