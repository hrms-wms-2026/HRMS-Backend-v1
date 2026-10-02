using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments;

/// <summary>Saves a comment or reply and notifies the thread's other participants in one transaction.</summary>
internal static class ApprovalCommentPosting
{
    public static async Task<Result<ApprovalCommentResponse>> PostAsync(
        Guid tenantId, Guid userId, Guid caller, ApprovalCommentSubject subject, Guid? parentCommentId, string content,
        IWorkApprovalCommentRepository comments, IWorkNotificationEngine notifications, IUnitOfWork unitOfWork,
        ICallerIdentityResolver identity, CancellationToken ct)
    {
        var comment = new WorkApprovalComment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SubjectType = subject.SubjectType, SubjectId = subject.SubjectId,
            ProjectId = subject.ProjectId, EmployeeId = caller, ParentCommentId = parentCommentId, Content = content,
            CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        };

        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await comments.AddAsync(comment, innerCt);
            await notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, subject.ProjectId, caller, WorkNotificationKinds.Commented, subject.ActionType, subject.TargetType,
                subject.TargetId, subject.TargetTitle, subject.ApprovalRequestId, subject.Participants), innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<bool>.Success(true);
        }, ct);

        var names = await identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [caller], ct);
        return Result<ApprovalCommentResponse>.Success(ApprovalCommentRules.ToResponse(comment, names, caller));
    }
}
