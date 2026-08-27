# Part 3: `EditTaskCommandHandler` writes TaskEditLog and TaskPercentageLog

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the task's owner/effective-manager edits a task directly (`EditTaskCommand`), apply
`ProgressPercent` to the task (up or down, per spec §5), and write one `TaskEditLog` row (`Source =
"direct"`) capturing only the fields that actually changed, plus a `TaskPercentageLog` row (`Source =
"manual_edit"`) when `ProgressPercent` specifically changed.

**Spec:** design spec §5 (manual percentage edit) and §3 (`TaskEditLog`/`TaskPercentageLog` shapes)

**Depends on:** Part 1 (tables/repositories), Part 2 (`EditTaskCommand.ProgressPercent`/`.Reason` exist).

## Architecture & Conventions

- Read `EditTaskCommandHandler.cs` in full before touching it — this Part inserts logging around the
  existing logic, it does not rewrite the handler's existing slack-check/sprint-frozen-check flow.
- Diff snapshots (`OldValuesJson`/`NewValuesJson`) are JSON via `System.Text.Json.JsonSerializer.Serialize`
  of a small anonymous-shaped record — **only include fields that actually changed**, per spec §5 ("only
  fields that actually changed — an edit that leaves a field untouched shouldn't clutter the diff"). Build
  this by comparing each field's old value (captured before mutation) against the incoming request value.
  Do not serialize the entire task both times and diff JSON strings — construct the changed-fields object
  directly, one property per potentially-changed field, only present when it changed.
- `TaskPercentageLog` is only written when `ProgressPercent` is both supplied (`request.ProgressPercent.HasValue`)
  and actually different from `task.ProgressPercent`'s prior value. A no-op edit (percent unchanged, or not
  supplied) writes zero `TaskPercentageLog` rows — but may still write a `TaskEditLog` row if other fields
  changed.
- Constructor injection order in this codebase's handlers always lists dependencies in the order the
  original author happened to add them — there is no enforced alphabetical/grouping rule. When adding the
  2 new repository dependencies, append them after the existing ones rather than reordering the whole
  constructor (keeps the diff minimal and reviewable).

## Global Constraints

- `TaskEditLog.EmployeeId` for a direct edit = the caller doing the edit (already resolved via
  `ICallerIdentityResolver` elsewhere in this module — `EditTaskCommandHandler` currently does **not**
  resolve the caller's `EmployeeId` at all, since ownership isn't checked at this layer today; this Part
  adds that resolution, see Task 1 below).
- Never write a `TaskEditLog`/`TaskPercentageLog` row outside the same transaction that mutates `WorkTask`
  — use the handler's existing `_unitOfWork.ExecuteInTransactionAsync` block, do not add a second
  `SaveChangesAsync` call.

---

### Task 1: Resolve the caller's EmployeeId and inject the 2 new repositories

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs`

**Interfaces:**
- Consumes: `ITaskEditLogRepository`, `ITaskPercentageLogRepository` (Part 1),
  `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync(tenantId, userId, ct) -> Guid?` (already used
  elsewhere in this module, e.g. `CreateTaskEditRequestCommandHandler.cs:50`).

- [ ] **Step 1: Add the constructor dependencies**

Add `ICallerIdentityResolver`, `ITaskEditLogRepository`, `ITaskPercentageLogRepository` to
`EditTaskCommandHandler`'s constructor (append, don't reorder existing params):

```csharp
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditLogRepository _editLogs;
    private readonly ITaskPercentageLogRepository _percentageLogs;

    public EditTaskCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, IObjectiveRepository objectives,
        IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork, ISprintRepository sprints,
        ICallerIdentityResolver identity, ITaskEditLogRepository editLogs, ITaskPercentageLogRepository percentageLogs)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _objectives = objectives;
        _slack = slack;
        _unitOfWork = unitOfWork;
        _sprints = sprints;
        _identity = identity;
        _editLogs = editLogs;
        _percentageLogs = percentageLogs;
    }
```

- [ ] **Step 2: Resolve the caller's EmployeeId right after the auth check**

Immediately after the `if (!_currentUser.IsAuthenticated) return ...;` block, add:

```csharp
        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");
```

**Note:** the handler already has a local `var tenantId = _currentUser.TenantId;` a few lines below the
task-lookup — remove the duplicate declaration further down (keep only this one, moved up).

- [ ] **Step 3: Build to confirm it compiles (no behavior change yet)**

Run: `dotnet build`
Expected: succeeds. Existing `EditTaskCommandHandlerTests` (if any — check for a file with that name)
should still pass unchanged: `dotnet test --filter EditTaskCommandHandlerTests`.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs
git commit -m "feat(work): resolve caller EmployeeId in EditTaskCommandHandler, inject logging repositories"
```

---

### Task 2: Write `TaskEditLog` for every changed field

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs` (add to the
  existing file — this handler already has tests per Part 3's Task 1 note; do not create a duplicate file)

**Interfaces:**
- Produces: on a successful edit, one `TaskEditLog` row exists with `Source = TaskEditLogSources.Direct`,
  `NewValuesJson` containing only the fields that changed.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_WhenTitleChanges_WritesTaskEditLogWithOnlyTheChangedField()
{
    var (handler, tasks, editLogs, callerEmployeeId, task) = ArrangeHandlerWithExistingTask(
        title: "Old Title", priority: WorkTaskPriorities.Medium, progressPercent: 20);

    var command = new EditTaskCommand(
        task.Id, "New Title", task.Description, task.Priority, task.DueDate,
        task.EstimatedHours, task.StoryPoints, null, null);

    var result = await handler.Handle(command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    var addedLog = editLogs.Added.Single();
    Assert.Equal(TaskEditLogSources.Direct, addedLog.Source);
    Assert.Equal(callerEmployeeId, addedLog.EmployeeId);
    Assert.Contains("\"title\"", addedLog.NewValuesJson, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("\"priority\"", addedLog.NewValuesJson, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task Handle_WhenNothingChanges_WritesNoEditLog()
{
    var (handler, tasks, editLogs, callerEmployeeId, task) = ArrangeHandlerWithExistingTask(
        title: "Same Title", priority: WorkTaskPriorities.Medium, progressPercent: 20);

    var command = new EditTaskCommand(
        task.Id, task.Title, task.Description, task.Priority, task.DueDate,
        task.EstimatedHours, task.StoryPoints, null, null);

    var result = await handler.Handle(command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Empty(editLogs.Added);
}
```

**Note on test scaffolding:** `ArrangeHandlerWithExistingTask(...)` and the `editLogs.Added` list-capturing
fake are this plan's assumed shape for this test file's existing helpers. **Before writing these two
tests, read `EditTaskCommandHandlerTests.cs` in full and match whatever arrange-helper/mock style it
already uses** (Moq vs. hand-written fakes, an existing `CreateHandler(...)` helper, etc.) — do not
introduce a second mocking convention into a file that already has one established.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "EditTaskCommandHandlerTests"`
Expected: FAIL — `editLogs` isn't referenced by the handler yet.

- [ ] **Step 3: Implement — capture old values, compute the diff, write the log inside the transaction**

Immediately before the `return await _unitOfWork.ExecuteInTransactionAsync(...)` block, capture the old
values (the handler already has `task` loaded tracked at this point):

```csharp
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        void TrackChange(string field, object? oldValue, object? newValue)
        {
            if (Equals(oldValue, newValue)) return;
            oldValues[field] = oldValue;
            newValues[field] = newValue;
        }
        TrackChange("title", task.Title, request.Title.Trim());
        TrackChange("description", task.Description, request.Description?.Trim());
        TrackChange("priority", task.Priority, request.Priority);
        TrackChange("dueDate", task.DueDate, request.DueDate);
        TrackChange("estimatedHours", task.EstimatedHours, request.EstimatedHours);
        TrackChange("storyPoints", task.StoryPoints, request.StoryPoints);
        if (request.ProgressPercent.HasValue)
            TrackChange("progressPercent", task.ProgressPercent, request.ProgressPercent.Value);
```

Then, inside the existing transaction block, after the `task.UpdatedAt = DateTimeOffset.UtcNow;` line and
before `await _unitOfWork.SaveChangesAsync(innerCt);`, add:

```csharp
            if (newValues.Count > 0)
            {
                await _editLogs.AddAsync(new TaskEditLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, Source = TaskEditLogSources.Direct,
                    OldValuesJson = JsonSerializer.Serialize(oldValues),
                    NewValuesJson = JsonSerializer.Serialize(newValues),
                    Reason = request.Reason?.Trim(), ChangedAt = now
                }, innerCt);
            }
```

**Note:** the handler needs a `var now = DateTimeOffset.UtcNow;` declared once at the top of the
transaction body if it doesn't already have one (currently it sets `task.UpdatedAt = DateTimeOffset.UtcNow;`
inline — introduce `now` and reuse it for both, so the log's `ChangedAt` and the task's `UpdatedAt` are
identical, not two separate clock reads a few microseconds apart). Add `using System.Text.Json;` and the
`ONEVO.Domain.Features.WorkManagement.Tasks.Entities` using (for `TaskEditLog`/`TaskEditLogSources`) if not
already present.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "EditTaskCommandHandlerTests"`
Expected: PASS, including all pre-existing tests in this file (the diff-tracking addition must not change
any existing assertion's outcome).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs
git commit -m "feat(work): write TaskEditLog on direct task edits, diffing only changed fields"
```

---

### Task 3: Write `TaskPercentageLog` when `ProgressPercent` changes via direct edit

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs`

**Interfaces:**
- Produces: when `request.ProgressPercent` differs from the task's prior value, one `TaskPercentageLog`
  row with `Source = TaskPercentageLogSources.ManualEdit`, `ClockingSessionId = null`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_WhenProgressPercentChanges_WritesManualEditPercentageLog()
{
    var (handler, tasks, editLogs, callerEmployeeId, task, percentageLogs) =
        ArrangeHandlerWithExistingTask(title: "T", priority: WorkTaskPriorities.Medium, progressPercent: 100);

    var command = new EditTaskCommand(
        task.Id, task.Title, task.Description, task.Priority, task.DueDate,
        task.EstimatedHours, task.StoryPoints, 40, "Reviewer found incomplete subtasks");

    var result = await handler.Handle(command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    var logged = percentageLogs.Added.Single();
    Assert.Equal(TaskPercentageLogSources.ManualEdit, logged.Source);
    Assert.Null(logged.ClockingSessionId);
    Assert.Equal(100, logged.PreviousPercent);
    Assert.Equal(40, logged.NewPercent);
    Assert.Equal(40, task.ProgressPercent);
}

[Fact]
public async Task Handle_WhenProgressPercentNotSupplied_WritesNoPercentageLog_AndLeavesPercentUnchanged()
{
    var (handler, tasks, editLogs, callerEmployeeId, task, percentageLogs) =
        ArrangeHandlerWithExistingTask(title: "T", priority: WorkTaskPriorities.Medium, progressPercent: 55);

    var command = new EditTaskCommand(
        task.Id, "New Title", task.Description, task.Priority, task.DueDate,
        task.EstimatedHours, task.StoryPoints, null, null);

    var result = await handler.Handle(command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Empty(percentageLogs.Added);
    Assert.Equal(55, task.ProgressPercent);
}
```

- [ ] **Step 2: Run to verify failure** — `percentageLogs` fixture arg doesn't exist yet if Task 2's helper
  signature needs extending; update the shared `ArrangeHandlerWithExistingTask` helper to also return the
  fake `ITaskPercentageLogRepository`, matching whatever pattern Task 2 established for `editLogs`.

- [ ] **Step 3: Implement**

Inside the same transaction block, right after applying `task.StoryPoints = request.StoryPoints;`, add:

```csharp
            if (request.ProgressPercent.HasValue && request.ProgressPercent.Value != task.ProgressPercent)
            {
                var previousPercent = task.ProgressPercent;
                task.ProgressPercent = request.ProgressPercent.Value;
                await _percentageLogs.AddAsync(new TaskPercentageLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, PreviousPercent = previousPercent,
                    NewPercent = task.ProgressPercent, Source = TaskPercentageLogSources.ManualEdit,
                    ClockingSessionId = null, Reason = request.Reason?.Trim(), ChangedAt = now
                }, innerCt);
            }
```

**Important — ordering with Task 2's diff tracking:** the `TrackChange("progressPercent", ...)` call from
Task 2 reads `task.ProgressPercent` **before** this block mutates it — confirm Task 2's diff-capture code
runs before this assignment (it does, since diff capture happens before the transaction opens and this
runs inside it), so the `TaskEditLog`'s `oldValues["progressPercent"]` and this `TaskPercentageLog`'s
`PreviousPercent` agree.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "EditTaskCommandHandlerTests"`
Expected: PASS, all tests in the file.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs
git commit -m "feat(work): write TaskPercentageLog when a direct edit changes ProgressPercent"
```

---

## Self-review checklist for this Part

- [ ] `EditTaskCommandHandler` still returns `Result<WorkTaskResponse>.Forbidden("Authentication
  required.")` and now also `Forbidden("No employee record for the current user.")` as its two auth-failure
  paths — this Part does not add any ownership check (this handler's existing behavior is that anyone with
  `[RequirePermission("projects:access")]` can call it directly; that authorization gap, if it is one,
  belongs to a different task — do not silently add an ownership check here, it isn't in scope).
- [ ] All pre-existing tests in `EditTaskCommandHandlerTests.cs` still pass unmodified.
- [ ] No test asserts on wall-clock time equality without a tolerance/fixed clock — if the existing test
  file has a time-freezing convention (an injected clock, or `Assert.True(Math.Abs(...) < ...)`), reuse it
  for any new `ChangedAt`/`UpdatedAt` assertions rather than comparing `DateTimeOffset.UtcNow` directly.
