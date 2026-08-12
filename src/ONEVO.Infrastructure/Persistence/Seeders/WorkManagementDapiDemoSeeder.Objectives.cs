using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class WorkManagementDapiDemoSeeder
{
    private const int DateInsetBaseDays = 4;
    private const decimal HoursRatioStart = 0.70m;
    private const decimal HoursRatioStep = 0.05m;
    private const decimal HoursRatioFloor = 0.30m;
    private const decimal MinimumAllocatedHours = 10m;

    private static async Task SeedProjectsAndObjectivesAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var categoryIdByName = await SeedProjectCategoriesAsync(db, now, ct);

        foreach (var tree in WorkManagementDapiDemoData.ProjectTrees)
        {
            var projectId = DeterministicGuid($"dapi-demo:project:{tree.ProjectKey}");
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null)
            {
                project = new Project
                {
                    Id = projectId,
                    TenantId = DapiTenantId,
                    OwningLegalEntityId = DapiLegalEntityId,
                    CategoryId = categoryIdByName[tree.CategoryName],
                    Name = tree.ProjectName,
                    Identifier = tree.Identifier,
                    Description = $"Development demo project - {tree.ProjectName}.",
                    LeadId = DapiOwnerUserId,
                    StartDate = tree.StartDate,
                    TargetDate = tree.TargetDate,
                    AllocatedHours = tree.AllocatedHours,
                    CompletedHours = 0m,
                    IsActive = true,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                };
                db.Projects.Add(project);
            }

            await SeedObjectiveNodeAsync(
                db,
                employeeIdByPersonKey,
                tree.ProjectKey,
                projectId,
                node: tree.Root,
                parentObjectiveId: null,
                isDefault: true,
                path: tree.Root.Title,
                start: tree.StartDate,
                end: tree.TargetDate,
                allocatedHours: tree.AllocatedHours,
                now: now,
                ct: ct);
        }
    }

    private static async Task<Dictionary<string, Guid>> SeedProjectCategoriesAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var categoryIdByName = new Dictionary<string, Guid>();
        foreach (var name in WorkManagementDapiDemoData.ProjectCategoryNames)
        {
            var categoryId = DeterministicGuid($"dapi-demo:category:{name}");
            var category = await db.ProjectCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null)
            {
                db.ProjectCategories.Add(new ProjectCategory
                {
                    Id = categoryId,
                    TenantId = DapiTenantId,
                    Name = name,
                    IsActive = true,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                });
            }
            categoryIdByName[name] = categoryId;
        }
        return categoryIdByName;
    }

    private static async Task SeedObjectiveNodeAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        string projectKey,
        Guid projectId,
        DemoObjectiveNode node,
        Guid? parentObjectiveId,
        bool isDefault,
        string path,
        DateOnly start,
        DateOnly end,
        decimal allocatedHours,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var objectiveId = DeterministicGuid($"dapi-demo:objective:{projectKey}:{path}");
        var ownerUserId = ResolveUserId(node.OwnerKey);

        var depth = path.Count(c => c == '/') + 1;
        var completedRatio = Math.Min(0.70m, depth * 0.15m);
        var completedHours = Math.Round(allocatedHours * completedRatio, 0, MidpointRounding.AwayFromZero);

        var objective = await db.Objectives.FirstOrDefaultAsync(o => o.Id == objectiveId, ct);
        if (objective is null)
        {
            objective = new Objective
            {
                Id = objectiveId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ParentObjectiveId = parentObjectiveId,
                IsDefault = isDefault,
                Title = node.Title,
                Description = $"Development demo objective - {node.Title}.",
                OwnerId = ownerUserId,
                ReportingManagerId = DapiOwnerUserId,
                IsActive = true,
                StartDate = start,
                EndDate = end,
                Progress = allocatedHours == 0m ? 0m : Math.Round(completedHours / allocatedHours * 100m, 1),
                AllocatedHours = allocatedHours,
                CompletedHours = completedHours,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            };
            db.Objectives.Add(objective);
        }

        await SeedProjectMemberAsync(db, projectKey, path, projectId, objectiveId, node.OwnerKey, employeeIdByPersonKey, now, ct);
        foreach (var extraKey in node.ExtraMemberKeys)
        {
            await SeedProjectMemberAsync(db, projectKey, path, projectId, objectiveId, extraKey, employeeIdByPersonKey, now, ct);
        }

        for (var i = 0; i < node.Children.Length; i++)
        {
            var child = node.Children[i];
            var (childStart, childEnd) = ComputeChildDates(start, end, i);
            var childHours = ComputeChildHours(allocatedHours, i);

            await SeedObjectiveNodeAsync(
                db,
                employeeIdByPersonKey,
                projectKey,
                projectId,
                child,
                parentObjectiveId: objectiveId,
                isDefault: false,
                path: $"{path}/{child.Title}",
                start: childStart,
                end: childEnd,
                allocatedHours: childHours,
                now: now,
                ct: ct);
        }
    }

    private static async Task SeedProjectMemberAsync(
        ApplicationDbContext db,
        string projectKey,
        string objectivePath,
        Guid projectId,
        Guid objectiveId,
        string personKey,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var memberId = DeterministicGuid($"dapi-demo:member:{projectKey}:{objectivePath}:{personKey}");
        var existing = await db.ProjectMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (existing is not null)
        {
            return;
        }

        var userId = ResolveUserId(personKey);
        var employeeId = employeeIdByPersonKey[personKey];

        db.ProjectMembers.Add(new ProjectMember
        {
            Id = memberId,
            TenantId = DapiTenantId,
            ProjectId = projectId,
            ObjectiveId = objectiveId,
            UserId = userId,
            EmployeeId = employeeId,
            MembershipSource = ProjectMembershipSources.System,
            IsActive = true,
            JoinedAt = now,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });
    }

    private static Guid ResolveUserId(string personKey)
        => personKey == "dabi"
            ? DapiOwnerUserId
            : DeterministicGuid($"dapi-demo:user:{personKey}");

    private static (DateOnly Start, DateOnly End) ComputeChildDates(DateOnly parentStart, DateOnly parentEnd, int siblingIndex)
    {
        var inset = DateInsetBaseDays + siblingIndex;
        return (parentStart.AddDays(inset), parentEnd.AddDays(-inset));
    }

    private static decimal ComputeChildHours(decimal parentHours, int siblingIndex)
    {
        var ratio = HoursRatioStart - (siblingIndex * HoursRatioStep);
        if (ratio < HoursRatioFloor)
        {
            ratio = HoursRatioFloor;
        }
        var hours = Math.Round(parentHours * ratio, 0, MidpointRounding.AwayFromZero);
        return hours < MinimumAllocatedHours ? MinimumAllocatedHours : hours;
    }
}
