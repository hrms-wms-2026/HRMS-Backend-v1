# My Team — Part 3: Work I Lead scope + `GET /api/v1/work/led-progress`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Team Progress. Show task-progress totals over the modules the caller **effectively
owns**, including their sub-modules, plus the top overdue tasks. Only Work Management ownership
rules apply; organizational coverage is never used.

**Architecture:**
- **`LedWorkScopeBuilder` (pure).** Picks the caller's *head modules*: owned modules with no owned
  ancestor. It expands each head to its sub-module subtree, then intersects the result with the
  modules the caller can actually open through membership.
- **`WorkLeadershipService` (WorkManagement).** Loads the inputs the builder needs in a constant
  number of queries.
- **`GetLedWorkProgressQueryHandler`.**
  1. Classifies top-level tasks with the shared `TaskProgressClassifier`.
  2. Rolls counts up per head module and per project.
  3. Returns the top N overdue tasks.
- **`WorkLeadershipController`.** Exposes the query under the standard Work module gate.

**Tech Stack:** .NET 10, EF Core, MediatR, xUnit, FluentAssertions, Testcontainers.

**Spec:** `docs/superpowers/specs/next/2026-09-29-my-team-dashboard-design.md` §8.3 (Team Progress), §10 rules 2-4, AC-2, AC-3, AC-6.

## Global Constraints

- Code under `Features/WorkManagement/**` must not reference any of these:
  - `IEmployeeAuthorityResolver`
  - `IEmployeeVisibilityScopeResolver`
  - `IEmployeeHierarchyClosureRepository`
  - `ManagementCoverageRecord`

  Task 5 adds an architecture test that enforces this.
- Plain module membership never makes someone a lead. Membership is only used to *restrict* the
  scope (intersection step), never to *qualify* someone.
- Top-level tasks only (`ParentTaskId == null`).
- No "Blocked" metric.
- Caller identity and legal entity both come from `IEmployeeRepository.GetDefaultForUserAsync`
  (the CoreHr one): one query, and it matches the attendance handlers. For single-legal-entity
  users it is the same employee `ICallerIdentityResolver` returns. For multi-legal-entity users it
  is the active legal entity's employee, which is what V1's single-legal-entity scope requires.
- Budget: at most 9 DB commands per request, regardless of the number of projects, modules or
  tasks (Task 4 test).
- No new permission codes. The only gate is `[RequireAnyModule("worksync_foundation","projects","objectives_milestones","tasks","boards","planning_sprints")]`.

## Prerequisites (from Part 2)

`ONEVO.Application.Features.WorkManagement.Objectives.Services`:
- `readonly record struct ObjectiveTreeNode(Guid Id, Guid? ParentObjectiveId)`
- `static HashSet<Guid> ObjectiveTreeExpander.ExpandWithDescendants(IEnumerable<Guid> roots, IReadOnlyCollection<ObjectiveTreeNode> activeNodes)`
- `static IReadOnlyDictionary<Guid, Guid?> ObjectiveTreeExpander.ParentMap(IReadOnlyCollection<ObjectiveTreeNode> nodes)`

`ONEVO.Application.Features.WorkManagement.Tasks.Services`:
- `enum TaskProgressBucket { Completed, Overdue, InProgress, NotStarted }`
- `static TaskProgressBucket TaskProgressClassifier.Classify(bool marksTaskComplete, int progressPercent, DateOnly? dueDate, DateOnly today)`

---

### Task 1: Pure `LedWorkScopeBuilder`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Services/LedWorkScopeBuilder.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/LedWorkScopeBuilderTests.cs`

**Interfaces:**
- Produces, in namespace `ONEVO.Application.Features.WorkManagement.Leadership.Services`:

```csharp
public sealed record LedObjectiveRow(Guid Id, Guid ProjectId, Guid? ParentObjectiveId, string Title, bool IsDefault, DateOnly EndDate);
public sealed record LedHeadModule(Guid ObjectiveId, Guid ProjectId, string Title, bool IsRootModule, DateOnly EndDate, IReadOnlySet<Guid> SubtreeObjectiveIds);
public sealed record LedWorkScope(IReadOnlyList<LedHeadModule> HeadModules, IReadOnlyList<Guid> DroppedOwnedObjectiveIds)
{
    public static readonly LedWorkScope Empty;
    public IReadOnlySet<Guid> AllObjectiveIds { get; }
}
public static class LedWorkScopeBuilder
{
    public static LedWorkScope Build(
        IReadOnlyCollection<Guid> ownedObjectiveIds,
        IReadOnlyCollection<LedObjectiveRow> activeTree,
        IReadOnlyCollection<Guid> membershipObjectiveIds);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/LedWorkScopeBuilderTests.cs
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class LedWorkScopeBuilderTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly End = new(2026, 12, 31);

    private static LedObjectiveRow Row(Guid id, Guid? parent, bool isDefault = false) => new(id, Project, parent, $"M-{id:N}"[..6], isDefault, End);

    [Fact]
    public void Nested_owned_modules_roll_up_into_the_topmost_head_only()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), a1 = Guid.NewGuid(), a11 = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(a1, a), Row(a11, a1) };

        var scope = LedWorkScopeBuilder.Build(new[] { a, a1 }, tree, membershipObjectiveIds: new[] { a });

        var head = Assert.Single(scope.HeadModules);
        Assert.Equal(a, head.ObjectiveId);
        Assert.Equal(new[] { a, a1, a11 }.OrderBy(x => x), head.SubtreeObjectiveIds.OrderBy(x => x));
        Assert.False(head.IsRootModule);
    }

    [Fact]
    public void Sibling_owned_modules_are_separate_heads()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(b, root) };

        var scope = LedWorkScopeBuilder.Build(new[] { a, b }, tree, new[] { a, b });

        Assert.Equal(2, scope.HeadModules.Count);
        Assert.Empty(scope.HeadModules[0].SubtreeObjectiveIds.Intersect(scope.HeadModules[1].SubtreeObjectiveIds));
    }

    [Fact]
    public void Root_owner_heads_the_whole_project()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(b, root) };

        var scope = LedWorkScopeBuilder.Build(new[] { root }, tree, new[] { root });

        var head = Assert.Single(scope.HeadModules);
        Assert.True(head.IsRootModule);
        Assert.Equal(3, head.SubtreeObjectiveIds.Count);
    }

    [Fact]
    public void Owner_without_membership_access_is_dropped_never_leaked()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root) };

        var scope = LedWorkScopeBuilder.Build(new[] { a }, tree, membershipObjectiveIds: Array.Empty<Guid>());

        Assert.Empty(scope.HeadModules);
        Assert.Equal(new[] { a }, scope.DroppedOwnedObjectiveIds);
    }

    [Fact]
    public void Membership_on_an_ancestor_grants_access_to_the_owned_subtree()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), a1 = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(a1, a) };

        var scope = LedWorkScopeBuilder.Build(new[] { a }, tree, membershipObjectiveIds: new[] { root });

        Assert.Equal(new[] { a, a1 }.OrderBy(x => x), Assert.Single(scope.HeadModules).SubtreeObjectiveIds.OrderBy(x => x));
    }

    [Fact]
    public void Nothing_owned_yields_empty_scope()
    {
        var scope = LedWorkScopeBuilder.Build(Array.Empty<Guid>(), Array.Empty<LedObjectiveRow>(), Array.Empty<Guid>());
        Assert.Same(LedWorkScope.Empty, scope);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~LedWorkScopeBuilderTests"`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Leadership/Services/LedWorkScopeBuilder.cs
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

public sealed record LedObjectiveRow(Guid Id, Guid ProjectId, Guid? ParentObjectiveId, string Title, bool IsDefault, DateOnly EndDate);

public sealed record LedHeadModule(
    Guid ObjectiveId, Guid ProjectId, string Title, bool IsRootModule, DateOnly EndDate, IReadOnlySet<Guid> SubtreeObjectiveIds);

public sealed record LedWorkScope(IReadOnlyList<LedHeadModule> HeadModules, IReadOnlyList<Guid> DroppedOwnedObjectiveIds)
{
    public static readonly LedWorkScope Empty = new(Array.Empty<LedHeadModule>(), Array.Empty<Guid>());

    public IReadOnlySet<Guid> AllObjectiveIds { get; } =
        HeadModules.SelectMany(head => head.SubtreeObjectiveIds).ToHashSet();
}

/// <summary>"Work I Lead" (spec §8.3.2): the modules the caller effectively OWNS - owner of the
/// module or of any ancestor module, the same test MilestoneMembershipCoordinator.IsEffectiveOwnerAsync
/// and SprintAccessService use - evaluated in bulk. Heads are owned modules with no owned ancestor,
/// so nested ownership never double counts. Membership is used ONLY to restrict (step 7): an owned
/// subtree the caller cannot open through TaskAccessResolver's membership rule is dropped rather
/// than leaked. Plain membership never qualifies anyone as a lead.</summary>
public static class LedWorkScopeBuilder
{
    public static LedWorkScope Build(
        IReadOnlyCollection<Guid> ownedObjectiveIds,
        IReadOnlyCollection<LedObjectiveRow> activeTree,
        IReadOnlyCollection<Guid> membershipObjectiveIds)
    {
        if (ownedObjectiveIds.Count == 0)
            return LedWorkScope.Empty;

        var owned = ownedObjectiveIds.ToHashSet();
        var nodes = activeTree.Select(row => new ObjectiveTreeNode(row.Id, row.ParentObjectiveId)).ToList();
        var parentOf = ObjectiveTreeExpander.ParentMap(nodes);
        var rowsById = activeTree.ToDictionary(row => row.Id);
        var accessible = ObjectiveTreeExpander.ExpandWithDescendants(membershipObjectiveIds, nodes);

        var heads = new List<LedHeadModule>();
        var dropped = new List<Guid>();
        foreach (var objectiveId in owned.OrderBy(id => id))
        {
            if (!rowsById.TryGetValue(objectiveId, out var row) || HasOwnedAncestor(objectiveId, owned, parentOf))
                continue;

            if (!accessible.Contains(objectiveId))
            {
                dropped.Add(objectiveId);
                continue;
            }

            var subtree = ObjectiveTreeExpander.ExpandWithDescendants(new[] { objectiveId }, nodes);
            subtree.IntersectWith(accessible);
            heads.Add(new LedHeadModule(row.Id, row.ProjectId, row.Title, row.IsDefault, row.EndDate, subtree));
        }

        return heads.Count == 0 && dropped.Count == 0 ? LedWorkScope.Empty : new LedWorkScope(heads, dropped);
    }

    private static bool HasOwnedAncestor(Guid objectiveId, IReadOnlySet<Guid> owned, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var visited = new HashSet<Guid> { objectiveId };
        var cursor = parentOf.GetValueOrDefault(objectiveId);
        while (cursor is Guid parent && visited.Add(parent))
        {
            if (owned.Contains(parent))
                return true;
            cursor = parentOf.GetValueOrDefault(parent);
        }
        return false;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~LedWorkScopeBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Leadership tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership
git commit -m "feat(work): pure LedWorkScopeBuilder for Work I Lead head modules"
```

---

### Task 2: Repository reads for the led scope and progress

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs`
- Modify: `tests/ONEVO.Tests.Integration/MyTeam/MyTeamDb.cs` (Work seeding helpers)
- Test: `tests/ONEVO.Tests.Integration/MyTeam/WorkLeadershipRepositoryIntegrationTests.cs`

**Interfaces:**
- Consumes: `MyTeamDb` (Part 1):
  - `CreateAsync()`, `TenantId`, `LegalEntityId`, `NewContext(counter?)`
  - `AddEmployee(db, departmentId?, legalEntityId?, lastName?)`
- Produces (repositories):

```csharp
// IObjectiveRepository
Task<bool> AnyActiveOwnedAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default);
Task<IReadOnlyList<(Guid ObjectiveId, Guid ProjectId)>> ListActiveOwnedIdsAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default);
Task<IReadOnlyList<LedObjectiveRow>> ListActiveTreeForProjectsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
// IProjectMemberRepository
Task<IReadOnlyList<Guid>> ListActiveMembershipObjectiveIdsAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
// IProjectRepository
Task<IReadOnlyList<Project>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
// IWorkTaskRepository
Task<IReadOnlyList<LedTaskProgressRow>> ListTopLevelProgressRowsAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default);
Task<IReadOnlyList<LedOverdueTaskRow>> ListTopLevelOverdueAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateOnly today, int take, CancellationToken ct = default);
```

- Records, in `IWorkTaskRepository.cs` (namespace `ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces`):

```csharp
public sealed record LedTaskProgressRow(Guid ObjectiveId, bool MarksTaskComplete, DateOnly? DueDate, int ProgressPercent);
public sealed record LedOverdueTaskRow(Guid TaskId, string ShortId, string Title, Guid ProjectId, Guid ObjectiveId, DateOnly DueDate, IReadOnlyList<Guid> AssigneeEmployeeIds);
```

- `MyTeamDb` Work helpers:
  - `(Guid ProjectId, Guid RootObjectiveId) AddProject(ApplicationDbContext db, Guid leadEmployeeId, bool active = true, Guid? legalEntityId = null)`
  - `Guid AddModule(ApplicationDbContext db, Guid projectId, Guid parentObjectiveId, Guid ownerEmployeeId, bool achieved = false)`
  - `void AddMember(ApplicationDbContext db, Guid projectId, Guid objectiveId, Guid employeeId)`
  - `Guid AddStatus(ApplicationDbContext db, Guid projectId, bool marksComplete)`
  - `WorkTask AddTask(ApplicationDbContext db, Guid projectId, Guid objectiveId, Guid statusId, int progress, DateOnly? due, Guid? parentTaskId = null, Guid? assigneeEmployeeId = null)`

- [ ] **Step 1: Add the Work seeding helpers to `MyTeamDb`**

Add these `using` lines:
- `using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;`
- `using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;`
- `using ONEVO.Domain.Features.WorkManagement.Projects.Entities;`
- `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;`
- `using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;`

Then add:

```csharp
    private readonly Dictionary<Guid, Guid> _taskCategoryByProject = new();

    public (Guid ProjectId, Guid RootObjectiveId) AddProject(ApplicationDbContext db, Guid leadEmployeeId, bool active = true, Guid? legalEntityId = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var project = new Project
        {
            Id = Guid.NewGuid(), TenantId = TenantId, OwningLegalEntityId = legalEntityId ?? LegalEntityId,
            CategoryId = Guid.NewGuid(), Name = $"Project {Guid.NewGuid():N}"[..16],
            Identifier = $"P{Guid.NewGuid():N}"[..8].ToUpperInvariant(), LeadId = leadEmployeeId,
            StartDate = today.AddMonths(-1), TargetDate = today.AddMonths(3), IsActive = active,
        };
        db.Projects.Add(project);
        var root = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = project.Id, ParentObjectiveId = null, IsDefault = true,
            Title = project.Name, OwnerId = leadEmployeeId, IsActive = true,
            StartDate = project.StartDate, EndDate = project.TargetDate,
        };
        db.Objectives.Add(root);
        AddMember(db, project.Id, root.Id, leadEmployeeId);
        var category = new TaskCategory { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = project.Id, Name = "General", DisplayOrder = 0 };
        db.TaskCategories.Add(category);
        _taskCategoryByProject[project.Id] = category.Id;
        return (project.Id, root.Id);
    }

    public Guid AddModule(ApplicationDbContext db, Guid projectId, Guid parentObjectiveId, Guid ownerEmployeeId, bool achieved = false)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var module = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, ParentObjectiveId = parentObjectiveId,
            Title = $"Module {Guid.NewGuid():N}"[..14], OwnerId = ownerEmployeeId, ReportingManagerId = ownerEmployeeId,
            IsActive = true, IsAchieved = achieved, StartDate = today.AddMonths(-1), EndDate = today.AddMonths(2),
        };
        db.Objectives.Add(module);
        return module.Id;
    }

    public void AddMember(ApplicationDbContext db, Guid projectId, Guid objectiveId, Guid employeeId)
        => db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, ObjectiveId = objectiveId,
            EmployeeId = employeeId, MembershipSource = ProjectMembershipSources.System, IsActive = true,
        });

    public Guid AddStatus(ApplicationDbContext db, Guid projectId, bool marksComplete)
    {
        var status = new TaskStatusEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId,
            Name = marksComplete ? "Done" : "Doing", DisplayOrder = marksComplete ? 9 : 1,
            MarksTaskComplete = marksComplete,
            Category = marksComplete ? TaskStatusCategories.Done : TaskStatusCategories.Active,
        };
        db.TaskStatuses.Add(status);
        return status.Id;
    }

    public WorkTask AddTask(ApplicationDbContext db, Guid projectId, Guid objectiveId, Guid statusId, int progress, DateOnly? due,
        Guid? parentTaskId = null, Guid? assigneeEmployeeId = null)
    {
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId, ObjectiveId = objectiveId, ParentTaskId = parentTaskId,
            ShortId = $"T-{Guid.NewGuid():N}"[..8], Title = $"Task {Guid.NewGuid():N}"[..12],
            CategoryId = _taskCategoryByProject[projectId], StatusId = statusId, Priority = WorkTaskPriorities.Medium,
            DueDate = due, ProgressPercent = progress,
        };
        db.WorkTasks.Add(task);
        if (assigneeEmployeeId is Guid assignee)
        {
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = Guid.NewGuid(), TaskId = task.Id, EmployeeId = assignee, UserId = Guid.NewGuid(),
                AssignedById = assignee, AssignedAt = DateTimeOffset.UtcNow,
            });
        }
        return task;
    }
```

- [ ] **Step 2: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/WorkLeadershipRepositoryIntegrationTests.cs
using FluentAssertions;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class WorkLeadershipRepositoryIntegrationTests
{
    [Fact]
    public async Task Owned_tree_membership_project_and_task_reads_are_scoped_correctly()
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid leadId, strangerId, projectId, rootId, moduleId, subId, achievedId, otherLeProjectId, inactiveProjectId;
        Guid topOverdueTaskId;
        await using (var db = helper.NewContext())
        {
            var lead = helper.AddEmployee(db);
            var stranger = helper.AddEmployee(db);
            leadId = lead.Id; strangerId = stranger.Id;
            (projectId, rootId) = helper.AddProject(db, strangerId);
            moduleId = helper.AddModule(db, projectId, rootId, leadId);
            subId = helper.AddModule(db, projectId, moduleId, strangerId);
            achievedId = helper.AddModule(db, projectId, rootId, leadId, achieved: true);
            helper.AddMember(db, projectId, moduleId, leadId);
            var otherLe = new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity { Id = Guid.NewGuid(), TenantId = helper.TenantId, Name = "Other LE" };
            db.LegalEntities.Add(otherLe);
            (otherLeProjectId, _) = helper.AddProject(db, leadId, legalEntityId: otherLe.Id);
            (inactiveProjectId, _) = helper.AddProject(db, leadId, active: false);

            var doing = helper.AddStatus(db, projectId, marksComplete: false);
            var done = helper.AddStatus(db, projectId, marksComplete: true);
            var parent = helper.AddTask(db, projectId, moduleId, doing, 0, today.AddDays(-5), assigneeEmployeeId: strangerId);
            topOverdueTaskId = parent.Id;
            helper.AddTask(db, projectId, moduleId, doing, 0, today.AddDays(-9), parentTaskId: parent.Id); // subtask: excluded
            helper.AddTask(db, projectId, subId, done, 100, today.AddDays(-20));                          // complete: not overdue
            helper.AddTask(db, projectId, subId, doing, 50, today.AddDays(3));
            await db.SaveChangesAsync();
        }

        await using var read = helper.NewContext();
        var objectives = new EfObjectiveRepository(read);
        var members = new EfProjectMemberRepository(read);
        var projects = new EfProjectRepository(read);
        var tasks = new EfWorkTaskRepository(read);

        (await objectives.AnyActiveOwnedAsync(helper.TenantId, leadId, helper.LegalEntityId)).Should().BeTrue();
        (await objectives.ListActiveOwnedIdsAsync(helper.TenantId, leadId, helper.LegalEntityId))
            .Select(x => x.ObjectiveId)
            .Should().BeEquivalentTo(new[] { moduleId },
                "achieved modules are excluded, and the lead's root modules in the other-legal-entity and inactive projects are filtered out");
        (await objectives.ListActiveTreeForProjectsAsync(helper.TenantId, new[] { projectId }))
            .Select(r => r.Id).Should().BeEquivalentTo(new[] { rootId, moduleId, subId, achievedId });
        (await members.ListActiveMembershipObjectiveIdsAsync(helper.TenantId, leadId, new[] { projectId }))
            .Should().BeEquivalentTo(new[] { moduleId });
        (await projects.ListByIdsAsync(helper.TenantId, new[] { projectId })).Should().ContainSingle(p => p.Id == projectId);

        var rows = await tasks.ListTopLevelProgressRowsAsync(helper.TenantId, new[] { moduleId, subId });
        rows.Should().HaveCount(3, "the subtask is excluded");
        var overdue = await tasks.ListTopLevelOverdueAsync(helper.TenantId, new[] { moduleId, subId }, today, take: 10);
        overdue.Should().ContainSingle().Which.TaskId.Should().Be(topOverdueTaskId);
        overdue[0].AssigneeEmployeeIds.Should().Equal(strangerId);
    }
}
```

`ListActiveTreeForProjectsAsync` returns active objectives, **including achieved ones**. The
subtree of a head keeps its achieved sub-modules' tasks. Only the *head* candidates exclude
achieved modules.

- [ ] **Step 3: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkLeadershipRepositoryIntegrationTests"`
Expected: compile errors, methods not found.

- [ ] **Step 4: Add the interface members**

`IObjectiveRepository.cs`: add `using ONEVO.Application.Features.WorkManagement.Leadership.Services;` and

```csharp
    /// <summary>My Team capability probe (spec §7.3 leadsWork): does the employee own any active,
    /// not-achieved module in an active project of this legal entity? One EXISTS.</summary>
    Task<bool> AnyActiveOwnedAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default);

    /// <summary>Active, not-achieved modules the employee owns directly, in active projects of this
    /// legal entity, with their project ids - the candidate heads for Work I Lead (spec §8.3.2 step 2).</summary>
    Task<IReadOnlyList<(Guid ObjectiveId, Guid ProjectId)>> ListActiveOwnedIdsAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default);

    /// <summary>Every active objective (achieved included) of the given projects, as tree rows, in
    /// one query (spec §8.3.2 step 3).</summary>
    Task<IReadOnlyList<LedObjectiveRow>> ListActiveTreeForProjectsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
```

`IProjectMemberRepository.cs`:

```csharp
    /// <summary>Objective ids the employee holds an ACTIVE membership row on, across the given
    /// projects, in one query - the no-leak intersection input for Work I Lead (spec §8.3.2 step 7).</summary>
    Task<IReadOnlyList<Guid>> ListActiveMembershipObjectiveIdsAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
```

`IProjectRepository.cs`:

```csharp
    Task<IReadOnlyList<Project>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default);
```

`IWorkTaskRepository.cs`: add the two records from **Interfaces** at the top of the file, next to
`TaskProgressRow`, and these members:

```csharp
    /// <summary>Top-level (ParentTaskId == null) task progress inputs for the given objectives,
    /// classified in memory by TaskProgressClassifier so Team Progress and the personal widget share
    /// one rule (spec §8.3.3).</summary>
    Task<IReadOnlyList<LedTaskProgressRow>> ListTopLevelProgressRowsAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default);

    /// <summary>Top-level overdue tasks (not in a MarksTaskComplete status, ProgressPercent &lt; 100,
    /// DueDate &lt; today) of the given objectives, most overdue first, with their assignees.</summary>
    Task<IReadOnlyList<LedOverdueTaskRow>> ListTopLevelOverdueAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateOnly today, int take, CancellationToken ct = default);
```

- [ ] **Step 5: Implement the repository methods**

`EfObjectiveRepository.cs`: add `using ONEVO.Application.Features.WorkManagement.Leadership.Services;` and

```csharp
    public async Task<bool> AnyActiveOwnedAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default)
        => await OwnedActive(tenantId, ownerEmployeeId, legalEntityId).AnyAsync(ct);

    public async Task<IReadOnlyList<(Guid ObjectiveId, Guid ProjectId)>> ListActiveOwnedIdsAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default)
        => (await OwnedActive(tenantId, ownerEmployeeId, legalEntityId).Select(o => new { o.Id, o.ProjectId }).ToListAsync(ct))
            .Select(x => (x.Id, x.ProjectId)).ToList();

    private IQueryable<Objective> OwnedActive(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId) =>
        from o in _db.Objectives.AsNoTracking()
        join p in _db.Projects.AsNoTracking() on o.ProjectId equals p.Id
        where o.TenantId == tenantId && p.TenantId == tenantId
            && o.OwnerId == ownerEmployeeId && o.IsActive && !o.IsAchieved
            && p.IsActive && p.OwningLegalEntityId == legalEntityId
        select o;

    public async Task<IReadOnlyList<LedObjectiveRow>> ListActiveTreeForProjectsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return Array.Empty<LedObjectiveRow>();
        var ids = projectIds.ToList();
        return await _db.Objectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId && ids.Contains(o.ProjectId) && o.IsActive)
            .Select(o => new LedObjectiveRow(o.Id, o.ProjectId, o.ParentObjectiveId, o.Title, o.IsDefault, o.EndDate))
            .ToListAsync(ct);
    }
```

`EfProjectMemberRepository.cs`:

```csharp
    public async Task<IReadOnlyList<Guid>> ListActiveMembershipObjectiveIdsAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return Array.Empty<Guid>();
        var ids = projectIds.ToList();
        return await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.EmployeeId == employeeId && m.IsActive && ids.Contains(m.ProjectId))
            .Select(m => m.ObjectiveId)
            .Distinct()
            .ToListAsync(ct);
    }
```

`EfProjectRepository.cs` (use the context field name that file already uses):

```csharp
    public async Task<IReadOnlyList<Project>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return Array.Empty<Project>();
        var ids = projectIds.ToList();
        return await _db.Projects.AsNoTracking().Where(p => p.TenantId == tenantId && ids.Contains(p.Id)).ToListAsync(ct);
    }
```

`EfWorkTaskRepository.cs`:

```csharp
    public async Task<IReadOnlyList<LedTaskProgressRow>> ListTopLevelProgressRowsAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return Array.Empty<LedTaskProgressRow>();
        var ids = objectiveIds.ToList();
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId && t.ParentTaskId == null && ids.Contains(t.ObjectiveId)
            select new LedTaskProgressRow(t.ObjectiveId, s.MarksTaskComplete, t.DueDate, t.ProgressPercent)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LedOverdueTaskRow>> ListTopLevelOverdueAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, DateOnly today, int take, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0 || take <= 0)
            return Array.Empty<LedOverdueTaskRow>();
        var ids = objectiveIds.ToList();
        // Same predicate as TaskProgressClassifier's Overdue bucket: not complete (status column or
        // 100%), and DueDate strictly before today.
        var rows = await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId && t.ParentTaskId == null && ids.Contains(t.ObjectiveId)
                && !s.MarksTaskComplete && t.ProgressPercent < 100
                && t.DueDate != null && t.DueDate < today
            orderby t.DueDate, t.ShortId
            select new { t.Id, t.ShortId, t.Title, t.ProjectId, t.ObjectiveId, DueDate = t.DueDate!.Value }
        ).Take(take).ToListAsync(ct);

        var taskIds = rows.Select(r => r.Id).ToList();
        var assignees = taskIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _db.TaskAssignments.AsNoTracking().Where(a => taskIds.Contains(a.TaskId))
                    .Select(a => new { a.TaskId, a.EmployeeId }).ToListAsync(ct))
                .GroupBy(a => a.TaskId).ToDictionary(g => g.Key, g => g.Select(a => a.EmployeeId).ToList());

        return rows.Select(r => new LedOverdueTaskRow(
            r.Id, r.ShortId, r.Title, r.ProjectId, r.ObjectiveId, r.DueDate,
            assignees.TryGetValue(r.Id, out var ids2) ? ids2 : new List<Guid>())).ToList();
    }
```

- [ ] **Step 6: Run the test**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkLeadershipRepositoryIntegrationTests"`
Expected: PASS.

If `SaveChangesAsync` rejects a seed row because of a missing required column, add that field in
the `MyTeamDb` helper; check the matching file in
`src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/`. Do not change entity
configuration.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement tests/ONEVO.Tests.Integration/MyTeam
git commit -m "feat(work): batched repository reads for the Work I Lead scope"
```

---

### Task 3: `IWorkLeadershipService`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Services/IWorkLeadershipService.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Services/WorkLeadershipService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (next to `ITaskStatusChangeAccessService`, around L377)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/WorkLeadershipServiceTests.cs`

**Interfaces:**
- Consumes: the Task 2 repository members and `LedWorkScopeBuilder` (Task 1).
- Produces (Parts 5 and 6 also call it):

```csharp
public interface IWorkLeadershipService
{
    Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);
    Task<LedWorkScope> ResolveLedScopeAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/WorkLeadershipServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class WorkLeadershipServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Me = Guid.NewGuid(), Le = Guid.NewGuid(), Project = Guid.NewGuid();

    [Fact]
    public async Task No_owned_modules_short_circuits_without_loading_trees()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<(Guid, Guid)>());
        var members = new Mock<IProjectMemberRepository>(MockBehavior.Strict);
        var sut = new WorkLeadershipService(objectives.Object, members.Object, NullLogger<WorkLeadershipService>.Instance);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Same(LedWorkScope.Empty, scope);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Builds_the_scope_from_one_tree_read_and_one_membership_read()
    {
        Guid root = Guid.NewGuid(), module = Guid.NewGuid();
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { (module, Project) });
        objectives.Setup(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LedObjectiveRow(root, Project, null, "Root", true, new DateOnly(2026, 12, 1)),
                new LedObjectiveRow(module, Project, root, "Payments", false, new DateOnly(2026, 11, 1)),
            });
        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveMembershipObjectiveIdsAsync(Tenant, Me, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { module });
        var sut = new WorkLeadershipService(objectives.Object, members.Object, NullLogger<WorkLeadershipService>.Instance);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Equal(module, Assert.Single(scope.HeadModules).ObjectiveId);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkLeadershipServiceTests"`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Leadership/Services/IWorkLeadershipService.cs
namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>The single home of "Work I Lead" (spec §8.3, §9.2). Relationship-based only: never
/// touches management coverage.</summary>
public interface IWorkLeadershipService
{
    Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);

    Task<LedWorkScope> ResolveLedScopeAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Leadership/Services/WorkLeadershipService.cs
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

public sealed class WorkLeadershipService(
    IObjectiveRepository objectives,
    IProjectMemberRepository members,
    ILogger<WorkLeadershipService> logger) : IWorkLeadershipService
{
    public Task<bool> LeadsAnyWorkAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
        => objectives.AnyActiveOwnedAsync(tenantId, employeeId, legalEntityId, ct);

    public async Task<LedWorkScope> ResolveLedScopeAsync(Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default)
    {
        var owned = await objectives.ListActiveOwnedIdsAsync(tenantId, employeeId, legalEntityId, ct);
        if (owned.Count == 0)
            return LedWorkScope.Empty;

        var projectIds = owned.Select(o => o.ProjectId).Distinct().ToList();
        var tree = await objectives.ListActiveTreeForProjectsAsync(tenantId, projectIds, ct);
        var membership = await members.ListActiveMembershipObjectiveIdsAsync(tenantId, employeeId, projectIds, ct);

        var scope = LedWorkScopeBuilder.Build(owned.Select(o => o.ObjectiveId).ToList(), tree, membership);
        if (scope.DroppedOwnedObjectiveIds.Count > 0)
        {
            // Data gap, not an error: the employee owns a module but holds no membership that lets
            // TaskAccessResolver open it (owners normally get one at create/transfer). Dropped so
            // the dashboard never lists a task that would 404 (spec §8.3.2 step 7).
            logger.LogInformation(
                "Work I Lead: {Count} owned module(s) dropped for employee {EmployeeId} - no membership access: {ObjectiveIds}",
                scope.DroppedOwnedObjectiveIds.Count, employeeId, scope.DroppedOwnedObjectiveIds);
        }
        return scope;
    }
}
```

- [ ] **Step 4: Register it**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, after the `ITaskStatusChangeAccessService`
registration:

```csharp
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Leadership.Services.IWorkLeadershipService,
            ONEVO.Application.Features.WorkManagement.Leadership.Services.WorkLeadershipService>();
```

- [ ] **Step 5: Run the tests**

Run:
- `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Leadership"`
- `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~WorkLeadershipRepositoryIntegrationTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement src/ONEVO.Infrastructure tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership tests/ONEVO.Tests.Integration/MyTeam
git commit -m "feat(work): IWorkLeadershipService resolves the Work I Lead scope"
```

---

### Task 4: `GetLedWorkProgressQuery` + endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/DTOs/LedWorkProgressResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Queries/GetLedWorkProgress/GetLedWorkProgressQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Leadership/Queries/GetLedWorkProgress/GetLedWorkProgressQueryHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/WorkLeadershipController.cs`
- Test: `tests/ONEVO.Tests.Integration/MyTeam/LedWorkProgressIntegrationTests.cs`

**Interfaces:**
- Consumes:
  - `IWorkLeadershipService` (Task 3)
  - `IWorkTaskRepository.ListTopLevelProgressRowsAsync` / `ListTopLevelOverdueAsync` (Task 2)
  - `IProjectRepository.ListByIdsAsync` (Task 2)
  - `TaskProgressClassifier` (Part 2)
  - `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository`:
    `GetDefaultForUserAsync(Guid, Guid, ct)` and
    `ListByIdsAsync(Guid, IReadOnlyCollection<Guid>, ct)` (returns `IReadOnlyDictionary<Guid, Employee>`).
- Produces: the HTTP contract `GET /api/v1/work/led-progress?overdueLimit=10`, returning
  `LedWorkProgressResponse`. The frontend reads it as camelCase JSON:

```text
{ totals: {total, completed, inProgress, notStarted, overdue},
  projects: [{ projectId, projectName, identifier, totals,
               modules: [{ objectiveId, title, isRootModule, endDate, totals }] }],
  overdueTasks: [{ taskId, shortId, title, projectId, objectiveId, dueDate, daysOverdue,
                   assignees: [{ employeeId, displayName }] }],
  overdueTotal }
```

- [ ] **Step 1: Write the DTOs and query**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Leadership/DTOs/LedWorkProgressResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

public sealed record LedWorkTotals(int Total, int Completed, int InProgress, int NotStarted, int Overdue)
{
    public static readonly LedWorkTotals Zero = new(0, 0, 0, 0, 0);
}

public sealed record LedModuleProgress(Guid ObjectiveId, string Title, bool IsRootModule, DateOnly EndDate, LedWorkTotals Totals);

public sealed record LedProjectProgress(
    Guid ProjectId, string ProjectName, string Identifier, LedWorkTotals Totals, IReadOnlyList<LedModuleProgress> Modules);

public sealed record LedTaskAssignee(Guid EmployeeId, string DisplayName);

public sealed record LedOverdueTask(
    Guid TaskId, string ShortId, string Title, Guid ProjectId, Guid ObjectiveId, DateOnly DueDate, int DaysOverdue,
    IReadOnlyList<LedTaskAssignee> Assignees);

public sealed record LedWorkProgressResponse(
    LedWorkTotals Totals, IReadOnlyList<LedProjectProgress> Projects, IReadOnlyList<LedOverdueTask> OverdueTasks, int OverdueTotal);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Leadership/Queries/GetLedWorkProgress/GetLedWorkProgressQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

/// <summary>My Team - Team Progress (spec §8.3): progress of the work in modules the caller
/// effectively owns. OverdueLimit 1..50, default 10.</summary>
public sealed record GetLedWorkProgressQuery(int OverdueLimit = 10) : IRequest<Result<LedWorkProgressResponse>>;
```

- [ ] **Step 2: Write the failing integration test**

This test drives the real handler over PostgreSQL. It asserts four things:
- the counts roll up per head module;
- a plain member sees nothing;
- the overdue list is correct;
- the budget is at most 9 commands, and the count does not grow with size.

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/LedWorkProgressIntegrationTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class LedWorkProgressIntegrationTests
{
    private sealed class StubUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private static GetLedWorkProgressQueryHandler Handler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId),
        new SystemDateTimeProvider(),
        new EfEmployeeRepository(db),
        new WorkLeadershipService(new EfObjectiveRepository(db), new EfProjectMemberRepository(db), NullLogger<WorkLeadershipService>.Instance),
        new EfWorkTaskRepository(db),
        new EfProjectRepository(db));

    private static async Task<(MyTeamDb Db, Guid LeadUserId, Guid MemberUserId, Guid ModuleId)> SeedAsync(int tasksPerModule)
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = helper.NewContext();
        var creator = helper.AddEmployee(db);   // project creator = root-module owner (leads the whole project)
        var lead = helper.AddEmployee(db);      // owns one module (and a nested one below it)
        var member = helper.AddEmployee(db);    // plain member of a sub-module only
        var (projectId, rootId) = helper.AddProject(db, creator.Id);
        var module = helper.AddModule(db, projectId, rootId, lead.Id);
        var sub = helper.AddModule(db, projectId, module, member.Id);
        var nestedOwned = helper.AddModule(db, projectId, sub, lead.Id); // nested ownership: must not double count
        helper.AddMember(db, projectId, module, lead.Id);
        helper.AddMember(db, projectId, sub, member.Id);
        var doing = helper.AddStatus(db, projectId, false);
        var done = helper.AddStatus(db, projectId, true);
        for (var i = 0; i < tasksPerModule; i++)
        {
            helper.AddTask(db, projectId, module, done, 100, today.AddDays(-3), assigneeEmployeeId: member.Id); // completed
            helper.AddTask(db, projectId, sub, doing, 20, today.AddDays(5));                                    // in progress
            helper.AddTask(db, projectId, nestedOwned, doing, 0, today.AddDays(-2), assigneeEmployeeId: member.Id); // overdue
        }
        await db.SaveChangesAsync();
        return (helper, lead.UserId, member.UserId, module);
    }

    [Fact]
    public async Task Lead_sees_rolled_up_counts_member_sees_nothing()
    {
        var (helper, leadUserId, memberUserId, moduleId) = await SeedAsync(tasksPerModule: 2);
        await using var db = helper.NewContext();

        var lead = await Handler(db, helper.TenantId, leadUserId).Handle(new GetLedWorkProgressQuery(), default);
        var member = await Handler(db, helper.TenantId, memberUserId).Handle(new GetLedWorkProgressQuery(), default);

        lead.IsSuccess.Should().BeTrue();
        var project = lead.Value!.Projects.Should().ContainSingle().Subject;
        var head = project.Modules.Should().ContainSingle("the nested owned module rolls into its owned ancestor").Subject;
        head.ObjectiveId.Should().Be(moduleId);
        head.Totals.Should().Be(new ONEVO.Application.Features.WorkManagement.Leadership.DTOs.LedWorkTotals(6, 2, 2, 0, 2));
        lead.Value.Totals.Should().Be(head.Totals);
        lead.Value.OverdueTotal.Should().Be(2);
        lead.Value.OverdueTasks.Should().HaveCount(2).And.OnlyContain(t => t.DaysOverdue == 2 && t.Assignees.Count == 1);

        member.Value!.Projects.Should().BeEmpty("plain membership never makes someone a lead");
    }

    [Fact]
    public async Task Command_count_is_constant_and_within_budget()
    {
        var small = await SeedAsync(tasksPerModule: 1);
        var large = await SeedAsync(tasksPerModule: 15);

        async Task<int> CountAsync((MyTeamDb Db, Guid LeadUserId, Guid MemberUserId, Guid ModuleId) seeded)
        {
            var counter = new CountingDbCommandInterceptor();
            await using var db = seeded.Db.NewContext(counter);
            await Handler(db, seeded.Db.TenantId, seeded.LeadUserId).Handle(new GetLedWorkProgressQuery(), default);
            return counter.Count;
        }

        var smallCount = await CountAsync(small);
        var largeCount = await CountAsync(large);

        smallCount.Should().Be(largeCount);
        largeCount.Should().BeLessThanOrEqualTo(9);
    }
}
```

- [ ] **Step 3: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~LedWorkProgressIntegrationTests"`
Expected: compile error, handler missing.

- [ ] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Leadership/Queries/GetLedWorkProgress/GetLedWorkProgressQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

public sealed class GetLedWorkProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    IWorkTaskRepository tasks,
    IProjectRepository projects)
    : IRequestHandler<GetLedWorkProgressQuery, Result<LedWorkProgressResponse>>
{
    private static readonly LedWorkProgressResponse Empty = new(LedWorkTotals.Zero, [], [], 0);

    public async Task<Result<LedWorkProgressResponse>> Handle(GetLedWorkProgressQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedWorkProgressResponse>.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<LedWorkProgressResponse>.Success(Empty);

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        if (scope.HeadModules.Count == 0)
            return Result<LedWorkProgressResponse>.Success(Empty);

        // UTC date - identical to GetMyTaskProgressQueryHandler, so My Day and My Team agree.
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var allObjectiveIds = scope.AllObjectiveIds.ToList();
        var rows = await tasks.ListTopLevelProgressRowsAsync(tenantId, allObjectiveIds, ct);
        var bucketsByObjective = rows
            .GroupBy(row => row.ObjectiveId)
            .ToDictionary(g => g.Key, g => g.Select(row =>
                TaskProgressClassifier.Classify(row.MarksTaskComplete, row.ProgressPercent, row.DueDate, today)).ToList());

        var overdueLimit = Math.Clamp(request.OverdueLimit, 1, 50);
        var overdueRows = await tasks.ListTopLevelOverdueAsync(tenantId, allObjectiveIds, today, overdueLimit, ct);
        var projectRows = (await projects.ListByIdsAsync(tenantId, scope.HeadModules.Select(h => h.ProjectId).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id);
        var assigneeIds = overdueRows.SelectMany(r => r.AssigneeEmployeeIds).Distinct().ToList();
        var names = assigneeIds.Count == 0
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            : await employees.ListByIdsAsync(tenantId, assigneeIds, ct);

        var projectProgress = scope.HeadModules
            .GroupBy(head => head.ProjectId)
            .Where(group => projectRows.ContainsKey(group.Key))
            .Select(group =>
            {
                var modules = group
                    .Select(head => new LedModuleProgress(head.ObjectiveId, head.Title, head.IsRootModule, head.EndDate,
                        Sum(head.SubtreeObjectiveIds.SelectMany(id => bucketsByObjective.GetValueOrDefault(id) ?? []))))
                    .OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var project = projectRows[group.Key];
                return new LedProjectProgress(project.Id, project.Name, project.Identifier, Add(modules.Select(m => m.Totals)), modules);
            })
            .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totals = Add(projectProgress.Select(p => p.Totals));
        var overdue = overdueRows.Select(r => new LedOverdueTask(
            r.TaskId, r.ShortId, r.Title, r.ProjectId, r.ObjectiveId, r.DueDate, today.DayNumber - r.DueDate.DayNumber,
            r.AssigneeEmployeeIds.Select(id => new LedTaskAssignee(id,
                names.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "Unknown employee")).ToList())).ToList();

        return Result<LedWorkProgressResponse>.Success(new LedWorkProgressResponse(totals, projectProgress, overdue, totals.Overdue));
    }

    private static LedWorkTotals Sum(IEnumerable<TaskProgressBucket> buckets)
    {
        int completed = 0, overdue = 0, inProgress = 0, notStarted = 0;
        foreach (var bucket in buckets)
        {
            switch (bucket)
            {
                case TaskProgressBucket.Completed: completed++; break;
                case TaskProgressBucket.Overdue: overdue++; break;
                case TaskProgressBucket.InProgress: inProgress++; break;
                default: notStarted++; break;
            }
        }
        return new LedWorkTotals(completed + overdue + inProgress + notStarted, completed, inProgress, notStarted, overdue);
    }

    private static LedWorkTotals Add(IEnumerable<LedWorkTotals> parts) => parts.Aggregate(LedWorkTotals.Zero, (a, b) =>
        new LedWorkTotals(a.Total + b.Total, a.Completed + b.Completed, a.InProgress + b.InProgress, a.NotStarted + b.NotStarted, a.Overdue + b.Overdue));
}
```

Check that `IEmployeeRepository.GetDefaultForUserAsync` exists on the **CoreHr** interface:
`grep -n GetDefaultForUserAsync src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs`.
If it lives elsewhere, use the interface `AttendanceReadHandler` injects as `employees`.

- [ ] **Step 5: Add the controller**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/WorkLeadershipController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

/// <summary>My Team - Work I Lead (spec §8.3). Relationship-based like the rest of Work
/// Management: the module gate only, no RequirePermission; the handler computes ownership.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public sealed class WorkLeadershipController(IMediator mediator) : ControllerBase
{
    [HttpGet("led-progress")]
    public async Task<IActionResult> LedProgress([FromQuery] int overdueLimit = 10, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetLedWorkProgressQuery(overdueLimit), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 6: Run the tests**

Run:
- `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~LedWorkProgressIntegrationTests"`
- `dotnet test tests/ONEVO.Tests.Architecture`

Expected: PASS.

If the command count is over 9, log the SQL (Part 1 Task 5 Step 2 shows how). Remove the per-item
query; do not raise the budget.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Leadership src/ONEVO.Api/Controllers/Tenant/WorkManagement/WorkLeadershipController.cs tests/ONEVO.Tests.Integration/MyTeam/LedWorkProgressIntegrationTests.cs
git commit -m "feat(work): GET /work/led-progress - Team Progress for modules the caller leads"
```

---

### Task 5: Architecture guard — Work Management never uses coverage

**Files:**
- Test: `tests/ONEVO.Tests.Architecture/MyTeamBoundaryArchitectureTests.cs`

**Interfaces:**
- Produces: `MyTeamBoundaryArchitectureTests`, which Part 5 extends with the Dashboard
  composition rule.

- [ ] **Step 1: Write the test**

```csharp
// tests/ONEVO.Tests.Architecture/MyTeamBoundaryArchitectureTests.cs
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>Spec §10: People I Manage vs Work I Lead are separate authorization domains.</summary>
public sealed class MyTeamBoundaryArchitectureTests
{
    private static readonly string[] CoverageIdentifiers =
    [
        "IEmployeeAuthorityResolver", "IEmployeeVisibilityScopeResolver",
        "IEmployeeHierarchyClosureRepository", "ManagementCoverageRecord",
    ];

    [Fact]
    public void WorkManagement_never_references_management_coverage()
    {
        var offenders = SourceFilesUnder("src", "ONEVO.Application", "Features", "WorkManagement")
            .Concat(SourceFilesUnder("src", "ONEVO.Infrastructure", "Persistence", "Repositories", "WorkManagement"))
            .Where(file => CoverageIdentifiers.Any(id => File.ReadAllText(file).Contains(id, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }

    internal static IEnumerable<string> SourceFilesUnder(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. segments]);
            if (Directory.Exists(candidate))
                return Directory.EnumerateFiles(candidate, "*.cs", SearchOption.AllDirectories);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(Path.Combine(segments));
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~MyTeamBoundaryArchitectureTests"`
Expected: PASS.

The comments in `GetProjectTasksQueryHandler` and `GetObjectiveMembersQueryHandler` mention
"EmployeeVisibilityScopeResolver" in prose. If that trips the test, extend the check to ignore
lines whose trimmed text starts with `//` or `///`:

```csharp
File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//")).Any(l => l.Contains(id, StringComparison.Ordinal))
```

Use that version. Code references are what matter, not comments.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Architecture/MyTeamBoundaryArchitectureTests.cs
git commit -m "test(arch): Work Management never references management coverage"
```

## Part 3 done when

- `GET /api/v1/work/led-progress` returns the rolled-up progress of head modules only.
- A plain member gets empty projects.
- An owner without membership access gets the module dropped.
- At most 9 commands, the same for small and large fixtures.
- The architecture guard is green.
