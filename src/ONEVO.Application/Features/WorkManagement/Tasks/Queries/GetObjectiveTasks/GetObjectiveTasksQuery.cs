using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;

public sealed record GetObjectiveTasksQuery(Guid ObjectiveId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
