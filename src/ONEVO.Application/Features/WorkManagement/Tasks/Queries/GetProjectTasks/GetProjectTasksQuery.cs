using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTasks;

public sealed record GetProjectTasksQuery(
    Guid ProjectId,
    IReadOnlyList<Guid>? AssigneeEmployeeIds = null) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
