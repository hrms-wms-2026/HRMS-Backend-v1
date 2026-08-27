# Part 9: Expose open-clocking-session state on task list endpoints

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The frontend's Board/Backlog/My Task **card- and row-level** clock widgets (frontend plan Part 6)
need to know, for every task in a list response, whether it currently has an open clocking session and
whose — without an extra per-card HTTP call. Add `OpenClockSessionEmployeeId: Guid?` to `WorkTaskResponse`,
populated via one batch query per list endpoint.

**Why this wasn't in the original spec:** the design spec's §4/§6 covered the **task detail** page's
clock widget (which already has full history loaded, see Part 7) but didn't address the card/row-level
widget's data needs on list endpoints — found as a real gap while writing the frontend plan's Part 6.
This is exactly the kind of interaction the earlier spec review should have caught; documenting it here
rather than silently leaving the frontend plan half-specified.

**Spec:** extends design spec §3/§7 (not separately documented there — treat this Part's own description as
the spec for this one addition).

**Depends on:** Part 1 (`ITaskClockingSessionRepository`), Part 8 (`GetMyProjectTasksQueryHandler`).

## Architecture & Conventions

- Batch, not per-task — mirrors how `GetProjectTasksQueryHandler`/`GetMyProjectTasksQueryHandler` already
  batch-fetch assignments via `ITaskAssignmentRepository.GetByTaskIdsAsync` instead of one query per task.
  Add the equivalent batch method to `ITaskClockingSessionRepository`.
- This field is **read-only list metadata** — it does not change any write path, and it duplicates
  information already derivable per-task from `ITaskClockingSessionRepository.GetOpenSessionForTaskAsync`
  (Part 1) applied one task at a time; this Part exists purely to make it cheap at list scale.

---

### Task 1: Batch repository method

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskClockingSessionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskClockingSessionRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskClockingSessionRepositoryTests.cs`

**Interfaces:**
- Produces: `GetOpenSessionsForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, ct) ->
  IReadOnlyDictionary<Guid, Guid>` (TaskId → the open session's EmployeeId; tasks with no open session are
  simply absent from the dictionary, not present with a null value).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetOpenSessionsForTasksAsync_ReturnsOnlyTasksWithAnOpenSession()
{
    var tenantId = Guid.NewGuid();
    var taskWithOpen = Guid.NewGuid();
    var taskWithClosedOnly = Guid.NewGuid();
    var taskWithNone = Guid.NewGuid();
    var openEmployeeId = Guid.NewGuid();
    await using var db = CreateContext();
    var repo = new EfTaskClockingSessionRepository(db);

    await repo.AddAsync(new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithOpen, EmployeeId = openEmployeeId, ClockInAt = DateTimeOffset.UtcNow });
    await repo.AddAsync(new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskWithClosedOnly, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-1), ClockOutAt = DateTimeOffset.UtcNow, DurationMinutes = 60 });
    await db.SaveChangesAsync();

    var result = await repo.GetOpenSessionsForTasksAsync(tenantId, [taskWithOpen, taskWithClosedOnly, taskWithNone]);

    Assert.Single(result);
    Assert.Equal(openEmployeeId, result[taskWithOpen]);
    Assert.False(result.ContainsKey(taskWithClosedOnly));
    Assert.False(result.ContainsKey(taskWithNone));
}
```

- [ ] **Step 2: Run to verify failure**

- [ ] **Step 3: Extend the interface and implementation**

```csharp
Task<IReadOnlyDictionary<Guid, Guid>> GetOpenSessionsForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
```

```csharp
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetOpenSessionsForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && taskIds.Contains(s.TaskId) && s.ClockOutAt == null)
            .ToDictionaryAsync(s => s.TaskId, s => s.EmployeeId, ct);
```

- [ ] **Step 4: Run the test to verify it passes; then Step 5: commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskClockingSessionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskClockingSessionRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskClockingSessionRepositoryTests.cs
git commit -m "feat(work): add batch open-clocking-session lookup for task list endpoints"
```

---

### Task 2: Add `OpenClockSessionEmployeeId` to `WorkTaskResponse` and populate it on all 3 list handlers

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTasks/GetProjectTasksQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyProjectTasks/GetMyProjectTasksQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`WorkTaskViewModel`)
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs` or wherever
  `WorkTaskResponse.ToViewModel()` is actually defined (grep for `static WorkTaskViewModel ToViewModel` to
  find the exact file — it was not directly opened during planning, do not guess its location)
- Test: extend `GetProjectTasksQueryHandlerTests.cs` (or whichever file it turns out to be, per Part 8's
  own note about this file's uncertain existence) and `GetMyProjectTasksQueryHandlerTests.cs`

**Interfaces:**
- Produces: `WorkTaskResponse` gains `Guid? OpenClockSessionEmployeeId` as its final positional parameter
  (append, don't insert in the middle — every existing call site constructs this record positionally, and
  Part 6/7/8's own new handlers already call it without this field, which will now fail to compile until
  fixed — see Task 3).

- [ ] **Step 1: Write the failing test (My Task query — most recently written, easiest to extend cleanly)**

```csharp
[Fact]
public async Task Handle_TaskWithOpenSession_IncludesOpenClockSessionEmployeeId()
{
    var openEmployeeId = Guid.NewGuid();
    var (handler, callerEmployeeId, project) = ArrangeMyTasksHandler(
        tasks: [TaskFixture(title: "Mine", assigneeEmployeeIds: new[] { CallerEmployeeIdConst })],
        openSessionsByTaskId: new Dictionary<Guid, Guid> { [TaskIdConst] = openEmployeeId });

    var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(openEmployeeId, result.Value![0].OpenClockSessionEmployeeId);
}

[Fact]
public async Task Handle_TaskWithNoOpenSession_HasNullOpenClockSessionEmployeeId()
{
    var (handler, callerEmployeeId, project) = ArrangeMyTasksHandler(
        tasks: [TaskFixture(title: "Mine", assigneeEmployeeIds: new[] { CallerEmployeeIdConst })]);

    var result = await handler.Handle(new GetMyProjectTasksQuery(project.Id, null), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Null(result.Value![0].OpenClockSessionEmployeeId);
}
```

(`ArrangeMyTasksHandler`'s optional `openSessionsByTaskId` parameter and `TaskIdConst` extend Part 8 Task
1's helper — add the `ITaskClockingSessionRepository` fake to it now.)

- [ ] **Step 2: Run to verify failure**

- [ ] **Step 3: Extend the response record**

```csharp
public sealed record WorkTaskResponse(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid>? AssigneeEmployeeIds = null, Guid? OpenClockSessionEmployeeId = null);
```

(Kept as an optional trailing parameter with a default, like `AssigneeEmployeeIds` already is — every
existing positional-construction call site across the whole codebase that doesn't pass this new field
keeps compiling unchanged, since C# allows omitting trailing optional parameters. Grep confirms which call
sites choose to actually populate it vs. leave it `null` by omission — Parts 3/4/5/6/7 of the backend plan,
Part 1 of this plan's own Task 6's `EditTaskCommandHandler`/etc. responses, intentionally leave it `null`:
this field is list-endpoint-only metadata, a single-task response after an edit/push doesn't need it.)

- [ ] **Step 4: Inject the repository and populate the field in all 3 list handlers**

For `GetProjectTasksQueryHandler`, `GetObjectiveTasksQueryHandler`, and `GetMyProjectTasksQueryHandler`:
add `ITaskClockingSessionRepository` to the constructor, and immediately before building the `responses`
list, add:

```csharp
        var openSessions = await _sessions.GetOpenSessionsForTasksAsync(tenantId, items.Select(t => t.Id).ToList(), ct);
```

then thread `openSessions.GetValueOrDefault(t.Id)` (this LINQ dictionary lookup on a `Guid` key returns
`default(Guid)` i.e. `Guid.Empty` for a missing key on a non-nullable `Dictionary<Guid,Guid>` — **this is
wrong for a `Guid?` target**, do not use plain `GetValueOrDefault`; instead use
`openSessions.TryGetValue(t.Id, out var openBy) ? openBy : (Guid?)null`) as the new trailing argument in
each handler's `WorkTaskResponse` construction.

**Note on `GetObjectiveTasksQueryHandler`** — this handler was not read in full during planning (only
`GetProjectTasksQueryHandler` was, and used as the structural template throughout this whole plan). Read it
before editing; if its `WorkTaskResponse` construction doesn't already batch-fetch assignments the same
way, apply the identical batch-open-sessions addition regardless — the response shape must stay consistent
across all list endpoints for the frontend to rely on the field being present.

- [ ] **Step 5: Add the ViewModel field and mapper pass-through**

```csharp
public sealed record WorkTaskViewModel(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid> AssigneeEmployeeIds, Guid? OpenClockSessionEmployeeId);
```

Update its `ToViewModel()` extension method (wherever Task 2's grep locates it) to pass the new field
through positionally.

- [ ] **Step 6: Run the tests to verify they pass; then Step 7: commit**

Run: `dotnet test --filter "GetMyProjectTasksQueryHandlerTests|GetProjectTasksQueryHandlerTests|GetObjectiveTasksQueryHandlerTests"`
Expected: PASS, including every pre-existing test (the new optional trailing parameter must not break any
existing positional-construction call site anywhere in the codebase — `dotnet build` with zero errors is
the real confirmation here, run it across the whole solution, not just this test filter).

```bash
git add -A
git commit -m "feat(work): expose OpenClockSessionEmployeeId on task list endpoints"
```

---

### Task 3: Fix Part 6's `PushTaskCommandHandler` response construction

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/PushTask/PushTaskCommandHandler.cs`

**Why this file specifically needs a look:** Part 6 Task 2 wrote `PushTaskCommandHandler`'s
`WorkTaskResponse` construction as a 14-positional-argument call, written before this Part's 15th
parameter existed. Since the new parameter is optional-with-default, that code still compiles unchanged —
**no fix is actually required**, this Task exists only to make you verify that explicitly rather than
assume.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: succeeds, zero errors, confirming every pre-Part-9 `WorkTaskResponse` construction site (Parts
2–8) still compiles with the new trailing optional parameter.

- [ ] **Step 2: No commit needed for this Task if the build is clean** — if it isn't, fix whichever call
  site broke and commit that fix with an explanit message naming the file.

---

## Self-review checklist for this Part

- [ ] `OpenClockSessionEmployeeId` is `null` (not `Guid.Empty`) for a task with no open session — this is
  the specific bug the `TryGetValue` note in Task 2 Step 4 exists to prevent; grep the 3 handler diffs for
  any use of `GetValueOrDefault` on the sessions dictionary and confirm none remain.
- [ ] No single-task command handler (`EditTaskCommandHandler`, `PushTaskCommandHandler`,
  `ApproveTaskEditRequestCommandHandler`, `MoveTaskStatusCommandHandler` — this last one returns `Result`,
  not `Result<WorkTaskResponse>`, so it's not applicable) was changed to populate this field — it's
  list-endpoint-only, per this Part's own Architecture section.
