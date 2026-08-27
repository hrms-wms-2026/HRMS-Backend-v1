# Part 7: Merged task history read endpoint

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `GET /api/v1/work/tasks/{id}/history` — merges `TaskEditLog`, `TaskStatusChangeLog`,
`TaskClockingSession`, and `TaskPercentageLog` into one time-sorted feed for the task detail page, visible
to any project member (not owner-gated). Push-sourced percentage entries nest inside their clocking
session's entry rather than appearing as a separate row.

**Spec:** design spec §6

**Depends on:** Part 1 (all 4 repositories' `GetForTaskAsync` methods), Part 6 (Task 1's
`ITaskClockingSessionRepository.GetOpenSessionForTaskAsync` isn't needed here, but the entity shape is).

## Architecture & Conventions

- Structural sibling: `GetProjectTasksQueryHandler.cs` (read Part 8's own copy of this note too — both
  Parts 7 and 8 are read-only query handlers following this same auth → resolve → load → shape-response
  flow, with no `IUnitOfWork`/transaction involved).
- **Merge logic lives in the handler, not a repository** — each repository's `GetForTaskAsync` returns its
  own entity list; the handler is where they combine into one feed. Do not add a fifth "history"
  repository that tries to do a raw SQL union — this codebase's WM repositories are all single-entity-typed
  (Part 1's convention), keep that.
- Employee display names: batch-resolve every distinct `EmployeeId` across all 4 sources in one call to
  `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct)` (used this way
  already in `CreateTaskEditRequestCommandHandler.cs:77`) — never resolve names in a loop (N+1).
- **This endpoint does not resolve status names for `TaskStatusChangeLog` entries** — it returns raw
  `FromStatusId`/`ToStatusId` GUIDs, same as `WorkTaskResponse` already does for `StatusId` elsewhere in
  this module. The frontend already has the project's task-status list loaded wherever it renders a task
  (Board/Backlog/My Task all need status names for column headers) — resolve names there, don't
  re-implement that lookup here.

## Global Constraints

- `[RequirePermission("projects:access")]`, **no ownership/effective-manager gate** — every project member
  can read a task's history, per spec §6 explicitly.
- The feed is sorted **newest first** (descending by `OccurredAt`).

---

### Task 1: Response DTOs

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskHistoryResponses.cs`

**Interfaces:**
- Produces the shapes Task 2's handler builds and Task 3's controller/viewmodel exposes.

- [ ] **Step 1: Write the DTOs**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public static class TaskHistoryEntryTypes
{
    public const string Edit = "edit";
    public const string StatusChange = "status_change";
    public const string ClockSession = "clock_session";
    public const string PercentageChange = "percentage_change";
}

public sealed record TaskHistoryEntryResponse(
    string Type, DateTimeOffset OccurredAt, Guid EmployeeId, string EmployeeName,
    TaskEditEntryDetails? Edit, TaskStatusChangeEntryDetails? StatusChange,
    TaskClockSessionEntryDetails? ClockSession, TaskPercentageChangeEntryDetails? PercentageChange);

public sealed record TaskEditEntryDetails(
    Guid LogId, string Source, Guid? EditRequestId, string OldValuesJson, string NewValuesJson, string? Reason);

public sealed record TaskStatusChangeEntryDetails(Guid FromStatusId, Guid ToStatusId);

public sealed record TaskClockSessionEntryDetails(
    Guid SessionId, DateTimeOffset ClockInAt, DateTimeOffset? ClockOutAt, int? DurationMinutes,
    string? SessionReason, int? PushedPercent, int? PreviousPercent, Guid? PercentageLogId,
    string? PercentageReason);

public sealed record TaskPercentageChangeEntryDetails(
    Guid LogId, int PreviousPercent, int NewPercent, string Source, string? Reason);

public sealed record TaskHistoryResponse(IReadOnlyList<TaskHistoryEntryResponse> Entries);
```

Reference the 4 type constants as `TaskHistoryEntryTypes.Edit`/`.StatusChange`/`.ClockSession`/
`.PercentageChange` everywhere in Task 2 and Task 3 below — never the literal string.

- [ ] **Step 2: Build to confirm the file compiles standalone**

Run: `dotnet build`
Expected: succeeds (no consumers yet).

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskHistoryResponses.cs
git commit -m "feat(work): add task history response DTOs"
```

---

### Task 2: `GetTaskHistoryQuery` + handler

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskHistory/GetTaskHistoryQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskHistory/GetTaskHistoryQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskHistoryQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetTaskHistoryQuery(Guid TaskId) : IRequest<Result<TaskHistoryResponse>>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskHistory;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetTaskHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_PushSourcedPercentageLog_NestsInsideItsClockSessionEntry_NotAsSeparateEntry()
    {
        var sessionId = Guid.NewGuid();
        var (handler, task) = ArrangeHistoryHandler(
            sessions: [new TaskClockingSession { Id = sessionId, TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, ClockInAt = At(-2), ClockOutAt = At(-1), DurationMinutes = 60 }],
            percentageLogs: [new TaskPercentageLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, PreviousPercent = 10, NewPercent = 40, Source = TaskPercentageLogSources.Push, ClockingSessionId = sessionId, ChangedAt = At(-1) }]);

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
        var entry = result.Value.Entries[0];
        Assert.Equal(TaskHistoryEntryTypes.ClockSession, entry.Type);
        Assert.Equal(40, entry.ClockSession!.PushedPercent);
        Assert.Equal(10, entry.ClockSession.PreviousPercent);
    }

    [Fact]
    public async Task Handle_ManualEditPercentageLog_AppearsAsStandalonePercentageChangeEntry()
    {
        var (handler, task) = ArrangeHistoryHandler(
            sessions: [],
            percentageLogs: [new TaskPercentageLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, PreviousPercent = 40, NewPercent = 20, Source = TaskPercentageLogSources.ManualEdit, ClockingSessionId = null, ChangedAt = At(0) }]);

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
        Assert.Equal(TaskHistoryEntryTypes.PercentageChange, result.Value.Entries[0].Type);
    }

    [Fact]
    public async Task Handle_OpenSessionWithNoPushYet_AppearsAsClockSessionEntryWithNullPushedPercent()
    {
        var (handler, task) = ArrangeHistoryHandler(
            sessions: [new TaskClockingSession { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, ClockInAt = At(0), ClockOutAt = null }],
            percentageLogs: []);

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
        Assert.Null(result.Value.Entries[0].ClockSession!.PushedPercent);
        Assert.Null(result.Value.Entries[0].ClockSession!.ClockOutAt);
    }

    [Fact]
    public async Task Handle_MultipleEntryKinds_SortedNewestFirst()
    {
        var (handler, task) = ArrangeHistoryHandler(
            editLogs: [new TaskEditLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, Source = TaskEditLogSources.Direct, OldValuesJson = "{}", NewValuesJson = "{}", ChangedAt = At(-10) }],
            statusChangeLogs: [new TaskStatusChangeLog { Id = Guid.NewGuid(), TaskId = TaskIdConst, EmployeeId = EmployeeIdConst, FromStatusId = Guid.NewGuid(), ToStatusId = Guid.NewGuid(), ChangedAt = At(0) }]);

        var result = await handler.Handle(new GetTaskHistoryQuery(TaskIdConst), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Entries.Count);
        Assert.Equal(TaskHistoryEntryTypes.StatusChange, result.Value.Entries[0].Type);
        Assert.Equal(TaskHistoryEntryTypes.Edit, result.Value.Entries[1].Type);
    }
}
```

**Note on test scaffolding:** `ArrangeHistoryHandler(...)`, `TaskIdConst`, `EmployeeIdConst`, and the
`At(offsetMinutes)` helper are this plan's assumed shape for a new arrange-helper in a new test file —
give the helper optional-list parameters for each of the 4 sources (defaulting to empty), a fixed
`TaskIdConst`/`EmployeeIdConst` so tests don't need to thread IDs through by hand, and `At(n)` returning
`DateTimeOffset.UtcNow.AddMinutes(n)`. Match whatever mocking convention Part 6's test files established
for repository fakes (`.Added` list-capturing fakes vs. Moq) for consistency across this plan.

- [ ] **Step 2: Run to verify failure (files don't exist yet)**

- [ ] **Step 3: Write the query**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskHistory;

public sealed record GetTaskHistoryQuery(Guid TaskId) : IRequest<Result<TaskHistoryResponse>>;
```

- [ ] **Step 4: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskHistory;

public sealed class GetTaskHistoryQueryHandler : IRequestHandler<GetTaskHistoryQuery, Result<TaskHistoryResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskEditLogRepository _editLogs;
    private readonly ITaskStatusChangeLogRepository _statusChangeLogs;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ITaskPercentageLogRepository _percentageLogs;

    public GetTaskHistoryQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskEditLogRepository editLogs, ITaskStatusChangeLogRepository statusChangeLogs,
        ITaskClockingSessionRepository sessions, ITaskPercentageLogRepository percentageLogs)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _editLogs = editLogs;
        _statusChangeLogs = statusChangeLogs;
        _sessions = sessions;
        _percentageLogs = percentageLogs;
    }

    public async Task<Result<TaskHistoryResponse>> Handle(GetTaskHistoryQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskHistoryResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<TaskHistoryResponse>.NotFound("Task not found.");

        var editLogs = await _editLogs.GetForTaskAsync(tenantId, task.Id, ct);
        var statusChangeLogs = await _statusChangeLogs.GetForTaskAsync(tenantId, task.Id, ct);
        var sessions = await _sessions.GetForTaskAsync(tenantId, task.Id, ct);
        var percentageLogs = await _percentageLogs.GetForTaskAsync(tenantId, task.Id, ct);

        var percentageLogsBySessionId = percentageLogs
            .Where(p => p.ClockingSessionId.HasValue)
            .ToDictionary(p => p.ClockingSessionId!.Value);
        var standalonePercentageLogs = percentageLogs.Where(p => !p.ClockingSessionId.HasValue).ToList();

        var entries = new List<TaskHistoryEntryResponse>();

        foreach (var log in editLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.Edit, log.ChangedAt, log.EmployeeId, string.Empty,
                new TaskEditEntryDetails(log.Id, log.Source, log.EditRequestId, log.OldValuesJson, log.NewValuesJson, log.Reason),
                null, null, null));
        }

        foreach (var log in statusChangeLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.StatusChange, log.ChangedAt, log.EmployeeId, string.Empty,
                null, new TaskStatusChangeEntryDetails(log.FromStatusId, log.ToStatusId), null, null));
        }

        foreach (var session in sessions)
        {
            percentageLogsBySessionId.TryGetValue(session.Id, out var matchedPush);
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.ClockSession, session.ClockOutAt ?? session.ClockInAt, session.EmployeeId, string.Empty,
                null, null,
                new TaskClockSessionEntryDetails(
                    session.Id, session.ClockInAt, session.ClockOutAt, session.DurationMinutes, session.Reason,
                    matchedPush?.NewPercent, matchedPush?.PreviousPercent, matchedPush?.Id, matchedPush?.Reason),
                null));
        }

        foreach (var log in standalonePercentageLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.PercentageChange, log.ChangedAt, log.EmployeeId, string.Empty,
                null, null, null,
                new TaskPercentageChangeEntryDetails(log.Id, log.PreviousPercent, log.NewPercent, log.Source, log.Reason)));
        }

        var employeeIds = entries.Select(e => e.EmployeeId).Distinct().ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct);
        entries = entries
            .Select(e => e with { EmployeeName = names.GetValueOrDefault(e.EmployeeId) ?? "A teammate" })
            .OrderByDescending(e => e.OccurredAt)
            .ToList();

        return Result<TaskHistoryResponse>.Success(new TaskHistoryResponse(entries));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter GetTaskHistoryQueryHandlerTests`
Expected: PASS, all 4 cases.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskHistory/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskHistoryQueryHandlerTests.cs
git commit -m "feat(work): add merged task history query, nesting push percentage entries under their session"
```

---

### Task 3: ViewModel + controller route

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`

**Interfaces:**
- Produces: `GET /api/v1/work/tasks/{id}/history` → JSON array-wrapped `TaskHistoryResponse`.

- [ ] **Step 1: Add ViewModels mirroring the DTOs 1:1**

In `TaskContracts.cs` (same pass-through style as `TaskStatusViewModel` — these are thin mirrors, not
re-shaped):

```csharp
public sealed record TaskHistoryEntryViewModel(
    string Type, DateTimeOffset OccurredAt, Guid EmployeeId, string EmployeeName,
    TaskEditEntryDetails? Edit, TaskStatusChangeEntryDetails? StatusChange,
    TaskClockSessionEntryDetails? ClockSession, TaskPercentageChangeEntryDetails? PercentageChange);

public static class TaskHistoryViewModelMapper
{
    public static IReadOnlyList<TaskHistoryEntryViewModel> ToViewModel(this TaskHistoryResponse response) =>
        response.Entries.Select(e => new TaskHistoryEntryViewModel(
            e.Type, e.OccurredAt, e.EmployeeId, e.EmployeeName, e.Edit, e.StatusChange, e.ClockSession, e.PercentageChange)).ToList();
}
```

Add the `using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;` import for
`TaskEditEntryDetails`/etc. (these detail records are reused as-is, not re-wrapped, matching how
`TaskEditRequestViewModel` reuses `TaskEditRequestPayload` directly rather than re-declaring its fields).

- [ ] **Step 2: Wire the controller route**

```csharp
    [HttpGet("tasks/{id:guid}/history")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTaskHistoryQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the `using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskHistory;` import.

- [ ] **Step 3: Build and run the full WM test suite**

Run: `dotnet build && dotnet test --filter FullyQualifiedName~WorkManagement`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs
git commit -m "feat(work): expose GET tasks/{id}/history endpoint"
```

---

## Self-review checklist for this Part

- [ ] A `TaskClockingSession` that has a matching Push `TaskPercentageLog` produces exactly **one** feed
  entry (`clock_session`, with `PushedPercent` populated) — never two.
- [ ] A `TaskPercentageLog` with `Source = ManualEdit` or `StatusChange` always produces its own standalone
  `percentage_change` entry — never gets silently dropped because it lacks a `ClockingSessionId`.
- [ ] `EmployeeName` resolution happens once, batched, after all entries are assembled — not per-entry.
- [ ] The endpoint has no `IsEffectiveManagerAsync`/ownership check anywhere in the handler — grep the new
  handler file for `IsEffectiveManagerAsync` and confirm zero matches.
