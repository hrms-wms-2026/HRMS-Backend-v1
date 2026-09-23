using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;

public sealed record GetSubtasksQuery(Guid ParentTaskId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
