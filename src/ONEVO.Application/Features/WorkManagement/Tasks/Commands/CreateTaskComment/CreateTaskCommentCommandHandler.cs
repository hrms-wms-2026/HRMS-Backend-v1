using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;

public sealed class CreateTaskCommentCommandHandler : IRequestHandler<CreateTaskCommentCommand, Result<TaskCommentResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ICallerIdentityResolver _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IWorkNotificationEngine _notifications;

    public CreateTaskCommentCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskAssetLinker assetLinker, ICallerIdentityResolver identity, IUnitOfWork unitOfWork,
        IObjectiveRepository objectives, IWorkHierarchyService hierarchy, ITaskAssignmentRepository assignments,
        IWorkNotificationEngine notifications)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _assetLinker = assetLinker;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _objectives = objectives;
        _hierarchy = hierarchy;
        _assignments = assignments;
        _notifications = notifications;
    }

    public async Task<Result<TaskCommentResponse>> Handle(CreateTaskCommentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskCommentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        Guid taskId;
        if (request.ParentCommentId is { } parentId)
        {
            var parent = await _comments.GetByIdForTenantAsync(tenantId, parentId, ct);
            if (parent is null || parent.IsDeleted)
                return Result<TaskCommentResponse>.NotFound("Comment not found.");
            if (parent.ParentCommentId is not null)
                return Result<TaskCommentResponse>.Failure("Cannot reply to a reply.", 400);

            taskId = parent.TaskId;
        }
        else
        {
            taskId = request.TaskId ?? Guid.Empty;
        }

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, taskId, ct);
        if (!access.IsSuccess)
            return Result<TaskCommentResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var task = access.Value!.Task;
        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TaskId = taskId,
            EmployeeId = access.Value!.CallerEmployeeId,
            ParentCommentId = request.ParentCommentId,
            Content = request.Content,
            IsEdited = false,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _comments.AddAsync(comment, ct);
        await _assetLinker.SyncCommentAttachmentsAsync(tenantId, userId, comment.Id, request.AttachmentFileIds, ct);
        await _assetLinker.SyncCommentDescriptionImagesAsync(tenantId, userId, comment.Id, request.Content, ct);

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is not null)
        {
            var tree = await _hierarchy.LoadTreeAsync(tenantId, task.ProjectId, ct);
            var assigneeIds = (await _assignments.GetByTaskIdAsync(task.Id, ct)).Select(a => a.EmployeeId);
            var recipients = TaskChangeRecipients.For(tree, task, objective, assigneeIds);

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, task.ProjectId, comment.EmployeeId, WorkNotificationKinds.Direct,
                WorkActionTypes.TaskComment, WorkTargetTypes.Task, task.Id, task.Title, null, recipients), ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, new[] { comment.EmployeeId }, ct);
        var employeeName = names.GetValueOrDefault(comment.EmployeeId) ?? "A teammate";

        return Result<TaskCommentResponse>.Success(new TaskCommentResponse(
            comment.Id, comment.TaskId, comment.ParentCommentId, comment.EmployeeId, employeeName, comment.Content,
            comment.IsEdited, false, comment.CreatedAt,
            Array.Empty<TaskCommentReactionDto>(), Array.Empty<TaskAttachmentDto>(), Array.Empty<TaskCommentResponse>()));
    }
}
