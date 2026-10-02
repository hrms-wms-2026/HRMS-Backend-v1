using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.EditApprovalComment;

/// <summary>Only the author may edit; the comment is flagged as edited.</summary>
public sealed record EditApprovalCommentCommand(Guid CommentId, string Content) : IRequest<Result<ApprovalCommentResponse>>;
