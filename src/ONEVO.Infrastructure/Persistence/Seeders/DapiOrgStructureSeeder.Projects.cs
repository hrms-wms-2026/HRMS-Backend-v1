using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiOrgStructureSeeder
{
    private const string OnexsoProjectKey = "onexso";
    private const string OrgStructureObjectiveTitle = "Org Structure And Position Management";

    private static readonly (string HireKey, string TaskTitleSuffix, string CategoryName, string StatusName, string Priority)[]
        NewHireTaskSpecs =
        [
            ("gm", "Review department & position rollout plan", "story", "In Process", "high"),
            ("hrmgr", "Verify onboarding checklist covers the new department structure", "task", "To Do", "medium"),
            ("opsexec", "Document the new reporting structure for the employee handbook", "task", "To Do", "medium"),
        ];

    /// <summary>
    /// Attaches the 3 new hires (GM, HR Manager, Operations Executive) to the existing "Onexso"
    /// project - it is literally the HR & Work Management product this tenant is building, so its
    /// "Org Structure And Position Management" objective is a natural fit for org-structure work.
    /// No-ops if WorkManagementDapiDemoSeeder hasn't run yet (project/objective/categories/statuses
    /// don't exist).
    /// </summary>
    private static async Task ConnectNewAccountsToProjectAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var projectId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:project:{OnexsoProjectKey}");
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
        {
            return;
        }

        var objective = await db.Objectives.FirstOrDefaultAsync(
            o => o.ProjectId == projectId && o.Title == OrgStructureObjectiveTitle, ct);
        if (objective is null)
        {
            return;
        }

        var categories = await db.TaskCategories
            .Where(c => c.ProjectId == projectId)
            .ToDictionaryAsync(c => c.Name, c => c.Id, ct);
        var statuses = await db.TaskStatuses
            .Where(s => s.ProjectId == projectId)
            .ToDictionaryAsync(s => s.Name, s => s.Id, ct);

        var nextTaskNumber = project.NextTaskNumber < 1 ? 1L : project.NextTaskNumber;

        foreach (var spec in NewHireTaskSpecs)
        {
            var hire = DapiOrgStructureData.NewHires.First(h => h.Key == spec.HireKey);
            var employeeId = DeterministicGuid($"dapi-org:employee:{hire.Key}");
            var userId = DeterministicGuid($"dapi-org:user:{hire.Key}");

            await SeedProjectMemberAsync(db, spec.HireKey, projectId, objective.Id, employeeId, now, ct);

            var taskId = DeterministicGuid($"dapi-org:task:{OnexsoProjectKey}:{spec.HireKey}");
            if (await db.WorkTasks.AnyAsync(t => t.Id == taskId, ct))
            {
                continue;
            }

            var shortId = $"{project.Identifier}-{nextTaskNumber}";
            nextTaskNumber++;

            db.WorkTasks.Add(new WorkTask
            {
                Id = taskId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ObjectiveId = objective.Id,
                ShortId = shortId,
                Title = $"{hire.FirstName} {hire.LastName} — {spec.TaskTitleSuffix}",
                Description = $"Org structure rollout task for {hire.FirstName} {hire.LastName} ({hire.PositionCode}).",
                CategoryId = categories[spec.CategoryName],
                StatusId = statuses[spec.StatusName],
                Priority = spec.Priority,
                EstimatedHours = 8m,
                CompletedHours = 0m,
                ProgressPercent = spec.StatusName == "In Process" ? 30 : 0,
                StartedAt = spec.StatusName == "In Process" ? now.AddDays(-2) : null,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });

            var assignmentId = DeterministicGuid($"dapi-org:task-assignment:{OnexsoProjectKey}:{spec.HireKey}");
            if (!await db.TaskAssignments.AnyAsync(a => a.Id == assignmentId, ct))
            {
                db.TaskAssignments.Add(new TaskAssignment
                {
                    Id = assignmentId,
                    TaskId = taskId,
                    UserId = userId,
                    EmployeeId = employeeId,
                    AssignedById = DapiOwnerUserId,
                    AssignedAt = now
                });
            }
        }

        project.NextTaskNumber = nextTaskNumber;
    }

    private static async Task SeedProjectMemberAsync(
        ApplicationDbContext db,
        string hireKey,
        Guid projectId,
        Guid objectiveId,
        Guid employeeId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var memberId = DeterministicGuid($"dapi-org:project-member:{OnexsoProjectKey}:{hireKey}");
        if (await db.ProjectMembers.AnyAsync(m => m.Id == memberId, ct))
        {
            return;
        }

        db.ProjectMembers.Add(new ProjectMember
        {
            Id = memberId,
            TenantId = DapiTenantId,
            ProjectId = projectId,
            ObjectiveId = objectiveId,
            EmployeeId = employeeId,
            MembershipSource = ProjectMembershipSources.System,
            IsActive = true,
            JoinedAt = now,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });
    }
}
