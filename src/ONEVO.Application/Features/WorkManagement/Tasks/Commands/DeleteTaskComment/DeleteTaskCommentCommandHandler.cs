using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;

public sealed class DeleteTaskCommentCommandHandler : IRequestHandler<DeleteTaskCommentCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTaskCommentCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteTaskCommentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var comment = await _comments.GetByIdForTenantAsync(tenantId, request.CommentId, ct);
        if (comment is null || comment.IsDeleted)
            return Result.NotFound("Comment not found.");

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, comment.TaskId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error!, access.StatusCode ?? 400);

        if (comment.EmployeeId != access.Value!.CallerEmployeeId)
            return Result.Forbidden("Only the comment's author may delete it.");

        var now = DateTimeOffset.UtcNow;
        comment.IsDeleted = true;
        comment.DeletedAt = now;
        comment.UpdatedAt = now;

        await _logs.AddAsync(new TaskCommentLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = comment.TaskId, CommentId = comment.Id,
            EmployeeId = comment.EmployeeId, Action = TaskCommentLogActions.Deleted,
            OccurredAt = now, CreatedById = userId, CreatedAt = now
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
