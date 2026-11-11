using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskById;

/// <param name="ForEmployeeId">The principal, for their active planning delegate: read as the principal, only their assigned
/// task or its parent (delegate task planning, 2026-11-11). Null means the caller.</param>
public sealed record GetTaskByIdQuery(Guid TaskId, Guid? ForEmployeeId = null) : IRequest<Result<WorkTaskResponse>>;
