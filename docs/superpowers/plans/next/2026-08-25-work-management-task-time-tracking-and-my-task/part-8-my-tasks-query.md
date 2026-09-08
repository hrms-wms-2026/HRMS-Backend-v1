# Part 8: `GET .../my-tasks` query (backend for the My Task page)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `GET /api/v1/work/projects/{projectId}/my-tasks?sprintId={guid?}` — tasks assigned to the caller
in this project, sorted nearest-deadline-first, optionally filtered to one sprint. This is the last backend
task in this plan; once it's done, the frontend plan (companion repo) can be implemented against a
complete API surface.

**Spec:** design spec §7

**Depends on:** none of the other Parts directly (doesn't touch the 4 new tables) — can be implemented in
parallel with Parts 2–7 if using subagent-driven execution, though this plan lists it last for a single
linear run.

## Architecture & Conventions

- Structural sibling: `GetProjectTasksQueryHandler.cs` (read in full — reread the copy already quoted in
  Part 7's Architecture section if needed). This handler follows the exact same shape: resolve caller →
  load project → load tasks → load assignments → filter → shape `WorkTaskResponse` list. The only new
  logic is the assignee filter and the sort.
- **Priority sort must use an explicit rank, never string ordering** — `WorkTaskPriorities` are
  `"low"`/`"medium"`/`"high"`/`"critical"`; alphabetically, `"critical" < "high" < "low" < "medium"`, which
  is not the intended order. Build a small rank lookup.

## Global Constraints

- Default sort: `DueDate` ascending, nulls last; ties broken by `Priority` descending (critical first).
- `sprintId` is optional; omitted means all sprints.

---

### Task 1: `GetMyProjectTasksQuery` + handler

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyProjectTasks/GetMyProjectTasksQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyProjectTasks/GetMyProjectTasksQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyProjectTasksQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetMyProjectTasksQuery(Guid ProjectId, Guid? SprintId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyProjectTasks;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetMyProjectTasksQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyTasksAssignedToCaller()
    {
        var (handler, callerEmployeeId, project) = ArrangeMyTasksHandler(
            tasks:
            [
                TaskFixture(title: "Mine", assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
                TaskFixture(title: "Someone else's", assigneeEmployeeIds: new[] { Guid.NewGuid() })
            ]);

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Mine", result.Value[0].Title);
    }

    [Fact]
    public async Task Handle_SortsByDueDateAscendingThenPriorityDescending_NullsDueDateLast()
    {
        var (handler, callerEmployeeId, project) = ArrangeMyTasksHandler(
            tasks:
            [
                TaskFixture(title: "No due date", dueDate: null, priority: "critical", assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
                TaskFixture(title: "Due later, high", dueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), priority: "high", assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
                TaskFixture(title: "Due sooner, low", dueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), priority: "low", assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
                TaskFixture(title: "Same day as sooner, critical", dueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), priority: "critical", assigneeEmployeeIds: new[] { CallerEmployeeIdConst })
            ]);

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "Same day as sooner, critical", "Due sooner, low", "Due later, high", "No due date" },
            result.Value!.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task Handle_WithSprintIdFilter_ReturnsOnlyThatSprintsTasks()
    {
        var sprintId = Guid.NewGuid();
        var (handler, callerEmployeeId, project) = ArrangeMyTasksHandler(
            tasks:
            [
                TaskFixture(title: "In sprint", sprintId: sprintId, assigneeEmployeeIds: new[] { CallerEmployeeIdConst }),
                TaskFixture(title: "Different sprint", sprintId: Guid.NewGuid(), assigneeEmployeeIds: new[] { CallerEmployeeIdConst })
            ]);

        var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, sprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("In sprint", result.Value[0].Title);
    }
}
```

**Note on test scaffolding:** `ArrangeMyTasksHandler(...)`, `TaskFixture(...)`, and `CallerEmployeeIdConst`
are this plan's assumed shape for this new test file — follow `GetProjectTasksQueryHandlerTests.cs`'s
existing arrange/fixture convention if that file exists (check before assuming it doesn't), otherwise
establish one consistent with the rest of this plan's new test files.

- [ ] **Step 2: Run to verify failure**

- [ ] **Step 3: Write the query**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyProjectTasks;

public sealed record GetMyProjectTasksQuery(Guid ProjectId, Guid? SprintId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
```

- [ ] **Step 4: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
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

    public GetMyProjectTasksQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IWorkTaskRepository tasks, ITaskAssignmentRepository assignments)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _tasks = tasks;
        _assignments = assignments;
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

        var items = await _tasks.GetByProjectAsync(tenantId, project.Id, ct);
        if (request.SprintId.HasValue)
            items = items.Where(t => t.SprintId == request.SprintId.Value).ToList();

        var assignments = await _assignments.GetByTaskIdsAsync(items.Select(t => t.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EmployeeId).ToList());

        var myTasks = items.Where(t => assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()).Contains(callerEmployeeId.Value));

        var sorted = myTasks
            .OrderBy(t => t.DueDate.HasValue ? 0 : 1)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => PriorityRank.GetValueOrDefault(t.Priority, 0))
            .ToList();

        var responses = sorted.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.CategoryId, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent, t.SprintId,
            assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()))).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
```

**Note the `OrderBy(t => t.DueDate.HasValue ? 0 : 1).ThenBy(t => t.DueDate)` pair** — this is the standard
EF/LINQ idiom for "nulls last" ascending sort (a plain `.OrderBy(t => t.DueDate)` would put nulls
**first**, which is wrong per spec). Since this handler already materializes `items` as an in-memory list
(`.ToList()` from the repository call above, following `GetProjectTasksQueryHandler`'s exact pattern), this
sort runs in-memory via LINQ-to-Objects, not translated to SQL — no `ORDER BY NULLS LAST` needed.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter GetMyProjectTasksQueryHandlerTests`
Expected: PASS, all 3 cases.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyProjectTasks/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyProjectTasksQueryHandlerTests.cs
git commit -m "feat(work): add GetMyProjectTasksQuery, sorted nearest-deadline-first"
```

---

### Task 2: Controller route

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`

- [ ] **Step 1: Add the route**

Add near `GetByProject`:

```csharp
    [HttpGet("projects/{projectId:guid}/my-tasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetMyTasks(Guid projectId, [FromQuery] Guid? sprintId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyProjectTasksQuery(projectId, sprintId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the `using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyProjectTasks;` import.

**Note:** `GetByProject` (the existing project-wide tasks endpoint, `GetProjectTasksQuery`) has **no**
`[RequirePermission]` attribute — it's the one WM-tasks endpoint that relies on caller-visible-Objectives
filtering instead. This new `my-tasks` endpoint **does** carry `[RequirePermission("projects:access")]`,
matching most of this controller's other routes — confirm this is intentional (it is, per spec §7's
`[RequirePermission("projects:access")]` requirement) rather than copying `GetByProject`'s exception.

- [ ] **Step 2: Build and run the full WM test suite**

Run: `dotnet build && dotnet test --filter FullyQualifiedName~WorkManagement`
Expected: all pass.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs
git commit -m "feat(work): expose GET projects/{id}/my-tasks endpoint"
```

---

## Self-review checklist for this Part

- [ ] The nulls-last due-date sort is verified by Task 1's second test (`"No due date"` task ends up
  last) — do not skip that assertion.
- [ ] Priority comparison never falls back to string ordering anywhere in this Part.
- [ ] This Part introduces zero changes to any of the 4 new tables from Part 1 — it's a pure read
  composed from `IWorkTaskRepository`/`ITaskAssignmentRepository`, both pre-existing.

---

## Backend plan complete — final self-review across all 8 Parts

- [ ] **Spec coverage:** re-read the backend design spec section by section — §3 (data model): Parts 1–2.
  §4 (Clock-in/Push): Part 6. §5 (manual edit + status-change fold-in): Parts 3–5. §6 (history):
  Part 7. §7 (my-tasks): Part 8. §8 (out of scope): confirm no Part touches
  `RequestAllocationExtension`, a project-wide task list, or `TimeAttendance`.
- [ ] **Type consistency:** `TaskPercentageLogSources`/`TaskEditLogSources`/`TaskHistoryEntryTypes` are
  each defined exactly once (Part 1 for the first two, Part 7 for the third) and referenced by name
  everywhere else — grep for any inline string literal like `"push"`/`"direct"`/`"clock_session"` outside
  those 3 definition sites and replace with the constant.
- [ ] Every migration in this plan (Parts 1 and 2) was dry-run validated and explicitly **not** applied,
  with the user told the exact command to run themselves.
