using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;

public sealed class CreateCalendarEventCommandHandler : IRequestHandler<CreateCalendarEventCommand, Result<CalendarEventResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCalendarEventCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IWorkTaskRepository tasks,
        ICalendarEventRepository calendarEvents,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _objectives = objectives;
        _tasks = tasks;
        _calendarEvents = calendarEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CalendarEventResponse>> Handle(CreateCalendarEventCommand request, CancellationToken ct)
    {
        var actorResult = await ResolveActorAsync(ct);
        if (!actorResult.IsSuccess)
            return Result<CalendarEventResponse>.Failure(actorResult.Error!, actorResult.StatusCode!.Value);

        var tenantId = _currentUser.TenantId;
        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<CalendarEventResponse>.NotFound("Project not found.");

        var objectiveIds = request.ObjectiveIds.Distinct().ToList();
        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);
        var objectiveIdSet = objectives.Select(o => o.Id).ToHashSet();
        var invalidObjectiveIds = objectiveIds.Where(id => !objectiveIdSet.Contains(id)).ToList();
        if (invalidObjectiveIds.Count > 0)
            return Result<CalendarEventResponse>.NotFound($"Objective(s) not found in project: {string.Join(", ", invalidObjectiveIds)}.");

        var startDate = request.StartDate;
        var endDate = request.EndDate;

        // Direct task picks must belong to the project.
        var projectTasks = await _tasks.GetByProjectAsync(tenantId, request.ProjectId, ct);
        var projectTaskById = projectTasks.ToDictionary(t => t.Id);
        var directTaskIds = request.TaskIds.Distinct().ToList();
        var missingTasks = directTaskIds.Where(id => !projectTaskById.ContainsKey(id)).ToList();
        if (missingTasks.Count > 0)
            return Result<CalendarEventResponse>.NotFound($"Task(s) not found in project: {string.Join(", ", missingTasks)}.");

        // Whole-module links contribute their current active tasks (spec §2, R4).
        var moduleTasks = new List<WorkTask>();
        foreach (var objectiveId in objectiveIds)
            moduleTasks.AddRange(await _tasks.GetByObjectiveIdAsync(tenantId, objectiveId, ct));

        var memberTasks = moduleTasks
            .Concat(directTaskIds.Select(id => projectTaskById[id]))
            .GroupBy(t => t.Id).Select(g => g.First()).ToList();

        // R2: every member task has a DueDate inside [startDate, endDate].
        var outOfWindow = memberTasks
            .Where(t => t.DueDate is null || t.DueDate < startDate || t.DueDate > endDate)
            .Select(t => t.ShortId).ToList();
        if (outOfWindow.Count > 0)
            return Result<CalendarEventResponse>.Conflict(
                $"Task(s) fall outside the event window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}: {string.Join(", ", outOfWindow)}. Widen the event or remove them.");

        // R1: no member task is already in another active event.
        var alreadyLinked = await _calendarEvents.ListActiveTaskLinksForTasksAsync(
            tenantId, memberTasks.Select(t => t.Id).ToList(), ct);
        if (alreadyLinked.Count > 0)
            return Result<CalendarEventResponse>.Conflict(
                $"Task(s) already belong to an active event: {string.Join(", ", alreadyLinked.Select(l => l.EventName).Distinct())}.");

        var now = DateTimeOffset.UtcNow;
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = request.ProjectId,
            Name = request.Name.Trim(),
            Color = request.Color.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            Status = CalendarEventStatuses.Active,
            CreatedById = actorResult.EmployeeId,
            CreatedAt = now
        };
        var memberships = objectiveIds.Select(objectiveId => new CalendarEventObjective
        {
            Id = Guid.NewGuid(),
            CalendarEventId = calendarEvent.Id,
            ObjectiveId = objectiveId,
            AddedAt = now
        }).ToList();
        var taskMemberships = directTaskIds.Select(taskId => new CalendarEventTask
        {
            Id = Guid.NewGuid(),
            CalendarEventId = calendarEvent.Id,
            TaskId = taskId,
            AddedAt = now
        }).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _calendarEvents.AddAsync(calendarEvent, innerCt);
            await _calendarEvents.AddMembershipsAsync(memberships, innerCt);
            await _calendarEvents.AddTaskMembershipsAsync(taskMemberships, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<CalendarEventResponse>.Success(ToResponse(calendarEvent, objectiveIds, directTaskIds));
    }

    private async Task<ActorResult> ResolveActorAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return ActorResult.Failure("Authentication required.", 403);

        if (_currentUser.TenantId == Guid.Empty)
            return ActorResult.Failure("Tenant context missing.", 403);

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        return employeeId is null
            ? ActorResult.Failure("No employee record for the current user.", 403)
            : ActorResult.Success(employeeId.Value);
    }

    internal static CalendarEventResponse ToResponse(
        CalendarEvent calendarEvent, IReadOnlyList<Guid> objectiveIds, IReadOnlyList<Guid> taskIds)
        => new(calendarEvent.Id, calendarEvent.ProjectId, calendarEvent.Name, calendarEvent.Color,
            calendarEvent.Status, calendarEvent.StartDate, calendarEvent.EndDate, objectiveIds, taskIds,
            calendarEvent.CreatedAt, calendarEvent.ArchivedById, calendarEvent.ArchivedAt);

    private sealed record ActorResult(bool IsSuccess, Guid EmployeeId, string? Error, int? StatusCode)
    {
        public static ActorResult Success(Guid employeeId) => new(true, employeeId, null, null);
        public static ActorResult Failure(string error, int statusCode) => new(false, Guid.Empty, error, statusCode);
    }
}
