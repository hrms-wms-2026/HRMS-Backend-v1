# WM Hierarchy / Approval / Notification Engines — Plan 3: Module and Sprint Actions

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route every Module (Objective) action and every Sprint action through the engines, then retire `objective_change_requests` and `SprintAccessService`.

- **Module actions:** edit, delete, transfer head, achieve, unachieve, allocation extend.
- **Sprint actions:** create, edit, start, complete, achieve, plus a **new delete** command.
- **The rule for every action:**
  - An actor at or above the object's creator position acts directly, and the engine records a notification.
  - Anyone else creates an approval request for the current position holder.
- **Frontend:** reads every Module and Sprint request from the unified inbox and reacts to 202 on Sprint calls.

**Architecture:**
- **Mirrors Plan 2's task pattern.**
  - `IModuleWriteService` and `ISprintWriteService` hold each action's **validation and mutation**. Today that code is split between the direct handlers and `ApproveObjectiveChangeRequestCommandHandler`'s `switch`.
  - Each handler calls `IWorkApprovalEngine.SubmitAsync`, which returns Direct or Pending. Direct applies the change through the write service and calls `IWorkNotificationEngine`.
  - One `IApprovalActionApplier` per action type applies approved requests through the same write service.
- **The old creator bypass is removed.** The old `objective.CreatedById == userId` shortcut ("creator applies directly") is replaced by the position rule. The creator is normally the parent's head, and so already at or above the position.
- **The old "edit always needs the Reporting Manager" rule becomes** "the parent owner (or anyone above) edits directly; anyone below requests". The approver is the same person the old code notified.

**Tech Stack:**
- Backend: .NET 10, EF Core/Npgsql, MediatR, xUnit + Moq + FluentAssertions.
- Frontend: Angular signals + `@ngrx/signals`, `ng test`.

**Spec:** `docs/superpowers/specs/next/2026-09-28-wm-hierarchy-approval-notification-engine-design.md` (Parts 5 and 6; §4, §5.2–§5.5).

**Builds on:**
- Plan 1 (executed).
- **Plan 2 as amended in `a29a1f43`.** Plan 2 must be fully executed and green before this plan starts. This plan uses Plan 2's:
  - `WorkTargetTypes.Project`;
  - the `IWorkApprovalRequestRepository` dependency in `GetWorkNotificationNavigationQueryHandler`;
  - the `EfWorkApprovalHistoryRepository` changes;
  - the frontend `WorkApprovalRequestDto`, `PendingApprovalDto`, `isPendingApproval`, `WorkApprovalsApiService.getProjectApprovals/approveWorkApproval/rejectWorkApproval/cancelWorkApproval`;
  - the store's `workApprovals` state and the `work_approval` mapper branch.

## Global Constraints

- **WM only.** Never edit CoreHr, Leave, TimeAttendance, People, Calendar, or shared notification/outbox code. `IEmployeeAuthorityResolver` stays untouched.
- **Module rules (from the spec and the user's A/B/CC/X example):**
  - A Module's creator position = `CreatorPositionObjectiveId ?? ParentObjectiveId`, which the engine already falls back to.
  - **Stamp** `CreatorPositionObjectiveId = parent.Id` in `CreateObjective`.
  - The **Default (root) Module** keeps its carve-outs. Edit, achieve, delete and transfer of the root still go through the *Project* endpoints and are **not touched here**, so the HR fallback is not exercised by this plan.
- **Sprint rules (user decisions, 2026-09-28):**
  - **Creator position of a sprint:** the Module closest to the root that the creator **owns**. Failing that, the Module closest to the root they are an **active member** of. Failing that, the project root Module. Stamped at create time.
  - **Create, edit, start, complete, achieve** ("same as edit"): at or above the position → direct + notification; anyone else → an approval request to the position holder.
  - **Delete (new):**
    - Only a sprint whose status is **Complete or Achieved** may be deleted. Draft or Active → **409** "Only a completed or achieved sprint can be deleted."
    - The **sprint's creator** (`Sprint.CreatedById == current UserId`) **or** anyone at or above its creator position deletes directly. Anyone else → an approval request.
    - Tasks still pointing at the deleted sprint get `SprintId = null` (back to the backlog).
    - The sprint row is removed through `ISprintRepository.Remove`, which the soft-delete interceptor turns into a soft delete.
  - `SetSprintTasks` keeps its own per-task module-ownership rule (spec D2). It is **not** routed through the engine.
  - `SprintResponse.CanManage` now means "may act on this sprint, directly or by request" = the caller is an active project member. The engine decides direct vs request, so the UI shows the actions to every project member.
- **HTTP contract:**
  - Module endpoints keep their current shapes: 200/201 when applied, 202 when pending. The 202 body becomes `{ approvalRequestId }` (it was a change-request view model).
  - Transfer's body keeps `applied` and `pendingInvitation`, and replaces `pendingChangeRequest` with `approvalRequestId`.
  - Sprint endpoints return **200** with `SprintDto` when applied and **202** `{ approvalRequestId }` when pending. The new `DELETE sprints/{id}` returns 204 or 202.
- **Payload JSON:** serialise with `JsonSerializerDefaults.Web` (camelCase) and read case-insensitively. The migration copies old PascalCase and camelCase rows as they are.
- Engines never `SaveChangesAsync`; handlers save inside `IUnitOfWork.ExecuteInTransactionAsync`.
- **Migration data SQL:** starts with `SET LOCAL app.tenant_context_mode = 'admin';` in the same `Sql()` call. **Never run `database update`.**
- **Known gotchas:**
  - The API folder is `src/ONEVO.Api`, and controllers need `using ONEVO.Api.Filters;`.
  - `Employee` is in `ONEVO.Domain.Features.CoreHr.Entities`.
  - Build and test with `-c Release`.
  - `dotnet ef` needs `ConnectionStrings__MigrationConnection` (a dummy value works).
  - Never write the word "SQLite" in `src/`.
  - After `migrations add`, the snapshot diff must contain **only** this plan's changes.
- **Backend gate after every task:**
  - `dotnet build -c Release`
  - `dotnet test tests/ONEVO.Tests.Unit -c Release`
  - `dotnet test tests/ONEVO.Tests.Architecture -c Release`
- **Frontend gate:** `npx ng build` and `npx ng test --watch=false`.
- **Useless-test rule:** delete a test only if it tests deleted code, duplicates another test, or asserts nothing behavioural. **List every deleted test or spec file in its commit message.** Tests that pinned the removed creator bypass ("creator applies directly without ReportingManager") must be **rewritten** to the position rule, not deleted.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File map

### Backend (`HRMS-Backend-v1`)

| File | Change |
|---|---|
| `Domain/.../Approvals/Entities/WorkApprovalRequest.cs` | add `WorkActionTypes.SprintStart`, `SprintComplete`, `SprintAchieve` |
| `Application/.../Notifications/Services/WorkActionLabels.cs` | labels for the 3 new sprint actions |
| `Application/.../Objectives/Services/IModuleWriteService.cs` + `ModuleWriteService.cs` | **new**: validate + apply edit/delete/transfer/achieve/unachieve/allocation-extend |
| `Application/.../Objectives/DTOs/ModuleActionPayloads.cs` | **new**: `ModuleEditInput`, `ModuleTransferInput`, `ModuleAllocationExtendInput` |
| `Application/.../Objectives/Appliers/*.cs` | **new**: 6 appliers |
| `Application/.../Objectives/Commands/{Edit,Delete,Achieve,Unachieve,TransferObjectiveHead}/*Handler.cs` | route through the engine |
| `Application/.../Objectives/Commands/CreateObjective/*Handler.cs` | stamp the creator position |
| `Application/.../ObjectiveChangeRequests/Commands/RequestAllocationExtension/*Handler.cs` | route through the engine (the folder moves in Task 7) |
| `Application/.../Objectives/DTOs/Responses/{ObjectiveChangeOutcomeResponse,ObjectiveEditOutcomeResponse,TransferOutcomeResponse}.cs` | `PendingRequest` → `ApprovalRequestId` |
| `Application/.../Sprints/Services/ISprintWriteService.cs` + `SprintWriteService.cs` | **new**: validate + apply create/edit/start/complete/achieve/delete |
| `Application/.../Sprints/DTOs/SprintActionPayloads.cs` | **new** |
| `Application/.../Sprints/Appliers/*.cs` | **new**: 6 appliers |
| `Application/.../Sprints/Commands/*` | route through the engine; **new** `DeleteSprint/` |
| `Application/.../Sprints/Services/SprintAccessService.cs` + interface | **deleted** (replaced by the membership check) |
| `Application/.../Sprints/Queries/{GetProjectSprints,GetObjectiveSprints}/*Handler.cs` | `CanManage` = project member |
| `Application/.../Sprints/RepositoryInterfaces/ISprintRepository.cs` + EF impl | `void Remove(Sprint sprint)` |
| `Application/.../Tasks/Queries/GetWorkNotificationNavigation/*Handler.cs` | `module`/`sprint` arms; drop the `objective_change_request` arm |
| `Infrastructure/.../EfWorkApprovalHistoryRepository.cs` | module rows come from `wm_approval_requests` |
| `Infrastructure/.../Seeders/WorkManagementDapiDemoSeeder.Tasks.cs` | seed allocation extends as `WorkApprovalRequest` |
| `Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`, `SprintsController.cs` | 202 `{ approvalRequestId }`; drop the change-request endpoints; add `DELETE sprints/{id}` |
| `Infrastructure/Migrations/<ts>_MoveModuleRequestsToWorkApprovals.cs` | copy rows, drop `objective_change_requests` |

### Frontend (`src/app/modules/work`)

| File | Change |
|---|---|
| `utils/approval.mapper.ts` | engine `module.*` → kinds `objective_edit` / `allocation_extend` / `objective_change`; `sprint.*` → `work_approval` |
| `state/work-approvals.store.ts` | objective/allocation kinds approve/reject through the generic API (edited payload JSON) |
| `data-access/work-approvals-api.service.ts` | delete the `objectives/change-requests/*` methods; `createAllocationRequest` handles 202 |
| `feature/work-approvals/work-approvals.component.ts` | sections derive from `workApprovals` by kind |
| `data-access/sprint-api.service.ts`, `state/sprint-list.store.ts`, `state/project-detail.store.ts` | 202 handling; **new** `delete` |
| `ui/sprint-tree-row/*`, `ui/tree-explanation-panel/*`, `feature/milestone-tree-tab/*` | "Delete sprint" action for complete/achieved sprints |
| `models/dto/milestone.dto.ts`, `models/dto/sprint.dto.ts` | `TransferObjectiveHeadOutcomeDto.approvalRequestId` |

---

## BACKEND

### Task 1: Action types, labels and `ISprintRepository.Remove`

**Files:**
- Modify:
  - `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkActionLabels.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintRepository.cs`
  - `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs` (find it with `grep -rl "class EfSprintRepository" src`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Notifications/WorkNotificationEngineTests.cs` (label cases)

- [ ] **Step 1: Write the failing test**

Add to `WorkNotificationEngineTests`:

```csharp
[Theory]
[InlineData("sprint.start", "started the sprint")]
[InlineData("sprint.complete", "completed the sprint")]
[InlineData("sprint.achieve", "achieved the sprint")]
public void Labels_cover_sprint_lifecycle(string actionType, string label)
    => WorkActionLabels.For(actionType).Should().Be(label);
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~Labels_cover_sprint_lifecycle"`
Expected: FAIL (the fallback returns "changed").

- [ ] **Step 3: Implement**

In `WorkActionTypes`, after `SprintDelete`:

```csharp
    public const string SprintStart = "sprint.start";
    public const string SprintComplete = "sprint.complete";
    public const string SprintAchieve = "sprint.achieve";
```

In `WorkActionLabels.Labels`:

```csharp
        [WorkActionTypes.SprintStart] = "started the sprint",
        [WorkActionTypes.SprintComplete] = "completed the sprint",
        [WorkActionTypes.SprintAchieve] = "achieved the sprint",
```

In `ISprintRepository`: `void Remove(Sprint sprint);`
In `EfSprintRepository`: `public void Remove(Sprint sprint) => _db.Sprints.Remove(sprint);`

`Sprint : BaseEntity`, and `SoftDeleteInterceptor` turns `Remove` into `IsDeleted = true`. Confirm by reading `SoftDeleteInterceptor.cs`. If it does **not** cover `Sprint`, stop and ask; do not add a hard delete.

- [ ] **Step 4: Run the test and the gate, then commit**

```bash
git commit -am "feat(work-management): sprint lifecycle action types/labels and ISprintRepository.Remove

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `IModuleWriteService` (behaviour-preserving extraction)

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/ModuleActionPayloads.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/IModuleWriteService.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ModuleWriteService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleWriteServiceTests.cs`

**Interfaces (produced):**

```csharp
// Objectives/DTOs/ModuleActionPayloads.cs
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs;

/// <summary>payload_json of module.edit. Same fields as the old EditObjectiveRequestPayload.</summary>
public sealed record ModuleEditInput(string Title, string? Description, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours);
/// <summary>payload_json of module.transfer.</summary>
public sealed record ModuleTransferInput(Guid NewHeadEmployeeId);
/// <summary>payload_json of module.allocation_extend. The approver may lower RequestedAdditionalHours.</summary>
public sealed record ModuleAllocationExtendInput(decimal RequestedAdditionalHours, string Reason);

public static class ModulePayloadJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
}
```

```csharp
// Objectives/Services/IModuleWriteService.cs
/// <summary>
/// The single home of Module (Objective) action validation and mutation, shared by the direct
/// command handlers and the approval appliers. Permission checks stay in callers. Never saves.
/// Every Validate* is read-only; every Apply* re-validates first (an approval may come much later).
/// </summary>
public interface IModuleWriteService
{
    Task<Result> ValidateEditAsync(Guid tenantId, Objective module, ModuleEditInput input, CancellationToken ct = default);
    Task<Result> ApplyEditAsync(Guid tenantId, Objective trackedModule, ModuleEditInput input, CancellationToken ct = default);

    Task<Result> ValidateDeleteAsync(Guid tenantId, Objective module, CancellationToken ct = default);
    Task<Result> ApplyDeleteAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default);

    Task<Result> ValidateTransferAsync(Guid tenantId, Objective module, ModuleTransferInput input, CancellationToken ct = default);
    Task<Result> ApplyTransferAsync(Guid tenantId, Objective trackedModule, ModuleTransferInput input, CancellationToken ct = default);

    Task<Result> ValidateAchieveAsync(Guid tenantId, Objective module, CancellationToken ct = default);
    Task<Result> ApplyAchieveAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default);

    Task<Result> ValidateUnachieveAsync(Guid tenantId, Objective module, CancellationToken ct = default);
    Task<Result> ApplyUnachieveAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default);

    Task<Result> ValidateAllocationExtendAsync(Guid tenantId, Objective module, ModuleAllocationExtendInput input, CancellationToken ct = default);
    Task<Result> ApplyAllocationExtendAsync(Guid tenantId, Objective trackedModule, ModuleAllocationExtendInput input, CancellationToken ct = default);
}
```

**Where each body comes from.** Move the code; keep the error strings byte-identical.

| Method | Source |
|---|---|
| `ValidateEditAsync` | `EditObjectiveCommandHandler`: `IsActive`, `IsDefault`, `IsAchieved` and parent checks, **plus** the `ObjectiveParentConstraintChecker.Conflicts` check from the Approve handler's `Edit` case (409 "The edited date range or allocated hours would exceed the parent milestone's.") |
| `ApplyEditAsync` | the Approve handler `Edit` case's field assignments + `UpdatedAt`, then `_objectives.Update` |
| `ValidateDeleteAsync` | `DeleteObjectiveCommandHandler`: `IsDefault` → Failure; `!IsActive` → Conflict "Objective already deleted." |
| `ApplyDeleteAsync` | `IsActive = false; UpdatedAt = now; _objectives.Update` |
| `ValidateTransferAsync` | `TransferObjectiveHeadCommandHandler`: `IsActive`/`IsDefault`/`IsAchieved`, and `GetActiveAssigneeAsync(NewHeadEmployeeId)` null → Failure "The new head must be an active employee in this tenant." |
| `ApplyTransferAsync` | the creator-branch body of `TransferObjectiveHead`: owner swap, children's `ReportingManagerId`, membership upsert/deactivate/`HasOtherActiveAccess` |
| `ValidateAchieveAsync` | `AchieveObjectiveCommandHandler` checks: `IsActive`, `IsDefault`, already achieved, children achieved, active sprint |
| `ApplyAchieveAsync` | the Achieve creator-branch body |
| `ValidateUnachieveAsync` / `ApplyUnachieveAsync` | `UnachieveObjectiveCommandHandler` checks, plus the Approve `Unachieve` case (head must be active) |
| `ValidateAllocationExtendAsync` | `RequestAllocationExtensionCommandHandler`: `IsActive`; `ParentObjectiveId is null` → Failure "This milestone has no Reporting Manager to route to - it is a top-level milestone. Edit the Project directly instead."; `RequestedAdditionalHours <= 0` → Failure "Approved additional hours must be greater than zero." |
| `ApplyAllocationExtendAsync` | the Approve `ExtendAllocation` case: parent slack check (409 "You don't have enough allocation yourself to approve this. …"), then `AllocatedHours += input.RequestedAdditionalHours` |

Constructor dependencies: `IObjectiveRepository`, `ISprintRepository`, `IMilestoneMembershipCoordinator`, `IObjectiveAllocationSlackCalculator`.

- [ ] **Step 1: Write the failing tests** (`ModuleWriteServiceTests`, Moq). Each test asserts a status code or exact entity fields:

```csharp
[Fact] public async Task ValidateEdit_DefaultModule_Fails()
[Fact] public async Task ValidateEdit_ExceedsParent_Conflict()
[Fact] public async Task ApplyEdit_SetsFieldsAndUpdatedAt()
[Fact] public async Task ValidateDelete_AlreadyInactive_Conflict()
[Fact] public async Task ApplyTransfer_SwapsOwner_RepointsChildren_MovesMembership()
[Fact] public async Task ValidateTransfer_InactiveNewHead_Fails()
[Fact] public async Task ValidateAchieve_UnachievedChild_Fails()
[Fact] public async Task ValidateAchieve_TasksInActiveSprint_Fails()
[Fact] public async Task ApplyAchieve_SetsAchievedAndDeactivatesHeadMembership()
[Fact] public async Task ApplyUnachieve_InactiveHead_Fails()
[Fact] public async Task ApplyAllocationExtend_AboveParentSlack_Conflict()
[Fact] public async Task ApplyAllocationExtend_AddsHours()
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~ModuleWriteServiceTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement by moving the code as mapped above.** DI, WM block: `services.AddScoped<IModuleWriteService, ModuleWriteService>();`

- [ ] **Step 4: Run the tests and the gate, then commit**

```bash
git commit -am "refactor(work-management): extract module action validation/mutation into IModuleWriteService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Module handlers through the engine, plus creator-position stamping

**Files:**
- Modify:
  - `Objectives/Commands/{EditObjective,DeleteObjective,AchieveObjective,UnachieveObjective,TransferObjectiveHead,CreateObjective}/*Handler.cs`
  - `ObjectiveChangeRequests/Commands/RequestAllocationExtension/*Handler.cs`
  - `Objectives/DTOs/Responses/{ObjectiveChangeOutcomeResponse,ObjectiveEditOutcomeResponse,TransferOutcomeResponse}.cs`
  - `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (the `Edit`, `Delete`, `Transfer`, `Achieve`, `Unachieve` and `allocation-requests` actions)
  - `src/ONEVO.Api/Contracts/WorkManagement/Objectives/TransferOutcomeViewModel.cs` and any `ToViewModel` for the changed responses
- Test:
  - new `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleActionsThroughEngineTests.cs`
  - rewrite the existing `EditObjectiveCommandHandlerTests`, `DeleteObjectiveCommandHandlerTests`, `AchieveObjectiveCommandHandlerTests`, `UnachieveObjectiveCommandHandlerTests`, `TransferObjectiveHeadCommandHandlerTests`, `RequestAllocationExtensionCommandHandlerTests`
  - add a stamping case to `CreateObjectiveCommandHandlerTests`

**Response changes:**

```csharp
public sealed record ObjectiveChangeOutcomeResponse(bool Applied, Guid? ApprovalRequestId);
public sealed record ObjectiveEditOutcomeResponse(bool Applied, ObjectiveDetailResponse? Objective, Guid? ApprovalRequestId);
public sealed record TransferOutcomeResponse(bool Applied, Guid? ApprovalRequestId, ProjectMemberInvitationResponse? PendingInvitation);
```

`RequestAllocationExtensionCommand` now returns `Result<ObjectiveChangeOutcomeResponse>` (it used to return the change-request response).

**Handler flow.** All six follow it: auth → caller → load the Module exactly as today.

```csharp
if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
    return <Result>.Forbidden(<the handler's existing forbidden message>);

var input = /* per action, see table */;
var validation = await _modules.ValidateXxxAsync(tenantId, objective, /*input*/, ct);
if (!validation.IsSuccess)
    return <Result>.Failure(validation.Error!, validation.StatusCode ?? 400);

return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
{
    var decision = await _approvals.SubmitAsync(new WorkAction(
        tenantId, objective.ProjectId, callerEmployeeId.Value,
        WorkActionTypes.ModuleXxx, WorkTargetTypes.Module,
        TargetId: objective.Id, TargetTitle: objective.Title,
        TargetModuleId: objective.Id, PositionModuleId: objective.CreatorPositionObjectiveId,
        PayloadJson: input is null ? "{}" : JsonSerializer.Serialize(input, ModulePayloadJson.Options),
        TargetUpdatedAt: objective.UpdatedAt ?? objective.CreatedAt), innerCt);
    if (!decision.IsSuccess)
        return <Result>.Failure(decision.Error!, decision.StatusCode ?? 400);

    if (!decision.Value!.IsDirect)
    {
        await _unitOfWork.SaveChangesAsync(innerCt);
        return <Result>.Success(new XxxOutcome(Applied: false, ..., ApprovalRequestId: decision.Value.ApprovalRequestId));
    }

    var tracked = await _objectives.GetTrackedByIdForTenantAsync(tenantId, objective.Id, innerCt);
    var applied = await _modules.ApplyXxxAsync(tenantId, tracked!, /*input*/, innerCt);
    if (!applied.IsSuccess)
        return <Result>.Failure(applied.Error!, applied.StatusCode ?? 400);

    await _notifications.NotifyAsync(new WorkNotificationEvent(
        tenantId, objective.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
        WorkActionTypes.ModuleXxx, WorkTargetTypes.Module, objective.Id, objective.Title, null,
        recipients), innerCt);
    await _unitOfWork.SaveChangesAsync(innerCt);
    return <Result>.Success(new XxxOutcome(Applied: true, ...));
}, ct);
```

| Handler | Action | Input | Recipients (Direct) |
|---|---|---|---|
| Edit | `ModuleEdit` | `new ModuleEditInput(request.Title.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, request.AllocatedHours)` | `[objective.OwnerId, parentOwner]` |
| Delete | `ModuleDelete` | none (`"{}"`) | `[objective.OwnerId, parentOwner]` |
| Transfer | `ModuleTransfer` | `new ModuleTransferInput(request.NewHeadEmployeeId)` | `[oldOwner, request.NewHeadEmployeeId, parentOwner]` |
| Achieve | `ModuleAchieve` | none | `[objective.OwnerId, parentOwner]` |
| Unachieve | `ModuleUnachieve` | none | `[objective.OwnerId, parentOwner]` |
| AllocationExtend | `ModuleAllocationExtend` | `new ModuleAllocationExtendInput(request.RequestedAdditionalHours, request.Reason.Trim())` | `[objective.OwnerId, parentOwner]` |

- `parentOwner` = `(await _objectives.GetByIdForTenantAsync(tenantId, objective.ParentObjectiveId!.Value, ct))?.OwnerId`; skip it when null. The engine drops the actor and duplicates.
- **Edit:** the "applied" branch also reloads and returns `ObjectiveDetailResponse`, exactly as the previous direct path did (see git history for `EditObjective` before the approval-gate commit). If no mapper call exists in the file, use `ObjectiveMapper.ToDetailResponse(...)` or whatever `GetObjectiveByIdQueryHandler` uses; note the choice.
- **Transfer:** keep the existing `objective.ReportingManagerId is null` → **leader invitation** branch *before* the engine call, unchanged.
- **Delete / Achieve / Unachieve / Transfer:** remove the `objective.CreatedById == userId` direct branch and the `HasPendingForObjectiveAsync` pre-check. The engine's 409 for a duplicate pending request now covers it, per (target, action). Also remove the `ObjectiveChangeRequest` construction and the `IObjectiveChangeRequestRepository` and `INotificationDispatcher` dependencies.
- **Behaviour change (intended; pin it in tests):** a Module whose head is **not** the parent owner now requests Delete/Achieve/Transfer/Unachieve from the parent owner even if they created it. Before, the creator applied directly. The parent owner (or anyone above) now applies Edit **directly**. Before, even the parent owner filed a request routed to themselves.

**`CreateObjectiveCommandHandler`:** in `new Objective { ... }` (line ~102) add `CreatorPositionObjectiveId = parent.Id,`. Module creation itself is not routed through the engine; the spec only covers edits and lifecycle.

**Controller:**

```csharp
// Edit
return !result.IsSuccess ? Problem(result.Error, statusCode: result.StatusCode ?? 400)
    : result.Value!.Applied ? Ok(result.Value.Objective!.ToViewModel())   // keep whatever the applied branch returned before
    : StatusCode(202, new { approvalRequestId = result.Value.ApprovalRequestId });
// Delete / Achieve / Unachieve: applied → the previous applied response (NoContent/Ok); pending → the same 202 body.
// Transfer: applied or not → Ok / 202 with result.Value.ToViewModel(), where the view model now has approvalRequestId.
// allocation-requests: applied → Ok(); pending → 202 { approvalRequestId }.
```

- [ ] **Step 1: Write the failing tests.** `ModuleActionsThroughEngineTests` covers:

```csharp
[Fact] public async Task Edit_ByChildHead_GoesPending_NoFieldChange()
[Fact] public async Task Edit_ByParentOwner_AppliesDirect_NotifiesHeadAndParent()
[Fact] public async Task Edit_ValidationFails_EngineNeverCalled()
[Fact] public async Task Delete_ByCreatorWhoIsNotAtOrAbovePosition_NowGoesPending()      // bypass removed
[Fact] public async Task Transfer_NoReportingManager_StillCreatesLeaderInvitation_EngineNeverCalled()
[Fact] public async Task Transfer_ByParentOwner_Direct_SwapsOwner()
[Fact] public async Task Achieve_Pending_ReturnsApprovalRequestId()
[Fact] public async Task AllocationExtend_ByHead_GoesPendingToParent()
[Fact] public async Task EngineConflict409_PassesThrough()
[Fact] public async Task PositionModuleIdIsCreatorPosition_TargetModuleIsSelf()
```

Plus, in `CreateObjectiveCommandHandlerTests`: `Create_StampsCreatorPositionAsParent`.

- [ ] **Step 2: Run them to verify they fail**

Expected: FAIL.

- [ ] **Step 3: Implement the handler flow and the controller mapping above.**

- [ ] **Step 4: Rewrite the six existing handler test classes to the engine**

- Engine mock default: `Result<ApprovalDecision>.Success(ApprovalDecision.Direct)`.
- Tests that asserted a `ObjectiveChangeRequest` was added now assert `_approvals.Verify(x => x.SubmitAsync(It.Is<WorkAction>(...)))` with engine `Pending`.
- Tests that asserted the creator bypass now assert the position rule.
- Keep every validation-failure test; those now go through `IModuleWriteService` (build a real one from the same mocks).

- [ ] **Step 5: Run the gate, then commit**

```bash
git commit -am "feat(work-management): module edit/delete/transfer/achieve/unachieve/allocation-extend through the approval engine; stamp module creator position

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Module appliers

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Appliers/ModuleEditApplier.cs`, `ModuleDeleteApplier.cs`, `ModuleTransferApplier.cs`, `ModuleAchieveApplier.cs`, `ModuleUnachieveApplier.cs`, `ModuleAllocationExtendApplier.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ModuleAppliersTests.cs`

Every applier has the same shape. Here is the Edit one in full; the others differ only in the action type, the input type and the service method pair:

```csharp
using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

public sealed class ModuleEditApplier : IApprovalActionApplier
{
    private readonly IObjectiveRepository _objectives;
    private readonly IModuleWriteService _modules;

    public ModuleEditApplier(IObjectiveRepository objectives, IModuleWriteService modules)
    {
        _objectives = objectives;
        _modules = modules;
    }

    public string ActionType => WorkActionTypes.ModuleEdit;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var module = await _objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId!.Value, ct);
        if (module is null || !module.IsActive)
            return ApplyOutcome.Stale;
        if (request.TargetUpdatedAtSnapshot is { } snap && (module.UpdatedAt ?? module.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var input = JsonSerializer.Deserialize<ModuleEditInput>(context.PayloadJson, ModulePayloadJson.Options);
        if (input is null || string.IsNullOrWhiteSpace(input.Title))
            return ApplyOutcome.Invalid("The edit request has no title.");

        var result = await _modules.ApplyEditAsync(request.TenantId, module, input, ct);
        return result.IsSuccess ? ApplyOutcome.Applied : ApplyOutcome.Invalid(result.Error ?? "The edit could not be applied.");
    }
}
```

| Applier | Input | Service call | Extra stale rule |
|---|---|---|---|
| `ModuleDeleteApplier` | none | `ApplyDeleteAsync` | `!IsActive` → `Stale` |
| `ModuleTransferApplier` | `ModuleTransferInput` | `ApplyTransferAsync` | none |
| `ModuleAchieveApplier` | none | `ApplyAchieveAsync` | already achieved → `Stale` |
| `ModuleUnachieveApplier` | none | `ApplyUnachieveAsync` | not achieved → `Stale` |
| `ModuleAllocationExtendApplier` | `ModuleAllocationExtendInput`. The approver's edited payload may lower the hours (the frontend sends `{"requestedAdditionalHours":n,"reason":...}`) | `ApplyAllocationExtendAsync` | **no `UpdatedAt` stale check** (hours may legitimately change meanwhile) |

For every applier, a validation failure from the service returns `Invalid(error)`, which keeps the request pending.

DI, WM block: six `services.AddScoped<IApprovalActionApplier, ModuleXxxApplier>();` lines.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Edit_UsesApproverEditedPayload()
[Fact] public async Task Edit_ModuleChangedAfterRequest_Stale()
[Fact] public async Task Edit_ReadsLegacyPascalCasePayload()           // {"Title":..,"StartDate":..} copied by the migration
[Fact] public async Task Delete_AlreadyInactive_Stale()
[Fact] public async Task Transfer_ReadsLegacyPayload_NewHeadEmployeeId()
[Fact] public async Task Achieve_AlreadyAchieved_Stale()
[Fact] public async Task AllocationExtend_ApproverLowersHours_AppliesLowerAmount()
[Fact] public async Task AllocationExtend_OverParentSlack_Invalid_StaysPending()
```

- [ ] **Steps 2–4:** run them to verify they fail → implement → run the gate. Then commit:

```bash
git commit -am "feat(work-management): module approval appliers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `ISprintWriteService`, and Sprint handlers through the engine (including the new Delete)

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/SprintActionPayloads.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintWriteService.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintWriteService.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/DeleteSprint/DeleteSprintCommand.cs` + `DeleteSprintCommandHandler.cs`
  - `Sprints/DTOs/Responses/SprintWriteOutcome.cs`
- Modify:
  - `Sprints/Commands/{CreateSprint,EditSprint,StartSprint,CompleteSprint,AchieveSprint}/*` (command result types + handlers)
  - `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs`
  - DI
- Test:
  - new `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintWriteServiceTests.cs`, `SprintActionsThroughEngineTests.cs`, `DeleteSprintCommandHandlerTests.cs`
  - rewrite `Create/Edit/Start/Complete/AchieveSprintCommandHandlerTests` to the engine

**Interfaces (produced):**

```csharp
// Sprints/DTOs/SprintActionPayloads.cs
public sealed record SprintCreateInput(Guid ProjectId, string Name, string? Goal, IReadOnlyList<Guid> TaskIds);
public sealed record SprintEditInput(string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);
public sealed record SprintStartInput(DateOnly StartDate, DateOnly EndDate, string? Goal);
public sealed record SprintCompleteInput(string Disposition, Guid? TargetSprintId);
public static class SprintPayloadJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
}

// Sprints/DTOs/Responses/SprintWriteOutcome.cs
/// <summary>Exactly one of Sprint / ApprovalRequestId is set (both null = a delete that was applied).</summary>
public sealed record SprintWriteOutcome(SprintResponse? Sprint, Guid? ApprovalRequestId);
```

```csharp
// Sprints/Services/ISprintWriteService.cs - validation + mutation only; permission and saving stay in callers.
public interface ISprintWriteService
{
    Task<Result> ValidateCreateAsync(Guid tenantId, Guid actorEmployeeId, SprintCreateInput input, CancellationToken ct = default);
    Task<Result<Sprint>> CreateAsync(Guid tenantId, Guid creatorUserId, Guid actorEmployeeId, SprintCreateInput input,
        Guid creatorPositionObjectiveId, CancellationToken ct = default);
    Task<Result> ValidateEditAsync(Sprint sprint, SprintEditInput input);
    Task<Result> ApplyEditAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintEditInput input, CancellationToken ct = default);
    Task<Result> ValidateStartAsync(Guid tenantId, Sprint sprint, SprintStartInput input, CancellationToken ct = default);
    Task<Result> ApplyStartAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintStartInput input, CancellationToken ct = default);
    Task<Result> ValidateCompleteAsync(Guid tenantId, Sprint sprint, SprintCompleteInput input, CancellationToken ct = default);
    Task<Result> ApplyCompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintCompleteInput input, CancellationToken ct = default);
    Task<Result> ValidateAchieveAsync(Guid tenantId, Sprint sprint, CancellationToken ct = default);
    Task<Result> ApplyAchieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, CancellationToken ct = default);
    Result ValidateDelete(Sprint sprint);
    Task ApplyDeleteAsync(Guid tenantId, Sprint trackedSprint, CancellationToken ct = default);
}
```

**Bodies:** move each handler's status checks and its transaction body (without `SaveChangesAsync` and without building the response) into the matching pair. Keep the error strings byte-identical.

- **Create:**
  - Validate: project active and the `PrepareAsync` dry run (`ISprintTaskAssignmentService.PrepareAsync`), which returns its error as-is.
  - `CreateAsync`: the existing insert + `ApplyAsync` + activity logs, with `CreatedById = creatorUserId` and `CreatorPositionObjectiveId = creatorPositionObjectiveId`.
- **Delete (new):**
  - `ValidateDelete`: `Status is not (SprintStatuses.Complete or SprintStatuses.Achieved)` → `Result.Conflict("Only a completed or achieved sprint can be deleted.")`.
  - `ApplyDeleteAsync`:

```csharp
foreach (var task in await _tasks.GetBySprintIdAsync(tenantId, trackedSprint.Id, ct))
{
    var tracked = await _tasks.GetTrackedByIdForTenantAsync(tenantId, task.Id, ct);
    if (tracked is null) continue;
    tracked.SprintId = null;
    tracked.UpdatedAt = DateTimeOffset.UtcNow;
}
_sprints.Remove(trackedSprint);
```

**Creator position for sprints** (helper inside `CreateSprintCommandHandler`):

```csharp
var tree = await _hierarchy.LoadTreeAsync(tenantId, project.Id, ct);
var owned = tree.HighestOwnedModuleId(callerEmployeeId.Value);
Guid? memberOf = null;
if (owned is null)
{
    var memberModuleIds = await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
    memberOf = memberModuleIds
        .Where(id => tree.Get(id) is not null)
        .OrderBy(id => tree.AncestorChain(id).Count)
        .Select(id => (Guid?)id)
        .FirstOrDefault();
}
var position = owned ?? memberOf ?? tree.Root!.Id;
```

**Handler flow:** as in Task 3, with these values:
- `TargetType = WorkTargetTypes.Sprint`.
- `TargetModuleId = tree.Root!.Id`, loaded once per handler with `_hierarchy.LoadTreeAsync(tenantId, sprint.ProjectId, ct)`.
- `PositionModuleId`:
  - create: the computed `position`;
  - otherwise: `sprint.CreatorPositionObjectiveId ?? tree.Root.Id`.
- `TargetId`: create → null, otherwise `sprint.Id`.
- `TargetTitle`: sprint name.
- `TargetUpdatedAt`: `sprint.UpdatedAt ?? sprint.CreatedAt`, or null for create.
- **Permission gate before the engine** (replaces `_access.CanManageAsync`): `await _members.HasActiveMembershipAsync(tenantId, sprint.ProjectId, callerEmployeeId.Value, ct)` → else 403 "Only project members can change sprints."
- **Direct recipients:** the position holder (`tree.Get(position)?.OwnerId`) and the sprint creator's employee (`_identity.ResolveCallerEmployeeIdAsync(tenantId, sprint.CreatedById)`).
- **Delete only:** before calling the engine, `if (sprint.CreatedById == _currentUser.UserId)` → treat it as Direct (skip the engine).
- **Response:** applied → `SprintResponse.From(sprint, canManage: true)`; pending → `new SprintWriteOutcome(null, id)`.
- **All five existing commands return `Result<SprintWriteOutcome>`.**

**Controller (`SprintsController`):**

```csharp
private IActionResult ToResult(Result<SprintWriteOutcome> result, int appliedStatus = 200)
    => !result.IsSuccess ? Problem(result.Error, statusCode: result.StatusCode ?? 400)
     : result.Value!.ApprovalRequestId is { } id ? StatusCode(202, new { approvalRequestId = id })
     : result.Value.Sprint is { } s ? StatusCode(appliedStatus, s.ToViewModel())
     : NoContent();

[HttpDelete("sprints/{id:guid}")]
public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    => ToResult(await _mediator.Send(new DeleteSprintCommand(id), ct));
```

Create keeps its previous applied status code (check the current `Create` action: 201 or 200).

- [ ] **Step 1: Write the failing tests**
  - `SprintWriteServiceTests`: one validation-failure and one apply case per action, plus:
    - `ValidateDelete_DraftOrActive_Conflict`
    - `ApplyDelete_UnassignsTasks_RemovesSprint`
  - `SprintActionsThroughEngineTests`:

```csharp
[Fact] public async Task Create_OwnerOfModule_PositionIsHighestOwned_Direct()
[Fact] public async Task Create_PlainMember_PositionIsHighestMemberModule_Pending()
[Fact] public async Task Create_NoOwnershipNoMembership_PositionIsRoot()
[Fact] public async Task Edit_ByParentOfPosition_Direct_NotifiesHolderAndCreator()
[Fact] public async Task Edit_ByOtherMember_Pending()
[Fact] public async Task Start_Complete_Achieve_ByOtherMember_Pending_WithTheirPayload()
[Fact] public async Task NonProjectMember_Forbidden_EngineNeverCalled()
```

  - `DeleteSprintCommandHandlerTests`:

```csharp
[Fact] public async Task Creator_DeletesDirect_EvenIfNotAtOrAbovePosition()
[Fact] public async Task ParentOfCreatorPosition_DeletesDirect()
[Fact] public async Task OtherMember_Pending()
[Fact] public async Task ActiveSprint_Conflict409_EngineNeverCalled()
[Fact] public async Task DraftSprint_Conflict409()
```

- [ ] **Steps 2–5:** run them to verify they fail → implement (service, handlers, `DeleteSprint`, controller, DI `services.AddScoped<ISprintWriteService, SprintWriteService>();`) → rewrite the five existing sprint handler test classes as in Task 3 Step 4 → run the gate.

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(work-management): sprint create/edit/start/complete/achieve through the approval engine; add sprint delete (complete/achieved only)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Sprint appliers; retire `SprintAccessService`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Appliers/SprintCreateApplier.cs`, `SprintEditApplier.cs`, `SprintStartApplier.cs`, `SprintCompleteApplier.cs`, `SprintAchieveApplier.cs`, `SprintDeleteApplier.cs`
- Modify:
  - `Sprints/Queries/GetProjectSprints/*Handler.cs`, `GetObjectiveSprints/*Handler.cs`
  - `Sprints/Commands/SetSprintTasks/*Handler.cs` (its two `CanManageAsync` calls compute the response flag)
  - DI
- Delete: `Sprints/Services/ISprintAccessService.cs`, `Sprints/Services/SprintAccessService.cs`, and `tests/.../Sprints/SprintAccessServiceTests.cs` (it tests deleted code)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintAppliersTests.cs`, plus the two query handler test classes

**Appliers:** the same shape as `ModuleEditApplier`.
- Load the sprint tracked by `request.TargetId`. A missing sprint → `Stale`; a `TargetUpdatedAtSnapshot` mismatch → `Stale`.
- Deserialize with `SprintPayloadJson.Options` and call the matching `Apply*`. A validation failure → `Invalid(error)`.
- Every applier uses `request.RequestedByEmployeeId` as the actor for the activity logs.
- **`SprintCreateApplier` specifics:**
  - resolve the requester's `UserId` via `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync`; if the requester is inactive, return `Stale`;
  - use `request.PositionObjectiveId ?? root.Id` as the creator position;
  - set `request.TargetId = created.Value!.Id` on success.

**Queries:** replace `_access.GetManageableSprintIdsAsync(...)` with:

```csharp
var isMember = await _members.HasActiveMembershipAsync(tenantId, projectId, callerEmployeeId.Value, ct);
// then SprintResponse.From(s, canManage: isMember) for every sprint
```

In `SetSprintTasks`, both `canManage` computations become the same membership check.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Create_AppliesAsRequester_StoresCreatedSprintIdAsTarget()
[Fact] public async Task Edit_SprintChangedAfterRequest_Stale()
[Fact] public async Task Start_ValidationFails_Invalid()
[Fact] public async Task Complete_AppliesDisposition()
[Fact] public async Task Delete_ActiveNow_Invalid()        // status changed back is impossible, but ValidateDelete is re-run
[Fact] public async Task Delete_SprintGone_Stale()
```

Plus the query tests: `ProjectMember_SeesCanManageTrue_ForEverySprint` and `NonMember_SeesCanManageFalse`.

- [ ] **Steps 2–4:** run them to verify they fail → implement + delete the access service → run the gate. Then commit (list the deleted test file):

```bash
git commit -am "feat(work-management): sprint approval appliers; retire SprintAccessService

Removed tests (tested deleted code): tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintAccessServiceTests.cs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Navigation, history and seeder, then retire `objective_change_requests`

**Files:**
- Modify:
  - `GetWorkNotificationNavigationQueryHandler.cs`
  - `EfWorkApprovalHistoryRepository.cs`
  - `WorkManagementDapiDemoSeeder.Tasks.cs` (`SeedAllocationExtendsAsync`)
  - `ObjectivesController.cs` (delete `change-requests/{id}/approve`, `change-requests/{id}/reject`, `change-requests/mine`)
- Delete:
  - `ObjectiveChangeRequests/Commands/ApproveObjectiveChangeRequest/`, `RejectObjectiveChangeRequest/`, `Queries/ListMyObjectiveChangeRequests/`, `RepositoryInterfaces/IObjectiveChangeRequestRepository.cs`, `DTOs/Responses/ObjectiveChangeRequestResponse.cs`, `DTOs/EditObjectiveRequestPayload.cs`, `DTOs/TransferObjectiveRequestPayload.cs`, `DTOs/ExtendAllocationRequestPayload.cs`
  - **move** `RequestAllocationExtension/` into `Objectives/Commands/RequestAllocationExtension/`, and fix its namespace and every `using`
  - `ObjectiveMapper.ToResponse(ObjectiveChangeRequest)`
  - the Domain entity `ObjectiveChangeRequest.cs`
  - Infrastructure: `ObjectiveChangeRequestConfiguration.cs`, `EfObjectiveChangeRequestRepository.cs`, their DI lines and the DbSet
  - API contracts: `ApproveObjectiveChangeRequestRequest.cs`, `ObjectiveChangeRequestViewModel.cs` (and its mapper lines in `ObjectiveViewModelMapper.cs`)
  - Tests (they test deleted code): `ApproveObjectiveChangeRequestCommandHandlerTests.cs`, `ApproveObjectiveChangeRequestCommandHandlerIntegrationTests.cs`, `RejectObjectiveChangeRequestCommandHandlerTests.cs`, `ListMyObjectiveChangeRequestsQueryHandlerTests.cs`
- Test: `GetWorkNotificationNavigationQueryHandlerTests.cs`, `EfWorkApprovalHistoryRepositoryTests.cs`, `WorkManagementDapiDemoSeederTests.cs`

- [ ] **Step 1: Update the tests first (they fail)**

- **Navigation:**
  - delete the `objective_change_request` / `allocation_extend` cases;
  - add `Navigation_Module_OpensTreeOfThatModule` → `(projectId, module.Id, null, "tree")`;
  - add `Navigation_Sprint_OpensProjectTree` → `(projectId, root.Id, null, "tree")`;
  - add `Navigation_WorkApprovalRequest_ForModule_StillOpensApprovals` (the Plan 2 arm is unchanged).
- **History:** replace the `ObjectiveChangeRequests` seed rows with `WorkApprovalRequests` rows. Assert kinds:
  - `module.edit` → `objective_edit`
  - `module.allocation_extend` → `allocation_extend`
  - `module.transfer|delete|achieve|unachieve` → `objective_change`
  - `sprint.*` → `sprint_change`
- **Seeder test:** the pending-extend assertion becomes `WorkApprovalRequests.CountAsync(r => r.ProjectId == project.Id && r.ActionType == "module.allocation_extend" && r.Status == "pending")`, and the idempotency count changes the same way.

- [ ] **Step 2: Implement**

**Navigation handler:** remove `IObjectiveChangeRequestRepository` and `FromChangeRequestAsync`. Add the switch arms:

```csharp
"module" => await FromModuleAsync(tenantId, request.RelatedEntityId, ct),
"sprint" => await FromSprintAsync(tenantId, request.RelatedEntityId, ct),
```

```csharp
private async Task<Result<WorkNotificationNavigationResponse>> FromModuleAsync(Guid tenantId, Guid moduleId, CancellationToken ct)
{
    var module = await _objectives.GetByIdForTenantAsync(tenantId, moduleId, ct);
    return module is null
        ? Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.")
        : Result<WorkNotificationNavigationResponse>.Success(new(module.ProjectId, module.Id, null, "tree"));
}

private async Task<Result<WorkNotificationNavigationResponse>> FromSprintAsync(Guid tenantId, Guid sprintId, CancellationToken ct)
{
    var sprint = await _sprints.GetByIdForTenantAsync(tenantId, sprintId, ct);
    if (sprint is null)
        return Result<WorkNotificationNavigationResponse>.NotFound("Sprint not found.");
    var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, sprint.ProjectId, ct);
    return root is null
        ? Result<WorkNotificationNavigationResponse>.NotFound("Objective not found.")
        : Result<WorkNotificationNavigationResponse>.Success(new(sprint.ProjectId, root.Id, null, "tree"));
}
```

(Inject `ISprintRepository` if it is not injected already.)

**History repository:** replace the `objectiveChanges` query with the same pattern as Plan 2's `engineRequests`. Filter `r.ActionType.StartsWith("module.") || r.ActionType.StartsWith("sprint.")` and map the kinds:

```csharp
r.ActionType switch
{
    "module.edit" => "objective_edit",
    "module.allocation_extend" => "allocation_extend",
    var a when a.StartsWith("module.") => "objective_change",
    _ => "sprint_change",
}
```

`SubjectTitle = r.TargetTitle`, `ObjectiveId = r.TargetType == "module" ? r.TargetId!.Value : (r.PositionObjectiveId ?? Guid.Empty)`. The simplest approach is to fold these into Plan 2's single engine query: drop its `StartsWith("task.")` filter and extend its switch.

**Seeder:** replace `SeedAllocationExtendsAsync`'s `ObjectiveChangeRequest` with a `WorkApprovalRequest`. Keep the id and the idempotency check, now against `db.WorkApprovalRequests`.

```csharp
ActionType = WorkActionTypes.ModuleAllocationExtend, TargetType = WorkTargetTypes.Module, TargetId = objectiveId,
TargetTitle = objective.Title, PositionObjectiveId = objective.ParentObjectiveId, ApproverSource = WorkApprovalSources.Hierarchy,
ApproverEmployeeId = <the parent's OwnerId>, RequestedByEmployeeId = <requester>,
PayloadJson = JsonSerializer.Serialize(new ModuleAllocationExtendInput(spec.Hours, spec.Reason), ModulePayloadJson.Options),
Status = WorkApprovalRequestStatuses.Pending
```

(Match the old code's field names for hours and reason exactly.)

Then delete the files listed above and run:

```bash
dotnet build -c Release
```

Expected: 0 errors. Fix any leftover references by pointing them at the engine, never by restoring the old type.

- [ ] **Step 3: Generate the migration and insert the data copy at the START of `Up`**

```bash
dotnet ef migrations add MoveModuleRequestsToWorkApprovals --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj
```

The snapshot diff must show only `objective_change_requests` removed. If the Sprint model changed (it should not), stop and diagnose.

```csharp
            // Copy every module change request (all statuses) into wm_approval_requests, ids preserved.
            // Old rows put the authenticated UserId in decided_by_id, so the decider is taken from
            // reporting_manager_id (the only employee the old approve/reject commands let act).
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO wm_approval_requests (
                    id, tenant_id, project_id, action_type, target_type, target_id, target_title,
                    position_objective_id, approver_source, approver_employee_id, requested_by_employee_id,
                    payload_json, status, decided_by_employee_id, decision_comment, target_updated_at_snapshot,
                    decided_at, created_at, updated_at, created_by_id, is_deleted, deleted_at)
                SELECT r.id, r.tenant_id, o.project_id,
                       CASE r.request_type WHEN 'extend_allocation' THEN 'module.allocation_extend'
                                           ELSE 'module.' || r.request_type END,
                       'module', r.objective_id, o.title,
                       COALESCE(o.creator_position_objective_id, o.parent_objective_id), 'hierarchy',
                       r.reporting_manager_id, r.requested_by_id,
                       COALESCE(r.payload_json, '{}'::jsonb),
                       r.status,
                       CASE WHEN r.status = 'pending' THEN NULL ELSE r.reporting_manager_id END,
                       NULL, NULL,
                       r.decided_at, r.created_at, r.updated_at, r.created_by_id, r.is_deleted, r.deleted_at
                FROM objective_change_requests r
                JOIN objectives o ON o.id = r.objective_id;
            ");
```

At the top of `Down`, add the comment `// Data is not restored: rows live on in wm_approval_requests.`

- [ ] **Step 4: Produce the SQL script and run the full gate**

```bash
dotnet ef migrations script <Plan-2-last-migration> MoveModuleRequestsToWorkApprovals --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj -o <scratchpad>/move_module_requests.sql
dotnet build -c Release
dotnet test tests/ONEVO.Tests.Unit -c Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
git grep -n "ObjectiveChangeRequest" -- src tests   # expect: migrations/snapshot history only
```

- [ ] **Step 5: Commit (list every deleted test file)**

```bash
git add -A
git commit -m "refactor(work-management): retire objective_change_requests in favour of wm_approval_requests; module/sprint navigation arms

Removed tests (tested deleted code): <list>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## FRONTEND

Continue on the frontend branch Plan 2 created (`feature/wm-hierarchy-approval-notification-engine`). Check `git status` first. **Never stash or discard user work.**

### Task 8: Approvals: Module requests come from the unified inbox

**Files:**
- Modify:
  - `utils/approval.mapper.ts`
  - `state/work-approvals.store.ts`
  - `data-access/work-approvals-api.service.ts`
  - `feature/work-approvals/work-approvals.component.ts`
  - `models/approval.model.ts`
- Test: the matching specs

- [ ] **Step 1: Write the failing specs**

- Mapper:
  - `module.edit engine dto → kind objective_edit with raw.payload = parsed edit fields (camelCase and PascalCase)`;
  - `module.allocation_extend → kind allocation_extend with raw.payload.requestedAdditionalHours`;
  - `module.achieve → kind objective_change`;
  - `sprint.start → kind work_approval titled "Start sprint: <name>"`.
- Store:
  - `approve(objective_edit engine item, editPayload) calls approveWorkApproval(id, JSON.stringify(editPayload))`;
  - `approve(allocation_extend engine item, 5) sends {"requestedAdditionalHours":5,"reason":<original>}`;
  - `reject(objective_change engine item) calls rejectWorkApproval`.
- Component: `allocation and objective-change sections render from workApprovals by kind`.

- [ ] **Step 2: Implement**

`approval.model.ts`: add `engine?: boolean` to `ApprovalItem`, set to true for items that came from `getProjectApprovals`.

`approval.mapper.ts`: extend `WORK_ACTION_LABELS` with:
- `'module.edit': 'Edit module'`
- `'module.delete': 'Delete module'`
- `'module.transfer': 'Transfer module'`
- `'module.achieve': 'Achieve module'`
- `'module.unachieve': 'Reopen module'`
- `'module.allocation_extend': 'More hours'`
- `'sprint.create': 'New sprint'`
- `'sprint.edit': 'Edit sprint'`
- `'sprint.start': 'Start sprint'`
- `'sprint.complete': 'Complete sprint'`
- `'sprint.achieve': 'Achieve sprint'`
- `'sprint.delete': 'Delete sprint'`

Then, **inside** the existing `'actionType' in dto` branch and before its generic return, add:

```ts
    const payload = safeParse(request.payloadJson);
    if (request.actionType === 'module.edit') {
      const edit = {
        title: payload?.title ?? payload?.Title ?? request.targetTitle,
        description: payload?.description ?? payload?.Description ?? null,
        startDate: payload?.startDate ?? payload?.StartDate ?? '',
        endDate: payload?.endDate ?? payload?.EndDate ?? '',
        allocatedHours: payload?.allocatedHours ?? payload?.AllocatedHours ?? 0
      };
      return { id: request.id, kind: 'objective_edit', objectiveId: request.targetId ?? '', engine: true,
        title: `Edit: ${edit.title}`, description: edit.description ?? '', requestedByName: request.requestedByName,
        targetTitle: request.targetTitle, targetType: 'module', targetId: request.targetId,
        createdAt: request.createdAt, raw: { ...request, payload: edit } as never };
    }
    if (request.actionType === 'module.allocation_extend') {
      const hours = payload?.requestedAdditionalHours ?? payload?.RequestedAdditionalHours ?? 0;
      const reason = payload?.reason ?? payload?.Reason ?? '';
      return { id: request.id, kind: 'allocation_extend', objectiveId: request.targetId ?? '', engine: true,
        title: `+${hours}h requested`, description: reason, requestedByName: request.requestedByName,
        targetTitle: request.targetTitle, targetType: 'module', targetId: request.targetId,
        createdAt: request.createdAt, raw: { ...request, payload: { requestedAdditionalHours: hours, reason } } as never };
    }
    if (request.actionType.startsWith('module.')) {
      return { id: request.id, kind: 'objective_change', objectiveId: request.targetId ?? '', engine: true,
        title: `${WORK_ACTION_LABELS[request.actionType]}: ${request.targetTitle}`, description: '',
        requestedByName: request.requestedByName, targetTitle: request.targetTitle, targetType: 'module',
        targetId: request.targetId, createdAt: request.createdAt, raw: request };
    }
```

```ts
function safeParse(json: string | null | undefined): Record<string, any> | null {
  if (!json) return null;
  try { return JSON.parse(json) as Record<string, any>; } catch { return null; }
}
```

The generic `work_approval` return also sets `engine: true`. Delete the old `ObjectiveChangeRequestDto`/`AllocationRequestDto` branches **only if** nothing else maps those DTOs (grep). `ObjectiveInvitationDto` stays.

`work-approvals.store.ts`:
- Drop the `getMyAllocationRequests` and `getMyObjectiveChangeRequests` loads.
- `allocationRequests` and `objectiveChangeRequests` become **computed** from `workApprovals` by kind (or keep them as state filled from the same array; pick the smaller diff).
- In `approve`, before the `switch`:

```ts
        if (item.engine) {
          let edited: string | undefined;
          if (item.kind === 'allocation_extend' && typeof override === 'number') {
            const reason = (item.raw as { payload?: { reason?: string } }).payload?.reason ?? '';
            edited = JSON.stringify({ requestedAdditionalHours: override, reason });
          } else if (item.kind === 'objective_edit' && override && typeof override === 'object') {
            edited = JSON.stringify(override);
          }
          await firstValueFrom(api.approveWorkApproval(item.id, edited));
          patchState(store, { workApprovals: store.workApprovals().filter((i) => i.id !== item.id) });
          return true;
        }
```

- `reject`: the same `if (item.engine)` short-circuit, with `rejectWorkApproval(item.id, comment)`.

`work-approvals-api.service.ts`:
- Delete `getMyObjectiveChangeRequests`, `getMyAllocationRequests`, `approveObjectiveChangeRequest`, `rejectObjectiveChangeRequest`, `approveAllocationRequest`, `rejectAllocationRequest`, `toAllocationRequest`.
- `createAllocationRequest` keeps its URL and returns `Observable<PendingApprovalDto | null>`.

`work-approvals.component.ts`:
- `allocationRequests` / `objectiveChangeRequests` read from the store as before. They are already project-scoped; **remove** `scopedToProject` for them if their `objectiveId` is a module id of the project, and keep it otherwise.
- The **Task Requests** section should list only `kind === 'work_approval'`.

- [ ] **Step 3: Run the build and all tests, then commit**

```bash
npx ng build
npx ng test --watch=false
git commit -am "feat(work): module requests (edit/delete/transfer/achieve/unachieve/allocation) decided through the unified approvals inbox

Removed specs (tested deleted methods): <list>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Sprints: 202 handling, Delete action, transfer DTO

**Files:**
- Modify:
  - `data-access/sprint-api.service.ts`
  - `state/sprint-list.store.ts`
  - `state/project-detail.store.ts` (`achieveSprint`, `completeSprint`, and the transfer at line ~255)
  - `models/dto/milestone.dto.ts` (`TransferObjectiveHeadOutcomeDto`)
  - `ui/sprint-tree-row/sprint-tree-row.component.ts`
  - `ui/tree-explanation-panel/tree-explanation-panel.component.ts`
  - `feature/milestone-tree-tab/milestone-tree-tab.component.ts`
  - any other component that calls `sprintListStore.*` (grep `inject(SprintListStore)`)
- Test: the matching specs

- [ ] **Step 1: Write the failing specs**

- `SprintApiService`:
  - `delete DELETEs /work/sprints/sp-1`;
  - `edit returns PendingApprovalDto on 202`.
- `SprintListStore`:
  - `edit pending sets approvalNotice and still reloads`;
  - `remove(sprint) calls api.delete and reloads`.
- `ProjectDetailStore`: `achieveSprint pending returns {success:true, pending:true} and leaves the tree node unchanged`.
- Tree row / panel: `shows "Delete sprint" only for complete or achieved sprints and emits deleteSprintRequested`.

- [ ] **Step 2: Implement**

`sprint-api.service.ts`: every mutating method's type becomes `Observable<SprintDto | PendingApprovalDto>`, and add:

```ts
  delete(sprintId: string): Observable<PendingApprovalDto | null> {
    return this.http.delete<PendingApprovalDto | null>(`${this.baseUrl}/sprints/${sprintId}`);
  }
```

`sprint-list.store.ts`:
- Add `approvalNotice: string | null` to the state (initially `null`).
- In `create`, `start`, `edit`, `complete` and `achieve`, capture the result: `const res = await firstValueFrom(api.xxx(...));`, then `patchState(store, { approvalNotice: isPendingApproval(res) ? 'Sent for approval.' : null });` before the reload.
- Add `remove(sprintId, projectId)` with the same shape, calling `api.delete`.
- Every component that already shows a toast after these calls reads `store.approvalNotice()` and shows `info(...)` when it is set. Grep the callers and add one line each.

`project-detail.store.ts`:
- `achieveSprint` / `completeSprint` return `Promise<{ success: boolean; pending?: boolean }>`. When `isPendingApproval(dto)`, return `{ success: true, pending: true }` **without** patching the tree node.
- Transfer (line ~255): keep `applied` and `viaInvitation`. The DTO's `pendingChangeRequest` field is replaced by `approvalRequestId?: string | null`.

Delete action UI:
- `sprint-tree-row` / `tree-explanation-panel`: add `deleteSprintRequested = output<MilestoneTreeNode>()`, and a "Delete sprint" button or menu item shown only when `node.status === 'complete' || node.status === 'achieved'`. Copy the markup and classes of the existing "Achieve sprint" item and use a trash icon.
- `milestone-tree-tab`: handle `(deleteSprintRequested)`, call `await this.store.deleteSprint(node.id)` (add it to `project-detail.store.ts` by calling `sprintApi.delete`), show `info('Sprint deletion sent for approval.')` when pending, otherwise remove the node / reload the subtree the same way `achieveSprint` refreshes it.

- [ ] **Step 3: Run the build and all tests, then commit**

```bash
npx ng build
npx ng test --watch=false
git commit -am "feat(work): sprint actions show 'sent for approval' on 202; delete completed/achieved sprints

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
git grep -n -E "ObjectiveChangeRequest|SprintAccessService|ISprintAccessService" -- src tests   # expect: migrations only
# frontend
cd ../Hrms--Web-application---front-end---v1
npx ng build
npx ng test --watch=false
git grep -n -E "change-requests/|getMyObjectiveChangeRequests|getMyAllocationRequests" -- src   # expect: none
```

Any failure that also fails on `origin/development` predates this work. Prove it on a clean checkout and report it separately.

## Left for Plan 4

- Replace the duplicated `ParentObjectiveId` ancestor walks in query handlers with `ProjectModuleTree`.
- Dead-code and useless-test sweep across WM.
- The Approvals page **Requests + History** tabs (History from `GET projects/{id}/work-notifications`), replacing the legacy approval-history modal and `EfWorkApprovalHistoryRepository`.
