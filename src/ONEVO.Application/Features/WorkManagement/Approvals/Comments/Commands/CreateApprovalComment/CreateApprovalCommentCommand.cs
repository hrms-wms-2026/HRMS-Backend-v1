using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.CreateApprovalComment;

/// <summary>A top-level comment on an approval request ("approval") or a module invitation ("invitation").</summary>
public sealed record CreateApprovalCommentCommand(string SubjectType, Guid SubjectId, string Content)
    : IRequest<Result<ApprovalCommentResponse>>;
