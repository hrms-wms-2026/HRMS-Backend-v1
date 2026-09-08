# Part 4: `ApproveTaskEditRequestCommandHandler` writes TaskEditLog and TaskPercentageLog

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mirror Part 3, but for the non-owner path: when a `TaskEditRequest` is approved, apply its
`ProgressPercent` (if present) to the task, and write `TaskEditLog` (`Source = "approved_request"`,
`EditRequestId` set) + conditionally `TaskPercentageLog` (`Source = "manual_edit"` — per spec §5, both the
direct-edit and approved-request paths log percentage changes under the same `manual_edit` source; only
`TaskEditLog.Source` distinguishes direct vs. approved-request).

**Spec:** design spec §5

**Depends on:** Part 1, Part 2 (`TaskEditRequestPayload.ProgressPercent`, `TaskEditRequest.Reason`).

## Architecture & Conventions

- Read `ApproveTaskEditRequestCommandHandler.cs` in full before editing. It already resolves
  `callerEmployeeId` (the approver) — **do not use the approver as `TaskEditLog.EmployeeId`**. Per spec §5,
  attribute the log to `pending.RequestedByEmployeeId` (whose edit this conceptually is), not
  `callerEmployeeId` (who happened to click Approve). This is the one place this Part's logic differs from
  Part 3's.
- The diff-tracking approach (only log fields that actually changed) is identical to Part 3 Task 2 — reuse
  the same `TrackChange` local-function pattern for consistency, adapted to compare `task`'s prior values
  against `payload`'s values instead of `request`'s.

## Global Constraints

- `TaskEditLog.EmployeeId` = `pending.RequestedByEmployeeId`, never the approver.
- `TaskPercentageLog.EmployeeId` = `pending.RequestedByEmployeeId`, same reasoning.

---

### Task 1: Inject the 2 new repositories

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs`

- [ ] **Step 1: Add constructor dependencies**

```csharp
    private readonly ITaskEditLogRepository _editLogs;
    private readonly ITaskPercentageLogRepository _percentageLogs;
```

Append to the existing constructor parameter list and assignment block (after `unitOfWork`), matching Part
3 Task 1's append-don't-reorder convention.

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build`
Expected: succeeds (constructor now takes 2 more params — check for any test that constructs this handler
directly with `new ApproveTaskEditRequestCommandHandler(...)` and update its call site to pass the 2 new
fakes; if the test file uses a DI container/factory helper instead, it may need no change).

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs
git commit -m "feat(work): inject logging repositories into ApproveTaskEditRequestCommandHandler"
```

---

### Task 2: Write `TaskEditLog` (Source = approved_request) and conditional `TaskPercentageLog`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskEditRequestCommandHandlerTests.cs`
  (this file already exists per the earlier repo grep — add to it, do not create a duplicate)

**Interfaces:**
- Produces: on approval, one `TaskEditLog` (`Source = TaskEditLogSources.ApprovedRequest`,
  `EditRequestId = pending.Id`, `EmployeeId = pending.RequestedByEmployeeId`) when any field changed, and
  one `TaskPercentageLog` (`Source = TaskPercentageLogSources.ManualEdit`) when `payload.ProgressPercent`
  differs from the task's prior value.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Handle_OnApproval_WritesTaskEditLogAttributedToRequester_NotApprover()
{
    var (handler, requests, tasks, editLogs, approverEmployeeId, pending, task) =
        ArrangeApprovalHandlerWithPendingRequest(
            requestedTitle: "New Title", currentTitle: "Old Title", progressPercent: null);

    var result = await handler.Handle(new ApproveTaskEditRequestCommand(pending.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var logged = editLogs.Added.Single();
    Assert.Equal(TaskEditLogSources.ApprovedRequest, logged.Source);
    Assert.Equal(pending.Id, logged.EditRequestId);
    Assert.Equal(pending.RequestedByEmployeeId, logged.EmployeeId);
    Assert.NotEqual(approverEmployeeId, logged.EmployeeId);
}

[Fact]
public async Task Handle_WhenPayloadProgressPercentDiffers_WritesManualEditPercentageLog()
{
    var (handler, requests, tasks, editLogs, approverEmployeeId, pending, task, percentageLogs) =
        ArrangeApprovalHandlerWithPendingRequest(
            requestedTitle: task_defaults_unchanged: true, progressPercent: 75, currentTaskPercent: 30);

    var result = await handler.Handle(new ApproveTaskEditRequestCommand(pending.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var logged = percentageLogs.Added.Single();
    Assert.Equal(TaskPercentageLogSources.ManualEdit, logged.Source);
    Assert.Null(logged.ClockingSessionId);
    Assert.Equal(pending.RequestedByEmployeeId, logged.EmployeeId);
    Assert.Equal(30, logged.PreviousPercent);
    Assert.Equal(75, logged.NewPercent);
    Assert.Equal(75, task.ProgressPercent);
}

[Fact]
public async Task Handle_WhenPayloadHasNoProgressPercent_WritesNoPercentageLog()
{
    var (handler, requests, tasks, editLogs, approverEmployeeId, pending, task, percentageLogs) =
        ArrangeApprovalHandlerWithPendingRequest(progressPercent: null, currentTaskPercent: 30);

    var result = await handler.Handle(new ApproveTaskEditRequestCommand(pending.Id), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Empty(percentageLogs.Added);
    Assert.Equal(30, task.ProgressPercent);
}
```

**Note on test scaffolding:** `ArrangeApprovalHandlerWithPendingRequest(...)` is this plan's assumed shape
for a shared arrange-helper in this test file. **Read `ApproveTaskEditRequestCommandHandlerTests.cs` in
full first** and adapt these three test bodies to whatever helper/mock convention it already uses (its
existing tests already construct a pending `TaskEditRequest` and a target `WorkTask` somehow — reuse that
exact setup path rather than inventing a parallel one). The pseudocode parameter
`task_defaults_unchanged: true` above is illustrative, not literal C# — replace it with whatever the real
helper needs to keep Title/Description/etc. unchanged from the task's current values so only
`ProgressPercent` differs, isolating that one test to the percentage-log behavior.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter ApproveTaskEditRequestCommandHandlerTests`
Expected: FAIL — logging repositories aren't called yet.

- [ ] **Step 3: Implement the diff-and-log logic**

Before the `return await _unitOfWork.ExecuteInTransactionAsync(...)` block (after `payload` is
deserialized), capture old values and compute the diff exactly like Part 3 Task 2, but comparing against
`payload` instead of `request`:

```csharp
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        void TrackChange(string field, object? oldValue, object? newValue)
        {
            if (Equals(oldValue, newValue)) return;
            oldValues[field] = oldValue;
            newValues[field] = newValue;
        }
        TrackChange("title", task.Title, payload.Title);
        TrackChange("description", task.Description, payload.Description);
        TrackChange("priority", task.Priority, payload.Priority);
        TrackChange("dueDate", task.DueDate, payload.DueDate);
        TrackChange("estimatedHours", task.EstimatedHours, payload.EstimatedHours);
        TrackChange("storyPoints", task.StoryPoints, payload.StoryPoints);
        if (payload.ProgressPercent.HasValue)
            TrackChange("progressPercent", task.ProgressPercent, payload.ProgressPercent.Value);
```

Inside the transaction block, after the existing `task.StoryPoints = payload.StoryPoints;` line and before
`pending.Status = TaskEditRequestStatuses.Approved;`, add:

```csharp
            if (payload.ProgressPercent.HasValue && payload.ProgressPercent.Value != task.ProgressPercent)
            {
                var previousPercent = task.ProgressPercent;
                task.ProgressPercent = payload.ProgressPercent.Value;
                await _percentageLogs.AddAsync(new TaskPercentageLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = pending.RequestedByEmployeeId, PreviousPercent = previousPercent,
                    NewPercent = task.ProgressPercent, Source = TaskPercentageLogSources.ManualEdit,
                    ClockingSessionId = null, Reason = pending.Reason, ChangedAt = now
                }, innerCt);
            }

            if (newValues.Count > 0)
            {
                await _editLogs.AddAsync(new TaskEditLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = pending.RequestedByEmployeeId, Source = TaskEditLogSources.ApprovedRequest,
                    EditRequestId = pending.Id, OldValuesJson = JsonSerializer.Serialize(oldValues),
                    NewValuesJson = JsonSerializer.Serialize(newValues), Reason = pending.Reason,
                    ChangedAt = now
                }, innerCt);
            }
```

**Note:** introduce `var now = DateTimeOffset.UtcNow;` once near the top of the transaction body if the
handler doesn't already have one it can reuse (it currently has `var now = DateTimeOffset.UtcNow;` right at
the top of the transaction lambda already — reuse that existing variable, do not declare a second one).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter ApproveTaskEditRequestCommandHandlerTests`
Expected: PASS, including every pre-existing test in the file.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskEditRequestCommandHandlerTests.cs
git commit -m "feat(work): write TaskEditLog and TaskPercentageLog on approved edit requests, attributed to the requester"
```

---

## Self-review checklist for this Part

- [ ] Every new `TaskEditLog`/`TaskPercentageLog` row in this Part uses `pending.RequestedByEmployeeId`,
  never `callerEmployeeId` — grep the diff for `callerEmployeeId` inside the new code blocks and confirm
  zero matches.
- [ ] `RejectTaskEditRequestCommandHandler` and `CancelTaskEditRequestCommandHandler` are **not** touched
  by this Part — a rejected or cancelled request never applies to the task, so it never logs.
- [ ] All pre-existing tests in `ApproveTaskEditRequestCommandHandlerTests.cs` still pass unmodified.
