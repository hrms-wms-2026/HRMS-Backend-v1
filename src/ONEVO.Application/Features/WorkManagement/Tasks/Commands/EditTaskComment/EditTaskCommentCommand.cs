using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;

public sealed record EditTaskCommentCommand(
    Guid CommentId, string Content, IReadOnlyList<Guid> AttachmentFileIds
) : IRequest<Result<TaskCommentResponse>>;
