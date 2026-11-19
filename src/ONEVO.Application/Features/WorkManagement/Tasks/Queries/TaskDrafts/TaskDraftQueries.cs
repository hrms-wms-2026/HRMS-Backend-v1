using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.TaskDrafts;

public sealed record ListMyTaskDraftsQuery : IRequest<Result<IReadOnlyList<TaskDraftSummaryResponse>>>;

public sealed record GetTaskDraftQuery(Guid DraftId) : IRequest<Result<TaskDraftResponse>>;

public sealed class ListMyTaskDraftsQueryHandler(ITaskDraftRepository drafts, ICurrentUser currentUser)
    : IRequestHandler<ListMyTaskDraftsQuery, Result<IReadOnlyList<TaskDraftSummaryResponse>>>
{
    public async Task<Result<IReadOnlyList<TaskDraftSummaryResponse>>> Handle(ListMyTaskDraftsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskDraftSummaryResponse>>.Forbidden("Authentication required.");

        var items = await drafts.ListOwnedAsync(currentUser.TenantId, currentUser.UserId, ct);
        IReadOnlyList<TaskDraftSummaryResponse> summaries = items
            .Select(d => new TaskDraftSummaryResponse(d.Id, d.ProjectId, d.Title, d.UpdatedAt ?? d.CreatedAt))
            .ToList();
        return Result<IReadOnlyList<TaskDraftSummaryResponse>>.Success(summaries);
    }
}

public sealed class GetTaskDraftQueryHandler(ITaskDraftRepository drafts, ICurrentUser currentUser)
    : IRequestHandler<GetTaskDraftQuery, Result<TaskDraftResponse>>
{
    public async Task<Result<TaskDraftResponse>> Handle(GetTaskDraftQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result<TaskDraftResponse>.Forbidden("Authentication required.");

        var draft = await drafts.GetOwnedAsync(currentUser.TenantId, currentUser.UserId, request.DraftId, ct);
        if (draft is null) return Result<TaskDraftResponse>.NotFound("Draft not found.");

        return Result<TaskDraftResponse>.Success(
            new TaskDraftResponse(draft.Id, draft.ProjectId, draft.Title, draft.PayloadJson, draft.UpdatedAt ?? draft.CreatedAt));
    }
}
