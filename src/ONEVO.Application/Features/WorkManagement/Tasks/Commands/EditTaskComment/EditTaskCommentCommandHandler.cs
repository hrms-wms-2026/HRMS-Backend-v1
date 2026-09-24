using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;

public sealed class EditTaskCommentCommandHandler : IRequestHandler<EditTaskCommentCommand, Result<TaskCommentResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentLogRepository _logs;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ICallerIdentityResolver _identity;
    private readonly IUnitOfWork _unitOfWork;

    public EditTaskCommentCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentLogRepository logs, ITaskAssetLinker assetLinker, ICallerIdentityResolver identity, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _logs = logs;
        _assetLinker = assetLinker;
        _identity = identity;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskCommentResponse>> Handle(EditTaskCommentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskCommentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var comment = await _comments.GetByIdForTenantAsync(tenantId, request.CommentId, ct);
        if (comment is null || comment.IsDeleted)
            return Result<TaskCommentResponse>.NotFound("Comment not found.");

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, comment.TaskId, ct);
        if (!access.IsSuccess)
            return Result<TaskCommentResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (comment.EmployeeId != access.Value!.CallerEmployeeId)
            return Result<TaskCommentResponse>.Forbidden("Only the comment's author may edit it.");

        comment.Content = request.Content;
        comment.IsEdited = true;
        comment.UpdatedAt = DateTimeOffset.UtcNow;

        await _assetLinker.SyncCommentAttachmentsAsync(tenantId, userId, comment.Id, request.AttachmentFileIds, ct);
        await _assetLinker.SyncCommentDescriptionImagesAsync(tenantId, userId, comment.Id, request.Content, ct);
        await _logs.AddAsync(new TaskCommentLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = comment.TaskId, CommentId = comment.Id,
            EmployeeId = comment.EmployeeId, Action = TaskCommentLogActions.Edited,
            OccurredAt = DateTimeOffset.UtcNow, CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, new[] { comment.EmployeeId }, ct);
        var employeeName = names.GetValueOrDefault(comment.EmployeeId) ?? "A teammate";

        return Result<TaskCommentResponse>.Success(new TaskCommentResponse(
            comment.Id, comment.TaskId, comment.ParentCommentId, comment.EmployeeId, employeeName, comment.Content,
            comment.IsEdited, false, comment.CreatedAt,
            Array.Empty<TaskCommentReactionDto>(), Array.Empty<TaskAttachmentDto>(), Array.Empty<TaskCommentResponse>()));
    }
}
