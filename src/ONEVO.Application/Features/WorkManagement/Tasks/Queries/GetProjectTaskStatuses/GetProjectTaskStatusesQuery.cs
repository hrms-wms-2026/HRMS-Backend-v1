using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatuses;

public sealed record GetProjectTaskStatusesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
