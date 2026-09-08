using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyProjectTasks;

public sealed class GetMyProjectTasksQueryHandler : IRequestHandler<GetMyProjectTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private static readonly IReadOnlyDictionary<string, int> PriorityRank = new Dictionary<string, int>
    {
        [WorkTaskPriorities.Critical] = 4,
        [WorkTaskPriorities.High] = 3,
        [WorkTaskPriorities.Medium] = 2,
        [WorkTaskPriorities.Low] = 1
    };

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;

    public GetMyProjectTasksQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IWorkTaskRepository tasks, ITaskAssignmentRepository assignments, ITaskClockingSessionRepository sessions)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetMyProjectTasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Project not found.");

        var items = (await _tasks.GetByProjectAsync(tenantId, project.Id, ct))
            .Where(task => task.ProjectId == project.Id)
            .ToList();
        if (request.SprintId.HasValue)
            items = items.Where(task => task.SprintId == request.SprintId.Value).ToList();

        var assignments = await _assignments.GetByTaskIdsAsync(items.Select(task => task.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(assignment => assignment.TaskId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(assignment => assignment.EmployeeId).ToList());

        var myTasks = items.Where(task =>
            assigneesByTaskId.GetValueOrDefault(task.Id, Array.Empty<Guid>()).Contains(callerEmployeeId.Value));
        var openSessions = await _sessions.GetOpenSessionsForTasksAsync(
            tenantId, myTasks.Select(task => task.Id).ToList(), ct);
        var totalLoggedMinutes = await _sessions.GetTotalClosedSessionMinutesForTasksAsync(
            tenantId, myTasks.Select(task => task.Id).ToList(), ct);

        var sorted = myTasks
            .OrderBy(task => task.DueDate.HasValue ? 0 : 1)
            .ThenBy(task => task.DueDate)
            .ThenByDescending(task => PriorityRank.GetValueOrDefault(task.Priority, 0))
            .ToList();

        var responses = sorted.Select(task => new WorkTaskResponse(
            task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description, task.CategoryId, task.StatusId,
            task.Priority, task.StoryPoints, task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId,
            assigneesByTaskId.GetValueOrDefault(task.Id, Array.Empty<Guid>()),
            openSessions.TryGetValue(task.Id, out var openSession) ? openSession.EmployeeId : (Guid?)null,
            openSession?.ClockInAt,
            totalLoggedMinutes.GetValueOrDefault(task.Id, 0))).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
