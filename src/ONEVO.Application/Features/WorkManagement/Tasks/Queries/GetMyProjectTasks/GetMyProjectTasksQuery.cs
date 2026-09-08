using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyProjectTasks;

public sealed record GetMyProjectTasksQuery(Guid ProjectId, Guid? SprintId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
