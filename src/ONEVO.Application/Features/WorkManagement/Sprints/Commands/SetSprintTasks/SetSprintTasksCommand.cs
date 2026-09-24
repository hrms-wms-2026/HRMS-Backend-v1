using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;

public sealed record SetSprintTasksCommand(Guid SprintId, IReadOnlyList<Guid> AddTaskIds, IReadOnlyList<Guid> RemoveTaskIds) : IRequest<Result<SprintResponse>>;
