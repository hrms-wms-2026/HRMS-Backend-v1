using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.EditApprovalComment;

public sealed class EditApprovalCommentCommandHandler : IRequestHandler<EditApprovalCommentCommand, Result<ApprovalCommentResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalCommentRepository _comments;
    private readonly IUnitOfWork _unitOfWork;

    public EditApprovalCommentCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalCommentRepository comments, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _comments = comments;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ApprovalCommentResponse>> Handle(EditApprovalCommentCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ApprovalCommentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ApprovalCommentResponse>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var (content, error) = ApprovalCommentRules.Validate(command.Content);
        if (error is not null)
            return Result<ApprovalCommentResponse>.Failure(error);

        var comment = await _comments.GetTrackedByIdForTenantAsync(tenantId, command.CommentId, ct);
        if (comment is null)
            return Result<ApprovalCommentResponse>.NotFound("Comment not found.");
        if (comment.EmployeeId != caller)
            return Result<ApprovalCommentResponse>.Forbidden("Only the author can edit this comment.");

        comment.Content = content!;
        comment.IsEdited = true;
        comment.UpdatedAt = DateTimeOffset.UtcNow;
        _comments.Update(comment);
        await _unitOfWork.SaveChangesAsync(ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [caller], ct);
        return Result<ApprovalCommentResponse>.Success(ApprovalCommentRules.ToResponse(comment, names, caller));
    }
}
