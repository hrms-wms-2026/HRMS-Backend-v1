using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;

public sealed class AddTaskCommentReactionCommandHandler : IRequestHandler<AddTaskCommentReactionCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentReactionRepository _reactions;
    private readonly IUnitOfWork _unitOfWork;

    public AddTaskCommentReactionCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentReactionRepository reactions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _reactions = reactions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddTaskCommentReactionCommand request, CancellationToken ct)
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

        var existing = await _reactions.GetAsync(tenantId, comment.Id, access.Value!.CallerEmployeeId, request.Emoji, ct);
        if (existing is not null)
            return Result.Success();

        await _reactions.AddAsync(new TaskCommentReaction
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CommentId = comment.Id,
            EmployeeId = access.Value.CallerEmployeeId, Emoji = request.Emoji,
            CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
