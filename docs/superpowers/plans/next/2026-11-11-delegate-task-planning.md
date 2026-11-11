# Delegate Task Planning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A meeting delegate with "Can also plan my tasks" sees the principal's Task list, plans/moves/resizes/removes
blocks on the principal's planner, and opens the principal's tasks read-only in the existing task form; the principal
is notified per write, every write is audited, and stale writes get a 409.

**Architecture:** One `IDelegateAccess` service decides, from the database on every request, whether the caller is an
active planning delegate of `forEmployeeId` and returns the principal as the access-check subject (never swapping
`ICurrentUser`). The planner and three read handlers (task, project, sprints) take an optional `forEmployeeId`.
Block moves/removes gain an optional `expectedVersion` for everyone. Notices go through one notifier.

**Tech Stack:** ASP.NET Core, MediatR, EF Core on SQL Server 2022 (RLS), xUnit + Moq + Testcontainers; Angular signals,
vitest.

**Spec:** `docs/superpowers/specs/2026-11-11-delegate-task-planning-design.md` (builds on
`docs/superpowers/specs/2026-11-10-meeting-delegates-design.md`).

## Global Constraints

- Branches: backend `feat/delegate-task-planning-backend` cut from `spec/delegate-task-planning` (which sits on
  `feat/meeting-delegates-backend`); frontend `feat/delegate-task-planning-frontend` cut from
  `origin/feat/meeting-delegates-frontend`. Worktrees under `C:/Users/User/onexso-worktrees/`. No PR unless asked.
- `ICurrentUser` is never set or faked; `forEmployeeId` only changes the subject passed into access checks.
- The delegate check reads the grant from the database on every request; no caching.
- Not an active planning delegate → the endpoint's existing "not found" result (planner `planner_not_found`, task/project
  "not found"), never a distinct 403.
- New tenant table columns go through a migration; new RLS tables: none.
- `forEmployeeId` is a query parameter on every endpoint that accepts it.
- 409 code `planner_block_changed`, message "This block was changed by someone else. Refresh and try again."
- Notification codes: `planner_block_planned_for_you`, `planner_block_moved_for_you`, `planner_block_removed_for_you`,
  category `work_management`, in-app only by default; `when` formatted in the principal's planner zone, e.g.
  "Tuesday, November 10, 3:15 PM – 4:15 PM (Asia/Colombo)".
- Audit actions: `meeting.delegate_updated`, `planner.block_planned_for_principal`, `planner.block_moved_for_principal`,
  `planner.block_removed_for_principal`.
- Backend tests: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo --filter <...>`; architecture and
  integration projects likewise (Docker is on). Frontend: `npx ng test --watch=false --include=<spec>`.
- Commit messages via file (`git commit -F`), ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A task id the principal is **not** assigned to (or a project without one) read with `forEmployeeId` → 404, even
   when the principal could read it normally.
2. The permission switched off between the page load and a block write → the write is 404, not applied.
3. Two writers move the same block: the second, with the old `expectedVersion`, gets 409 and the block is untouched.
4. The principal's own block writes send no notice and no audit row; a delegate's send exactly one of each.
5. `when` in a notice uses the principal's planner zone, not the delegate's and not UTC.

---

### Task 1: The "Can also plan my tasks" permission

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Meetings/Entities/MeetingDelegate.cs` (add `bool CanPlanTasks`)
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/Meetings/MeetingDelegateConfiguration.cs`
- Create: migration `AddMeetingDelegateCanPlanTasks` (`can_plan_tasks bit NOT NULL DEFAULT 0`)
- Modify: `IMeetingDelegateRepository` / `EfMeetingDelegateRepository` (`IsActivePlanningDelegateAsync`)
- Modify: `MeetingDelegateContracts.cs`, `MeetingDelegateCommands.cs`, `MeetingDelegateAudit.cs`, `MeetingsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingDelegateCommandTests.cs`,
  `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingDelegateRepositoryIntegrationTests.cs`

**Interfaces:**
- Produces: `Task<bool> IsActivePlanningDelegateAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, DateTimeOffset now, CancellationToken ct)`;
  `AddMeetingDelegateRequest(Guid DelegateEmployeeId, DateTimeOffset? ExpiresAt, bool CanPlanTasks = false)`;
  `UpdateMeetingDelegateRequest(bool CanPlanTasks)`; `UpdateMeetingDelegateCommand(Guid Id, UpdateMeetingDelegateRequest Request) : IRequest<Result<MeetingDelegateView>>`;
  `MeetingDelegateView(..., bool CanPlanTasks)`; `MeetingPrincipalView(Guid EmployeeId, string Name, bool CanPlanTasks)`;
  route `PATCH /meetings/delegates/{id}`.

- [ ] **Step 1: Write the failing tests**
  - `AddDelegate_StoresCanPlanTasks_AndAuditsIt` — add with `CanPlanTasks: true`; saved row `CanPlanTasks == true`; the
    `meeting.delegate_added` audit new values contain `canPlanTasks = true`.
  - `AddDelegate_WithoutCanPlanTasks_IsFalse`.
  - `UpdateDelegate_ByThePrincipal_ChangesIt_AndAuditsOldAndNew` — `meeting.delegate_updated`, old `false`, new `true`.
  - `UpdateDelegate_SomeoneElsesOrRevoked_IsNotFound` — 404 `meeting_delegate_not_found`.
  - `ListPrincipals_CarriesCanPlanTasks`.
  - Integration `IsActivePlanningDelegate_RequiresTheFlag` — active grant with flag false → false; true → true; revoked
    → false; expired → false.
- [ ] **Step 2: Run** unit filter `MeetingDelegateCommandTests`. Expected: FAIL (missing members).
- [ ] **Step 3: Implement.** Migration via `dotnet ef migrations add AddMeetingDelegateCanPlanTasks`; no RLS change
  (the table already has a policy). Principals view selects the flag.
- [ ] **Step 4: Run** the unit filter, the integration filter `MeetingDelegateRepositoryIntegrationTests`, and the
  architecture project. Expected: PASS.
- [ ] **Step 5: Commit** `feat(meetings): let a principal allow a delegate to plan their tasks`.

### Task 2: `IDelegateAccess` — the one delegate check

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/DelegateAccess.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IDelegateScopeRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfDelegateScopeRepository.cs`
- Modify: DI registration (where `IMeetingDelegateRepository` is registered)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/DelegateAccessTests.cs`,
  `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/DelegateScopeRepositoryIntegrationTests.cs`

**Interfaces:**
- Consumes: `IsActivePlanningDelegateAsync` (Task 1).
- Produces:
  - `public sealed record DelegateSubject(Guid EmployeeId, Guid UserId);`
  - `interface IDelegateAccess { Task<DelegateSubject?> PlanningSubjectAsync(Guid tenantId, Guid callerEmployeeId, Guid forEmployeeId, CancellationToken ct); Task<bool> CanReadTaskAsync(Guid tenantId, Guid principalEmployeeId, Guid taskId, CancellationToken ct); Task<bool> CanReadProjectAsync(Guid tenantId, Guid principalEmployeeId, Guid projectId, CancellationToken ct); }`
  - `IDelegateScopeRepository`: `IsAssignedOrParentOfAssignedAsync(tenantId, employeeId, taskId)`,
    `HasAssignedTaskInProjectAsync(tenantId, employeeId, projectId)`, `GetUserIdAsync(tenantId, employeeId)`.

- [ ] **Step 1: Write the failing tests**
  - `PlanningSubject_ForAnActivePlanningDelegate_IsThePrincipal` — returns principal employee id and user id.
  - `PlanningSubject_ForAMeetingOnlyDelegate_IsNull`; `PlanningSubject_ForAStranger_IsNull`;
    `PlanningSubject_ForOneself_IsNull` (callers use their own path).
  - `PlanningSubject_ReadsTheGrantEveryCall` — repository mock returns true then false; second call null.
  - Integration: `IsAssignedOrParentOfAssigned` true for an assigned task and for its parent, false for a sibling task
    and for a task in the same project assigned to someone else; `HasAssignedTaskInProject` true/false likewise.
- [ ] **Step 2: Run** unit filter `DelegateAccessTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with `IDateTimeProvider` for "now"; no caching fields.
- [ ] **Step 4: Run** unit + integration filters. Expected: PASS.
- [ ] **Step 5: Commit** `feat(meetings): one check for what a planning delegate may read`.

### Task 3: Planner reads for a planning delegate

**Files:**
- Modify: `PlannerCommands.cs` (`GetMyPlannerFocusQuery(Guid? ForEmployeeId = null)`; range handler), `PlannerService.cs`,
  `PlannerContracts.cs`, `PlannerController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerRangeDelegateTests.cs`,
  new `PlannerFocusDelegateTests.cs`

**Interfaces:**
- Consumes: `IDelegateAccess.PlanningSubjectAsync` (Task 2).
- Produces: `PlannerRangeBlockView(..., string? PlannedByName = null, string Version = "")`; `GET /planner/me/focus?forEmployeeId=`.

- [ ] **Step 1: Write the failing tests**
  - `Range_ForAMeetingOnlyDelegate_HasNoEditableDay` — every `Editable == false`.
  - `Range_ForAPlanningDelegate_KeepsThePrincipalsEditableDays`.
  - `Range_BlockWrittenByADelegate_CarriesPlannedByName`; `Range_OwnBlock_HasNoPlannedByName`.
  - `Range_Block_CarriesVersion` — equals `(UpdatedAt ?? CreatedAt).ToString("O")`.
  - `Focus_ForAPlanningDelegate_IsThePrincipals`; `Focus_ForAMeetingOnlyDelegate_IsNotFound`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** The range handler keeps T7's meeting-delegate read and adds the editable override; focus
  uses `PlanningSubjectAsync`.
- [ ] **Step 4: Run** the Planner unit tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat(planner): a planning delegate reads the principal's focus and editable days`.

### Task 4: Planner writes for a planning delegate, and stale-write 409

**Files:**
- Modify: `PlannerCommands.cs` (`CreatePlannedBlockCommand`, `MovePlannedBlockCommand`, `DeletePlannedBlockCommand`
  gain `Guid? ForEmployeeId = null`; Move/Delete gain `string? ExpectedVersion`), `PlannerService.cs`
  (`CreateBlockAsync`/`MoveBlockAsync`/`DeleteBlockAsync` take `Guid subjectEmployeeId, Guid writerEmployeeId`),
  `PlannerContracts.cs` (`MovePlannedBlockRequest(..., string? ExpectedVersion = null)`), `PlannerController.cs`,
  planner error codes (`BlockChanged = "planner_block_changed"`)
- Test: new `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerDelegateWriteTests.cs`

**Interfaces:**
- Consumes: Task 2, Task 3.
- Produces: `IPlannerBlockWriter` unchanged for existing callers; `PlannerService` raises its writes through a
  `BlockWritten(subject, writer, action, block, task, oldStart?, oldEnd?)` hook used by Tasks 5 and 6.

- [ ] **Step 1: Write the failing tests**
  - `Create_ByAPlanningDelegate_LandsOnThePrincipal_CreatedByTheDelegate`.
  - `Create_ByAPlanningDelegate_FollowsThePrincipalsRules` — past day, beyond horizon, overlap with the principal's
    block, task not assigned to the principal → the same error codes the principal gets.
  - `Write_ByAMeetingOnlyDelegate_IsNotFound` (create, move, delete).
  - `Move_WithAStaleExpectedVersion_Is409_AndLeavesTheBlock`; same for `Delete`; for the principal's own write too.
  - `Move_WithoutExpectedVersion_StillWorks`.
  - `Move_ByADelegate_CanMoveTheBlockThePrincipalMade`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Version compare inside the existing plan lock/transaction. The controller reads
  `forEmployeeId` and `expectedVersion` (DELETE) from the query.
- [ ] **Step 4: Run** the Planner unit tests and the architecture project. Expected: PASS.
- [ ] **Step 5: Commit** `feat(planner): a planning delegate plans the principal's tasks; stale writes get 409`.

### Task 5: Notify the principal

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerDelegateNotifier.cs`
- Modify: `NotificationEventCatalog.cs` (three codes), `NotificationTemplateSeeder.cs` (three templates, titles/bodies
  from the spec), `CalendarNotificationSender` or the in-app sender the catalog events use, `PlannerService.cs` (call
  after commit), `tests/ONEVO.Tests.Integration/SqlServerMigration/ReferenceDataParityTests.cs` (added-after-move
  allowance for the three template codes, like `AddedTables`)
- Test: new `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerDelegateNotifierTests.cs`;
  `tests/ONEVO.Tests.Unit/Features/Auth/NotificationTemplateSeederTests.cs`

**Interfaces:**
- Produces: `interface IPlannerDelegateNotifier { Task BlockWrittenAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, string action, string taskTitle, DateTimeOffset start, DateTimeOffset end, CancellationToken ct); }`
  with `action` in `planned` / `moved` / `removed`.

- [ ] **Step 1: Write the failing tests**
  - `Planned_SendsOneNoticeToThePrincipal_WithWhenInTheirZone` — principal zone Asia/Colombo, block 09:45Z–10:45Z →
    `when` = "Tuesday, November 10, 3:15 PM – 4:15 PM (Asia/Colombo)".
  - `Moved_UsesTheNewTime`; `Removed_UsesTheRemovedTime`.
  - `ThePrincipalsOwnWrite_SendsNothing` (through `PlannerService`).
  - Seeder test: the three codes exist with the spec's title/body and variables.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Zone from the principal's planner zone (`PlannerZones`), UTC fallback.
- [ ] **Step 4: Run** unit tests; integration filter `ReferenceDataParityTests|SchemaParityTests`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(planner): tell the principal when a delegate plans their time`.

### Task 6: Audit delegated block writes

**Files:**
- Modify: `MeetingDelegateAudit.cs` (add `RecordBlockForPrincipalAsync(Guid tenantId, Guid principalEmployeeId, Guid blockId, string action, object newValues, object? oldValues, CancellationToken ct)`; action constants), `PlannerService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingDelegateAuditTests.cs`,
  new `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerDelegateAuditIntegrationTests.cs`

- [ ] **Step 1: Write the failing tests**
  - `BlockPlanned_RecordsActorDelegate_OnBehalfOfPrincipal` (actor = `ICurrentUser`, resource `planned_task_block`).
  - `BlockMoved_RecordsOldAndNewTimes` (also a resize).
  - `BlockRemoved_RecordsTheRemovedTimes`.
  - Integration: a delegate's create, move, delete each write one audit row in the same transaction as the block;
    the principal's own write writes none.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** inside the write transaction (tracked add, saved with the block).
- [ ] **Step 4: Run** unit + integration filters. Expected: PASS.
- [ ] **Step 5: Commit** `feat(planner): audit what a delegate does on the principal's planner`.

### Task 7: Task form reads as the principal (least privilege)

**Files:**
- Modify: `GetTaskByIdQuery(Guid TaskId, Guid? ForEmployeeId = null)` + handler; `GetProjectByIdQuery(..., Guid? ForEmployeeId = null)` + handler;
  `GetProjectSprintsQuery(..., Guid? ForEmployeeId = null)` + handler; `TasksController.GetById`,
  `ProjectsController.GetById`, `SprintsController` project sprints action (`[FromQuery] Guid? forEmployeeId`)
- Test: new `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DelegateTaskReadTests.cs`,
  `.../Projects/DelegateProjectReadTests.cs`, `.../Sprints/DelegateSprintReadTests.cs`;
  new architecture test `tests/ONEVO.Tests.Architecture/DelegateReadArchitectureTests.cs`

**Interfaces:**
- Consumes: `IDelegateAccess` (Task 2).

- [ ] **Step 1: Write the failing tests** (each handler)
  - `WithoutForEmployeeId_IsUnchanged` (existing caller path; a non-member is still refused).
  - `ForAStranger_OrMeetingOnlyDelegate_IsNotFound`.
  - `ForAPlanningDelegate_ATaskNotAssignedToThePrincipal_IsNotFound` / `AProjectWithoutTheirTask_IsNotFound`.
  - `ForAPlanningDelegate_ThePrincipalsTask_IsTheirView` (access checks receive the principal's ids;
    `ICurrentUser.UserId` untouched).
  - Task only: `ForAPlanningDelegate_TheParentOfTheirSubtask_IsReadable`.
  - Architecture: only these three query types (plus the planner/calendar ones) declare a `ForEmployeeId` property;
    no command type does.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Resolve the subject via `PlanningSubjectAsync`, then scope (`CanReadTaskAsync` /
  `CanReadProjectAsync`), then the existing body with subject ids.
- [ ] **Step 4: Run** unit + architecture. Then the delegate integration filters and `SqlServerMigration`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(work): a planning delegate reads the principal's task in the task form`.

After Task 7: backend unit, architecture, and integration filters `Meeting|Planner|Delegate|SqlServerMigration`; push
`feat/delegate-task-planning-backend`.

### Task 8: Frontend models and API

**Files:**
- Modify: `meeting.models.ts` (`canPlanTasks` on `MeetingDelegateView`, `MeetingPrincipalView`), `meeting-api.service.ts`
  (`addDelegate(employeeId, expiresAt?, canPlanTasks = false)`, `updateDelegate(id, canPlanTasks)`),
  `planner.models.ts` (`plannedByName?`, `version?` on `PlannerRangeBlockView`), `planner-api.service.ts`
  (`getFocus(forEmployeeId?)`, `createBlock(req, forEmployeeId?)`, `moveBlock(id, req & {expectedVersion?}, forEmployeeId?)`,
  `deleteBlock(id, expectedVersion?, forEmployeeId?)`), `task-api.service.ts` (`getTaskById(id, forEmployeeId?)`),
  `project-api.service.ts` (`getById(id, forEmployeeId?)`), `sprint-api.service.ts` (`getByProject(id, forEmployeeId?)`)
- Test: the matching `*.service.spec.ts` files

- [ ] **Step 1:** Specs: each method sends `forEmployeeId` / `expectedVersion` only when given; `updateDelegate` PATCHes
  `{ canPlanTasks }`; `addDelegate` sends `canPlanTasks` only when true.
- [ ] **Step 2: Run.** Expected: FAIL. **Step 3:** Implement. **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(planner): API for delegate task planning`.

### Task 9: Delegates modal — the permission

**Files:** `meeting-delegates-modal.component.ts` + spec

- [ ] **Step 1:** Specs: `adds with Can also plan my tasks ticked`; `a row shows Meetings or Meetings · Tasks`;
  `the row switch updates it`; `a failed switch shows the server error and reverts`.
- [ ] **Steps 2–4:** RED, implement, GREEN.
- [ ] **Step 5: Commit** `feat(calendar): choose whether a delegate may plan your tasks`.

### Task 10: Calendar page — planning on a principal's calendar

**Files:** `calendar-page.component.ts/.html`, `planner-layer.store.ts`, `calendar-page.planner.spec.ts` (delegates
describe block), block rendering component that shows the task title (add "Planned by")

- [ ] **Step 1:** Specs:
  - `a planning delegate sees the principal's Task list` (focus read with `forEmployeeId`).
  - `a planning delegate's editable days are the principal's`; `a meeting-only delegate's stay read-only` (existing).
  - `a block planned there is sent with forEmployeeId`; `a move sends expectedVersion`.
  - `a 409 says the block changed and reloads the range`.
  - `a 404 on a write reloads principals and falls back` ("You can no longer plan for <name>.").
  - `a block with plannedByName shows Planned by <name>`.
  - The popover still offers only Meeting on a principal's calendar.
- [ ] **Steps 2–4:** RED, implement (store write methods take `forEmployeeId` from state like `load`), GREEN.
- [ ] **Step 5: Commit** `feat(calendar): plan the principal's tasks on their calendar`.

### Task 11: Task form read-only for a delegate

**Files:** `task-form-modal.component.ts` + spec, `calendar-page.component.ts/.html`

- [ ] **Step 1:** Specs:
  - `readOnly with actingFor reads the task, project and sprints with forEmployeeId, and statuses/categories without`.
  - `readOnly makes no other request` (history, subtasks, comments, members, milestones, monitor check not fetched).
  - `readOnly shows no edit control` — no Save/Request changes, delete, duplicate, convert, assign, status move,
    upload; fields disabled; comments and attachments hidden.
  - `readOnly closes without the unsaved-changes prompt and saves no draft`.
  - Page: `a principal's task opens read-only for a planning delegate` (no `loadProject` beyond the allowed read).
- [ ] **Steps 2–4:** RED, implement, GREEN; then the calendar, work, planner, meetings, dashboard spec folders and
  eslint on changed files.
- [ ] **Step 5: Commit** `feat(work): the task form read-only for a delegate`; push
  `feat/delegate-task-planning-frontend`.
