using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskById;

public sealed record GetTaskByIdQuery(Guid TaskId) : IRequest<Result<WorkTaskResponse>>;
