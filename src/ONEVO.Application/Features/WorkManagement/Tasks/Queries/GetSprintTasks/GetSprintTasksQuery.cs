using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSprintTasks;

public sealed record GetSprintTasksQuery(Guid SprintId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
