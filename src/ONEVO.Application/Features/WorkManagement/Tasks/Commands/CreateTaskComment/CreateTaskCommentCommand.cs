using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;

/// <summary>
/// Posts a top-level comment (TaskId set, ParentCommentId null) or a reply
/// (ParentCommentId set, TaskId null — the handler resolves the task from the
/// parent). Exactly one of TaskId/ParentCommentId must be non-null; the
/// controller enforces this by construction (two distinct routes).
/// </summary>
public sealed record CreateTaskCommentCommand(
    Guid? TaskId, Guid? ParentCommentId, string Content, IReadOnlyList<Guid> AttachmentFileIds
) : IRequest<Result<TaskCommentResponse>>;
