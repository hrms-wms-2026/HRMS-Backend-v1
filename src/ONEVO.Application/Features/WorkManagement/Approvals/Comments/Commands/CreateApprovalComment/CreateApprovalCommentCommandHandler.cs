using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.CreateApprovalComment;

public sealed class CreateApprovalCommentCommandHandler : IRequestHandler<CreateApprovalCommentCommand, Result<ApprovalCommentResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IApprovalCommentAccess _access;
    private readonly IWorkApprovalCommentRepository _comments;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public CreateApprovalCommentCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IApprovalCommentAccess access,
        IWorkApprovalCommentRepository comments, IWorkNotificationEngine notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _comments = comments;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ApprovalCommentResponse>> Handle(CreateApprovalCommentCommand command, CancellationToken ct)
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

        var subject = await _access.ResolveAsync(tenantId, command.SubjectType, command.SubjectId, caller, ct);
        if (subject is null)
            return Result<ApprovalCommentResponse>.NotFound(ApprovalCommentRules.NotFoundMessage);

        return await ApprovalCommentPosting.PostAsync(
            tenantId, _currentUser.UserId, caller, subject, parentCommentId: null, content!,
            _comments, _notifications, _unitOfWork, _identity, ct);
    }
}
