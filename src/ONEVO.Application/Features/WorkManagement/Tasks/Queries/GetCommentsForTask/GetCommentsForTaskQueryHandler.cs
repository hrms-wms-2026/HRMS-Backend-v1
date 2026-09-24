using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;

public sealed class GetCommentsForTaskQueryHandler : IRequestHandler<GetCommentsForTaskQuery, Result<IReadOnlyList<TaskCommentResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentReactionRepository _reactions;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICallerIdentityResolver _identity;

    public GetCommentsForTaskQueryHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentReactionRepository reactions, IEntityAssetRepository entityAssets, ICallerIdentityResolver identity)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _reactions = reactions;
        _entityAssets = entityAssets;
        _identity = identity;
    }

    public async Task<Result<IReadOnlyList<TaskCommentResponse>>> Handle(GetCommentsForTaskQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskCommentResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var access = await _access.ResolveViewableTaskAsync(tenantId, _currentUser.UserId, request.TaskId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<TaskCommentResponse>>.Failure(access.Error!, access.StatusCode ?? 400);

        var allComments = await _comments.GetForTaskAsync(tenantId, request.TaskId, ct);
        var commentIds = allComments.Select(c => c.Id).ToList();

        var allReactions = await _reactions.GetForCommentIdsAsync(tenantId, commentIds, ct);

        var allAssets = await _entityAssets.ListByOwnersAsync(tenantId, EntityAssetOwnerTypes.Comment, commentIds, ct);
        var attachmentsByComment = allAssets
            .Where(a => a.AssetPurpose == UploadPurposeCatalog.CommentAttachment)
            .GroupBy(a => a.OwnerId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskAttachmentDto>)g.Select(a => new TaskAttachmentDto(a.FileRecordId, a.OriginalFileName, a.FileSizeBytes, a.ContentType)).ToList());

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, allComments.Select(c => c.EmployeeId).Concat(allReactions.Select(r => r.EmployeeId)).Distinct().ToList(), ct);

        var reactionsByComment = allReactions.GroupBy(r => r.CommentId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<TaskCommentReactionDto>)g.GroupBy(r => r.Emoji)
                .Select(eg =>
                {
                    var ordered = eg.OrderBy(r => r.UpdatedAt ?? r.CreatedAt).ToList();
                    return new TaskCommentReactionDto(
                        eg.Key,
                        ordered.Select(r => r.EmployeeId).ToList(),
                        ordered.Select(r => new TaskCommentReactorDto(r.EmployeeId, names.GetValueOrDefault(r.EmployeeId) ?? "A teammate")).ToList());
                })
                .ToList());

        TaskCommentResponse ToResponse(TaskComment c, IReadOnlyList<TaskCommentResponse> replies) => new(
            c.Id, c.TaskId, c.ParentCommentId, c.EmployeeId, names.GetValueOrDefault(c.EmployeeId) ?? "A teammate",
            c.IsDeleted ? string.Empty : c.Content, c.IsEdited, c.IsDeleted, c.CreatedAt,
            reactionsByComment.GetValueOrDefault(c.Id, Array.Empty<TaskCommentReactionDto>()),
            attachmentsByComment.GetValueOrDefault(c.Id, Array.Empty<TaskAttachmentDto>()),
            replies);

        var repliesByParent = allComments
            .Where(c => c.ParentCommentId is not null && !c.IsDeleted)
            .GroupBy(c => c.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.CreatedAt).ToList());

        var result = new List<TaskCommentResponse>();
        foreach (var topLevel in allComments.Where(c => c.ParentCommentId is null))
        {
            var replies = repliesByParent.GetValueOrDefault(topLevel.Id, new List<TaskComment>());
            if (topLevel.IsDeleted && replies.Count == 0)
                continue;

            var replyResponses = replies.Select(r => ToResponse(r, Array.Empty<TaskCommentResponse>())).ToList();
            result.Add(ToResponse(topLevel, replyResponses));
        }

        result = result.OrderByDescending(r => r.CreatedAt).ToList();

        return Result<IReadOnlyList<TaskCommentResponse>>.Success(result);
    }
}
