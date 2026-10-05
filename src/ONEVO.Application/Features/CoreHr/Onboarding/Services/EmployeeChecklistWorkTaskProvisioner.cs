using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Lookups;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

public sealed class EmployeeChecklistWorkTaskProvisioner(
    IOfficeProjectProvisioner officeProjects,
    IEmployeeRepository employees,
    IProjectRepository projects,
    IWorkTaskRepository workTasks,
    ITaskAssignmentRepository assignments,
    IProjectMemberRepository members,
    IWorkNotificationEngine notifications) : IEmployeeChecklistWorkTaskProvisioner
{
    public async Task<Result<OfficeProjectContext>> EnsureOfficeProjectAsync(
        Guid tenantId, Guid legalEntityId, Guid actingUserId, DateOnly targetDate,
        CancellationToken ct = default)
    {
        var actor = await employees.GetByUserIdAsync(tenantId, actingUserId, ct);
        if (actor is null || actor.EmploymentStatusId != EmploymentStatusIds.Active)
            return Result<OfficeProjectContext>.Forbidden("No active employee record exists for the current user.");

        var office = await officeProjects.EnsureAsync(
            tenantId, legalEntityId, actingUserId, actor.Id, targetDate, ct);
        return Result<OfficeProjectContext>.Success(office);
    }

    public async Task<Result<int>> ProvisionAsync(
        OfficeProjectContext office, EmployeeEntity employee,
        IReadOnlyList<EmployeeChecklistTask> checklistTasks, Guid actingUserId,
        CancellationToken ct = default)
    {
        if (checklistTasks.Count == 0)
            return Result<int>.Success(0);

        var actor = await employees.GetByUserIdAsync(employee.TenantId, actingUserId, ct);
        if (actor is null || actor.EmploymentStatusId != EmploymentStatusIds.Active)
            return Result<int>.Forbidden("No active employee record exists for the current user.");

        var unresolvedUserIds = checklistTasks
            .Where(task => task.WorkTaskId is null && task.AssignedToId != employee.UserId)
            .Select(task => task.AssignedToId)
            .Distinct()
            .ToList();
        var candidates = await employees.GetByUserIdsAsync(employee.TenantId, unresolvedUserIds, ct);
        var candidatesByUser = candidates
            .Where(candidate => candidate.EmploymentStatusId == EmploymentStatusIds.Active)
            .GroupBy(candidate => candidate.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var created = 0;
        foreach (var checklistTask in checklistTasks.OrderBy(task => task.Sequence).ThenBy(task => task.Id))
        {
            if (checklistTask.WorkTaskId is not null)
                continue;

            EmployeeEntity assignee;
            if (checklistTask.AssignedToId == employee.UserId)
            {
                assignee = employee;
            }
            else if (!candidatesByUser.TryGetValue(checklistTask.AssignedToId, out var matches))
            {
                return Result<int>.UnprocessableEntity(
                    $"Checklist task '{checklistTask.TaskTitle}' no longer has an active employee assignee.");
            }
            else
            {
                var sameCompany = matches.Where(candidate => candidate.LegalEntityId == employee.LegalEntityId).ToList();
                var usable = sameCompany.Count == 1 ? sameCompany : matches;
                if (usable.Count != 1)
                    return Result<int>.UnprocessableEntity(
                        $"Checklist task '{checklistTask.TaskTitle}' has an ambiguous employee assignee.");
                assignee = usable[0];
            }

            var categoryKey = string.Equals(checklistTask.LifecycleType, "offboarding", StringComparison.OrdinalIgnoreCase)
                ? "offboarding"
                : "onboarding";
            if (!office.CategoryIds.TryGetValue(categoryKey, out var categoryId))
                return Result<int>.Failure($"The Office project has no {categoryKey} category.", 500);

            var number = await projects.IncrementAndGetNextTaskNumberAsync(
                employee.TenantId, office.ProjectId, ct);
            var now = DateTimeOffset.UtcNow;
            var workTask = new WorkTask
            {
                Id = Guid.NewGuid(),
                TenantId = employee.TenantId,
                ProjectId = office.ProjectId,
                ObjectiveId = office.ObjectiveId,
                CreatorPositionObjectiveId = office.ObjectiveId,
                ShortId = $"OFFICE-{number}",
                Title = $"{checklistTask.TaskTitle} — {employee.FirstName} {employee.LastName}".Trim(),
                Description = $"{char.ToUpperInvariant(categoryKey[0]) + categoryKey[1..]} checklist for {employee.FirstName} {employee.LastName} ({employee.EmployeeNumber}).",
                CategoryId = categoryId,
                StatusId = office.ToDoStatusId,
                Priority = WorkTaskPriorities.Medium,
                DueDate = checklistTask.DueDate,
                EstimatedHours = null,
                SprintId = null,
                ParentTaskId = null,
                TaskKind = WorkTaskKinds.EmployeeChecklist,
                VisibilityScope = WorkTaskVisibilityScopes.Assignees,
                CreatedById = actingUserId,
                CreatedAt = now
            };
            await workTasks.AddAsync(workTask, ct);
            await assignments.AddAsync(new TaskAssignment
            {
                Id = Guid.NewGuid(),
                TaskId = workTask.Id,
                UserId = assignee.UserId,
                EmployeeId = assignee.Id,
                AssignedById = actor.Id,
                AssignedAt = now
            }, ct);

            var membership = await members.GetTrackedForObjectiveAsync(
                employee.TenantId, office.ProjectId, office.ObjectiveId, assignee.Id, ct);
            if (membership is null)
            {
                await members.AddAsync(new ProjectMember
                {
                    Id = Guid.NewGuid(), TenantId = employee.TenantId,
                    ProjectId = office.ProjectId, ObjectiveId = office.ObjectiveId,
                    EmployeeId = assignee.Id, MembershipSource = ProjectMembershipSources.System,
                    IsActive = true, JoinedAt = now, CreatedById = actingUserId, CreatedAt = now
                }, ct);
            }
            else if (!membership.IsActive)
            {
                membership.IsActive = true;
                membership.RemovedAt = null;
                membership.JoinedAt = now;
                members.Update(membership);
            }

            checklistTask.WorkTaskId = workTask.Id;
            await notifications.NotifyAsync(new WorkNotificationEvent(
                employee.TenantId, office.ProjectId, actor.Id, WorkNotificationKinds.Direct,
                WorkActionTypes.TaskCreate, WorkTargetTypes.Task, workTask.Id, workTask.Title, null,
                [assignee.Id]), ct);
            created++;
        }

        return Result<int>.Success(created);
    }
}
