using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class WorkManagementDapiDemoSeeder
{
    private static readonly string[] CanonicalStatusNames = ["To Do", "In Process", "Review", "Done"];
    private static readonly string[] CanonicalCategoryNames = ["task", "bug", "story", "feature"];

    private static async Task SeedTasksAndApprovalsAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var categoryIdsByProjectKey = new Dictionary<string, Dictionary<string, Guid>>();

        foreach (var tree in WorkManagementDapiDemoData.ProjectTrees)
        {
            var projectId = DeterministicGuid($"dapi-demo:project:{tree.ProjectKey}");
            // Projects were just Added in SeedProjectsAndObjectivesAsync and may not be
            // flushed yet — FirstOrDefaultAsync hits the store and would miss Local entries.
            var project = db.Projects.Local.FirstOrDefault(p => p.Id == projectId)
                ?? await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null)
            {
                continue;
            }

            await SeedProjectTemplateStatusesAsync(db, tree.ProjectKey, projectId, now, ct);
            var categoryIdByName = await SeedProjectTaskCategoriesAsync(db, tree.ProjectKey, projectId, now, ct);
            categoryIdsByProjectKey[tree.ProjectKey] = categoryIdByName;

            var nextTaskNumber = project.NextTaskNumber < 1 ? 1L : project.NextTaskNumber;
            var leaves = EnumerateLeaves(tree.Root, tree.Root.Title, tree.AllocatedHours).ToList();
            for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
            {
                var (path, node, allocatedHours) = leaves[leafIndex];
                var objectiveId = DeterministicGuid($"dapi-demo:objective:{tree.ProjectKey}:{path}");
                await SeedObjectiveStatusesAsync(db, tree.ProjectKey, path, projectId, objectiveId, now, ct);

                nextTaskNumber = await SeedLeafTasksAsync(
                    db,
                    employeeIdByPersonKey,
                    categoryIdByName,
                    tree,
                    project,
                    path,
                    node,
                    objectiveId,
                    allocatedHours,
                    leafIndex,
                    nextTaskNumber,
                    now,
                    ct);
            }

            project.NextTaskNumber = nextTaskNumber;
        }

        await SeedTaskCreationRequestsAsync(db, employeeIdByPersonKey, categoryIdsByProjectKey, now, ct);
        await SeedAllocationExtendsAsync(db, employeeIdByPersonKey, now, ct);
    }

    /// <summary>
    /// Seeds the 4 canonical TaskCategory rows (task/bug/story/feature) for a demo project.
    /// Existence is keyed on the natural business key (ProjectId, Name) — not on this seeder's
    /// deterministic id — because Task 1's migration self-heal step already inserted these rows
    /// for the 5 dapi demo projects using gen_random_uuid(), not deterministic ids. Matching by
    /// id here would miss those rows and attempt duplicate inserts, violating
    /// ix_task_categories_one_name_per_project.
    /// </summary>
    private static async Task<Dictionary<string, Guid>> SeedProjectTaskCategoriesAsync(
        ApplicationDbContext db,
        string projectKey,
        Guid projectId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var categoryIdByName = new Dictionary<string, Guid>();

        for (var order = 0; order < CanonicalCategoryNames.Length; order++)
        {
            var name = CanonicalCategoryNames[order];

            var existing = db.TaskCategories.Local.FirstOrDefault(c => c.ProjectId == projectId && c.Name == name)
                ?? await db.TaskCategories.FirstOrDefaultAsync(c => c.ProjectId == projectId && c.Name == name, ct);
            if (existing is not null)
            {
                categoryIdByName[name] = existing.Id;
                continue;
            }

            var categoryId = DeterministicGuid($"dapi-demo:task-category:{projectKey}:{name}");
            db.TaskCategories.Add(new TaskCategory
            {
                Id = categoryId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                Name = name,
                DisplayOrder = order,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });
            categoryIdByName[name] = categoryId;
        }

        return categoryIdByName;
    }

    private static async Task SeedProjectTemplateStatusesAsync(
        ApplicationDbContext db,
        string projectKey,
        Guid projectId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var order = 0; order < CanonicalStatusNames.Length; order++)
        {
            var name = CanonicalStatusNames[order];
            var statusId = DeterministicGuid($"dapi-demo:task-status:{projectKey}:template:{name}");
            if (db.TaskStatuses.Local.Any(s => s.Id == statusId)
                || await db.TaskStatuses.AnyAsync(s => s.Id == statusId, ct))
            {
                continue;
            }

            db.TaskStatuses.Add(new TaskStatusEntity
            {
                Id = statusId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ObjectiveId = null,
                Name = name,
                DisplayOrder = order,
                MarksTaskComplete = name == "Done",
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });
        }
    }

    private static async Task SeedObjectiveStatusesAsync(
        ApplicationDbContext db,
        string projectKey,
        string objectivePath,
        Guid projectId,
        Guid objectiveId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var order = 0; order < CanonicalStatusNames.Length; order++)
        {
            var name = CanonicalStatusNames[order];
            var statusId = DeterministicGuid($"dapi-demo:task-status:{projectKey}:{objectivePath}:{name}");
            if (db.TaskStatuses.Local.Any(s => s.Id == statusId)
                || await db.TaskStatuses.AnyAsync(s => s.Id == statusId, ct))
            {
                continue;
            }

            db.TaskStatuses.Add(new TaskStatusEntity
            {
                Id = statusId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ObjectiveId = objectiveId,
                Name = name,
                DisplayOrder = order,
                MarksTaskComplete = name == "Done",
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });
        }
    }

    private static async Task<long> SeedLeafTasksAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        Dictionary<string, Guid> categoryIdByName,
        DemoProjectTree tree,
        Project project,
        string path,
        DemoObjectiveNode node,
        Guid objectiveId,
        decimal leafAllocatedHours,
        int leafIndex,
        long nextTaskNumber,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var slotIndex = 0; slotIndex < WorkManagementDapiDemoData.LeafTaskSlots.Count; slotIndex++)
        {
            var slot = WorkManagementDapiDemoData.LeafTaskSlots[slotIndex];
            var statusName = slot.StatusName;
            var markComplete = slot.MarkComplete;
            if (slotIndex == 2 && leafIndex % 2 == 0)
            {
                statusName = "Done";
                markComplete = true;
            }

            var taskId = DeterministicGuid($"dapi-demo:task:{tree.ProjectKey}:{path}:{slotIndex}");
            if (db.WorkTasks.Local.Any(t => t.Id == taskId)
                || await db.WorkTasks.AnyAsync(t => t.Id == taskId, ct))
            {
                continue;
            }

            var statusId = DeterministicGuid($"dapi-demo:task-status:{tree.ProjectKey}:{path}:{statusName}");
            var estimatedHours = Math.Max(
                2m,
                Math.Round(leafAllocatedHours * slot.EstimatedHoursFraction, 0, MidpointRounding.AwayFromZero));

            var shortId = $"{tree.Identifier}-{nextTaskNumber}";
            nextTaskNumber++;

            var task = new WorkTask
            {
                Id = taskId,
                TenantId = DapiTenantId,
                ProjectId = project.Id,
                ObjectiveId = objectiveId,
                ShortId = shortId,
                Title = $"{node.Title} — {slot.TitleSuffix}",
                Description = $"Development demo task under {path}.",
                CategoryId = categoryIdByName[slot.CategoryName],
                StatusId = statusId,
                Priority = slot.Priority,
                EstimatedHours = estimatedHours,
                CompletedHours = markComplete ? estimatedHours : 0m,
                ProgressPercent = markComplete ? 100 : (statusName == "In Process" ? 40 : 0),
                StartedAt = statusName is "In Process" or "Review" or "Done" ? now.AddDays(-3) : null,
                CompletedAt = markComplete ? now.AddDays(-1) : null,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            };
            db.WorkTasks.Add(task);

            await SeedTaskAssignmentAsync(
                db,
                tree.ProjectKey,
                path,
                slotIndex,
                taskId,
                node.OwnerKey,
                employeeIdByPersonKey,
                now,
                ct);

            if (slot.AssignExtraMember && node.ExtraMemberKeys.Length > 0)
            {
                await SeedTaskAssignmentAsync(
                    db,
                    tree.ProjectKey,
                    path,
                    slotIndex,
                    taskId,
                    node.ExtraMemberKeys[0],
                    employeeIdByPersonKey,
                    now,
                    ct);
            }
        }

        return nextTaskNumber;
    }

    private static async Task SeedTaskAssignmentAsync(
        ApplicationDbContext db,
        string projectKey,
        string path,
        int slotIndex,
        Guid taskId,
        string personKey,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var assignmentId = DeterministicGuid(
            $"dapi-demo:task-assignment:{projectKey}:{path}:{slotIndex}:{personKey}");
        if (db.TaskAssignments.Local.Any(a => a.Id == assignmentId)
            || await db.TaskAssignments.AnyAsync(a => a.Id == assignmentId, ct))
        {
            return;
        }

        db.TaskAssignments.Add(new TaskAssignment
        {
            Id = assignmentId,
            TaskId = taskId,
            UserId = ResolveUserId(personKey),
            EmployeeId = employeeIdByPersonKey[personKey],
            AssignedById = DapiOwnerUserId,
            AssignedAt = now
        });
    }

    private static async Task SeedTaskCreationRequestsAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        Dictionary<string, Dictionary<string, Guid>> categoryIdsByProjectKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var spec in WorkManagementDapiDemoData.TaskCreationRequests)
        {
            var requestId = DeterministicGuid(
                $"dapi-demo:task-creation-request:{spec.ProjectKey}:{spec.ObjectivePath}:{spec.Title}");
            if (db.TaskCreationRequests.Local.Any(r => r.Id == requestId)
                || await db.TaskCreationRequests.AnyAsync(r => r.Id == requestId, ct))
            {
                continue;
            }

            var objectiveId = DeterministicGuid($"dapi-demo:objective:{spec.ProjectKey}:{spec.ObjectivePath}");
            var categoryId = categoryIdsByProjectKey[spec.ProjectKey][spec.CategoryName];
            var payload = new TaskCreationRequestPayload(
                spec.Title,
                spec.Description,
                categoryId,
                spec.Priority,
                DueDate: null,
                EstimatedHours: spec.EstimatedHours,
                StoryPoints: null,
                SprintId: Guid.Empty);

            db.TaskCreationRequests.Add(new TaskCreationRequest
            {
                Id = requestId,
                TenantId = DapiTenantId,
                ObjectiveId = objectiveId,
                RequestedByEmployeeId = employeeIdByPersonKey[spec.RequesterKey],
                PayloadJson = JsonSerializer.Serialize(payload),
                Status = TaskCreationRequestStatuses.Pending,
                CreatedById = ResolveUserId(spec.RequesterKey),
                CreatedAt = now
            });
        }
    }

    private static async Task SeedAllocationExtendsAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var spec in WorkManagementDapiDemoData.AllocationExtends)
        {
            var requestId = DeterministicGuid($"dapi-demo:allocation-extend:{spec.ProjectKey}:{spec.ObjectivePath}");
            if (db.ObjectiveChangeRequests.Local.Any(r => r.Id == requestId)
                || await db.ObjectiveChangeRequests.AnyAsync(r => r.Id == requestId, ct))
            {
                continue;
            }

            var objectiveId = DeterministicGuid($"dapi-demo:objective:{spec.ProjectKey}:{spec.ObjectivePath}");
            var objective = db.Objectives.Local.FirstOrDefault(o => o.Id == objectiveId)
                ?? await db.Objectives.FirstOrDefaultAsync(o => o.Id == objectiveId, ct);
            if (objective is null || objective.ReportingManagerId is null)
            {
                continue;
            }

            // Owner of the objective is the person who would call RequestAllocationExtension.
            var ownerPersonKey = FindOwnerKey(spec.ProjectKey, spec.ObjectivePath);
            if (ownerPersonKey is null)
            {
                continue;
            }

            var payload = new ExtendAllocationRequestPayload(spec.AdditionalHours, spec.Reason);
            db.ObjectiveChangeRequests.Add(new ObjectiveChangeRequest
            {
                Id = requestId,
                TenantId = DapiTenantId,
                ObjectiveId = objectiveId,
                RequestType = ObjectiveChangeRequestTypes.ExtendAllocation,
                RequestedById = employeeIdByPersonKey[ownerPersonKey],
                ReportingManagerId = objective.ReportingManagerId.Value,
                Status = ObjectiveChangeRequestStatuses.Pending,
                PayloadJson = JsonSerializer.Serialize(payload),
                CreatedById = ResolveUserId(ownerPersonKey),
                CreatedAt = now
            });
        }
    }

    private static string? FindOwnerKey(string projectKey, string objectivePath)
    {
        var tree = WorkManagementDapiDemoData.ProjectTrees.FirstOrDefault(t => t.ProjectKey == projectKey);
        if (tree is null)
        {
            return null;
        }

        DemoObjectiveNode? current = tree.Root;
        var segments = objectivePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments[0] is root title; walk from children for the rest.
        for (var i = 1; i < segments.Length; i++)
        {
            current = current.Children.FirstOrDefault(c => c.Title == segments[i]);
            if (current is null)
            {
                return null;
            }
        }

        return current?.OwnerKey;
    }

    private static IEnumerable<(string Path, DemoObjectiveNode Node, decimal AllocatedHours)> EnumerateLeaves(
        DemoObjectiveNode node,
        string path,
        decimal allocatedHours)
    {
        if (node.Children.Length == 0)
        {
            yield return (path, node, allocatedHours);
            yield break;
        }

        for (var i = 0; i < node.Children.Length; i++)
        {
            var child = node.Children[i];
            var childHours = ComputeChildHours(allocatedHours, i, node.Children.Length);
            foreach (var leaf in EnumerateLeaves(child, $"{path}/{child.Title}", childHours))
            {
                yield return leaf;
            }
        }
    }
}
