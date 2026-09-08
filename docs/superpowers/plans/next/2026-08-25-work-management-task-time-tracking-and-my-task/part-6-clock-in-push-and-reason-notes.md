# Part 6: Clock-in / Push commands, and the after-the-fact reason-note endpoints

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The core new subsystem — `POST /tasks/{id}/clock-in`, `POST /tasks/{id}/push`, and the two
`PATCH .../reason` endpoints for adding an optional note to an existing clocking session or percentage-log
row after the fact.

**Spec:** design spec §4 (Clock-in/Push state machine)

**Depends on:** Part 1 (tables/repositories). Independent of Parts 2–5 (does not touch `EditTaskCommand`/
`MoveTaskStatus`), but **must run after Part 1** since it uses `ITaskClockingSessionRepository`/
`ITaskPercentageLogRepository`.

## Architecture & Conventions

- Structural sibling for both new commands: `AssignTaskCommandHandler.cs` — same auth → resolve
  `callerEmployeeId` → load-and-validate → `_unitOfWork.ExecuteInTransactionAsync` shape. Read it in full
  before writing `ClockInTaskCommandHandler`.
- `ITaskAssignmentRepository.GetByTaskAndEmployeeAsync(taskId, employeeId, ct)` (already exists, used by
  `AssignTaskCommandHandler.cs:64`) is how you check "is this caller an assignee of this task" — Clock In
  requires this to return non-null.
- **Lock rule enforcement is two-layered, both required:** the partial unique index from Part 1 Task 3 is
  the database-level backstop against a race; the handler's own check-then-act (`GetOpenSessionForTaskAsync`
  returning non-null → 409) is what produces a clean error message instead of a raw constraint-violation
  500. Implement the handler check; do not rely on the index alone to produce a good error.
- Percent-must-be-strictly-greater validation belongs in the **handler**, not the command validator — it's
  a cross-field business rule (compares the incoming value against the task's current stored value, which
  FluentValidation's per-command validators in this codebase don't have access to; check
  `EditTaskCommandHandler`'s existing slack-check for the precedent of "business rule needing a DB read
  lives in the handler, not the validator").

## Global Constraints

- Only the task's own assignees may Clock In (checked via `ITaskAssignmentRepository`).
- A task with `ProgressPercent == 100` cannot be clocked into (409).
- Only one open session per task at a time (409 on a second Clock In).
- Push may only close the session the same caller opened (403 otherwise).
- Push's `percent` must be strictly greater than the task's current `ProgressPercent` (400 otherwise).

---

### Task 1: `ClockInTaskCommand` + handler + controller route

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ClockInTaskCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ClockInTaskCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs`

**Interfaces:**
- Produces: `ClockInTaskCommand(Guid TaskId) : IRequest<Result>`. On success, one open
  `TaskClockingSession` row exists for `(TaskId, callerEmployeeId)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ClockInTaskCommandHandlerTests
{
    [Fact]
    public async Task Handle_AssigneeWithNoOpenSessionAndTaskNotLocked_OpensSession()
    {
        var (handler, sessions, callerEmployeeId, task) =
            ArrangeClockInHandler(isAssignee: true, hasOpenSession: false, taskProgressPercent: 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var opened = sessions.Added.Single();
        Assert.Equal(task.Id, opened.TaskId);
        Assert.Equal(callerEmployeeId, opened.EmployeeId);
        Assert.Null(opened.ClockOutAt);
    }

    [Fact]
    public async Task Handle_TaskAlreadyHasOpenSession_ReturnsConflict()
    {
        var (handler, sessions, callerEmployeeId, task) =
            ArrangeClockInHandler(isAssignee: true, hasOpenSession: true, taskProgressPercent: 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions.Added);
    }

    [Fact]
    public async Task Handle_TaskLockedAt100Percent_ReturnsConflict()
    {
        var (handler, sessions, callerEmployeeId, task) =
            ArrangeClockInHandler(isAssignee: true, hasOpenSession: false, taskProgressPercent: 100);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions.Added);
    }

    [Fact]
    public async Task Handle_CallerNotAnAssignee_ReturnsForbidden()
    {
        var (handler, sessions, callerEmployeeId, task) =
            ArrangeClockInHandler(isAssignee: false, hasOpenSession: false, taskProgressPercent: 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

**Note on test scaffolding:** `ArrangeClockInHandler(...)` is this plan's assumed arrange-helper shape for
this new test file — since there's no prior sibling test file to copy exactly, follow
`AssignTaskCommandHandlerTests.cs`'s (or whichever existing WM command-handler test file is most recently
added — check `CreateTaskEditRequestCommandHandlerTests.cs`) mocking convention for `ICurrentUser`,
`ICallerIdentityResolver`, and the repository fakes, rather than introducing a new one.

- [ ] **Step 2: Run to verify failure (files don't exist yet)**

- [ ] **Step 3: Write the command**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public sealed record ClockInTaskCommand(Guid TaskId) : IRequest<Result>;
```

- [ ] **Step 4: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public class ClockInTaskCommandHandler : IRequestHandler<ClockInTaskCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;

    public ClockInTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments, ITaskClockingSessionRepository sessions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ClockInTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        if (await _assignments.GetByTaskAndEmployeeAsync(task.Id, callerEmployeeId.Value, ct) is null)
            return Result.Forbidden("Only an assignee of this task can clock in.");

        if (task.ProgressPercent == 100)
            return Result.Conflict("This task is complete - reduce its percentage before clocking in again.");

        if (await _sessions.GetOpenSessionForTaskAsync(tenantId, task.Id, ct) is not null)
            return Result.Conflict("This task already has an open clock-in session.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _sessions.AddAsync(new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, ClockInAt = DateTimeOffset.UtcNow
            }, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

**Check `Result.Conflict(...)`'s exact signature before using it** — confirm it exists as a static factory
returning `Result`/`Result<T>` with a 409 status code (used elsewhere in this module, e.g.
`ApproveTaskEditRequestCommandHandler`'s `Result<WorkTaskResponse>.Conflict(...)`) rather than assuming;
if `Result` (non-generic) doesn't have a `Conflict` overload, use `Result.Failure(message, 409)` instead —
check `Application/Common/Models/Result.cs` for the exact available static factories.

- [ ] **Step 5: Wire the controller route**

In `TasksController.cs`, add near the other task-mutation routes (after `MoveStatus`):

```csharp
    [HttpPost("tasks/{id:guid}/clock-in")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ClockIn(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ClockInTaskCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the `using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;` import.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter ClockInTaskCommandHandlerTests`
Expected: PASS, all 4 cases.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs
git commit -m "feat(work): add Clock In command, handler, and endpoint"
```

---

### Task 2: `PushTaskCommand` + validator + handler + controller route

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/PushTask/PushTaskCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/PushTask/PushTaskCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/PushTask/PushTaskCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/PushTaskCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/PushTaskCommandValidatorTests.cs`

**Interfaces:**
- Consumes: `ITaskClockingSessionRepository.GetOpenSessionForTaskAsync` (Part 1).
- Produces: `PushTaskCommand(Guid TaskId, int Percent, string? Reason) : IRequest<Result<WorkTaskResponse>>`.
  On success: the task's open session is closed (`ClockOutAt`/`DurationMinutes` set), one
  `TaskPercentageLog` row (`Source = Push`, `ClockingSessionId` = the closed session's id),
  `task.ProgressPercent` updated.

- [ ] **Step 1: Write the failing validator test**

```csharp
using FluentValidation.TestHelper;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class PushTaskCommandValidatorTests
{
    private readonly PushTaskCommandValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Percent_OutOfRange_Fails(int percent)
    {
        var result = _validator.TestValidate(new PushTaskCommand(Guid.NewGuid(), percent, null));
        result.ShouldHaveValidationErrorFor(x => x.Percent);
    }

    [Fact]
    public void Reason_TooLong_Fails()
    {
        var result = _validator.TestValidate(new PushTaskCommand(Guid.NewGuid(), 50, new string('a', 1001)));
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
```

- [ ] **Step 2: Write the failing handler tests**

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class PushTaskCommandHandlerTests
{
    [Fact]
    public async Task Handle_PercentGreaterThanCurrent_ClosesSessionAndLogsPushPercentage()
    {
        var (handler, sessions, percentageLogs, tasks, callerEmployeeId, task, openSession) =
            ArrangePushHandlerWithOpenSession(sessionOwnedByCaller: true, taskCurrentPercent: 30, clockedInMinutesAgo: 45);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 60, "made progress"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value!.ProgressPercent);
        Assert.NotNull(openSession.ClockOutAt);
        Assert.True(openSession.DurationMinutes >= 44 && openSession.DurationMinutes <= 46);
        var logged = percentageLogs.Added.Single();
        Assert.Equal(TaskPercentageLogSources.Push, logged.Source);
        Assert.Equal(openSession.Id, logged.ClockingSessionId);
        Assert.Equal(30, logged.PreviousPercent);
        Assert.Equal(60, logged.NewPercent);
    }

    [Fact]
    public async Task Handle_PercentNotGreaterThanCurrent_ReturnsBadRequest_AndDoesNotCloseSession()
    {
        var (handler, sessions, percentageLogs, tasks, callerEmployeeId, task, openSession) =
            ArrangePushHandlerWithOpenSession(sessionOwnedByCaller: true, taskCurrentPercent: 30, clockedInMinutesAgo: 10);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 30, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Null(openSession.ClockOutAt);
        Assert.Empty(percentageLogs.Added);
    }

    [Fact]
    public async Task Handle_NoOpenSession_ReturnsConflict()
    {
        var (handler, sessions, percentageLogs, tasks, callerEmployeeId, task, _) =
            ArrangePushHandlerWithOpenSession(sessionOwnedByCaller: true, taskCurrentPercent: 30, clockedInMinutesAgo: 10, hasOpenSession: false);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 60, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_OpenSessionBelongsToSomeoneElse_ReturnsForbidden()
    {
        var (handler, sessions, percentageLogs, tasks, callerEmployeeId, task, openSession) =
            ArrangePushHandlerWithOpenSession(sessionOwnedByCaller: false, taskCurrentPercent: 30, clockedInMinutesAgo: 10);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 60, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(openSession.ClockOutAt);
    }

    [Fact]
    public async Task Handle_PercentReaches100_LocksTaskFromFurtherClockIn()
    {
        var (handler, sessions, percentageLogs, tasks, callerEmployeeId, task, openSession) =
            ArrangePushHandlerWithOpenSession(sessionOwnedByCaller: true, taskCurrentPercent: 90, clockedInMinutesAgo: 5);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 100, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, task.ProgressPercent);
        // Locking itself is enforced by ClockInTaskCommandHandler reading task.ProgressPercent == 100
        // (Task 1 of this Part) - this test only confirms Push correctly persists the 100 value it's
        // given, it does not re-test the lock (that's ClockInTaskCommandHandlerTests' job).
    }
}
```

**Note on test scaffolding:** `ArrangePushHandlerWithOpenSession(...)` follows Task 1's established
arrange-helper convention for this Part's new test files — no prior sibling to copy from, so establish it
consistently with `ArrangeClockInHandler`'s style from Task 1.

- [ ] **Step 3: Run to verify all fail (files don't exist yet)**

- [ ] **Step 4: Write the command and validator**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;

public sealed record PushTaskCommand(Guid TaskId, int Percent, string? Reason) : IRequest<Result<WorkTaskResponse>>;
```

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;

public class PushTaskCommandValidator : AbstractValidator<PushTaskCommand>
{
    public PushTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty).WithMessage("Task is required.");
        RuleFor(x => x.Percent).InclusiveBetween(0, 100).WithMessage("Percent must be between 0 and 100.");
        RuleFor(x => x.Reason).MaximumLength(1000).WithMessage("Reason must be 1000 characters or fewer.");
    }
}
```

- [ ] **Step 5: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;

public class PushTaskCommandHandler : IRequestHandler<PushTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ITaskPercentageLogRepository _percentageLogs;
    private readonly IUnitOfWork _unitOfWork;

    public PushTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskClockingSessionRepository sessions, ITaskPercentageLogRepository percentageLogs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _sessions = sessions;
        _percentageLogs = percentageLogs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(PushTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var openSession = await _sessions.GetOpenSessionForTaskAsync(tenantId, task.Id, ct);
        if (openSession is null)
            return Result<WorkTaskResponse>.Conflict("This task has no open clock-in session to push.");

        if (openSession.EmployeeId != callerEmployeeId.Value)
            return Result<WorkTaskResponse>.Forbidden("Only the employee who clocked in can push this session.");

        if (request.Percent <= task.ProgressPercent)
            return Result<WorkTaskResponse>.Failure(
                $"Percent must be greater than the task's current progress ({task.ProgressPercent}%).", 400);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var trackedSession = await _sessions.GetTrackedByIdForTenantAsync(tenantId, openSession.Id, innerCt);
            trackedSession!.ClockOutAt = now;
            trackedSession.DurationMinutes = (int)(now - trackedSession.ClockInAt).TotalMinutes;
            trackedSession.UpdatedAt = now;
            _sessions.Update(trackedSession);

            var previousPercent = task.ProgressPercent;
            task.ProgressPercent = request.Percent;
            task.UpdatedAt = now;

            await _percentageLogs.AddAsync(new TaskPercentageLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, PreviousPercent = previousPercent,
                NewPercent = task.ProgressPercent, Source = TaskPercentageLogSources.Push,
                ClockingSessionId = trackedSession.Id, Reason = request.Reason?.Trim(), ChangedAt = now
            }, innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId));
        }, ct);
    }
}
```

**Why re-fetch a tracked session inside the transaction** even though `openSession` was already loaded:
`GetOpenSessionForTaskAsync` returns `AsNoTracking()` (Part 1 Task 3's repository implementation) — it
can't be mutated and saved. `GetTrackedByIdForTenantAsync` (also from Part 1 Task 3) gives an
EF-change-tracked instance. Do not attempt to attach/mutate the no-tracking instance directly.

- [ ] **Step 6: Extend the two contracts and wire the controller route**

In `TaskContracts.cs`, add:

```csharp
public sealed record PushTaskRequest(int Percent, string? Reason);
```

In `TasksController.cs`, add near `ClockIn`:

```csharp
    [HttpPost("tasks/{id:guid}/push")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Push(Guid id, [FromBody] PushTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new PushTaskCommand(id, request.Percent, request.Reason), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the `using ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;` import.

- [ ] **Step 7: Run all tests to verify they pass**

Run: `dotnet test --filter "PushTaskCommandHandlerTests|PushTaskCommandValidatorTests"`
Expected: PASS, all cases.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/PushTask/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/PushTaskCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/PushTaskCommandValidatorTests.cs
git commit -m "feat(work): add Push command, handler, and endpoint"
```

---

### Task 3: After-the-fact reason notes on a clocking session and a percentage-log row

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskPercentageLogRepository.cs`
  (add `GetTrackedByIdForTenantAsync` + `Update`, mirroring `ITaskClockingSessionRepository`'s shape from
  Part 1 Task 3)
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskPercentageLogRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddClockingSessionReason/AddClockingSessionReasonCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddClockingSessionReason/AddClockingSessionReasonCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddPercentageLogReason/AddPercentageLogReasonCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddPercentageLogReason/AddPercentageLogReasonCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddClockingSessionReasonCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddPercentageLogReasonCommandHandlerTests.cs`

**Interfaces:**
- Produces: `AddClockingSessionReasonCommand(Guid SessionId, string Reason) : IRequest<Result>`,
  `AddPercentageLogReasonCommand(Guid LogId, string Reason) : IRequest<Result>`. Both only succeed when the
  caller is the `EmployeeId` on the row being annotated.

- [ ] **Step 1: Extend `ITaskPercentageLogRepository`**

```csharp
public interface ITaskPercentageLogRepository
{
    Task AddAsync(TaskPercentageLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskPercentageLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    Task<TaskPercentageLog?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    void Update(TaskPercentageLog log);
}
```

Add the matching implementation to `EfTaskPercentageLogRepository` (same shape as
`EfTaskClockingSessionRepository.GetTrackedByIdForTenantAsync`/`.Update` from Part 1 Task 3).

- [ ] **Step 2: Write the failing tests for both handlers**

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class AddClockingSessionReasonCommandHandlerTests
{
    [Fact]
    public async Task Handle_CallerOwnsTheSession_SetsReason()
    {
        var (handler, sessions, callerEmployeeId, session) = ArrangeReasonHandler(sessionOwnedByCaller: true);

        var result = await handler.Handle(
            new AddClockingSessionReasonCommand(session.Id, "context on why this took long"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("context on why this took long", session.Reason);
    }

    [Fact]
    public async Task Handle_CallerDoesNotOwnTheSession_ReturnsForbidden()
    {
        var (handler, sessions, callerEmployeeId, session) = ArrangeReasonHandler(sessionOwnedByCaller: false);

        var result = await handler.Handle(
            new AddClockingSessionReasonCommand(session.Id, "not mine"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(session.Reason);
    }
}
```

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class AddPercentageLogReasonCommandHandlerTests
{
    [Fact]
    public async Task Handle_CallerOwnsTheLog_SetsReason()
    {
        var (handler, logs, callerEmployeeId, log) = ArrangePercentageLogReasonHandler(logOwnedByCaller: true);

        var result = await handler.Handle(
            new AddPercentageLogReasonCommand(log.Id, "why the estimate changed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("why the estimate changed", log.Reason);
    }

    [Fact]
    public async Task Handle_CallerDoesNotOwnTheLog_ReturnsForbidden()
    {
        var (handler, logs, callerEmployeeId, log) = ArrangePercentageLogReasonHandler(logOwnedByCaller: false);

        var result = await handler.Handle(
            new AddPercentageLogReasonCommand(log.Id, "not mine"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(log.Reason);
    }
}
```

- [ ] **Step 3: Run to verify failure**

- [ ] **Step 4: Write both commands and handlers**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;

public sealed record AddClockingSessionReasonCommand(Guid SessionId, string Reason) : IRequest<Result>;
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;

public class AddClockingSessionReasonCommandHandler : IRequestHandler<AddClockingSessionReasonCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;

    public AddClockingSessionReasonCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskClockingSessionRepository sessions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddClockingSessionReasonCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var session = await _sessions.GetTrackedByIdForTenantAsync(tenantId, request.SessionId, ct);
        if (session is null)
            return Result.NotFound("Clocking session not found.");

        if (session.EmployeeId != callerEmployeeId.Value)
            return Result.Forbidden("Only the employee who clocked in can add a note to this session.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            session.Reason = request.Reason.Trim();
            session.UpdatedAt = DateTimeOffset.UtcNow;
            _sessions.Update(session);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

Write `AddPercentageLogReasonCommand`/`AddPercentageLogReasonCommandHandler` identically, substituting
`ITaskPercentageLogRepository`/`TaskPercentageLog`/`LogId` for
`ITaskClockingSessionRepository`/`TaskClockingSession`/`SessionId` throughout.

- [ ] **Step 5: Extend contracts and wire both controller routes**

```csharp
public sealed record AddReasonRequest(string Reason);
```

```csharp
    [HttpPatch("clocking-sessions/{id:guid}/reason")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddClockingSessionReason(Guid id, [FromBody] AddReasonRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddClockingSessionReasonCommand(id, request.Reason), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("percentage-log/{id:guid}/reason")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddPercentageLogReason(Guid id, [FromBody] AddReasonRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddPercentageLogReasonCommand(id, request.Reason), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add both `using` imports.

- [ ] **Step 6: Run all tests to verify they pass; then Step 7: commit**

Run: `dotnet test --filter "AddClockingSessionReasonCommandHandlerTests|AddPercentageLogReasonCommandHandlerTests"`

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskPercentageLogRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskPercentageLogRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddClockingSessionReason/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddPercentageLogReason/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddClockingSessionReasonCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AddPercentageLogReasonCommandHandlerTests.cs
git commit -m "feat(work): add after-the-fact reason notes for clocking sessions and percentage-log entries"
```

---

## Self-review checklist for this Part

- [ ] `ClockInTaskCommandHandler` checks assignment, lock, and open-session in that order, each returning
  before the next check runs (no silent fallthrough).
- [ ] `PushTaskCommandHandler` never mutates the `AsNoTracking()` session/task instances directly — it
  re-fetches tracked instances inside the transaction (or, for `task`, already loaded tracked via
  `GetTrackedByIdForTenantAsync` at the top — confirm this, don't re-fetch it twice).
- [ ] Both reason-note handlers reject a caller who isn't the row's own `EmployeeId` — this is intentionally
  stricter than "any project member," per spec's "addable by the employee who created that log entry."
- [ ] Every new controller route has `[RequirePermission("projects:access")]`, matching every other
  mutation route in this controller.
