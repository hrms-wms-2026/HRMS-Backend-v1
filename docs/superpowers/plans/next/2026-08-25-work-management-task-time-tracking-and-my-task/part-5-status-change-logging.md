# Part 5: `MoveTaskStatusCommandHandler` writes TaskStatusChangeLog and TaskPercentageLog

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every status move writes a `TaskStatusChangeLog` row (unconditional). When the move flips
`ProgressPercent` via this handler's **existing** `MarksTaskComplete` side effect (0↔100, already
implemented, do not change that logic), also write a `TaskPercentageLog` row (`Source = "status_change"`).
This is what makes a status-driven completion lock/unlock clocking the same way a Push does — see spec §4.

**Spec:** design spec §5 ("MoveTaskStatusCommandHandler's existing 0/100 flip")

**Depends on:** Part 1.

## Architecture & Conventions

- **Do not change `MoveTaskStatusCommandHandler`'s existing completion logic** (`task.CompletedHours`,
  `task.CompletedAt`, `objective.CompletedHours`, the 0/100 `ProgressPercent` assignment). This Part only
  adds logging alongside it — read the handler in full first, the two blocks you're adding logging calls
  next to are the `if (!wasComplete && willBeComplete)` / `else if (wasComplete && !willBeComplete)` pair.
- `callerEmployeeId` is already resolved in this handler (used for the effective-manager check) — reuse it
  directly as both logs' `EmployeeId`, no new resolution needed.

## Global Constraints

- `TaskStatusChangeLog` is written on **every** successful status move, unconditionally.
- `TaskPercentageLog` (`Source = "status_change"`) is written **only** when the existing handler's
  `wasComplete`/`willBeComplete` branch actually changes `task.ProgressPercent` (i.e., only inside the two
  existing `if`/`else if` bodies, never outside them).

---

### Task 1: Inject the 2 new repositories

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`

- [ ] **Step 1: Add constructor dependencies**

```csharp
    private readonly ITaskStatusChangeLogRepository _statusChangeLogs;
    private readonly ITaskPercentageLogRepository _percentageLogs;
```

Append after the existing `sprints` parameter, matching this handler's existing append-order convention.

- [ ] **Step 2: Build to confirm it compiles; update any direct-construction test call site**

Run: `dotnet build`. Check `MoveTaskStatusCommandHandlerTests.cs` for a direct `new
MoveTaskStatusCommandHandler(...)` call and add the 2 new fakes to it.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs
git commit -m "feat(work): inject logging repositories into MoveTaskStatusCommandHandler"
```

---

### Task 2: Write `TaskStatusChangeLog` unconditionally on every move

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`
  (existing file — add to it)

**Interfaces:**
- Produces: one `TaskStatusChangeLog` per successful `MoveTaskStatusCommand`, `FromStatusId =
  task.StatusId` (captured before mutation), `ToStatusId = newStatus.Id`, `EmployeeId = callerEmployeeId`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_OnSuccessfulMove_WritesTaskStatusChangeLog()
{
    var (handler, tasks, statusChangeLogs, callerEmployeeId, task, oldStatusId, newStatus) =
        ArrangeMoveHandlerWithTaskInStatus(marksComplete: false);

    var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var logged = statusChangeLogs.Added.Single();
    Assert.Equal(task.Id, logged.TaskId);
    Assert.Equal(oldStatusId, logged.FromStatusId);
    Assert.Equal(newStatus.Id, logged.ToStatusId);
    Assert.Equal(callerEmployeeId, logged.EmployeeId);
}
```

**Note on test scaffolding:** read `MoveTaskStatusCommandHandlerTests.cs` in full first and match its
existing arrange-helper convention for `ArrangeMoveHandlerWithTaskInStatus(...)` — it already has to set up
a task, an old status, and a new status to test the existing `MarksTaskComplete` behavior; extend that
exact setup rather than writing a parallel one.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MoveTaskStatusCommandHandlerTests`
Expected: FAIL — nothing writes to `statusChangeLogs` yet.

- [ ] **Step 3: Implement**

Capture the old status id before it's overwritten — right before `task.StatusId = newStatus.Id;` inside the
transaction block:

```csharp
            var fromStatusId = task.StatusId;
            task.StatusId = newStatus.Id;
```

Then, after the existing `if (!wasComplete && willBeComplete) { ... } else if (wasComplete &&
!willBeComplete) { ... }` block (i.e., after it, not inside either branch — this log always writes,
regardless of completion-flip), and before `task.UpdatedAt = DateTimeOffset.UtcNow;`, add:

```csharp
            var now = DateTimeOffset.UtcNow;
            await _statusChangeLogs.AddAsync(new TaskStatusChangeLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, FromStatusId = fromStatusId, ToStatusId = newStatus.Id,
                ChangedAt = now
            }, innerCt);
```

Replace the handler's existing two `DateTimeOffset.UtcNow` calls (`task.UpdatedAt = ...` and
`objective.UpdatedAt = ...`) with this same `now` variable so all three timestamps in one request agree.

- [ ] **Step 4: Run to verify it passes; then Step 5: commit**

Run: `dotnet test --filter MoveTaskStatusCommandHandlerTests` — expect PASS, all tests in the file.

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs
git commit -m "feat(work): write TaskStatusChangeLog on every task status move"
```

---

### Task 3: Write `TaskPercentageLog` when the existing completion side effect flips ProgressPercent

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Produces: when a move sets `MarksTaskComplete` true (percent 0→100) or false (percent 100→0), one
  `TaskPercentageLog` row, `Source = TaskPercentageLogSources.StatusChange`, `ClockingSessionId = null`.
  When the move doesn't cross a completion boundary, zero `TaskPercentageLog` rows.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Handle_WhenMoveMarksTaskComplete_WritesStatusChangePercentageLogTo100()
{
    var (handler, tasks, statusChangeLogs, callerEmployeeId, task, oldStatusId, newStatus, percentageLogs) =
        ArrangeMoveHandlerWithTaskInStatus(marksComplete: true, taskCurrentPercent: 40);

    var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var logged = percentageLogs.Added.Single();
    Assert.Equal(TaskPercentageLogSources.StatusChange, logged.Source);
    Assert.Null(logged.ClockingSessionId);
    Assert.Equal(40, logged.PreviousPercent);
    Assert.Equal(100, logged.NewPercent);
    Assert.Equal(100, task.ProgressPercent);
}

[Fact]
public async Task Handle_WhenMoveUnmarksCompletion_WritesStatusChangePercentageLogTo0()
{
    var (handler, tasks, statusChangeLogs, callerEmployeeId, task, oldStatusId, newStatus, percentageLogs) =
        ArrangeMoveHandlerWithTaskInStatus(fromMarksComplete: true, toMarksComplete: false, taskCurrentPercent: 100);

    var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var logged = percentageLogs.Added.Single();
    Assert.Equal(TaskPercentageLogSources.StatusChange, logged.Source);
    Assert.Equal(100, logged.PreviousPercent);
    Assert.Equal(0, logged.NewPercent);
    Assert.Equal(0, task.ProgressPercent);
}

[Fact]
public async Task Handle_WhenMoveDoesNotCrossCompletionBoundary_WritesNoPercentageLog()
{
    var (handler, tasks, statusChangeLogs, callerEmployeeId, task, oldStatusId, newStatus, percentageLogs) =
        ArrangeMoveHandlerWithTaskInStatus(fromMarksComplete: false, toMarksComplete: false, taskCurrentPercent: 30);

    var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Empty(percentageLogs.Added);
    Assert.Equal(30, task.ProgressPercent);
}
```

**Note on test scaffolding:** `ArrangeMoveHandlerWithTaskInStatus`'s optional parameters
(`fromMarksComplete`/`toMarksComplete`/`taskCurrentPercent`) extend Task 2's helper — adapt to whatever the
real existing helper for this handler's pre-existing `wasComplete`/`willBeComplete` tests already supports;
those tests already had to construct exactly this kind of before/after status pair.

- [ ] **Step 2: Run to verify failure**

- [ ] **Step 3: Implement**

Inside the existing `if (!wasComplete && willBeComplete)` branch, after `objective.CompletedHours +=
task.CompletedHours;`, add:

```csharp
                await _percentageLogs.AddAsync(new TaskPercentageLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, PreviousPercent = 0, NewPercent = 100,
                    Source = TaskPercentageLogSources.StatusChange, ClockingSessionId = null, ChangedAt = now
                }, innerCt);
```

Inside the existing `else if (wasComplete && !willBeComplete)` branch, after `task.CompletedAt = null;`,
add:

```csharp
                await _percentageLogs.AddAsync(new TaskPercentageLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, PreviousPercent = 100, NewPercent = 0,
                    Source = TaskPercentageLogSources.StatusChange, ClockingSessionId = null, ChangedAt = now
                }, innerCt);
```

Both reuse the `now` variable introduced in Task 2 of this Part — make sure it's declared before these two
branches run (move its declaration to the top of the transaction body if Task 2 placed it after the
branches).

**Note:** the existing code sets `task.ProgressPercent = 100;`/`task.ProgressPercent = 0;` as a plain
literal assignment inside these branches already (that's the pre-existing behavior this Part observes, not
changes) — `PreviousPercent`/`NewPercent` above are hardcoded to `0`/`100` for the same reason the existing
code hardcodes the assignment: this handler's completion flip is always exactly 0↔100, never a partial
value, so there's no "capture the old value" step needed here unlike Parts 3–4's manual-edit case.

- [ ] **Step 4: Run to verify it passes; then Step 5: commit**

Run: `dotnet test --filter MoveTaskStatusCommandHandlerTests` — expect PASS, all tests.

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs
git commit -m "feat(work): write TaskPercentageLog when a status move flips task completion"
```

---

## Self-review checklist for this Part

- [ ] `task.ProgressPercent`'s existing assignment logic (`= 100;` / `= 0;`) is byte-for-byte unchanged —
  `git diff` should show only new lines added near it, never a modified existing line in that assignment.
- [ ] The `TaskPercentageLog` write sits **inside** the `if`/`else if` branches, never after them
  unconditionally (that would log a row on every move, not just completion-crossing ones).
- [ ] All pre-existing tests in `MoveTaskStatusCommandHandlerTests.cs` still pass unmodified.
- [ ] This Part does not touch `TasksController.MoveStatus` — no new endpoint, this handler is already
  wired up.
