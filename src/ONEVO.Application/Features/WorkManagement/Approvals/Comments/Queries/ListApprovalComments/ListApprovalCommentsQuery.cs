using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Queries.ListApprovalComments;

/// <summary>The thread of an approval request or module invitation, oldest first.</summary>
public sealed record ListApprovalCommentsQuery(string SubjectType, Guid SubjectId) : IRequest<Result<IReadOnlyList<ApprovalCommentResponse>>>;
