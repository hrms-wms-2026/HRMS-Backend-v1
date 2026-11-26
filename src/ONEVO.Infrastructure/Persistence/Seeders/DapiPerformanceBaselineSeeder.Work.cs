using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiPerformanceBaselineSeeder
{
    private const int MilestonesPerProject = 3;
    private const string ProjectIdentifierPrefix = "PF";

    private static readonly (string Name, string Category, string Color, bool Complete)[] Statuses =
    [
        ("To Do", TaskStatusCategories.NotStarted, "#94A3B8", false),
        ("In Process", TaskStatusCategories.Active, "#2563EB", false),
        ("Review", TaskStatusCategories.Active, "#7C3AED", false),
        ("Done", TaskStatusCategories.Done, "#16A34A", true)
    ];

    private static readonly string[] Priorities =
        [WorkTaskPriorities.Low, WorkTaskPriorities.Medium, WorkTaskPriorities.High, WorkTaskPriorities.Critical];

    private static readonly (string Name, string Kind, string? SystemKey)[] Categories =
    [
        ("task", TaskCategoryKinds.Task, null),
        ("bug", TaskCategoryKinds.Bug, null),
        ("story", TaskCategoryKinds.Story, null),
        ("feature", TaskCategoryKinds.Feature, null),
        (TaskCategorySystemKeys.Meeting, TaskCategoryKinds.Meeting, TaskCategorySystemKeys.Meeting)
    ];

    /// <summary>Adds projects up to <see cref="TargetProjects"/> and spreads tasks over them up to
    /// <see cref="TargetTasks"/>. Each project belongs to one squad: its lead runs it and its engineers are members.</summary>
    private static async Task<string> SeedWorkAsync(
        ApplicationDbContext db, SeededPeople people, DateTimeOffset now, CancellationToken ct)
    {
        var baseProjects = await db.Projects.CountAsync(
            p => p.TenantId == DapiTenantId && !p.Identifier.StartsWith(ProjectIdentifierPrefix), ct);
        var baseTasks = await db.WorkTasks.CountAsync(
            t => t.TenantId == DapiTenantId && !t.ShortId.StartsWith(ProjectIdentifierPrefix), ct);
        var projectCount = Math.Max(0, TargetProjects - baseProjects);
        var taskCount = Math.Max(0, TargetTasks - baseTasks);
        if (projectCount == 0)
            return "0 projects";

        var categoryIds = await db.ProjectCategories
            .Where(c => c.TenantId == DapiTenantId && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var existing = (await db.Projects
                .Where(p => p.TenantId == DapiTenantId && p.Identifier.StartsWith(ProjectIdentifierPrefix))
                .Select(p => p.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var squads = people.All
            .GroupBy(p => (p.Team, p.LeadIndex))
            .Select(g => (Lead: g.FirstOrDefault(p => p.IsLead), Members: g.ToList()))
            .Where(s => s.Lead is not null)
            .ToList();
        if (squads.Count == 0 || categoryIds.Count == 0)
            return "0 projects (no squads or project categories)";

        var today = DateOnly.FromDateTime(now.UtcDateTime.Date);
        var addedProjects = 0;
        var addedTasks = 0;
        for (var p = 0; p < projectCount; p++)
        {
            var projectId = Id($"project:{p}");
            if (existing.Contains(projectId))
                continue;

            var squad = squads[p % squads.Count];
            var tasksHere = (taskCount / projectCount) + (p < taskCount % projectCount ? 1 : 0);
            addedTasks += AddProject(db, p, projectId, squad.Lead!, squad.Members, categoryIds[p % categoryIds.Count],
                tasksHere, today, now);
            addedProjects++;
            await SaveBatchAsync(db, ct);
        }

        return $"{addedProjects} projects, {addedTasks} tasks";
    }

    private static int AddProject(
        ApplicationDbContext db, int index, Guid projectId, SeededPerson lead, IReadOnlyList<SeededPerson> members,
        Guid categoryId, int taskCount, DateOnly today, DateTimeOffset now)
    {
        var identifier = $"{ProjectIdentifierPrefix}{index + 1:00}";
        var start = today.AddDays(-AttendanceDays + (index * 5));
        var target = today.AddDays(90 + (index * 3));

        db.Projects.Add(new Project
        {
            Id = projectId,
            TenantId = DapiTenantId,
            OwningLegalEntityId = DapiLegalEntityId,
            CategoryId = categoryId,
            Name = $"Baseline Project {index + 1:00}",
            Identifier = identifier,
            Description = "Performance baseline data (task 06 Part B).",
            LeadId = lead.EmployeeId,
            StartDate = start,
            TargetDate = target,
            AllocatedHours = 6000m,
            CompletedHours = 0m,
            NextTaskNumber = taskCount + 1,
            IsActive = true,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });

        var objectiveIds = new List<Guid>();
        var rootId = Id($"objective:{index}:root");
        db.Objectives.Add(new Objective
        {
            Id = rootId,
            TenantId = DapiTenantId,
            ProjectId = projectId,
            IsDefault = true,
            Title = $"Baseline Project {index + 1:00}",
            OwnerId = lead.EmployeeId,
            IsActive = true,
            StartDate = start,
            EndDate = target,
            Progress = 0m,
            AllocatedHours = 5000m,
            CompletedHours = 0m,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });
        for (var m = 0; m < MilestonesPerProject; m++)
        {
            var milestoneId = Id($"objective:{index}:m{m}");
            objectiveIds.Add(milestoneId);
            db.Objectives.Add(new Objective
            {
                Id = milestoneId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ParentObjectiveId = rootId,
                IsDefault = false,
                Title = $"Milestone {m + 1}",
                OwnerId = lead.EmployeeId,
                IsActive = true,
                StartDate = start.AddDays(m * 120),
                EndDate = start.AddDays((m + 1) * 120),
                Progress = 0m,
                AllocatedHours = 1300m,
                CompletedHours = 0m,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });
        }

        foreach (var objectiveId in objectiveIds.Prepend(rootId))
        {
            foreach (var member in members)
            {
                db.ProjectMembers.Add(new ProjectMember
                {
                    Id = Id($"member:{index}:{objectiveId:N}:{member.EmployeeId:N}"),
                    TenantId = DapiTenantId,
                    ProjectId = projectId,
                    ObjectiveId = objectiveId,
                    EmployeeId = member.EmployeeId,
                    MembershipSource = ProjectMembershipSources.System,
                    IsActive = true,
                    JoinedAt = now,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                });
            }
        }

        db.ProjectVersions.Add(new ProjectVersion
        {
            Id = Id($"version:{index}"),
            TenantId = DapiTenantId,
            ProjectId = projectId,
            Name = "Initial Release",
            StatusId = VersionStatusIds.Planned,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });

        var statusIds = new Guid[Statuses.Length];
        for (var s = 0; s < Statuses.Length; s++)
        {
            var (name, category, color, complete) = Statuses[s];
            statusIds[s] = Id($"status:{index}:{name}");
            db.TaskStatuses.Add(new TaskStatusEntity
            {
                Id = statusIds[s],
                TenantId = DapiTenantId,
                ProjectId = projectId,
                Name = name,
                DisplayOrder = s,
                Category = category,
                Color = color,
                MarksTaskComplete = complete,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });
        }

        var categoryIds = new Guid[Categories.Length];
        for (var c = 0; c < Categories.Length; c++)
        {
            var (name, kind, systemKey) = Categories[c];
            categoryIds[c] = Id($"task-category:{index}:{name}");
            db.TaskCategories.Add(new TaskCategory
            {
                Id = categoryIds[c],
                TenantId = DapiTenantId,
                ProjectId = projectId,
                Name = name,
                DisplayOrder = c,
                SystemKey = systemKey,
                Kind = kind,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            });
        }

        for (var t = 0; t < taskCount; t++)
        {
            var seed = (index * 100_000) + t;
            var roll = Pick(seed, 100);
            var status = roll < 35 ? 0 : roll < 60 ? 1 : roll < 70 ? 2 : 3;
            var done = status == 3;
            var hours = 2m + Pick(seed + 1, 7);
            var created = now.AddDays(-Pick(seed + 2, AttendanceDays));
            var assignee = members[Pick(seed + 3, members.Count)];
            var taskId = Id($"task:{index}:{t}");

            db.WorkTasks.Add(new WorkTask
            {
                Id = taskId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ObjectiveId = objectiveIds[t % objectiveIds.Count],
                ShortId = $"{identifier}-{t + 1}",
                Title = $"Baseline task {t + 1} of {identifier}",
                CategoryId = categoryIds[Pick(seed + 4, 4)],
                StatusId = statusIds[status],
                Priority = Priorities[Pick(seed + 5, Priorities.Length)],
                EstimatedHours = hours,
                CompletedHours = done ? hours : 0m,
                ProgressPercent = done ? 100 : status == 1 ? 40 : status == 2 ? 80 : 0,
                StartedAt = status > 0 ? created.AddDays(1) : null,
                CompletedAt = done ? created.AddDays(3) : null,
                CreatedById = DapiOwnerUserId,
                CreatedAt = created
            });
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = Id($"task-assignment:{index}:{t}"),
                TaskId = taskId,
                UserId = assignee.UserId,
                EmployeeId = assignee.EmployeeId,
                AssignedById = DapiOwnerUserId,
                AssignedAt = created
            });
        }

        return taskCount;
    }
}
