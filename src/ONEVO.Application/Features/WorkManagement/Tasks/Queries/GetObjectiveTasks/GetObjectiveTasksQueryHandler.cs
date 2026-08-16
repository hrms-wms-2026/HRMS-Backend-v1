using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;

public class GetObjectiveTasksQueryHandler : IRequestHandler<GetObjectiveTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;

    public GetObjectiveTasksQueryHandler(ICurrentUser currentUser, IWorkTaskRepository tasks)
    {
        _currentUser = currentUser;
        _tasks = tasks;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetObjectiveTasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("Authentication required.");

        var items = await _tasks.GetByObjectiveIdAsync(_currentUser.TenantId, request.ObjectiveId, ct);
        var responses = items.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.TaskType, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent)).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
