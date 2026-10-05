using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Queries.ListApprovalComments;

public sealed class ListApprovalCommentsQueryHandler : IRequestHandler<ListApprovalCommentsQuery, Result<IReadOnlyList<ApprovalCommentResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IApprovalCommentAccess _access;
    private readonly IWorkApprovalCommentRepository _comments;

    public ListApprovalCommentsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IApprovalCommentAccess access, IWorkApprovalCommentRepository comments)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _comments = comments;
    }

    public async Task<Result<IReadOnlyList<ApprovalCommentResponse>>> Handle(ListApprovalCommentsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ApprovalCommentResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ApprovalCommentResponse>>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var subject = await _access.ResolveAsync(tenantId, query.SubjectType, query.SubjectId, caller, ct);
        if (subject is null)
            return Result<IReadOnlyList<ApprovalCommentResponse>>.NotFound(ApprovalCommentRules.NotFoundMessage);

        var comments = await _comments.ListBySubjectAsync(tenantId, subject.SubjectType, subject.SubjectId, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, comments.Select(c => c.EmployeeId).Distinct().ToList(), ct);
        return Result<IReadOnlyList<ApprovalCommentResponse>>.Success(
            comments.Select(c => ApprovalCommentRules.ToResponse(c, names, caller)).ToList());
    }
}
