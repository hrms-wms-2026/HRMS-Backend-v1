using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;

public sealed class UpdateCalendarEventCommandHandler : IRequestHandler<UpdateCalendarEventCommand, Result<CalendarEventResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCalendarEventCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IObjectiveRepository objectives,
        IWorkTaskRepository tasks,
        ICalendarEventRepository calendarEvents,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _tasks = tasks;
        _calendarEvents = calendarEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CalendarEventResponse>> Handle(UpdateCalendarEventCommand request, CancellationToken ct)
    {
        var actorResult = await ResolveActorAsync(ct);
        if (!actorResult.IsSuccess)
            return Result<CalendarEventResponse>.Failure(actorResult.Error!, actorResult.StatusCode!.Value);

        var tenantId = _currentUser.TenantId;
        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.Id, ct);
        if (calendarEvent is null)
            return Result<CalendarEventResponse>.NotFound("Calendar event not found.");
        if (calendarEvent.Status != CalendarEventStatuses.Active)
            return Result<CalendarEventResponse>.Failure("Archived calendar events cannot be edited.");

        var startDate = request.StartDate ?? calendarEvent.StartDate;
        var endDate = request.EndDate ?? calendarEvent.EndDate;
        if (endDate < startDate)
            return Result<CalendarEventResponse>.Failure("End date must be on or after the start date.");

        var currentMemberships = await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct);
        var objectiveIds = request.ObjectiveIds is null
            ? currentMemberships.Select(m => m.ObjectiveId).Distinct().ToList()
            : request.ObjectiveIds.Distinct().ToList();

        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, calendarEvent.ProjectId, ct);
        var objectiveIdSet = objectives.Select(o => o.Id).ToHashSet();
        var invalidObjectiveIds = objectiveIds.Where(id => !objectiveIdSet.Contains(id)).ToList();
        if (invalidObjectiveIds.Count > 0)
            return Result<CalendarEventResponse>.NotFound($"Objective(s) not found in project: {string.Join(", ", invalidObjectiveIds)}.");

        var currentTaskLinks = await _calendarEvents.ListTaskMembershipsForEventAsync(calendarEvent.Id, ct);
        var desiredTaskIds = request.TaskIds is null
            ? currentTaskLinks.Select(l => l.TaskId).Distinct().ToList()
            : request.TaskIds.Distinct().ToList();

        var projectTasks = await _tasks.GetByProjectAsync(tenantId, calendarEvent.ProjectId, ct);
        var projectTaskById = projectTasks.ToDictionary(t => t.Id);
        var missingTasks = desiredTaskIds.Where(id => !projectTaskById.ContainsKey(id)).ToList();
        if (missingTasks.Count > 0)
            return Result<CalendarEventResponse>.NotFound($"Task(s) not found in project: {string.Join(", ", missingTasks)}.");

        var moduleTasks = new List<WorkTask>();
        foreach (var objectiveId in objectiveIds)
            moduleTasks.AddRange(await _tasks.GetByObjectiveIdAsync(tenantId, objectiveId, ct));

        var memberTasks = moduleTasks
            .Concat(desiredTaskIds.Select(id => projectTaskById[id]))
            .GroupBy(t => t.Id).Select(g => g.First()).ToList();

        // R2/R3: re-validate every member task against the (possibly new) window.
        var outOfWindow = memberTasks
            .Where(t => t.DueDate is null || t.DueDate < startDate || t.DueDate > endDate)
            .Select(t => t.ShortId).ToList();
        if (outOfWindow.Count > 0)
            return Result<CalendarEventResponse>.Conflict(
                $"Task(s) fall outside the event window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}: {string.Join(", ", outOfWindow)}. Widen the event or remove them.");

        // R1: a member task must not already belong to a *different* active event.
        var alreadyLinked = await _calendarEvents.ListActiveTaskLinksForTasksAsync(
            tenantId, memberTasks.Select(t => t.Id).ToList(), ct);
        var foreignLinks = alreadyLinked.Where(l => l.CalendarEventId != calendarEvent.Id).ToList();
        if (foreignLinks.Count > 0)
            return Result<CalendarEventResponse>.Conflict(
                $"Task(s) already belong to another active event: {string.Join(", ", foreignLinks.Select(l => l.EventName).Distinct())}.");

        var desiredObjectiveIds = objectiveIds.ToHashSet();
        var existingObjectiveIds = currentMemberships.Select(m => m.ObjectiveId).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var membershipsToRemove = currentMemberships.Where(m => !desiredObjectiveIds.Contains(m.ObjectiveId)).ToList();
        var membershipsToAdd = objectiveIds
            .Where(id => !existingObjectiveIds.Contains(id))
            .Select(id => new CalendarEventObjective
            {
                Id = Guid.NewGuid(),
                CalendarEventId = calendarEvent.Id,
                ObjectiveId = id,
                AddedAt = now
            })
            .ToList();

        var desiredTaskIdSet = desiredTaskIds.ToHashSet();
        var existingTaskIds = currentTaskLinks.Select(l => l.TaskId).ToHashSet();
        var taskLinksToRemove = currentTaskLinks.Where(l => !desiredTaskIdSet.Contains(l.TaskId)).ToList();
        var taskLinksToAdd = desiredTaskIds
            .Where(id => !existingTaskIds.Contains(id))
            .Select(id => new CalendarEventTask
            {
                Id = Guid.NewGuid(),
                CalendarEventId = calendarEvent.Id,
                TaskId = id,
                AddedAt = now
            })
            .ToList();

        calendarEvent.Name = request.Name is null ? calendarEvent.Name : request.Name.Trim();
        calendarEvent.Color = request.Color is null ? calendarEvent.Color : request.Color.Trim();
        calendarEvent.StartDate = startDate;
        calendarEvent.EndDate = endDate;

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _calendarEvents.Update(calendarEvent);
            _calendarEvents.RemoveMemberships(membershipsToRemove);
            await _calendarEvents.AddMembershipsAsync(membershipsToAdd, innerCt);
            _calendarEvents.RemoveTaskMemberships(taskLinksToRemove);
            await _calendarEvents.AddTaskMembershipsAsync(taskLinksToAdd, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        return Result<CalendarEventResponse>.Success(
            CreateCalendarEventCommandHandler.ToResponse(calendarEvent, objectiveIds, desiredTaskIds));
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

    private sealed record ActorResult(bool IsSuccess, Guid EmployeeId, string? Error, int? StatusCode)
    {
        public static ActorResult Success(Guid employeeId) => new(true, employeeId, null, null);
        public static ActorResult Failure(string error, int statusCode) => new(false, Guid.Empty, error, statusCode);
    }
}
