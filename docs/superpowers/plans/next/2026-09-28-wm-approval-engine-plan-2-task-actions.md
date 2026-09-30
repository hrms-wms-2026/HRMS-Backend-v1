# WM Hierarchy / Approval / Notification Engines — Plan 2: Task Actions

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route task create, edit and delete through `IWorkApprovalEngine`, which decides whether the change applies now or becomes an approval request. Apply the user's new task-status rule. Stamp `CreatorPositionObjectiveId` on new tasks. Move the three old task-request flows into `wm_approval_requests`, then delete them. **Amendment (Task 6A / 9A):** move the project task-status template requests (`TaskStatusChangeRequest`) onto the engine as well. Switch the frontend to "always call the normal endpoint; a 202 means it was sent for approval".

**Architecture:**
- A new `ITaskWriteService` holds the task create/edit/delete **validation and mutation**. Today that logic is duplicated between `CreateTask`/`EditTask` and `ApproveTaskCreationRequest`/`ApproveTaskEditRequest`.
- The direct handlers and three new `IApprovalActionApplier`s (`task.create`, `task.edit`, `task.delete`) all call it. So the old Approve/Reject/Cancel/"mine" commands and their tables can be deleted.
- The backend, not the UI, decides between "direct" and "request":
  - `POST objectives/{id}/tasks` returns **201** with the task, or **202** with `{ approvalRequestId }`;
  - `PATCH tasks/{id}` returns **200** or **202**;
  - `DELETE tasks/{id}` returns **204** or **202**.

**Tech Stack:**
- Backend: .NET 10, EF Core/Npgsql, MediatR, xUnit + Moq + FluentAssertions.
- Frontend: Angular (signals, `@ngrx/signals`), `ng test`.

**Spec:** `docs/superpowers/specs/next/2026-09-28-wm-hierarchy-approval-notification-engine-design.md` (Parts 4 and 7; §5.2, §5.3, §8)

**Builds on:** Plan 1 (`2026-09-28-wm-approval-engine-plan-1-foundation.md`), already executed: commits `18aadce4..4b7b293b` on `feature/wm-hierarchy-approval-notification-engine`.

## Global Constraints

- **WM only.** Never edit CoreHr, Leave, TimeAttendance, People, Calendar, or shared notification/outbox code. `IEmployeeAuthorityResolver` is untouched.
- **Out of scope in this plan:** Module (Objective) change requests and Sprints. Those are Plan 3.
- **Task Status template requests (amendment, user decision 2026-09-28; supersedes the earlier "leave it as is"):**
  - `TaskStatusChangeRequest` (a request to add/rename/delete/reorder the **project's** task statuses) moves onto the engine in **Task 6A** (backend) and **Task 9A** (frontend).
  - It is **not** `task.status_change`, which Task 3 uses for moving one task's status. The new action type is `project.status_template_change`, and the new target type is `project`.
  - Approver = the **root (default) Module owner only**. Root Module *members* lose the right to approve. If the root owner is inactive, the engine's HR fallback applies.
  - Consequence, applied on purpose so the rule is consistent: direct status-template edits (`CreateTaskStatus`, `EditTaskStatus`, `DeleteTaskStatus`, `ReorderTaskStatuses`) are also **root-owner only**. Root members now send a change request instead.
  - The old `outdated` status becomes the engine's `stale`.
  - Several pending template requests per project stay allowed (`TargetId = null`, so the one-pending-per-target index does not apply). The conflict sweeper keeps closing conflicting ones.
- **Task status move rule (user decision, 2026-09-28):**
  - It never needs approval.
  - It is allowed for:
    - anyone **at or above the task's creator position** ("parent");
    - the task's **assignees**;
    - the task's **creator** (`WorkTask.CreatedById == current UserId`).
  - Anyone else is forbidden.
  - Statuses with `Visibility == Private` stay **parent-only** (the user chose to keep this rule).
  - Every move notifies the task's **Module owner** and the **task creator** (Kind `direct`, action `task.status_change`).
- **Approver** = current holder of the creator position (Plan 1 engine). For a create, the position is the target Module.
- **Creator position stamped on new tasks:**
  - direct create: the highest Module in the task's ancestor chain that the actor owns;
  - otherwise (and for approved member requests): the task's own Module;
  - subtask and duplicate: the parent task's value, or the destination Module.
- **Attachments** only apply on a *direct* create or edit, exactly as the old request flows (which had no attachments). A pending request ignores `AttachmentFileIds`.
- Engines never `SaveChangesAsync`; the handlers save inside `IUnitOfWork.ExecuteInTransactionAsync`.
- **Migration data SQL:** starts with `SET LOCAL app.tenant_context_mode = 'admin';` in the same `migrationBuilder.Sql` call. **Never run `database update`.** The user applies migrations.
- **Known gotchas from Plan 1 (trust them):**
  - The API project folder is `src/ONEVO.Api`, and controllers need `using ONEVO.Api.Filters;` for `[RequireAnyModule]`.
  - `Employee` is in the namespace `ONEVO.Domain.Features.CoreHr.Entities`.
  - `dotnet ef` needs the `ConnectionStrings__MigrationConnection` env var; a dummy value works for `migrations add`/`script`.
  - Build and test with `-c Release`. A running Debug API locks `bin/Debug`.
  - `AuditableEntityInterceptor` overwrites `CreatedAt` on insert.
  - Never write the word "SQLite" in production code under `src/` (an architecture test forbids it).
  - `NotificationTemplateSeederTests` hard-codes the template count. This plan adds no templates.
- **Backend gate after every task:**
  - `dotnet build -c Release`
  - `dotnet test tests/ONEVO.Tests.Unit -c Release`
  - `dotnet test tests/ONEVO.Tests.Architecture -c Release`
- **Frontend gate:** `npx ng build` and `npx ng test --watch=false`.
- **Useless-test rule:** delete a test only if it tests deleted code, duplicates another test, or asserts nothing behavioural. **List every deleted test file in its commit message.**
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File map

### Backend (`HRMS-Backend-v1`)

| File | Change |
|---|---|
| `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskWriteService.cs` + `TaskWriteService.cs` | **new**: validation + mutation for create/edit/delete |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskWriteInputs.cs` | **new**: `TaskCreateInput`, `TaskEditInput`, `TaskWriteOutcome` |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/Mappers/WorkTaskResponseMapper.cs` | **new**: one `WorkTask → WorkTaskResponse` mapping |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskCreateApplier.cs`, `TaskEditApplier.cs`, `TaskDeleteApplier.cs` | **new** |
| `Tasks/Commands/CreateTask/*`, `EditTask/*`, `DeleteTask/*` | route through the engine; return `TaskWriteOutcome` |
| `Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs` | new gate + notification |
| `Tasks/Commands/CreateSubtask/*Handler.cs`, `DuplicateTask/*Handler.cs` | stamp `CreatorPositionObjectiveId` |
| `Tasks/Queries/GetWorkNotificationNavigation/*Handler.cs` | drop the old request branches; add `work_approval_request` |
| `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalHistoryRepository.cs` | task rows come from `wm_approval_requests` |
| `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs` | seed `WorkApprovalRequest` instead of `TaskCreationRequest` |
| `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` | 201/202, 200/202, 204/202; delete the old request endpoints |
| `src/ONEVO.Infrastructure/Migrations/<ts>_MoveTaskRequestsToWorkApprovals.cs` | copy rows, drop the FK and 2 tables |
| **Deleted** | listed in Task 6 |
| **Task 6A (amendment):** `Domain/.../Approvals/Entities/WorkApprovalRequest.cs` | add `WorkTargetTypes.Project`, `WorkActionTypes.ProjectStatusTemplateChange` |
| `Tasks/DTOs/TaskStatusTemplateChangePayload.cs` | **new**: payload of `project.status_template_change` |
| `Tasks/Appliers/TaskStatusTemplateChangeApplier.cs` | **new** |
| `Tasks/Services/TaskStatusChangeAccessService.cs`, `TaskStatusChangeRequestConflictSweeper.cs` | root-owner-only; sweep `wm_approval_requests` |
| `Tasks/Commands/CreateTaskStatusChangeRequest/*Handler.cs`, `Queries/GetProjectTaskStatusChangeRequests/*Handler.cs` | submit to / read from the engine |
| `Tasks/Commands/{Create,Edit,Delete}TaskStatus/*Handler.cs`, `ReorderTaskStatuses/*Handler.cs` | root-owner-only gate; new sweeper signature |
| `Approvals/RepositoryInterfaces/IWorkApprovalRequestRepository.cs` + EF impl | `ListTrackedPendingByActionAsync` |
| `src/ONEVO.Infrastructure/Migrations/<ts>_MoveTaskStatusChangeRequestsToWorkApprovals.cs` | copy rows, drop `task_status_change_requests` |

### Frontend (`Hrms--Web-application---front-end---v1`, `src/app/modules/work`)

| File | Change |
|---|---|
| `models/dto/work-approval.dto.ts` | **new**: `WorkApprovalRequestDto`, `PendingApprovalDto`, `isPendingApproval()` |
| **Task 9A (amendment):** `data-access/task-status-change-request-api.service.ts`, `models/dto/task-status-change-request.dto.ts` | approve/reject/cancel use the generic `/work/approvals/{id}/...`; status `outdated` → `stale` |
| `data-access/task-api.service.ts` | `createTask`/`editTask`/`deleteTask` may return `PendingApprovalDto`; old request methods removed |
| `data-access/work-approvals-api.service.ts` | generic approvals methods; old task-request methods removed |
| `ui/task-form-modal/task-form-modal.component.ts`, `feature/task-board/task-board.component.ts` | no client-side owner branching; handle 202 |
| `state/work-approvals.store.ts`, `utils/approval.mapper.ts`, `models/approval.model.ts`, `feature/work-approvals/work-approvals.component.ts` | one `work_approval` kind replaces `task_creation` and `task_edit` |
| **Deleted** | `models/dto/task-creation-request.dto.ts`, `models/dto/task-edit-request.dto.ts` |

---

## BACKEND

### Task 1: Extract `ITaskWriteService` (behaviour-preserving refactor)

**Files:**
- Create:
  - `Tasks/DTOs/TaskWriteInputs.cs`
  - `Tasks/Mappers/WorkTaskResponseMapper.cs`
  - `Tasks/Services/ITaskWriteService.cs`
  - `Tasks/Services/TaskWriteService.cs`
- Modify:
  - `CreateTaskCommandHandler.cs` and `EditTaskCommandHandler.cs`: delegate to the service; routes, results and messages are unchanged
  - `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskWriteServiceTests.cs`, plus the existing `CreateTaskCommandHandlerTests` and `EditTaskCommandHandlerTests`, which must stay green with only constructor updates

**Interfaces (produced):**

```csharp
// Tasks/DTOs/TaskWriteInputs.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>Serialised as wm_approval_requests.payload_json for task.create. ObjectiveId = target Module.</summary>
public sealed record TaskCreateInput(
    Guid ObjectiveId, string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId);

/// <summary>Serialised as payload_json for task.edit. SprintId null = leave the sprint alone.</summary>
public sealed record TaskEditInput(
    string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours,
    int? StoryPoints, int? ProgressPercent, string? Reason, Guid? SprintId);

/// <summary>Exactly one of Task / ApprovalRequestId is set: applied now, or sent for approval.</summary>
public sealed record TaskWriteOutcome(Responses.WorkTaskResponse? Task, Guid? ApprovalRequestId);
```

```csharp
// Tasks/Services/ITaskWriteService.cs
public interface ITaskWriteService
{
    /// <summary>Every read-only check CreateTask did (objective active, project, category, sprint, event window, slack, default status).</summary>
    Task<Result> ValidateCreateAsync(Guid tenantId, TaskCreateInput input, CancellationToken ct = default);
    /// <summary>Re-validates, then inserts the task (+ sprint log). No SaveChanges.</summary>
    Task<Result<WorkTask>> CreateAsync(Guid tenantId, Guid creatorUserId, Guid actorEmployeeId, TaskCreateInput input,
        Guid creatorPositionObjectiveId, CancellationToken ct = default);
    /// <summary>Every read-only check EditTask did (frozen sprint, target sprint, event window, slack).</summary>
    Task<Result> ValidateEditAsync(Guid tenantId, WorkTask task, Objective objective, TaskEditInput input, CancellationToken ct = default);
    /// <summary>Re-validates, then mutates the tracked task and writes edit/percentage/sprint logs attributed to
    /// actorEmployeeId. No SaveChanges.</summary>
    Task<Result> ApplyEditAsync(Guid tenantId, Guid actorEmployeeId, WorkTask trackedTask, Objective objective,
        TaskEditInput input, string editLogSource, Guid? approvalRequestId, CancellationToken ct = default);
    /// <summary>Removes the task. No SaveChanges.</summary>
    void Delete(WorkTask trackedTask);
}
```

- [ ] **Step 1: Write the service tests (they fail to compile)**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskWriteServiceTests.cs`. Build the service with Moq mocks of the repositories listed in Step 3's constructor. Cover exactly these behaviours, which are the ones the old handlers enforced:

```csharp
[Fact] public async Task ValidateCreate_InactiveObjective_NotFound()
[Fact] public async Task ValidateCreate_DueDateOutsideModuleEventWindow_Conflict()
[Fact] public async Task ValidateCreate_EstimateAboveSlack_ConflictWithSlackJson()
[Fact] public async Task ValidateCreate_NoDefaultStatus_422()
[Fact] public async Task Create_StampsCreatorPositionAndCreatedBy_AndLogsSprintAdd()
[Fact] public async Task ValidateEdit_AchievedSprint_Forbidden()
[Fact] public async Task ValidateEdit_TargetSprintInOtherProject_Conflict()
[Fact] public async Task ApplyEdit_WritesEditLogWithSourceAndApprovalRequestId_AttributedToActor()
[Fact] public async Task ApplyEdit_ProgressChange_WritesPercentageLog()
```

Every test must assert the `StatusCode` (404/409/403/422) or the exact entity fields. Copy the error strings from the handlers below; the tests pin them.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~TaskWriteServiceTests"`
Expected: build FAIL, `TaskWriteService` not found.

- [ ] **Step 3: Implement the service by moving code, not rewriting it**

`TaskWriteService` constructor dependencies:
- `IObjectiveRepository`, `IProjectRepository`, `IWorkTaskRepository`, `ITaskStatusRepository`, `ISprintRepository`, `ITaskCategoryRepository`
- `IObjectiveAllocationSlackCalculator`, `ICalendarEventRepository`
- `ISprintActivityLogRepository`, `ITaskEditLogRepository`, `ITaskPercentageLogRepository`

The method bodies are the existing code, moved:

- `ValidateCreateAsync`: the block from `CreateTaskCommandHandler.Handle`, starting at `var objective = await _objectives.GetByIdForTenantAsync(...)` through the slack check.
  - Remove the `IsEffectiveManagerAsync` line (permission is the handler's job).
  - Use `input.*` in place of `request.*`.
  - Return `Result.Success()` at the end.
  - Error strings stay byte-identical.
- `CreateAsync`:
  - calls `ValidateCreateAsync` (return its failure as `Result<WorkTask>.Failure(error, status)`);
  - reloads `objective`, `project` and `defaultStatus` (the validation results are not cached; keep it simple);
  - then runs the existing body from `var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(...)` through the sprint-log add;
  - sets `CreatedById = creatorUserId` and `CreatorPositionObjectiveId = creatorPositionObjectiveId`;
  - returns `Result<WorkTask>.Success(task)`.
- `ValidateEditAsync`: the `EditTaskCommandHandler` checks for achieved sprint, target sprint, event window and slack, with `request` → `input`.
  - Keep the long comment about `SprintId == null` meaning "leave alone".
- `ApplyEditAsync`: calls `ValidateEditAsync`, then runs the `ExecuteInTransactionAsync` body of `EditTaskCommandHandler` **without** the transaction, `SaveChangesAsync`, asset sync or response build, with these substitutions:
  - `callerEmployeeId.Value` → `actorEmployeeId`
  - `Source = TaskEditLogSources.Direct` → `Source = editLogSource`
  - add `EditRequestId = approvalRequestId` to the `TaskEditLog`
  - `request.Reason` → `input.Reason`
  - Recompute `targetSprint` / `previousSprintId` inside the method. `TrackChange` moves in as a local function.
- `Delete`: `_tasks.Remove(trackedTask);`

`WorkTaskResponseMapper`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.Mappers;

public static class WorkTaskResponseMapper
{
    public static WorkTaskResponse ToResponse(WorkTask task, IReadOnlyList<Guid>? assigneeIds = null) => new(
        task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
        task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
        task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId,
        assigneeIds ?? Array.Empty<Guid>(), CreatedAt: task.CreatedAt);
}
```

> Check the `WorkTaskResponse` positional signature in `WorkTaskResponse.cs`. If the assignee parameter is optional or named differently, match it. This is a mechanical fix; note it as a deviation.

DI, WM block: `services.AddScoped<ITaskWriteService, TaskWriteService>();`

- [ ] **Step 4: Point the two handlers at the service**

- `CreateTaskCommandHandler`: replace the moved validation with `await _writes.ValidateCreateAsync(...)`, and replace the entity construction with `_writes.CreateAsync(..., creatorPositionObjectiveId: objective.Id, ...)`. Task 2 corrects the position.
- `EditTaskCommandHandler`: `ValidateEditAsync` plus `ApplyEditAsync(..., TaskEditLogSources.Direct, null, ...)` inside its existing transaction.
- Keep all permission checks as they are for now. Task 2 changes them.
- Remove the constructor dependencies that are no longer used.

- [ ] **Step 5: Update the existing handler tests' constructors only, and run everything**

In `CreateTaskCommandHandlerTests` and `EditTaskCommandHandlerTests`, build a **real** `TaskWriteService` from the same mocks the tests already create. That way every existing behavioural assertion keeps exercising the same code.

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~Features.WorkManagement.Tasks"`
Expected: all green, including the 9 new service tests.

- [ ] **Step 6: Commit**

```bash
git add -A src/ONEVO.Application/Features/WorkManagement/Tasks src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks
git commit -m "refactor(work-management): extract task create/edit/delete into ITaskWriteService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Route create/edit/delete through the approval engine

**Files:**
- Modify:
  - `CreateTaskCommand.cs`, `EditTaskCommand.cs`, `DeleteTaskCommand.cs`: the result type changes
  - the three handlers
  - `TasksController.cs` (`Create`, `Edit`, `Delete` actions)
- Test:
  - new `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskActionsThroughEngineTests.cs`
  - update the three existing handler test classes

**Interfaces:**
- Consumes (Plan 1):
  - `IWorkApprovalEngine.SubmitAsync(WorkAction)`
  - `IWorkNotificationEngine.NotifyAsync(WorkNotificationEvent)`
  - `IWorkHierarchyService.LoadTreeAsync`
  - `ProjectModuleTree.AncestorChain`
  - `WorkActionTypes.TaskCreate/TaskEdit/TaskDelete`, `WorkTargetTypes.Task`, `WorkNotificationKinds.Direct`
- Produces:
  - `CreateTaskCommand : IRequest<Result<TaskWriteOutcome>>`
  - `EditTaskCommand : IRequest<Result<TaskWriteOutcome>>`
  - `DeleteTaskCommand : IRequest<Result<TaskWriteOutcome>>`, whose `Task` is always null; `ApprovalRequestId` null means deleted

**Handler flow, written once here; all three follow it.**

```csharp
// after auth + callerEmployeeId + loading task/objective exactly as today:
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
    return Result<TaskWriteOutcome>.Forbidden("Only members of this module can change its tasks.");

var validation = await _writes.ValidateXxxAsync(...);          // create/edit only
if (!validation.IsSuccess)
    return Result<TaskWriteOutcome>.Failure(validation.Error!, validation.StatusCode ?? 400);

var tree = await _hierarchy.LoadTreeAsync(tenantId, objective.ProjectId, ct);

return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
{
    var decision = await _approvals.SubmitAsync(new WorkAction(
        tenantId, objective.ProjectId, callerEmployeeId.Value,
        WorkActionTypes.TaskXxx, WorkTargetTypes.Task,
        TargetId: /* create: null, else task.Id */,
        TargetTitle: /* create: input.Title, else task.Title */,
        TargetModuleId: objective.Id,
        PositionModuleId: /* create: objective.Id, else task.CreatorPositionObjectiveId */,
        PayloadJson: JsonSerializer.Serialize(input),                // delete: "{}"
        TargetUpdatedAt: /* create: null, else task.UpdatedAt ?? task.CreatedAt */), innerCt);
    if (!decision.IsSuccess)
        return Result<TaskWriteOutcome>.Failure(decision.Error!, decision.StatusCode ?? 400);

    if (!decision.Value!.IsDirect)
    {
        await _unitOfWork.SaveChangesAsync(innerCt);
        return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, decision.Value.ApprovalRequestId));
    }

    // Direct: apply through ITaskWriteService (create/edit/delete - see below), then:
    await _notifications.NotifyAsync(new WorkNotificationEvent(
        tenantId, objective.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
        WorkActionTypes.TaskXxx, WorkTargetTypes.Task, task.Id, task.Title, null, recipients), innerCt);
    await _unitOfWork.SaveChangesAsync(innerCt);
    // create/edit only: the existing _assetLinker.SyncAttachmentsAsync / SyncDescriptionImagesAsync calls, unchanged
    return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(WorkTaskResponseMapper.ToResponse(task, assigneeIds), null));
}, ct);
```

**Per action:**

- **Create:**
  - Input: `new TaskCreateInput(objective.Id, request.Title.Trim(), request.Description?.Trim(), request.CategoryId, request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId)`
  - Creator position:
    ```csharp
    var position = tree.AncestorChain(objective.Id).LastOrDefault(m => m.OwnerId == callerEmployeeId.Value)?.Id ?? objective.Id;
    ```
    `AncestorChain` goes self → root, so `LastOrDefault` returns the owned Module closest to the root.
  - Apply: `_writes.CreateAsync(tenantId, userId, callerEmployeeId.Value, input, position, innerCt)`
  - Recipients: `[objective.OwnerId]`
- **Edit:**
  - Input: `new TaskEditInput(request.Title.Trim(), request.Description?.Trim(), request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints, request.ProgressPercent, request.Reason?.Trim(), request.SprintId)`
  - Apply: `_writes.ApplyEditAsync(tenantId, callerEmployeeId.Value, task, objective, input, TaskEditLogSources.Direct, null, innerCt)`
  - Recipients:
    - `tree.Get(task.CreatorPositionObjectiveId ?? task.ObjectiveId)?.OwnerId`;
    - every assignee `EmployeeId` (`_assignments.GetByTaskIdAsync(task.Id, innerCt)`);
    - `objective.OwnerId`.
- **Delete:**
  - Load the task **tracked**: `GetTrackedByIdForTenantAsync`.
  - Read the assignees **before** `_writes.Delete(task)`.
  - Recipients are the same as for Edit.

**Controller mapping (`TasksController`):**

```csharp
// Create
return !result.IsSuccess ? Problem(result.Error, statusCode: result.StatusCode ?? 400)
    : result.Value!.Task is { } created ? StatusCode(201, created.ToViewModel())
    : StatusCode(202, new { approvalRequestId = result.Value.ApprovalRequestId });
// Edit: same, with Ok(task.ToViewModel()) for the applied branch.
// Delete: applied → NoContent(); pending → StatusCode(202, new { approvalRequestId = ... }).
```

- [ ] **Step 1: Write the failing engine-routing tests**

In `TaskActionsThroughEngineTests.cs`, mock `IWorkApprovalEngine`, `IWorkNotificationEngine`, `IWorkHierarchyService` and `ITaskWriteService`. Pin these behaviours:

```csharp
[Fact] public async Task Create_EnginePending_Returns202Outcome_NoTaskInserted_Saved()
[Fact] public async Task Create_EngineDirect_CreatesWithHighestOwnedModuleAsPosition_NotifiesModuleOwner()
[Fact] public async Task Create_EngineDirect_ActorOwnsNothingInChain_PositionIsTargetModule()
[Fact] public async Task Create_NotModuleMember_Forbidden_EngineNeverCalled()
[Fact] public async Task Create_ValidationFails_ReturnsItsStatus_EngineNeverCalled()
[Fact] public async Task Edit_PassesCreatorPositionAndUpdatedAtToEngine()
[Fact] public async Task Edit_EngineDirect_AppliesWithDirectSource_NotifiesPositionHolderAssigneesAndOwner()
[Fact] public async Task Edit_EngineConflict409_PassesThrough()
[Fact] public async Task Delete_EnginePending_TaskNotRemoved()
[Fact] public async Task Delete_EngineDirect_RemovesAndNotifies()
```

Assert the `WorkAction` fields with `It.Is<WorkAction>(a => a.ActionType == ... && a.PositionModuleId == ... && a.TargetId == ...)`. Assert recipients with `e.RecipientEmployeeIds.Should().BeEquivalentTo(...)`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~TaskActionsThroughEngineTests"`
Expected: FAIL. The constructor and result types don't match yet.

- [ ] **Step 3: Implement the handler changes and the controller mapping above**

- [ ] **Step 4: Update the existing `CreateTask`/`EditTask`/`DeleteTask` handler tests**

- Add mocks for the new dependencies, with the engine defaulting to `Result<ApprovalDecision>.Success(ApprovalDecision.Direct)`.
- Change result assertions from `result.Value!.X` to `result.Value!.Task!.X`.
- Tests that asserted **"non-owner member is Forbidden"** now expect: member → engine is called. Rewrite them to assert that the engine receives the call, because the engine (not the handler) now decides. Do **not** keep them as Forbidden.

- [ ] **Step 5: Run the tests and the backend gate**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~Features.WorkManagement"`, then the full gate.
Expected: green.

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(work-management): route task create/edit/delete through the approval engine (201/200/204 or 202)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Task status move rule, and creator-position stamping on subtask and duplicate

**Files:**
- Modify:
  - `Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
  - `Tasks/Commands/CreateSubtask/CreateSubtaskCommandHandler.cs` (line ~92)
  - `Tasks/Commands/DuplicateTask/DuplicateTaskCommandHandler.cs` (line ~138)
- Test: `MoveTaskStatusCommandHandlerTests.cs`, `CreateSubtaskCommandHandlerTests.cs`, `DuplicateTaskCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing tests** in `MoveTaskStatusCommandHandlerTests`:

```csharp
[Fact] public async Task Parent_AtOrAbovePosition_CanMove_IncludingPrivateStatus()
[Fact] public async Task Assignee_CanMove_PublicStatus()
[Fact] public async Task Creator_CanMove_PublicStatus()          // task.CreatedById == current UserId
[Fact] public async Task Assignee_PrivateStatus_Forbidden()
[Fact] public async Task PlainModuleMember_NotAssigneeNotCreator_Forbidden()
[Fact] public async Task Move_NotifiesModuleOwnerAndCreator_DirectStatusChange()
```

Also, in the subtask and duplicate tests:

```csharp
[Fact] public async Task CreateSubtask_InheritsParentCreatorPosition()
[Fact] public async Task DuplicateTask_PositionIsDestinationModule()
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~MoveTaskStatusCommandHandlerTests|FullyQualifiedName~CreateSubtaskCommandHandlerTests|FullyQualifiedName~DuplicateTaskCommandHandlerTests"`
Expected: the new tests FAIL.

- [ ] **Step 3: Replace the `MoveTaskStatus` permission block**

Replace the whole `if (!await _membership.IsEffectiveManagerAsync(...)) { ... }` block with:

```csharp
var tree = await _hierarchy.LoadTreeAsync(tenantId, task.ProjectId, ct);
var isParent = tree.IsAtOrAbove(callerEmployeeId.Value, task.CreatorPositionObjectiveId ?? task.ObjectiveId);
var isAssignee = await _assignments.GetByTaskAndEmployeeAsync(task.Id, callerEmployeeId.Value, ct) is not null;
var isCreator = task.CreatedById == _currentUser.UserId;

if (!isParent && !isAssignee && !isCreator)
    return Result.Forbidden("Only the task's creator, its assignees, or a module owner above it can change its status.");
if (newStatus.Visibility == TaskStatusVisibilities.Private && !isParent)
    return Result.Forbidden("Only the module owner can move a task into this status.");
```

Inside the transaction, just before `await _unitOfWork.SaveChangesAsync(innerCt);`:

```csharp
var creatorEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, task.CreatedById, innerCt);
await _notifications.NotifyAsync(new WorkNotificationEvent(
    tenantId, task.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
    WorkActionTypes.TaskStatusChange, WorkTargetTypes.Task, task.Id, task.Title, null,
    creatorEmployeeId is { } c ? [objective.OwnerId, c] : [objective.OwnerId]), innerCt);
```

Add the constructor dependencies: `IWorkHierarchyService`, `ITaskAssignmentRepository` (if not already injected), and `IWorkNotificationEngine`.

- [ ] **Step 4: Stamp the creator position in the subtask and duplicate handlers**

- `CreateSubtask` `new WorkTask { ... }`: add `CreatorPositionObjectiveId = parent.CreatorPositionObjectiveId ?? parent.ObjectiveId,`
- `DuplicateTask` `new WorkTask { ... }` (the copy, line ~138): add `CreatorPositionObjectiveId = destinationObjective.Id,`

- [ ] **Step 5: Run the tests and the backend gate**

Expected: green. Update the other existing `MoveTaskStatus` tests only where the old member rule was asserted.

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(work-management): task status move allowed for parent/assignee/creator with owner+creator notification; stamp creator position on subtask/duplicate

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Task appliers (`task.create`, `task.edit`, `task.delete`)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskCreateApplier.cs`, `TaskEditApplier.cs`, `TaskDeleteApplier.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAppliersTests.cs`

**Interfaces:**
- Consumes (Plan 1): `IApprovalActionApplier`, `ApprovalApplyContext(Request, PayloadJson, DeciderEmployeeId)`, `ApplyOutcome.Applied/Stale/Invalid(msg)`
- Consumes (Task 1): `ITaskWriteService`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Create_AppliesAsRequester_PositionIsTargetModule_StoresCreatedTaskIdAsTarget()
[Fact] public async Task Create_ObjectiveGone_Stale()
[Fact] public async Task Create_ValidationFails_Invalid_WithMessage()
[Fact] public async Task Create_RequesterInactive_Stale()
[Fact] public async Task Create_ReadsLegacyPascalCasePayload()          // {"ObjectiveId":..,"Title":..} from the data migration
[Fact] public async Task Edit_TaskChangedAfterRequest_Stale()           // task.UpdatedAt > Request.TargetUpdatedAtSnapshot
[Fact] public async Task Edit_NoSnapshot_AppliesWithApprovedRequestSourceAndRequestId()
[Fact] public async Task Edit_TaskGone_Stale()
[Fact] public async Task Delete_TaskGone_Stale()
[Fact] public async Task Delete_RemovesTask()
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~TaskAppliersTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

internal static class TaskPayload
{
    // Case-insensitive: rows migrated from the old request tables were serialised PascalCase.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed class TaskCreateApplier : IApprovalActionApplier
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;

    public TaskCreateApplier(ITaskWriteService writes, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership)
    {
        _writes = writes;
        _objectives = objectives;
        _membership = membership;
    }

    public string ActionType => WorkActionTypes.TaskCreate;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var input = JsonSerializer.Deserialize<TaskCreateInput>(context.PayloadJson, TaskPayload.Options);
        if (input is null || string.IsNullOrWhiteSpace(input.Title))
            return ApplyOutcome.Invalid("The task request has no title.");

        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, input.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return ApplyOutcome.Stale;

        var requester = await _membership.GetActiveAssigneeAsync(request.TenantId, request.RequestedByEmployeeId, ct);
        if (requester is null)
            return ApplyOutcome.Stale;

        var created = await _writes.CreateAsync(request.TenantId, requester.UserId, request.RequestedByEmployeeId,
            input, input.ObjectiveId, ct);
        if (!created.IsSuccess)
            return ApplyOutcome.Invalid(created.Error ?? "The task could not be created.");

        request.TargetId = created.Value!.Id;   // links the approved request to the task it produced
        return ApplyOutcome.Applied;
    }
}
```

`TaskEditApplier`:
- Load the task tracked (`IWorkTaskRepository.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId!.Value)`); if it is null, return `Stale`.
- If `request.TargetUpdatedAtSnapshot is { } snap && (task.UpdatedAt ?? task.CreatedAt) > snap`, return `Stale`.
- Load the objective; if it is null, return `Stale`.
- Deserialize `TaskEditInput` with `TaskPayload.Options`.
- Call `_writes.ApplyEditAsync(request.TenantId, request.RequestedByEmployeeId, task, objective, input, TaskEditLogSources.ApprovedRequest, request.Id, ct)`.
- A failure returns `Invalid(error)`.

`TaskDeleteApplier`: load the task tracked; if it is null, return `Stale`; otherwise `_writes.Delete(task)` and return `Applied`.

DI, WM block:

```csharp
        services.AddScoped<IApprovalActionApplier, TaskCreateApplier>();
        services.AddScoped<IApprovalActionApplier, TaskEditApplier>();
        services.AddScoped<IApprovalActionApplier, TaskDeleteApplier>();
```

- [ ] **Step 4: Run the tests and the backend gate**

Expected: green.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(work-management): add task create/edit/delete approval appliers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Navigation, history and demo seeder now read `wm_approval_requests`

**Files:**
- Modify:
  - `GetWorkNotificationNavigationQueryHandler.cs`
  - `EfWorkApprovalHistoryRepository.cs`
  - `WorkManagementDapiDemoSeeder.Tasks.cs`
- Test:
  - `GetWorkNotificationNavigationQueryHandlerTests.cs`
  - `EfWorkApprovalHistoryRepositoryTests.cs`
  - `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs`

- [ ] **Step 1: Update the tests first (they fail)**

- **Navigation:**
  - Replace the `task_creation_request` and `task_edit_request` cases with `Navigation_WorkApprovalRequest_OpensProjectApprovalsTab`: expect `(projectId, rootObjective.Id, null, "approvals")`.
  - Add `Navigation_WorkApprovalRequest_Missing_NotFound`.
- **History repository:** replace the `TaskCreationRequests`/`TaskEditRequests` seed rows with `WorkApprovalRequests` rows (`task.create`, `task.edit`, `task.delete`). Assert:
  - kinds `task_creation`, `task_edit`, `task_delete`;
  - the "participated" rule: requester, decider, or pending approver.
- **Demo seeder test:**
  - Replace `verify.TaskCreationRequests` with `verify.WorkApprovalRequests.CountAsync(r => r.ProjectId == project.Id && r.ActionType == "task.create" && r.Status == "pending")`.
  - Change the idempotency count the same way.

- [ ] **Step 2: Implement**

**Navigation handler:**
- Remove the `ITaskCreationRequestRepository`/`ITaskEditRequestRepository` dependencies and the two branch methods.
- Add the `IWorkApprovalRequestRepository` dependency and the switch arm:

```csharp
"work_approval_request" => await FromWorkApprovalRequestAsync(tenantId, request.RelatedEntityId, ct),
```

```csharp
private async Task<Result<WorkNotificationNavigationResponse>> FromWorkApprovalRequestAsync(
    Guid tenantId, Guid requestId, CancellationToken ct)
{
    var approval = await _workApprovals.GetTrackedByIdForTenantAsync(tenantId, requestId, ct);
    if (approval is null)
        return Result<WorkNotificationNavigationResponse>.NotFound("Approval request not found.");

    var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, approval.ProjectId, ct);
    if (root is null)
        return Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.");

    return Result<WorkNotificationNavigationResponse>.Success(new(approval.ProjectId, root.Id, null, "approvals"));
}
```

**History repository:** replace the `taskCreation` and `taskEdits` queries with one query:

```csharp
var engineRequests = await _db.WorkApprovalRequests.AsNoTracking()
    .Where(r => r.TenantId == tenantId && r.ProjectId == projectId && r.ActionType.StartsWith("task.")
        && (r.RequestedByEmployeeId == employeeId || r.DecidedByEmployeeId == employeeId
            || (r.Status == WorkApprovalRequestStatuses.Pending && r.ApproverEmployeeId == employeeId)))
    .ToListAsync(ct);
var taskRecords = engineRequests.Select(r => new WorkApprovalHistoryRecord(
    r.Id, r.PositionObjectiveId ?? Guid.Empty,
    r.ActionType switch { "task.create" => "task_creation", "task.edit" => "task_edit", "task.delete" => "task_delete", _ => r.ActionType },
    r.Status, r.TargetTitle, r.PayloadJson, r.RequestedByEmployeeId,
    r.DecidedByEmployeeId ?? r.ApproverEmployeeId, r.DecidedByEmployeeId, r.DecisionComment,
    r.CreatedAt, r.DecidedAt)).ToList();
```

Use `taskRecords` wherever `taskCreation`/`taskEdits` were concatenated. Leave the objective-change, status-template and invitation queries untouched **in this task**. Task 6A moves the status-template query onto `wm_approval_requests`.

> "Pending approver" uses the stored approver. After a transfer, the new holder sees the request in the inbox (`scope=inbox`), not in this legacy history view. That is accepted; Plan 4 replaces this view.

**Demo seeder `SeedTaskCreationRequestsAsync`:** rename it to `SeedTaskApprovalRequestsAsync`. Keep the deterministic id and the `IgnoreQueryFilters` idempotency check, now against `db.WorkApprovalRequests`. Build the row:

```csharp
var objective = db.Objectives.Local.FirstOrDefault(o => o.Id == objectiveId)
    ?? await db.Objectives.IgnoreQueryFilters().FirstAsync(o => o.TenantId == DapiTenantId && o.Id == objectiveId, ct);
var input = new TaskCreateInput(objectiveId, spec.Title, spec.Description, categoryId, spec.Priority,
    DueDate: null, EstimatedHours: spec.EstimatedHours, StoryPoints: null, SprintId: null);
db.WorkApprovalRequests.Add(new WorkApprovalRequest
{
    Id = requestId, TenantId = DapiTenantId, ProjectId = objective.ProjectId,
    ActionType = WorkActionTypes.TaskCreate, TargetType = WorkTargetTypes.Task, TargetId = null,
    TargetTitle = spec.Title, PositionObjectiveId = objectiveId, ApproverSource = WorkApprovalSources.Hierarchy,
    ApproverEmployeeId = objective.OwnerId, RequestedByEmployeeId = employeeIdByPersonKey[spec.RequesterKey],
    PayloadJson = JsonSerializer.Serialize(input), Status = WorkApprovalRequestStatuses.Pending,
    CreatedById = ResolveUserId(spec.RequesterKey), CreatedAt = now
});
```

The old code passed `SprintId: Guid.Empty`. That was a bug (a non-null empty sprint fails validation on approve), so it is fixed to `null` here. Note it in the commit.

- [ ] **Step 3: Run the tests and the backend gate**

Expected: green.

- [ ] **Step 4: Commit**

```bash
git commit -am "refactor(work-management): navigation, approval history and dapi demo seed read task requests from wm_approval_requests

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Delete the old task-request flows and migrate their data

- [ ] **Step 1: Delete these files** (grep first; each must be referenced only by the others in this list or by its own test):

**Application:**
- `Tasks/Commands/`: `ApproveTaskCreationRequest/`, `RejectTaskCreationRequest/`, `CancelTaskCreationRequest/`, `CreateTaskCreationRequest/`, `ApproveTaskEditRequest/`, `RejectTaskEditRequest/`, `CancelTaskEditRequest/`, `CreateTaskEditRequest/` (whole folders)
- `Tasks/Queries/`: `GetMyTaskCreationRequests/`, `GetMyTaskEditRequests/`
- `Tasks/DTOs/TaskCreationRequestPayload.cs`, `Tasks/DTOs/TaskEditRequestPayload.cs`
- `Tasks/RepositoryInterfaces/ITaskCreationRequestRepository.cs`, `ITaskEditRequestRepository.cs`
- In `Tasks/DTOs/Responses/WorkTaskResponse.cs`: delete **only** the `TaskCreationRequestResponse` and `TaskEditRequestResponse` records

**Domain:** `Tasks/Entities/TaskCreationRequest.cs`, `Tasks/Entities/TaskEditRequest.cs`

**Infrastructure:**
- `Configurations/WorkManagement/TaskCreationRequestConfiguration.cs`, `TaskEditRequestConfiguration.cs`
- `Repositories/WorkManagement/EfTaskCreationRequestRepository.cs`, `EfTaskEditRequestRepository.cs`
- their 4 DI lines
- the 2 DbSets in `ApplicationDbContext.cs`
- In `TaskEditLogConfiguration.cs`: delete the line `builder.HasOne<TaskEditRequest>()...` (keep the `EditRequestId` column; it now holds a `wm_approval_requests` id)

**API:**
- `TasksController.cs`: the 10 actions from `CreateEditRequest` (`tasks/{taskId}/edit-requests`) through `task-creation-requests/mine`
- In `Contracts/WorkManagement/Tasks/`: `TaskCreationRequestContracts.cs`, plus `CreateTaskEditRequestRequest` and any `ToViewModel` for the deleted responses in `TaskContracts.cs`

**Tests (useless: they test deleted code):**
- `ApproveTaskCreationRequestCommandHandlerTests.cs`, `ApproveTaskEditRequestCommandHandlerTests.cs`, `CancelTaskEditRequestCommandHandlerTests.cs`
- `CreateTaskCreationRequestCommandHandlerTests.cs`, `CreateTaskEditRequestCommandHandlerTests.cs`, `CreateTaskEditRequestCommandValidatorTests.cs`
- `GetMyTaskCreationRequestsQueryHandlerTests.cs`, `GetMyTaskEditRequestsQueryHandlerTests.cs`
- `RejectTaskEditRequestCommandHandlerTests.cs`, `RejectTaskEditRequestCommandValidatorTests.cs`
- `TaskCreationRequestConfigurationTests.cs`, `TaskEditRequestConfigurationTests.cs`

**Also:** the doc comment in `MyProjectMilestoneResponse.cs:15` that names `Approve|RejectTaskEditRequestCommandHandler`. Reword it to "the task approval decision (WorkApprovalDecisionRules)".

Then run:

```bash
dotnet build -c Release
```

Expected: 0 errors. Any remaining reference is a missed caller; fix it by pointing it at the engine flow, never by restoring the deleted type.

- [ ] **Step 2: Generate the migration**

```bash
dotnet ef migrations add MoveTaskRequestsToWorkApprovals --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj
```

Expected generated `Up`:
- `DropForeignKey` on `task_edit_logs` → `task_edit_requests`;
- `DropTable("task_edit_requests")` and `DropTable("task_creation_requests")`.

Check the `ApplicationDbContextModelSnapshot.cs` diff: only those removals.

- [ ] **Step 3: Insert the data copy at the START of `Up`, before any generated drop**

```csharp
            // Copy every old task request (all statuses, so history survives) into wm_approval_requests,
            // keeping the same ids so task_edit_logs.edit_request_id still points at the right row.
            // The old flow allowed several pending edits per task; the new unique index allows one, so
            // only the newest pending edit per task stays pending and older ones become 'stale'.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, o.project_id, 'task.create', 'task', r.created_task_id,
                       COALESCE(r.payload_json->>'Title', r.payload_json->>'title', 'Task'),
                       r.objective_id, 'hierarchy', COALESCE(r.decided_by_employee_id, o.owner_id),
                       r.requested_by_employee_id,
                       jsonb_build_object('ObjectiveId', r.objective_id) || r.payload_json,
                       r.status, r.decided_by_employee_id, r.decision_comment, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM task_creation_requests r
                JOIN objectives o ON o.id = r.objective_id;

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, t.project_id, 'task.edit', 'task', r.task_id, t.title,
                       COALESCE(t.creator_position_objective_id, t.objective_id), 'hierarchy',
                       COALESCE(r.decided_by_employee_id, o.owner_id), r.requested_by_employee_id,
                       r.payload_json || jsonb_build_object('Reason', r.reason),
                       CASE WHEN r.status = 'pending'
                                 AND ROW_NUMBER() OVER (PARTITION BY r.task_id, r.status ORDER BY r.created_at DESC) > 1
                            THEN 'stale' ELSE r.status END,
                       r.decided_by_employee_id, r.decision_comment, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM task_edit_requests r
                JOIN tasks t ON t.id = r.task_id
                JOIN objectives o ON o.id = t.objective_id;
            ");
```

> **Before you commit, verify the column names against the Plan 1 migration** (`20260928061506_AddWorkApprovalEngineFoundation.cs`) and `BaseEntity`. The list above assumes snake_case of every `WorkApprovalRequest` + `BaseEntity` property. If a column differs (for example, `deleted_at` is absent), match the real schema. Report it as a deviation.

`Down`: leave the generated re-create of the tables (empty) and add, at the top of `Down`:

```csharp
            // Data is not restored: rows live on in wm_approval_requests.
```

- [ ] **Step 4: Produce the SQL script and run the full gate**

```bash
dotnet ef migrations script AddWorkApprovalEngineFoundation MoveTaskRequestsToWorkApprovals --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj -o <scratchpad>/move_task_requests.sql
dotnet build -c Release
dotnet test tests/ONEVO.Tests.Unit -c Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
```

Expected: the script shows the 2 INSERTs before the DROPs, and all suites are green.

- [ ] **Step 5: Commit, listing every deleted test file in the body**

```bash
git add -A
git commit -m "refactor(work-management): retire task creation/edit request tables in favour of wm_approval_requests

Migrates all rows (ids preserved), drops task_creation_requests and task_edit_requests.
Removed tests (tested deleted code): <list the 12 files>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6A (amendment): Task-status template requests onto the engine

Rules: see **Global Constraints → Task Status template requests**. Do Task 6 first; this task reuses Task 4's `TaskPayload.Options` and Task 5's `IWorkApprovalRequestRepository` dependency in the navigation handler.

**Files:**
- Modify:
  - `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkActionLabels.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/RepositoryInterfaces/IWorkApprovalRequestRepository.cs`
  - `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalRequestRepository.cs`
  - `Tasks/Services/TaskStatusChangeAccessService.cs`
  - `Tasks/Services/TaskStatusChangeRequestConflictSweeper.cs`
  - `Tasks/Commands/CreateTaskStatusChangeRequest/CreateTaskStatusChangeRequestCommandHandler.cs`
  - `Tasks/Queries/GetProjectTaskStatusChangeRequests/GetProjectTaskStatusChangeRequestsQueryHandler.cs`
  - `Tasks/Commands/CreateTaskStatus/*Handler.cs`, `EditTaskStatus/*Handler.cs`, `DeleteTaskStatus/*Handler.cs`, `ReorderTaskStatuses/*Handler.cs`
  - `Tasks/DTOs/Responses/TaskStatusChangeRequestResponses.cs`
  - `Tasks/Queries/GetWorkNotificationNavigation/GetWorkNotificationNavigationQueryHandler.cs`
  - `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalHistoryRepository.cs`
  - `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TaskStatusChangeRequestsController.cs`
  - `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskStatusChangeRequestContracts.cs`
  - `src/ONEVO.Infrastructure/DependencyInjection.cs`, `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create:
  - `Tasks/DTOs/TaskStatusTemplateChangePayload.cs`
  - `Tasks/Appliers/TaskStatusTemplateChangeApplier.cs`
  - migration `MoveTaskStatusChangeRequestsToWorkApprovals`
- Test:
  - rewrite `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskStatusChangeRequestHandlerTests.cs`
  - update `EditTaskStatusCommandHandlerTests.cs`, `DeleteTaskStatusCommandHandlerTests.cs`, `ReorderTaskStatusesCommandHandlerTests.cs` (and `CreateTaskStatusCommandHandlerTests.cs` if it exists)
  - update `GetWorkNotificationNavigationQueryHandlerTests.cs`, `EfWorkApprovalHistoryRepositoryTests.cs`, `EfWorkApprovalRequestRepositoryTests.cs`

**Interfaces:**
- Consumes (Plan 1): `IWorkApprovalEngine.SubmitAsync`, `WorkApprovalDecisionRules.CanDecide`, `IWorkHierarchyService.LoadTreeAsync`, `IWorkNotificationEngine.NotifyAsync`, `IApprovalActionApplier`, `ApplyOutcome`
- Consumes (Task 4): `TaskPayload.Options` (case-insensitive JSON, `internal` in `Tasks.Appliers`)
- Consumes (existing, unchanged): `TaskStatusChangeSet`, `TaskStatusChangeSet.Footprint()`, `TaskStatusChangeFootprint.ConflictsWith`, `TaskStatusChangeSetRules.Validate`, `TaskStatusChangeSetApplier.Apply(...)` with outcomes `Applied/Stale/Invalid`, `ITaskStatusRepository.GetProjectTemplateAsync/AddAsync/Update/Remove`, `IWorkTaskRepository.AnyActiveByStatusIdAsync`
- Produces:
  - `WorkTargetTypes.Project = "project"`, `WorkActionTypes.ProjectStatusTemplateChange = "project.status_template_change"`
  - `TaskStatusTemplateChangePayload(TaskStatusChangeSet Changes, string? Note)`
  - `IWorkApprovalRequestRepository.ListTrackedPendingByActionAsync(Guid tenantId, Guid projectId, string actionType, CancellationToken ct = default)` returning `Task<IReadOnlyList<WorkApprovalRequest>>`
  - `ITaskStatusChangeRequestConflictSweeper.MarkConflictingStaleAsync(Guid tenantId, Guid projectId, Guid actorEmployeeId, TaskStatusChangeFootprint applied, Guid? excludingRequestId, CancellationToken ct = default)` returning `Task<int>`
  - `ITaskStatusChangeAccessService.ResolveAsync` only (`ListApproverEmployeeIdsAsync` is removed)
  - HTTP: `GET projects/{projectId}/task-status-change-requests` and `POST projects/{projectId}/task-status-change-requests` keep their routes and response shapes. Approve/reject/cancel move to the generic `POST approvals/{id}/approve|reject|cancel` (Plan 1).

- [ ] **Step 1: Write the failing tests**

Rewrite `TaskStatusChangeRequestHandlerTests.cs`. Keep its existing fixture helpers (project, root objective, statuses) where they still compile. Replace the 12 old cases with these; each asserts a status code, the exact `WorkAction`, or exact entity fields:

```csharp
// Create (handler + mocked IWorkApprovalEngine)
[Fact] public async Task Create_by_submodule_member_submits_template_change_to_engine_with_root_position()
    // engine receives ActionType "project.status_template_change", TargetType "project", TargetId null,
    // TargetModuleId == PositionModuleId == root.Id; result.Value.Id == the engine's ApprovalRequestId; Status "pending"
[Fact] public async Task Create_by_root_owner_is_refused_because_they_edit_directly()      // 400, engine never called
[Fact] public async Task Create_by_root_member_who_is_not_owner_now_goes_to_the_engine()  // behaviour change pinned
[Fact] public async Task Create_by_non_member_is_forbidden()                               // 403
[Fact] public async Task Create_against_already_stale_snapshot_is_a_conflict()              // 409, engine never called
[Fact] public async Task Create_engine_failure_passes_through()                             // engine 422 → 422

// Applier
[Fact] public async Task Applier_applies_changes_and_sweeps_conflicts_excluding_itself()
[Fact] public async Task Applier_stale_snapshot_returns_Stale_without_touching_statuses()
[Fact] public async Task Applier_delete_with_tasks_still_in_the_status_returns_Invalid_with_message()
[Fact] public async Task Applier_reads_migrated_PascalCase_payload()  // {"Changes":{...},"Note":"x"}

// Sweeper (mocked IWorkApprovalRequestRepository + IWorkNotificationEngine)
[Fact] public async Task Sweeper_marks_only_conflicting_pending_requests_stale_and_notifies_their_requesters()

// Access + decision rule
[Fact] public async Task Access_direct_edit_is_root_owner_only_and_root_member_can_request()
[Fact] public async Task Root_member_cannot_decide_a_template_request()
    // WorkApprovalDecisionRules.CanDecide(tree, request{PositionObjectiveId = root, Hierarchy}, rootMember) == false; root owner == true
```

In `EditTaskStatusCommandHandlerTests` add `Root_module_member_who_is_not_owner_is_forbidden` (403). In the four direct status-handler test classes, change fixtures that granted access through `IsEffectiveManagerAsync` to make the caller the root owner (`defaultObjective.OwnerId = callerEmployeeId`). Update sweeper mock verifications to `MarkConflictingStaleAsync`.

In `EfWorkApprovalRequestRepositoryTests` add `ListTrackedPendingByAction_ReturnsOnlyPendingOfThatActionInThatProject`.

In `GetWorkNotificationNavigationQueryHandlerTests` change the `task_status_change_request` case to seed a `WorkApprovalRequest` (same id) instead of a `TaskStatusChangeRequest`; the expected result `(projectId, root.Id, null, "approvals")` is unchanged.

In `EfWorkApprovalHistoryRepositoryTests` replace the `TaskStatusChangeRequests` seed row with a `WorkApprovalRequests` row (`ActionType = "project.status_template_change"`, `TargetType = "project"`) and assert kind `task_status_change` with title `Task statuses`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~TaskStatusChangeRequestHandlerTests|FullyQualifiedName~TaskStatusCommandHandlerTests|FullyQualifiedName~ReorderTaskStatusesCommandHandlerTests|FullyQualifiedName~GetWorkNotificationNavigationQueryHandlerTests|FullyQualifiedName~EfWorkApprovalHistoryRepositoryTests|FullyQualifiedName~EfWorkApprovalRequestRepositoryTests"`
Expected: build FAIL (new types and signatures missing).

- [ ] **Step 3: Constants, label, payload, repository method**

`WorkApprovalRequest.cs`:

```csharp
public static class WorkTargetTypes
{
    public const string Module = "module";
    public const string Task = "task";
    public const string Sprint = "sprint";
    /// <summary>Project-wide settings with no single row as target, e.g. the task-status template.</summary>
    public const string Project = "project";
}
```

and, in `WorkActionTypes`, after `SprintDelete`:

```csharp
    /// <summary>Add/rename/delete/reorder the project's task statuses. Not task.status_change (one task's move).</summary>
    public const string ProjectStatusTemplateChange = "project.status_template_change";
```

`WorkActionLabels.Labels`, new entry (the notification reads "Bala changed the task statuses of "Portal""):

```csharp
        [WorkActionTypes.ProjectStatusTemplateChange] = "changed the task statuses of",
```

`Tasks/DTOs/TaskStatusTemplateChangePayload.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>wm_approval_requests.payload_json for project.status_template_change.</summary>
public sealed record TaskStatusTemplateChangePayload(TaskStatusChangeSet Changes, string? Note);
```

`IWorkApprovalRequestRepository`:

```csharp
    /// <summary>Tracked pending requests of one action type in a project - used by conflict sweeps.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListTrackedPendingByActionAsync(
        Guid tenantId, Guid projectId, string actionType, CancellationToken ct = default);
```

`EfWorkApprovalRequestRepository`:

```csharp
    public async Task<IReadOnlyList<WorkApprovalRequest>> ListTrackedPendingByActionAsync(
        Guid tenantId, Guid projectId, string actionType, CancellationToken ct = default)
        => await _db.WorkApprovalRequests
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId
                && r.ActionType == actionType && r.Status == WorkApprovalRequestStatuses.Pending)
            .ToListAsync(ct);
```

- [ ] **Step 4: Access service: root owner only**

In `TaskStatusChangeAccessService`:
- Delete `ListApproverEmployeeIdsAsync` from the interface and the class (the engine notifies the approver now).
- Replace the `IsEffectiveManagerAsync` line in `ResolveAsync` with:

```csharp
        // Root Module has no parent, so "at or above the root position" == "owns the root".
        // Root members are not approvers any more (user decision 2026-09-28): they request.
        if (root.OwnerId == employeeId)
            return new TaskStatusChangeAccess(root, CanEditDirectly: true, CanRequest: false);
```

- Keep the `canRequest` logic. Remove the `IMilestoneMembershipCoordinator` constructor dependency if nothing else uses it.
- Update the class doc comment: "Approver = root (default) module owner only; anyone else who owns or is an active member of any module in the project may file a change request."

- [ ] **Step 5: Direct status handlers: root owner only**

In `CreateTaskStatusCommandHandler`, `EditTaskStatusCommandHandler`, `DeleteTaskStatusCommandHandler` and `ReorderTaskStatusesCommandHandler`, replace the `if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))` check with:

```csharp
        if (defaultObjective.OwnerId != callerEmployeeId.Value)
            return <same Result type>.Forbidden(
                "Only the project's top module owner can change task statuses directly. Others can send a change request.");
```

Remove `_membership` from a handler's constructor only if nothing else in it uses `_membership`.

Change their sweeper calls to the new signature (Step 6):

```csharp
await _sweeper.MarkConflictingStaleAsync(tenantId, project.Id, callerEmployeeId.Value, footprint, null, innerCt);
```

(Use each handler's existing footprint variable/expression; the old `project.Name` argument is dropped.)

- [ ] **Step 6: Sweeper over `wm_approval_requests`**

Replace the body of `TaskStatusChangeRequestConflictSweeper.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public interface ITaskStatusChangeRequestConflictSweeper
{
    /// <summary>
    /// Closes as stale every pending status-template request in the project whose footprint conflicts
    /// with a change that was just applied, and notifies its requester. Never calls SaveChangesAsync -
    /// run inside the caller's transaction. Returns how many were closed.
    /// </summary>
    Task<int> MarkConflictingStaleAsync(
        Guid tenantId, Guid projectId, Guid actorEmployeeId, TaskStatusChangeFootprint applied,
        Guid? excludingRequestId, CancellationToken ct = default);
}

public sealed class TaskStatusChangeRequestConflictSweeper : ITaskStatusChangeRequestConflictSweeper
{
    public const string OutdatedComment = "Another change to the same statuses was applied first. Resubmit against the current statuses.";
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkNotificationEngine _notifications;

    public TaskStatusChangeRequestConflictSweeper(IWorkApprovalRequestRepository requests, IWorkNotificationEngine notifications)
    {
        _requests = requests;
        _notifications = notifications;
    }

    public async Task<int> MarkConflictingStaleAsync(
        Guid tenantId, Guid projectId, Guid actorEmployeeId, TaskStatusChangeFootprint applied,
        Guid? excludingRequestId, CancellationToken ct = default)
    {
        if (applied.IsEmpty)
            return 0;

        var pending = await _requests.ListTrackedPendingByActionAsync(
            tenantId, projectId, WorkActionTypes.ProjectStatusTemplateChange, ct);
        var now = DateTimeOffset.UtcNow;
        var closed = 0;

        foreach (var request in pending)
        {
            if (request.Id == excludingRequestId)
                continue;

            var payload = JsonSerializer.Deserialize<TaskStatusTemplateChangePayload>(request.PayloadJson, PayloadOptions);
            if (payload?.Changes is null || !payload.Changes.Footprint().ConflictsWith(applied))
                continue;

            request.Status = WorkApprovalRequestStatuses.Stale;
            request.DecisionComment = OutdatedComment;
            request.DecidedAt = now;
            _requests.Update(request);
            closed++;

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, projectId, actorEmployeeId, WorkNotificationKinds.Stale,
                WorkActionTypes.ProjectStatusTemplateChange, WorkTargetTypes.Project, null,
                request.TargetTitle, request.Id, [request.RequestedByEmployeeId]), ct);
        }

        return closed;
    }
}
```

- [ ] **Step 7: Create-request handler submits to the engine**

In `CreateTaskStatusChangeRequestCommandHandler`:
- Constructor: remove `ITaskStatusChangeRequestRepository`, `IMilestoneMembershipCoordinator`, `INotificationDispatcher`; add `IWorkApprovalEngine _approvals`.
- Keep everything up to and including the dry-run checks and the `requesterDisplayName` lookup, unchanged. Delete the `approverIds` line.
- Replace the transaction body with:

```csharp
        var payload = new TaskStatusTemplateChangePayload(
            request.Changes, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var decision = await _approvals.SubmitAsync(new WorkAction(
                tenantId, project.Id, callerEmployeeId.Value,
                WorkActionTypes.ProjectStatusTemplateChange, WorkTargetTypes.Project,
                TargetId: null, TargetTitle: project.Name,
                TargetModuleId: access.RootObjective.Id, PositionModuleId: access.RootObjective.Id,
                PayloadJson: JsonSerializer.Serialize(payload), TargetUpdatedAt: null), innerCt);
            if (!decision.IsSuccess)
                return Result<TaskStatusChangeRequestResponse>.Failure(decision.Error!, decision.StatusCode ?? 400);
            if (decision.Value!.IsDirect) // cannot happen: access said the caller is not the root owner
                return Result<TaskStatusChangeRequestResponse>.Failure(
                    "You can edit task statuses directly - no request needed.", 400);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskStatusChangeRequestResponse>.Success(new TaskStatusChangeRequestResponse(
                decision.Value.ApprovalRequestId!.Value, project.Id, WorkApprovalRequestStatuses.Pending,
                callerEmployeeId.Value, requesterDisplayName, payload.Note, payload.Changes,
                DateTimeOffset.UtcNow, null, null, null, CanDecide: false, CanCancel: true));
        }, ct);
```

- [ ] **Step 8: Response mapper and list query read `wm_approval_requests`**

`TaskStatusChangeRequestResponses.cs`: replace `From(TaskStatusChangeRequest ...)` with:

```csharp
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public static TaskStatusChangeRequestResponse From(
        WorkApprovalRequest request, string requesterDisplayName, bool canDecide, bool canCancel)
    {
        var payload = JsonSerializer.Deserialize<TaskStatusTemplateChangePayload>(request.PayloadJson, PayloadOptions)!;
        return new(
            request.Id, request.ProjectId, request.Status, request.RequestedByEmployeeId, requesterDisplayName,
            payload.Note, payload.Changes, request.CreatedAt, request.DecidedByEmployeeId, request.DecisionComment,
            request.DecidedAt, canDecide, canCancel);
    }
```

(The `static readonly` field goes inside the record body; swap the `Tasks.Entities` using for `ONEVO.Domain.Features.WorkManagement.Approvals.Entities`.)

`GetProjectTaskStatusChangeRequestsQueryHandler`: swap `ITaskStatusChangeRequestRepository` for `IWorkApprovalRequestRepository` + `IWorkHierarchyService`, and replace everything from `var pending = ...` to the `visible` line with:

```csharp
        var pending = (await _requests.ListByProjectAsync(
                tenantId, project.Id, null, WorkApprovalRequestStatuses.Pending, ct))
            .Where(r => r.ActionType == WorkActionTypes.ProjectStatusTemplateChange)
            .ToList();
        var tree = await _hierarchy.LoadTreeAsync(tenantId, project.Id, ct);
        var visible = pending
            .Where(r => access.CanEditDirectly
                || r.RequestedByEmployeeId == callerEmployeeId.Value
                || WorkApprovalDecisionRules.CanDecide(tree, r, callerEmployeeId.Value))
            .ToList();
```

and compute each row's `canDecide` as `WorkApprovalDecisionRules.CanDecide(tree, r, callerEmployeeId.Value)` instead of `access.CanEditDirectly`. That also covers an HR-fallback approver.

- [ ] **Step 9: Applier**

`Tasks/Appliers/TaskStatusTemplateChangeApplier.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

/// <summary>Applies an approved project.status_template_change: the old ApproveTaskStatusChangeRequest body.</summary>
public sealed class TaskStatusTemplateChangeApplier : IApprovalActionApplier
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusChangeRequestConflictSweeper _sweeper;

    public TaskStatusTemplateChangeApplier(
        ICurrentUser currentUser, ITaskStatusRepository statuses, IWorkTaskRepository tasks,
        ITaskStatusChangeRequestConflictSweeper sweeper)
    {
        _currentUser = currentUser;
        _statuses = statuses;
        _tasks = tasks;
        _sweeper = sweeper;
    }

    public string ActionType => WorkActionTypes.ProjectStatusTemplateChange;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var payload = JsonSerializer.Deserialize<TaskStatusTemplateChangePayload>(context.PayloadJson, TaskPayload.Options);
        if (payload?.Changes is null)
            return ApplyOutcome.Invalid("The status change request has no changes.");

        // Tasks still in a status to delete block approval but don't make the request stale:
        // it stays pending so it can be approved once the tasks are moved, or rejected.
        foreach (var delete in payload.Changes.Deletes)
        {
            if (await _tasks.AnyActiveByStatusIdAsync(request.TenantId, delete.StatusId, ct))
                return ApplyOutcome.Invalid($"Move all tasks out of \"{delete.Name}\" before approving its deletion.");
        }

        var current = await _statuses.GetProjectTemplateAsync(request.TenantId, request.ProjectId, ct);
        var applied = TaskStatusChangeSetApplier.Apply(
            current, payload.Changes, request.TenantId, request.ProjectId, _currentUser.UserId, DateTimeOffset.UtcNow);

        if (applied.Outcome == TaskStatusChangeApplyOutcome.Invalid)
            return ApplyOutcome.Invalid(applied.Message!);
        if (applied.Outcome == TaskStatusChangeApplyOutcome.Stale)
            return ApplyOutcome.Stale;

        foreach (var status in applied.Added)
            await _statuses.AddAsync(status, ct);
        foreach (var status in applied.Modified)
            _statuses.Update(status);
        foreach (var status in applied.Deleted)
            _statuses.Remove(status);

        await _sweeper.MarkConflictingStaleAsync(
            request.TenantId, request.ProjectId, context.DeciderEmployeeId, payload.Changes.Footprint(), request.Id, ct);

        return ApplyOutcome.Applied;
    }
}
```

> Copy the exact member names from the deleted `ApproveTaskStatusChangeRequestCommandHandler` if any differ (for example the `delete.Name` field or `applied.FinalOrder`). That is a mechanical fix; note it as a deviation.

DI, WM block: `services.AddScoped<IApprovalActionApplier, TaskStatusTemplateChangeApplier>();`

Behaviour differences to accept (the decide flow is Plan 1's): a stale approve now returns 200 with status `stale` (was 409 + `outdated`); an invalid apply now returns 422 and the request stays pending (was 409).

- [ ] **Step 10: Navigation and history**

`GetWorkNotificationNavigationQueryHandler.FromStatusChangeRequestAsync`: keep the `"task_status_change_request"` arm (old bell notifications carry those ids, which the migration preserves), but read `_workApprovals.GetTrackedByIdForTenantAsync(tenantId, requestId, ct)` (the dependency Task 5 added) and delete the `ITaskStatusChangeRequestRepository` dependency. The returned value is unchanged.

`EfWorkApprovalHistoryRepository`: delete the `statusChanges` query and its `.Concat(statusChanges)`. Widen Task 5's `engineRequests` filter and mapping:

```csharp
    .Where(r => r.TenantId == tenantId && r.ProjectId == projectId
        && (r.ActionType.StartsWith("task.") || r.ActionType == WorkActionTypes.ProjectStatusTemplateChange)
        && (r.RequestedByEmployeeId == employeeId || r.DecidedByEmployeeId == employeeId
            || (r.Status == WorkApprovalRequestStatuses.Pending && r.ApproverEmployeeId == employeeId)))
```

```csharp
    r.ActionType switch
    {
        "task.create" => "task_creation", "task.edit" => "task_edit", "task.delete" => "task_delete",
        WorkActionTypes.ProjectStatusTemplateChange => "task_status_change",
        _ => r.ActionType
    },
    r.Status,
    r.ActionType == WorkActionTypes.ProjectStatusTemplateChange ? "Task statuses" : r.TargetTitle,
```

(`WorkActionTypes.ProjectStatusTemplateChange` is a `const`, so it is valid as a switch pattern.)

- [ ] **Step 11: Delete the old status-change request code**

Grep first; each item must be referenced only by the others in this list or by its own test:
- `Tasks/Commands/ApproveTaskStatusChangeRequest/`, `RejectTaskStatusChangeRequest/`, `CancelTaskStatusChangeRequest/` (whole folders)
- `Tasks/RepositoryInterfaces/ITaskStatusChangeRequestRepository.cs`
- `Domain/.../Tasks/Entities/TaskStatusChangeRequest.cs` (entity + `TaskStatusChangeRequestStatuses`)
- `Configurations/WorkManagement/TaskStatusChangeRequestConfiguration.cs`, `Repositories/WorkManagement/EfTaskStatusChangeRequestRepository.cs`, their DI lines, and the `TaskStatusChangeRequests` DbSet
- `TaskStatusChangeRequestsController`: the `approve`, `reject` and `cancel` actions and their `using`s (keep `List` and `Create`)
- `TaskStatusChangeRequestContracts.cs`: `RejectTaskStatusChangeRequestRequest` (keep `CreateTaskStatusChangeRequestRequest`)
- Keep the `work_task_status_change_request_*` templates in `NotificationTemplateSeeder` (unused now; Plan 4 removes them with the seeder test count).

Then `dotnet build -c Release`. Expected: 0 errors.

- [ ] **Step 12: Generate the migration and add the data copy**

```bash
dotnet ef migrations add MoveTaskStatusChangeRequestsToWorkApprovals --configuration Release --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj
```

Expected generated `Up`: only `DropTable("task_status_change_requests")`. Check the snapshot diff: only that removal.

Insert at the **start** of `Up`, before the generated drop:

```csharp
            // Copy every status-template request (all statuses, ids preserved so old bell
            // notifications still navigate) into wm_approval_requests. 'outdated' becomes 'stale'.
            // Position/approver = the project's root (default) module and its owner.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, r.project_id, 'project.status_template_change', 'project', NULL,
                       COALESCE(p.name, 'Task statuses'),
                       root.id, 'hierarchy',
                       COALESCE(r.decided_by_employee_id, root.owner_id, r.requested_by_employee_id),
                       r.requested_by_employee_id,
                       jsonb_build_object('Changes', r.changes_json, 'Note', r.note),
                       CASE WHEN r.status = 'outdated' THEN 'stale' ELSE r.status END,
                       r.decided_by_employee_id, r.decision_comment, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM task_status_change_requests r
                LEFT JOIN projects p ON p.id = r.project_id
                LEFT JOIN LATERAL (
                    SELECT o.id, o.owner_id FROM objectives o
                    WHERE o.project_id = r.project_id AND o.is_default AND o.parent_objective_id IS NULL
                    ORDER BY o.is_deleted, o.created_at
                    LIMIT 1
                ) root ON true;
            ");
```

At the top of `Down`: `// Data is not restored: rows live on in wm_approval_requests.`

The column list matches the Plan 1 migration (verified 2026-09-28: `is_deleted`/`deleted_at` exist). Still re-check the `task_status_change_requests` column names against its configuration and snapshot before committing.

- [ ] **Step 13: SQL script and full gate**

```bash
dotnet ef migrations script MoveTaskRequestsToWorkApprovals MoveTaskStatusChangeRequestsToWorkApprovals --configuration Release --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj -o <scratchpad>/move_status_change_requests.sql
dotnet build -c Release
dotnet test tests/ONEVO.Tests.Unit -c Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
```

Expected: the script shows the INSERT before the DROP, and all suites are green. **Do not run `database update`.**

- [ ] **Step 14: Commit, listing every deleted test and test case**

```bash
git add -A
git commit -m "refactor(work-management): task-status template requests go through the approval engine

Root module owner is now the only approver and the only direct editor of the status template;
root members send a change request. 'outdated' becomes 'stale'. Migrates all rows (ids preserved)
and drops task_status_change_requests.
Removed tests (tested deleted code): <list the old TaskStatusChangeRequestHandlerTests cases replaced>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## FRONTEND

**Before you start:** the frontend repo's current branch (`feature/wm-tree-highlight-filter`) has **uncommitted user work**.
- **Do not stash, reset or discard it.**
- Run `git status`. If it is dirty, stop and ask the user to commit or park it.
- Then run `git fetch` and `git switch -c feature/wm-hierarchy-approval-notification-engine origin/development`.
- Never junction `node_modules` into a temporary worktree.

### Task 7: DTOs and API services

**Files:**
- Create: `src/app/modules/work/models/dto/work-approval.dto.ts`
- Modify:
  - `data-access/task-api.service.ts`
  - `data-access/work-approvals-api.service.ts`
  - their `.spec.ts` files
- Delete: `models/dto/task-creation-request.dto.ts`, `models/dto/task-edit-request.dto.ts`

- [ ] **Step 1: Create the DTO file**

```ts
// models/dto/work-approval.dto.ts
export type WorkApprovalStatus = 'pending' | 'approved' | 'rejected' | 'cancelled' | 'stale';

export interface WorkApprovalRequestDto {
  id: string;
  projectId: string;
  actionType: string;          // 'task.create' | 'task.edit' | 'task.delete' | 'project.status_template_change' | ...
  targetType: 'task' | 'module' | 'sprint' | 'project';
  targetId: string | null;
  targetTitle: string;
  status: WorkApprovalStatus;
  requestedByEmployeeId: string;
  requestedByName: string;
  approverEmployeeId: string;
  approverName: string;
  payloadJson: string;
  decisionComment: string | null;
  createdAt: string;
  decidedAt: string | null;
}

/** Body of a 202 from create/edit/delete: the change was sent for approval instead of applied. */
export interface PendingApprovalDto {
  approvalRequestId: string;
}

export function isPendingApproval(value: unknown): value is PendingApprovalDto {
  return !!value && typeof value === 'object' && 'approvalRequestId' in value;
}
```

- [ ] **Step 2: Write the failing API spec cases first**

In `task-api.service.spec.ts`:
- delete the `createTaskEditRequest` and `getMyTaskEditRequests` cases;
- add `createTask returns PendingApprovalDto on 202`, flushing `{ approvalRequestId: 'r1' }` with `{ status: 202, statusText: 'Accepted' }`;
- add `deleteTask returns PendingApprovalDto on 202`.

In `work-approvals-api.service.spec.ts`:
- delete the `getMyTaskCreationRequests` case;
- add `getProjectApprovals GETs /work/projects/p1/approvals?scope=inbox`;
- add `approveWorkApproval POSTs editedPayloadJson + comment`;
- add `rejectWorkApproval` and `cancelWorkApproval` cases.

- [ ] **Step 3: Update the services**

`task-api.service.ts`:
- Import `PendingApprovalDto`.
- Change these signatures (the URLs are unchanged):

```ts
  createTask(objectiveId: string, request: CreateTaskRequestDto): Observable<WorkTaskDto | PendingApprovalDto> {
    return this.http.post<WorkTaskDto | PendingApprovalDto>(`${this.baseUrl}/objectives/${objectiveId}/tasks`, request);
  }
  deleteTask(taskId: string): Observable<PendingApprovalDto | null> {
    return this.http.delete<PendingApprovalDto | null>(`${this.baseUrl}/tasks/${taskId}`);
  }
  editTask(taskId: string, request: EditTaskRequestDto): Observable<WorkTaskDto | PendingApprovalDto> {
    return this.http.patch<WorkTaskDto | PendingApprovalDto>(`${this.baseUrl}/tasks/${taskId}`, request);
  }
```

- Delete `createTaskCreationRequest`, `createTaskEditRequest`, the `task-edit-requests/*` methods and `getMyTaskEditRequests`, together with their imports.

`work-approvals-api.service.ts`:
- Delete `getMyTaskCreationRequests`, `approveTaskCreationRequest`, `rejectTaskCreationRequest`, `getMyTaskEditRequests`, `approveTaskEditRequest`, `rejectTaskEditRequest`, and their imports.
- Add:

```ts
  getProjectApprovals(projectId: string, scope: 'inbox' | 'mine' = 'inbox'): Observable<WorkApprovalRequestDto[]> {
    return this.http.get<WorkApprovalRequestDto[]>(`${this.workBase}/projects/${projectId}/approvals`, { params: { scope } });
  }

  approveWorkApproval(id: string, editedPayloadJson?: string, comment?: string): Observable<WorkApprovalRequestDto> {
    return this.http.post<WorkApprovalRequestDto>(`${this.workBase}/approvals/${id}/approve`, { editedPayloadJson, comment });
  }

  rejectWorkApproval(id: string, comment?: string): Observable<WorkApprovalRequestDto> {
    return this.http.post<WorkApprovalRequestDto>(`${this.workBase}/approvals/${id}/reject`, { comment });
  }

  cancelWorkApproval(id: string): Observable<WorkApprovalRequestDto> {
    return this.http.post<WorkApprovalRequestDto>(`${this.workBase}/approvals/${id}/cancel`, {});
  }
```

- [ ] **Step 4: Run the tests**

Run: `npx ng test --watch=false --include='**/work/data-access/**'` (if the builder doesn't support `--include`, run the full suite).
Expected: the API specs are green. Callers are fixed in Task 8.

- [ ] **Step 5: Commit** (only after Task 8 compiles; Tasks 7 and 8 share one commit if `ng build` breaks in between).

---

### Task 8: Task modal and board: the backend decides and the UI reacts to 202

**Files:**
- Modify:
  - `ui/task-form-modal/task-form-modal.component.ts` (around lines 3292–3310 and 3355–3385 on `origin/development`)
  - `feature/task-board/task-board.component.ts` (around lines 441–470)
  - `state/*board*.store.ts`: whichever store's `createTask` calls `taskApi.createTask`; find it with `grep -rn "taskApi.createTask\|this.api.createTask" src/app/modules/work/state`
  - every other caller of `taskApi.deleteTask` / `editTask` / `createTask` (`grep -rn "\.deleteTask(\|\.editTask(\|\.createTask(" src/app/modules/work`)
- Test: the matching `.spec.ts` files

- [ ] **Step 1: Write the failing spec cases**

- Modal spec:
  - `edit by non-owner calls editTask (not an edit request) and shows the "sent for approval" info toast on 202`;
  - `create by non-owner calls createTask and shows "sent for approval" on 202`.
- Board spec: `quick-create by non-owner calls store.createTask; a PendingApprovalDto result shows the info toast and does not assign/move status`.

- [ ] **Step 2: Implement**

**Modal edit (`submitEdit` region):** replace

```ts
      if (this.canEditTaskDirectly()) { ...editTask... success('Task updated successfully.') } else { ...createTaskEditRequest... info(...) }
```

with

```ts
      const edited = await firstValueFrom(this.taskApi.editTask(taskId, {
        ...fields,
        attachmentFileIds: this.attachedFiles().map((f) => f.fileId)
      }));
      if (isPendingApproval(edited)) {
        this.notificationService.info('Task edit sent for approval.');
      } else {
        this.notificationService.success('Task updated successfully.');
      }
```

**Modal create:**
- Delete the whole `if (!this.selectedModuleIsOwner()) { ... return; }` block.
- After `const result = await this.boardStore.createTask(objectiveId, request);`, handle the pending case first:

```ts
      if (isPendingApproval(result)) {
        this.created.emit('');
        this.notificationService.info('Task sent for approval.');
        if (action === 'create_and_add_another') {
          this.title.set('');
          this.description.set('');
          this.descRte()?.clear();
          this.attachedFiles.set([]);
          this.draftSubtasks.set([]);
          this.cancelAddSubtask();
          this.successToastMessage.set('Sent for approval! You can enter another task below.');
          setTimeout(() => this.taskTitleInput()?.nativeElement?.focus());
        } else {
          this.closed.emit();
        }
        return;
      }
```

- Then the existing `'availableSlackHours' in result` handling continues unchanged.
- Remove `canEditTaskDirectly` and `selectedModuleIsOwner` **only if** nothing else in the component uses them (grep). If they drive UI hints (for example a "will need approval" label), keep them.

**Board store `createTask`:** widen its return type to include `PendingApprovalDto`, and return it untouched when `isPendingApproval(dto)`. Do not push it into the task list.

**Board quick-create:**
- Delete the `const isOwner = ...` line and the `if (!isOwner) { ... return; }` block.
- After `const result = await this.store.createTask(event.objectiveId, request);` add:

```ts
      if (isPendingApproval(result)) {
        this.notificationService.info('Task sent for approval.');
        event.resolve(true);
        this.reload();
        return;
      }
```

**Delete callers:** where a caller removes the task from local state after `deleteTask`, first check the result:

```ts
const res = await firstValueFrom(this.taskApi.deleteTask(id));
if (isPendingApproval(res)) { this.notificationService.info('Task deletion sent for approval.'); return; }
```

- [ ] **Step 3: Run the build and the full test suite**

```bash
npx ng build
npx ng test --watch=false
```

Expected: build OK, and all specs green. Fix specs that referenced the deleted DTOs or methods. **Delete spec cases only for deleted methods, and list them.**

- [ ] **Step 4: Commit (Tasks 7 and 8)**

```bash
git add -A src/app/modules/work
git commit -m "feat(work): task create/edit/delete always call the normal endpoint; 202 means sent for approval

Removed old task creation/edit request API methods and DTOs.
Removed specs (tested deleted methods): <list>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: The approvals page lists task requests from the unified inbox

**Files:**
- Modify:
  - `models/approval.model.ts`
  - `utils/approval.mapper.ts`
  - `state/work-approvals.store.ts`
  - `feature/work-approvals/work-approvals.component.ts`
  - the history-kind label helper (grep `historyKindLabel`)
- Test: `work-approvals.store.spec.ts`, `approval.mapper.spec.ts`, `work-approvals.component.spec.ts` (whichever exist)

- [ ] **Step 1: Write the failing spec cases**

- Mapper: `maps WorkApprovalRequestDto to kind 'work_approval' with title "<label>: <targetTitle>" and targetType/targetId from the dto`.
- Store:
  - `loadAll(projectId) also loads getProjectApprovals(projectId,'inbox') into workApprovals`;
  - `approve(work_approval item) calls approveWorkApproval and removes it`;
  - `reject(work_approval item, comment) calls rejectWorkApproval`.
- Component: `renders one 'Task Requests' section from workApprovals; no Task Creation/Task Edit sections`.

- [ ] **Step 2: Implement**

`approval.model.ts`:
- In `ApprovalKind`, replace `'task_creation' | 'task_edit'` with `'work_approval'`.
- In the `raw` union, replace `TaskCreationRequestDto | TaskEditRequestDto` with `WorkApprovalRequestDto`.

`approval.mapper.ts`:
- Remove the two task branches and their imports and overloads.
- Add an overload and a branch **first in the function**:

```ts
const WORK_ACTION_LABELS: Record<string, string> = {
  'task.create': 'New task',
  'task.edit': 'Edit task',
  'task.delete': 'Delete task',
};

  if ('actionType' in dto) {
    const request = dto as WorkApprovalRequestDto;
    return {
      id: request.id,
      kind: 'work_approval',
      objectiveId: '',                       // already project-scoped by the endpoint
      title: `${WORK_ACTION_LABELS[request.actionType] ?? 'Change'}: ${request.targetTitle}`,
      description: '',
      requestedByName: request.requestedByName,
      targetTitle: request.targetTitle,
      targetType: request.targetType === 'sprint' || request.targetType === 'project' ? 'request' : request.targetType,
      targetId: request.targetId,
      createdAt: request.createdAt,
      raw: request
    };
  }
```

`work-approvals.store.ts`:
- State: replace `taskCreationRequests` and `taskEditRequests` with `workApprovals: readonly ApprovalItem[]`, and update `pendingCount`.
- `loadAll(projectId?: string)`: drop the two old calls. When `projectId` is set, also load `firstValueFrom(api.getProjectApprovals(projectId, 'inbox'))` → `workApprovals: items.map(toApprovalItem)`; otherwise use `[]`.
- `approve`: replace the two task cases with:

```ts
          case 'work_approval':
            await firstValueFrom(api.approveWorkApproval(item.id));
            patchState(store, { workApprovals: store.workApprovals().filter((i) => i.id !== item.id) });
            break;
```

- `reject`: the same shape, with `api.rejectWorkApproval(item.id, comment)`.

`work-approvals.component.ts`:
- Replace the two template sections ("Task Creation Requests" and "Task Edit Requests") with one section:

```html
        @if (workApprovals().length > 0) {
        <section>
          <h2>Task Requests <span>{{ workApprovals().length }}</span></h2>
          @for (item of workApprovals(); track item.id) {
            <app-approval-request-row
              [item]="item"
              (approved)="onApprove(item, $event)"
              (rejected)="onReject(item, $event)"
              (viewRequested)="onViewTarget($event)"
            />
          }
        </section>
        }
```

- Class changes:
  - Replace the `taskCreationRequests`/`taskEditRequests` computeds with `protected readonly workApprovals = computed(() => this.store.workApprovals());`. **No module scoping**, because the endpoint is already per project.
  - In `visiblePendingCount`, replace the two terms with `+ this.workApprovals().length`.
  - Change every `this.store.loadAll()` to `this.store.loadAll(this.projectId())`, and the template's `store.loadAll()` to `store.loadAll(projectId())`.
  - Also call `void this.store.loadAll(projectId)` in the `paramMap` subscription after `this.projectId.set(projectId)`, **if** `loadModules()` doesn't already trigger `loadAll`. Check `loadModules()` before adding a second call.
- In `historyKindLabel` (and any status/kind map for the history modal), add `task_delete: 'Task deletion'`.

- [ ] **Step 3: Run the build and the full test suite**

```bash
npx ng build
npx ng test --watch=false
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(work): approvals page lists task requests from the unified approval inbox

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9A (amendment): Status-template panel decides through the generic approvals API

Backend Task 6A keeps `GET`/`POST projects/{id}/task-status-change-requests` (same shapes, and the list now returns `wm_approval_requests` ids). It removes the old `task-status-change-requests/{id}/approve|reject|cancel` routes.

**Files:**
- Modify:
  - `data-access/task-status-change-request-api.service.ts` (+ its `.spec.ts` if it exists)
  - `models/dto/task-status-change-request.dto.ts`
  - `state/work-approvals.store.ts` (Task 9's `workApprovals` load)
- Test: `task-status-change-requests-panel.component.spec.ts`, `work-approvals.store.spec.ts`

- [ ] **Step 1: Write the failing spec cases**

- API service (create the spec if it doesn't exist):
  - `approve POSTs /work/approvals/r1/approve`
  - `reject POSTs /work/approvals/r1/reject with { comment }`
  - `cancel POSTs /work/approvals/r1/cancel`
- Store: `loadAll(projectId) leaves project.status_template_change items out of workApprovals` (the status panel on the same page already lists them, so they would show twice).

- [ ] **Step 2: Implement**

`task-status-change-request-api.service.ts`: keep the method names so the panel and `board-structure-editor` need no change. Only the three URLs and the doc comment change:

```ts
/** Requests from project members to change the project's task statuses. Listing and creating use
 *  the status-template endpoints; deciding goes through the unified work approvals API, and only
 *  the project's top (root) module owner can decide. */
  approve(id: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/approvals/${id}/approve`, {});
  }

  reject(id: string, comment: string | null): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/approvals/${id}/reject`, { comment });
  }

  cancel(id: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/approvals/${id}/cancel`, {});
  }
```

(If a caller relied on the old `Observable<void>` type, keep `void`/`unknown` whichever compiles; the body is ignored.)

`task-status-change-request.dto.ts`:

```ts
export type TaskStatusChangeRequestStatus = 'pending' | 'approved' | 'rejected' | 'cancelled' | 'stale';
```

Grep `'outdated'` in `src/app/modules/work` and rename every use to `'stale'` (labels may keep the word "Outdated" for users).

`work-approvals.store.ts` (Task 9's load):

```ts
workApprovals: items
  .filter((i) => i.actionType !== 'project.status_template_change')
  .map(toApprovalItem)
```

- [ ] **Step 3: Run the build and the full test suite**

```bash
npx ng build
npx ng test --watch=false
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(work): status-template requests are decided through the unified approvals API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Final verification (both repos)

```bash
# backend
cd HRMS-Backend-v1
dotnet build -c Release
dotnet test tests/ONEVO.Tests.Unit -c Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
git grep -n -E "TaskCreationRequest|TaskEditRequest|TaskStatusChangeRequest\b|ITaskStatusChangeRequestRepository" -- src tests   # expect: only migrations + the Designer/snapshot history
# frontend
cd ../Hrms--Web-application---front-end---v1
npx ng build
npx ng test --watch=false
git grep -n -E "task-creation-requests|task-edit-requests|TaskCreationRequestDto|TaskEditRequestDto" -- src   # expect: none
git grep -n -E "task-status-change-requests/\\$\\{id\\}|'outdated'" -- src/app/modules/work   # expect: none (Task 9A)
```

Any test failure that also fails on `origin/development` predates this work. Prove it on a clean checkout and report it separately.

## Left for Plan 3 / Plan 4

- **Plan 3:**
  - Module actions (`ObjectiveChangeRequest` → engine; module appliers; stamp `CreateObjective`);
  - Sprint create/edit/delete (a new delete command; stamp `CreateSprint`; retire `SprintAccessService.CanManageAsync`);
  - navigation arms for `module`/`sprint` target types.
- **Plan 4:**
  - replace the duplicated ancestor walks with `ProjectModuleTree`;
  - dead-code and useless-test sweep, including the now-unused `work_task_creation_request_*`, `work_task_edit_request_decided` and `work_task_status_change_request_*` templates (bump `NotificationTemplateSeederTests`);
  - Approvals page Requests + **History** tabs (backed by `work-notifications`), replacing the legacy approval-history modal and `EfWorkApprovalHistoryRepository`.
