# Work Management: Hierarchy, Approval and Notification Engines — Design

- **Date:** 2026-09-28
- **Scope:** Work Management (WM) module only, in `HRMS-Backend-v1` and `Hrms--Web-application---front-end---v1`. No file outside WM is edited.
- **Branch (both repos):** `feature/wm-hierarchy-approval-notification-engine`, cut from `development`.
- **Delivery:** staged (7 parts). Every part builds and keeps the full backend + frontend suites green.

## 1. Problem

WM hierarchy and approval rules are spread across the codebase:

- Four separate request tables, each with its own Create/Approve/Reject/Cancel commands (~20 handlers): `TaskCreationRequest`, `TaskEditRequest`, `TaskStatusChangeRequest`, `ObjectiveChangeRequest`.
- Approver logic is duplicated: `objective.OwnerId` checks inside `EfWorkApprovalHistoryRepository`, `ReportingManagerId` on objective changes, `TaskStatusChangeAccessService.ListApproverEmployeeIdsAsync`, `SprintAccessService.CanManageAsync`.
- The same `while (cursor.ParentObjectiveId …)` ancestor walk is copied into ~13 query handlers.
- Sprint create/edit has no approval, task delete has no request flow, and there is no sprint delete command.
- Notifications only fire for 4 membership events. Edits never notify anyone.
- Approvals always go to the *person* who owns something today, never to the *position* in the project tree, so a Module transfer breaks the approval chain.

## 2. Goal

One place decides "who must approve this action" (the **Approval engine**), and one place decides "who must be told about this action" (the **Notification engine**). Both are built on one **Hierarchy service** that understands the project's Module tree. Every WM action is wired through them, and the old scattered code is deleted.

## 3. Reference: HR `EmployeeAuthorityResolver`

`ONEVO.Application.Features.CoreHr.EmployeeAuthority` (owned by the HR module) resolves approvers from the **org chart**: position coverage, then department coverage, then reporting line. It fails closed and has a batched inbox-scope method.

WM's hierarchy is the **project Module tree**, not the org chart, so this design copies the resolver's *pattern*:
- one interface;
- a fixed priority order;
- fails closed;
- tenant taken from `ICurrentUser`;
- batched lookups for inboxes.

WM calls `IEmployeeAuthorityResolver.ResolveApproverAsync` **read-only** in exactly one case: the root-Module fallback (§5.4). The CoreHr code is not modified.

## 4. Core concept: the creator position

The approver of a change is **whoever currently holds the position the object was created under**. It is not the person who created the object.

**Worked example**
1. A owns parent Module P.
2. B creates child Module C under P and assigns CC as C's owner. C's creator position = "owner of P".
3. P is transferred from A to X.
4. B or CC edits C. The creator position is "owner of P", which is X today. **X gets the approval request.** A does not.
5. If the editor already holds that position, or holds any position above it in the tree, the edit is applied directly and the Notification engine records it.

**Storage.** Add a new nullable column, `CreatorPositionObjectiveId` (`Guid?`), to `Objective`, `Sprint` and `WorkTask`. Its value is the Module whose **current owner** is the creator position.

| Object | Value set at creation | Backfill for existing rows |
|---|---|---|
| Module (`Objective`) | `ParentObjectiveId` (null for a root Module) | `ParentObjectiveId` |
| Task (`WorkTask`) | The highest Module in the task's ancestor chain owned by the acting employee. If the actor owns none (a member request that was approved), the task's own `ObjectiveId`. | Same rule, using the creator's employee from `CreatedById`. Falls back to `ObjectiveId`. |
| Sprint | The acting employee's highest owned Module in the project | Same rule from `CreatedById`. Falls back to the project's root Module. |

"Highest" means the one closest to the root.

## 5. Components (backend, `Features/WorkManagement/Common/Hierarchy|Approvals|Notifications`)

### 5.1 `IWorkHierarchyService`

- `GetAncestorChainAsync(tenantId, moduleId)`: returns self → root, loaded in one query per project and walked in memory.
- `GetCurrentHolderAsync(tenantId, positionModuleId)`: returns the Module's current `OwnerId`, if that employee is active.
- `IsAtOrAboveAsync(tenantId, employeeId, positionModuleId)`: true if the employee owns the position Module or any of its ancestors.
- `GetHighestOwnedModuleAsync(tenantId, employeeId, moduleId | projectId)`: used to set the creator position at creation time.

Built on the existing `IMilestoneMembershipCoordinator.IsEffectiveOwnerAsync` semantics. It replaces the duplicated ancestor-walk loops.

### 5.2 `IWorkApprovalEngine`

```csharp
Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct);
// ApprovalDecision = Direct | Pending(approvalRequestId)
```

`WorkAction` fields:
- `ActionType`
- `TargetType` (Module, Sprint or Task)
- `TargetId` (null for create)
- `PositionModuleId`
- `ProjectId`
- `ActorEmployeeId`
- `PayloadJson`

**Rules, in order**

1. **Position:**
   - edit, delete or status change: the target's `CreatorPositionObjectiveId`;
   - create: the target Module (for a Sprint, the actor's highest owned Module).
2. **Direct:** if `IsAtOrAbove(actor, position)` is true, return `Direct`. The caller applies the change and calls the Notification engine.
3. **Otherwise, find the approver:**
   - the current holder of the position;
   - if that holder is inactive or missing, the next active ancestor owner;
   - if the position is a root Module (§5.4), the HR fallback.
4. **Pending:** insert a `wm_approval_requests` row with status `pending` and notify the approver.
5. **No approver found:** return `Result.UnprocessableEntity("No eligible approver was found for this action.")`. The engine fails closed.

This reproduces the existing rules:
- Module members' task and sprint create/edit/delete go to the Module owner as requests.
- Parents act directly and the change is notified.
- A Module edit by its own owner goes to the parent Module owner (today's "Reporting Manager").

### 5.3 Deciding a request

One command per decision: `ApproveWorkApprovalRequest`, `RejectWorkApprovalRequest` and `CancelWorkApprovalRequest`.

- **Approver at decision time:** the approver is resolved again from the position when the request is decided. After a transfer, only the new holder (or anyone above them) can decide it.
- **Cancel:** only the requester can cancel.
- **Approve:** the approver may send an edited payload. This keeps the existing Module-edit "edit before approve" behaviour.
- **Applying the change:** it is done through `IApprovalActionApplier`, one implementation per `ActionType`, registered in DI and resolved by key. The apply logic moves out of the old Approve handlers into these appliers.
- **Stale requests:** if the target was deleted, or changed since the request was made (compared by `UpdatedAt`), the request becomes `stale` and nothing is applied.
- **Duplicates:** only one pending request is allowed per (target, action type). A second one gets a 409.

### 5.4 Root-Module fallback

A root Module has no parent owner. Its approver is the actor's HR approver, found with:

```csharp
IEmployeeAuthorityResolver.ResolveApproverAsync(
    SubjectEmployeeId = actor,
    LegalEntityId     = project's legal entity,
    RequiredPermission = existing WM approve permission,
    Purpose            = closest existing EmployeeAuthorityPurpose value)
```

No new enum value is added, because that would be a CoreHr change.

### 5.5 `IWorkNotificationEngine`

```csharp
Task RecordAsync(WorkNotificationEvent e, CancellationToken ct);
```

It is called for:
- every `Direct` action;
- every approval lifecycle step (requested, approved, rejected, cancelled, stale).

**Recipients**, deduplicated, and the actor is always excluded:

| Event | Recipients |
|---|---|
| Direct edit or delete | Creator-position holder, plus the object's owner (Module) or assignees (Task) |
| Direct create | Target Module owner |
| Requested | Approver |
| Decided | Requester |

**Output**
- One `wm_notification_log` row per recipient.
- One `OutboxMessageTypes.WorkNotification` message per recipient, in the **same transaction** as the change. The existing `WorkNotificationOutboxHandler` pushes it through the shared `INotificationDispatcher` (in-app).
- New template codes (`work_activity_recorded`, `work_approval_requested`, `work_approval_decided`) are added to the existing WM block of `NotificationTemplateSeeder`, where every other `work_*` template already lives.

The shared dispatcher, bell and templating code are not modified.

## 6. Data model (new WM tables, tenant-owned, RLS coverage migration included)

**`wm_approval_requests`**

| Column | Type / values |
|---|---|
| id | |
| tenant_id | |
| project_id | |
| action_type | text |
| target_type | text |
| target_id | nullable |
| position_objective_id | nullable (null only for HR approvals) |
| approver_source | `hierarchy` or `hr`. Hierarchy approvals follow the position, so anyone at or above it may decide. HR approvals are fixed to the resolved HR approver. |
| target_title | |
| approver_employee_id | |
| requested_by_employee_id | |
| payload_json | jsonb |
| status | `pending`, `approved`, `rejected`, `cancelled`, `stale` |
| decided_by_employee_id | |
| decision_comment | |
| target_updated_at_snapshot | |
| created_at | |
| decided_at | |

Indexes:
- (tenant, project, status);
- (tenant, approver, status);
- a unique partial index on (tenant, target_type, target_id, action_type) where status = `pending`.

**`wm_notification_log`**

| Column | Notes |
|---|---|
| id | |
| tenant_id | |
| project_id | |
| recipient_employee_id | |
| actor_employee_id | |
| action_type | |
| target_type | |
| target_id | |
| target_title | |
| approval_request_id | nullable |
| kind | `direct`, `requested`, `approved`, `rejected`, `cancelled`, `stale` |
| created_at | |

Index: (tenant, project, recipient, created_at desc).

**Columns added:** `CreatorPositionObjectiveId` on `objectives`, `sprints` and `work_tasks`.

**Tables dropped** (after pending rows are migrated into `wm_approval_requests`): `task_creation_requests`, `task_edit_requests`, `task_status_change_requests`, `objective_change_requests`.

**Action types:**
- `task.create`, `task.edit`, `task.delete`, `task.status_change`
- `module.edit`, `module.delete`, `module.transfer`, `module.achieve`, `module.unachieve`, `module.allocation_extend`
- `sprint.create`, `sprint.edit`, `sprint.delete`

## 7. API (WM controllers only)

- `GET /api/v1/work/projects/{projectId}/approvals?scope=inbox|mine&status=`
- `POST /api/v1/work/approvals/{id}/approve` (optional edited payload), `/reject`, `/cancel`
- `GET /api/v1/work/projects/{projectId}/work-notifications?page=`

Existing create/edit/delete endpoints keep their routes. Their response adds `{ applied: bool, approvalRequestId?: Guid }`, so the UI can show "sent for approval".

The old per-type request endpoints are removed in the part that migrates each type.

## 8. Frontend (`src/app/modules/work` only)

- One `work-approvals-api.service` and store for the generic endpoints.
- The Approvals page has two tabs:
  - **Requests:** inbox and mine, with Approve / Reject / Cancel. It keeps the Module-edit "edit before approve" form.
  - **History:** the project's `work-notifications` for the current user.
- Create, edit and delete flows show "Sent for approval" when `applied=false`.
- **Deleted:** `task-status-change-request-api.service`, the task-creation/task-edit/status-change/allocation request DTOs, `task-status-change-requests-panel`, `approval-history-modal`, and the per-type mapper branches, plus their specs.
- The shared bell and the other modules' approval pages are not touched.

## 9. Staged parts

| Part | Backend | Frontend |
|---|---|---|
| 1 | `CreatorPositionObjectiveId` with its backfill migration; `IWorkHierarchyService`; replace the duplicated ancestor walks | none |
| 2 | `wm_approval_requests`; Approval engine; applier registry; generic decide endpoints; RLS coverage | none |
| 3 | `wm_notification_log`; Notification engine; template seed; history endpoint | none |
| 4 | Task create/edit/delete/status change onto the engines. Migrate and drop the 3 task request tables and their handlers, endpoints and tests. | Generic approvals API; remove the old task request code |
| 5 | Module edit/delete/transfer/achieve/unachieve/allocation-extend onto the engines. Migrate and drop `objective_change_requests`. | Remove the old Module request code |
| 6 | Sprint create/edit and a new delete command behind the engines; retire `SprintAccessService.CanManageAsync` | Sprint request states |
| 7 | Dead-code and useless-test removal | Approvals page Requests and History tabs; dead-code and spec removal |

## 10. Cleanup rules

- Delete WM code only when grep and the build prove nothing references it any more.
- A WM test is "useless", and is removed, if it:
  - tests deleted code;
  - duplicates another test exactly;
  - only asserts not-null, or only that a mock was wired, with no behaviour checked.
- Every removed test is listed in the commit message.

## 11. Testing

- **Engine rule tests:**
  - the A/B/CC/X transfer example;
  - member → request to the Module owner;
  - owner of the child Module → request to the parent owner;
  - parent → direct plus notification;
  - inactive holder → walk up to the next ancestor owner;
  - root Module → HR fallback;
  - no approver → 422;
  - duplicate pending → 409;
  - stale target → `stale`.
- **Backfill migration tests** for all three objects.
- **Applier tests** for each action type (moved from the old Approve handler tests where they still hold).
- **Notification recipient tests:** the actor is excluded, recipients are deduplicated, and the outbox message is written in the same transaction.
- **Architecture tests:** RLS coverage for both new tables.
- **Frontend:** store and component specs for the Approvals page tabs and the "sent for approval" state.
- **Gate:** the full backend unit + architecture suite and the full frontend suite are green after every part.

## 12. Out of scope

- Any change to CoreHr, Leave, Attendance, People, Calendar, or the shared Notifications/Outbox infrastructure.
- Email notifications (in-app only).
- Multi-step approval chains (one approver per request).
