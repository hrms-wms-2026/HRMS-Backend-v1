using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.ReplyApprovalComment;

/// <summary>A reply. Replying to a reply attaches to its top-level comment (one reply level).</summary>
public sealed record ReplyApprovalCommentCommand(Guid ParentCommentId, string Content) : IRequest<Result<ApprovalCommentResponse>>;
