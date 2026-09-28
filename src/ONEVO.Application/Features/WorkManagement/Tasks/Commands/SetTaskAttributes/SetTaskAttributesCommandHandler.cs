using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.SetTaskAttributes;

public sealed class SetTaskAttributesCommandHandler(
    ICurrentUser currentUser,
    ICallerIdentityResolver identity,
    IWorkTaskRepository tasks,
    IObjectiveRepository objectives,
    IMilestoneMembershipCoordinator membership,
    ISprintRepository sprints,
    ICalendarEventRepository calendarEvents,
    ITaskEditLogRepository editLogs,
    IUnitOfWork unitOfWork) : IRequestHandler<SetTaskAttributesCommand, Result>
{
    public async Task<Result> Handle(SetTaskAttributesCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        var callerEmployeeId = await identity.ResolveCallerEmployeeIdAsync(tenantId, currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var task = await tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var objective = await objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (!await membership.IsEffectiveOwnerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only this milestone's owner can change task priority or due date.");

        if (task.SprintId.HasValue)
        {
            var sprint = await sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result.Forbidden("This task's sprint has been achieved and is now frozen.");
        }

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        if (request.Priority is not null && request.Priority != task.Priority)
        {
            oldValues["priority"] = task.Priority;
            newValues["priority"] = request.Priority;
        }

        if (request.SetDueDate && request.DueDate != task.DueDate)
        {
            var windows = await calendarEvents.ListActiveEventWindowsForTaskAsync(
                tenantId, task.Id, task.ObjectiveId, ct);
            var windowError = TaskDueDateEventWindowRule.Validate(windows, request.DueDate);
            if (windowError is not null)
                return Result.Conflict(windowError);

            oldValues["dueDate"] = task.DueDate;
            newValues["dueDate"] = request.DueDate;
        }

        if (newValues.Count == 0)
            return Result.Success();

        var now = DateTimeOffset.UtcNow;
        if (newValues.ContainsKey("priority"))
            task.Priority = request.Priority!;
        if (newValues.ContainsKey("dueDate"))
            task.DueDate = request.DueDate;
        task.UpdatedAt = now;

        await editLogs.AddAsync(new TaskEditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TaskId = task.Id,
            EmployeeId = callerEmployeeId.Value,
            Source = TaskEditLogSources.Direct,
            OldValuesJson = JsonSerializer.Serialize(oldValues),
            NewValuesJson = JsonSerializer.Serialize(newValues),
            Reason = null,
            ChangedAt = now
        }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
