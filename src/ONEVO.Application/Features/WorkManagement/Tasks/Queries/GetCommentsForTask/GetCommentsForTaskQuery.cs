using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;

public sealed record GetCommentsForTaskQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<TaskCommentResponse>>>;
