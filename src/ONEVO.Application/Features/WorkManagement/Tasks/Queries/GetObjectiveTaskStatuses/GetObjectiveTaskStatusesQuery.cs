using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;

public sealed record GetObjectiveTaskStatusesQuery(Guid ObjectiveId) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
