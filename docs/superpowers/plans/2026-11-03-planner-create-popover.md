# Planner Create Popover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One create popover on the Planner (Event · Meeting · Work item · Time off), with "Create & plan" for work items, requests that assign and plan on approval, and the caller's leave drawn on the Planner.

**Architecture:** Backend: one additive change. A task create request can carry the requester as assignee and a plan slot; the `task.create` applier assigns and plans on approval. The approver sees both, read-only. Frontend: a popover shell owns the shared draft (title, start, end, all-day). Each tab is a small component that reuses the existing stores and APIs. The Meeting tab is the existing meeting form in a new compact mode. A `WorkItemPlanFlow` service runs create → assign → plan with retry safeguards. A leave layer store draws the caller's pending/approved leave on the hour grid.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core, xUnit + Moq + Testcontainers; Angular 21 standalone + signals + @ngrx/signals, Vitest (`npx ng test`).

**Spec:** `docs/superpowers/specs/2026-11-03-planner-create-popover-design.md` (and the mockup next to it). Read it first.

## Global Constraints

- Tab order and labels: **Event · Meeting · Work item · Time off**. Default tab Event.
- Popover only while the Planner layer is active (`PlannerLayerStore.active()`) and the slot's day is editable (`plannerEditable()` has the date). Otherwise slot click/drag and **New event** keep today's behaviour (event form).
- All slot instants are built with the page's existing `toIso(date, minutes)` (the planner's zone), never the browser zone.
- Estimate ≠ plan: the Estimate field starts at the slot length for a new item; afterwards neither changes the other.
- Copy (exact):
  - pending work item: `Sent for approval. Once approved it's yours and planned for {Ddd HH:mm–HH:mm}.`
  - created, not planned: `Created {shortId}, not planned: {reason}` with buttons `Retry planning` and `Pick another time`.
  - lost create response: `We couldn't confirm the work item was created. Check your Task queue before trying again.`
  - leave labels: `Pending leave` / `Leave`.
- Never re-send a work item create once its id is known; never auto-retry a create.
- Backend: the new create fields are optional; every existing caller and payload behaves exactly as before.
- FE build must stay warning-free (`node scripts/check-no-warnings.mjs`); BE build must not add warnings in touched files.
- Branches:
  - BE `feature/planner-create-popover-backend` from `origin/feature/category-kind-explicit`.
  - FE `feature/planner-create-popover` from `origin/feature/category-kind-explicit`, then `git merge origin/fix/meeting-form-start-in-future` (FE #138) as its first commit.
  - Push only; no PR.

## Review Focus

1. **Planner zone ≠ browser zone:** every tab must send the instants shown on the grid. → Task F2 test `uses the planner zone for the slot`.
2. **All-day chosen, then Work item tab:** a work item can't be planned all day. **Create & plan** is disabled with `Turn off All day to plan a work item.` → Task F5 test.
3. **Today's slot already partly past (meeting tab):** the meeting start-in-past rule from #138 shows its message and blocks Create. → Task F4 test `shows the past-start note for a slot that has started`.
4. **Week/day navigation while the popover is open:** the popover closes on any range change (stale slot). → Task F2 test.
5. **Requester left the module before approval:** the applier returns Stale as today (no task, no assignment); **planner off at approval time:** task assigned, not planned, approval succeeds. → Task B2 tests.

---

## Backend

### Task B1: A create request can carry the requester as assignee and a plan slot

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`CreateTaskRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs:157-160`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs`, `CreateTaskCommandHandler.cs:83-85`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskWriteInputs.cs` (`TaskCreateInput`)
- Modify: `docs/postman-request/Work Management/` create-task request doc
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs`

**Interfaces:**
- Produces: `TaskCreateInput(..., Guid? SourceMeetingOccurrenceId = null, Guid? AssigneeEmployeeId = null, DateTimeOffset? PlanStartAt = null, DateTimeOffset? PlanEndAt = null)`. Same three optional params appended to `CreateTaskRequest` and `CreateTaskCommand`. Error code `TaskErrorCodes.InvalidPlanRequest = "invalid_plan_request"` (400).

- [ ] **Step 1: Write the failing tests** in `CreateTaskCommandHandlerTests`:
  - `Handle_AssigneeIsTheCaller_AndASlot_ArePutInTheApprovalPayload`: a member (not owner) → the `WorkAction.PayloadJson` sent to the approval engine contains `assigneeEmployeeId` = caller and `planStartAt`/`planEndAt` as sent.
  - `Handle_AssigneeIsSomeoneElse_Is400` → `(400, "invalid_plan_request")`, nothing sent to the engine.
  - `Handle_OnlyOneSlotEnd_Is400` and `Handle_SlotEndNotAfterStart_Is400` → `(400, "invalid_plan_request")`.
  - `Handle_NoNewFields_PayloadUnchanged`: the payload has no `assigneeEmployeeId`/`planStartAt`/`planEndAt` values (null), as before.
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskCommandHandlerTests"`. Expected: FAIL (compile errors, then assertions).
- [ ] **Step 3:** Add the three optional params end to end, and validate in the handler before building `TaskCreateInput`:
  - assignee given and ≠ the caller's employee id → 400;
  - exactly one of start/end given → 400;
  - end ≤ start → 400.
  - The direct (201) path ignores the fields. `TaskWriteService.CreateAsync` is unchanged.
- [ ] **Step 4:** Run the same filter. Expected: PASS. Then run all of `FullyQualifiedName~WorkManagement.Tasks`. Expected: PASS.
- [ ] **Step 5:** Update the create-task request doc (three optional fields, 400 `invalid_plan_request`). Commit `feat(work): a task create request can carry the requester as assignee and a plan slot`.

### Task B2: On approval, assign the requester and plan the slot

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/IPlannerBlockWriter.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerService.cs` (implement the interface)
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Appliers/TaskCreateApplier.cs`
- Modify: DI registration where `PlannerService` is registered (`src/ONEVO.Infrastructure/DependencyInjection.cs` or the Application registration that adds it)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCreateApplierTests.cs` (create if missing), `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerServiceIntegrationTests.cs`

**Interfaces:**
- Consumes: `TaskCreateInput.AssigneeEmployeeId/PlanStartAt/PlanEndAt` (B1); `ApprovalApplyContext(Request, PayloadJson, DeciderEmployeeId)`.
- Produces: `public interface IPlannerBlockWriter { Task<Result> PlanForAsync(Guid tenantId, Guid employeeId, Guid taskId, DateTimeOffset startAt, DateTimeOffset endAt, CancellationToken ct); }`, implemented by `PlannerService` as `CreateBlockAsync(tenantId, employeeId, new CreatePlannedBlockRequest(taskId, startAt, endAt), ct)` mapped to a non-generic `Result`.

- [ ] **Step 1: Write the failing unit tests** in `TaskCreateApplierTests` (mocks for `ITaskWriteService`, `IObjectiveRepository`, `IMilestoneMembershipCoordinator`, `ITaskAssignmentRepository`, `IUnitOfWork`, `IPlannerBlockWriter`):
  - `Apply_WithAssigneeAndSlot_AssignsTheRequester_ThenPlans`:
    - one `TaskAssignment` is added with `EmployeeId` = requester, `UserId` = the requester's user, `AssignedById` = `DeciderEmployeeId`;
    - `UpsertMembershipAsync` runs for the requester;
    - `SaveChangesAsync` happens **before** `PlanForAsync(tenant, requester, taskId, start, end)`;
    - the outcome is `Applied`.
  - `Apply_PlanFails_StillApplied_AndAssigned`: `PlanForAsync` returns a failure → `Applied`, and the assignment was still added.
  - `Apply_WithoutNewFields_NoAssignmentAndNoPlan`: today's behaviour, no calls to the assignment repo or `PlanForAsync`.
  - `Apply_ApproverChangedAssigneeOrSlot_IsInvalid`: `PayloadJson` differs from `Request.PayloadJson` in `planStartAt` → `Invalid("The assignee and planned time cannot be changed by the approver.")`.
  - `Apply_RequesterNoLongerActive_IsStale`: unchanged path, nothing created.
- [ ] **Step 2: Write the failing integration test** in `PlannerServiceIntegrationTests`:
  - `PlanFor_ATaskAssignedToTheEmployee_AddsTheBlock`;
  - `PlanFor_ATaskNotAssigned_IsTaskNotPlannable` (`PlannerErrorCodes.TaskNotPlannable`).
- [ ] **Step 3:** Run the unit filter `~TaskCreateApplierTests` and the integration filter `~PlannerServiceIntegrationTests`. Expected: FAIL.
- [ ] **Step 4:** Implement `IPlannerBlockWriter` on `PlannerService` and register it (`AddScoped<IPlannerBlockWriter>(sp => sp.GetRequiredService<PlannerService>())`).
  - In the applier, after `CreateAsync` succeeds and the link step:
    - if `input.AssigneeEmployeeId is { } assignee` (it equals `RequestedByEmployeeId`, enforced at create): add the assignment the way `AssignTaskCommandHandler.cs:94-104` does, then `SaveChangesAsync`.
    - Then, if `PlanStartAt/PlanEndAt` are set, call `PlanForAsync`. Ignore a failure.
  - The approver-change guard sits next to the existing `SourceMeetingOccurrenceId` guard.
- [ ] **Step 5:** Run both filters. Expected: PASS. Then run `dotnet test tests/ONEVO.Tests.Unit` and `dotnet test tests/ONEVO.Tests.Architecture`. Expected: all pass.
- [ ] **Step 6:** Commit `feat(work): an approved work item request is assigned to the requester and planned at its slot`.

### Task B3: The approver sees the assignee and slot, and can't edit them

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalChangeSetBuilder.cs`
- Modify: wherever `ModuleTransfer`'s `newHeadEmployeeId` gets its display name (`requestedOverrides`, in `GetApprovalDetailQueryHandler` / `WorkInboxItemMapper`). Do the same for `assigneeEmployeeId`.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ApprovalChangeSetBuilderTests.cs`

**Interfaces:**
- Produces: `FieldSpec(string Name, string Label, string Kind, bool Locked = false)`; a locked row's `Editable` is always false.
- `TaskCreate` specs = `TaskFields` plus:
  - `("assigneeEmployeeId", "Assign to", "employee", Locked: true)`
  - `("planStartAt", "Planned from", "datetime", Locked: true)`
  - `("planEndAt", "Planned to", "datetime", Locked: true)`

- [ ] **Step 1: Write the failing tests:**
  - `TaskCreate_WithPlan_ShowsAssigneeAndSlot_ReadOnly`: three rows, `Editable == false`, the title row still editable.
  - `TaskCreate_WithoutPlan_HasNoPlanRows`: rows whose requested value is null are omitted, or are present with null values. Match how the builder treats absent optional fields today, and assert that.
  - `TaskCreate_AssigneeRow_ShowsTheEmployeeName` (through the override).
- [ ] **Step 2:** Run `~ApprovalChangeSetBuilderTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `Locked` and the three specs. Resolve the assignee name like `newHeadEmployeeId`.
- [ ] **Step 4:** Run the filter, then the full unit suite. Expected: PASS.
- [ ] **Step 5:** Commit `feat(work): approvers see a work item request's assignee and planned time`. Push the branch.

---

## Frontend

### Task F1: Branch setup, and Location on the event form

**Files:**
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.{ts,html}`
- Create: `src/app/modules/calendar/utils/event-draft.ts`
- Test: `calendar-event-form-modal.component.spec.ts`, `src/app/modules/calendar/utils/event-draft.spec.ts`

**Interfaces:**
- Produces:
  - `export interface EventDraft { title: string; description: string | null; start: string; end: string; isAllDay: boolean; timezone: string; repeat: 'none' | 'daily' | 'weekly' | 'monthly'; location: string | null; }`
  - `export function eventCreateRequest(draft: EventDraft): CreateCalendarEventRequest`. It sends the same `recurrence`/`recurrenceRule` values the event form sends for those presets today, with `participantEmployeeIds: []`, `meetingLink: null` and `color: null`.
  - On the event form: `prefill = input<EventDraft | null>(null)`, which fills every field.

- [ ] **Step 1:** Create the branch and merge #138 (Global Constraints). Run `npx ng test --watch=false --include "src/app/modules/work/meetings/**/*.spec.ts"`. Expected: PASS.
- [ ] **Step 2: Write the failing tests:**
  - `eventCreateRequest` maps every field, one test per repeat preset, asserting the event form's current values;
  - the event form `shows a Location field and sends it`;
  - the event form `fills every field from prefill`.
- [ ] **Step 3:** Run the calendar specs. Expected: FAIL.
- [ ] **Step 4:** Implement. The event form builds its create request through `eventCreateRequest` (one source of truth).
- [ ] **Step 5:** Run `npx ng test --watch=false --include "src/app/modules/calendar/**/*.spec.ts"`. Expected: PASS. Commit `feat(calendar): events have a location; one builder for the create request`.

### Task F2: The popover shell and its wiring on the Planner

**Files:**
- Create: `src/app/modules/calendar/ui/planner-create-popover/planner-create-popover.component.ts` (+ `.spec.ts`)
- Create: `src/app/modules/calendar/ui/planner-create-popover/planner-create-draft.ts`
- Modify: `src/app/modules/calendar/feature/calendar-page/calendar-page.component.{ts,html}` (`openCreate`, `onSlotClick`, `onSlotRange`, the New event button)
- Test: `planner-create-popover.component.spec.ts`, `calendar-page.planner.spec.ts`

**Interfaces:**
- Produces:
  - `export type CreateTab = 'event' | 'meeting' | 'workItem' | 'timeOff';`
  - `export interface CreateSlot { date: string; startMinutes: number; endMinutes: number; startIso: string; endIso: string; }`
  - Popover inputs: `slot = input.required<CreateSlot>()`, `tabs = input.required<readonly CreateTab[]>()`, `anchor = input<DOMRect | null>(null)`.
  - Popover outputs: `closed`, and `moreOptions = output<{ tab: CreateTab; draft: unknown }>()`.
  - Shared draft signal `{ title: string; isAllDay: boolean }`, passed to each tab.
  - Page: `createSlot = signal<CreateSlot | null>(null)` and `createTabs = computed<readonly CreateTab[]>`. `meeting` comes from `meetingsAvailability.canCreate()`. `timeOff` comes from `permissions.hasModule('leave') && permissions.hasPermission('leave:read-own')`. `event` and `workItem` are always present while the popover exists.

- [ ] **Step 1: Write the failing tests** (popover):
  - `shows the tabs in order Event, Meeting, Work item, Time off`;
  - `hides a tab that is not in tabs`;
  - `keeps the title when switching tabs`;
  - `closes on Escape, on ✕ and on an outside click`.
- [ ] **Step 2: Write the failing tests** (page):
  - `slot click on an editable day opens the popover, not the event form`;
  - `slot click when the planner is off opens the event form`;
  - `drag range fills start and end`;
  - `uses the planner zone for the slot` (planner timezone ≠ browser);
  - `New event opens the popover at the next half hour, 1h`;
  - `closes the popover when the range changes`.
- [ ] **Step 3:** Run the calendar specs. Expected: FAIL.
- [ ] **Step 4:** Implement. The popover anchors next to `anchor` on desktop (≥ 768px) and opens as a bottom sheet below that width. Tabs render placeholders until F3–F7.
- [ ] **Step 5:** Run the calendar specs. Expected: PASS. Commit `feat(planner): a create popover with tabs opens from a slot or New event`.

### Task F3: Event tab

**Files:**
- Create: `src/app/modules/calendar/ui/planner-create-popover/event-tab.component.ts` (+ spec)

**Interfaces:**
- Consumes: `EventDraft`, `eventCreateRequest` (F1), `CalendarEventStore.createEvent`.
- Produces: outputs `created`, `switchToMeeting`, and `moreOptions = output<EventDraft>()`.

- [ ] **Step 1: Write the failing tests:**
  - `Save sends eventCreateRequest(draft) once and emits created` (a double click sends one request);
  - `the People/video note switches to the Meeting tab, keeping title and time`;
  - `More options emits the full EventDraft`.
- [ ] **Step 2:** Run the spec. Expected: FAIL.
- [ ] **Step 3:** Implement. The page reloads events on `created` and closes the popover. On `moreOptions`, the page opens the event form with `prefill`.
- [ ] **Step 4:** Run the spec. Expected: PASS. Commit `feat(planner): Event tab`.

### Task F4: Meeting tab = the meeting form in compact mode

**Files:**
- Modify: `src/app/modules/work/meetings/ui/meeting-form-modal/meeting-form-modal.component.ts`
- Test: `meeting-form-modal.component.spec.ts`, `calendar-page.planner.spec.ts`

**Interfaces:**
- Produces:
  - `export interface MeetingDraft extends MeetingPrefill { description: string | null; isAllDay: boolean; timezone: string; recurrence: RecurrencePreset; platform: PlatformChoice; projectId: string | null; objectiveId: string | null; participantIds: readonly string[]; }`
  - `prefill` accepts `MeetingPrefill | MeetingDraft`.
  - `compact = input(false)`. Compact shows title, time, all-day/timezone/repeat (presets only), project/module, platform, participants with availability, and agenda. It hides private, location, the custom rule and bulk invite.
  - `moreOptions = output<MeetingDraft>()`, emitted by a `More options` button shown only when compact.

- [ ] **Step 1: Write the failing tests:**
  - `compact hides private, location, custom rule and bulk invite`;
  - `More options emits every filled field as MeetingDraft`;
  - `a MeetingDraft prefill fills every field`;
  - `shows the past-start note for a slot that has started` (#138 rule);
  - page: `Meeting tab creates through the meeting form and opens the drawer`.
- [ ] **Step 2:** Run the spec. Expected: FAIL.
- [ ] **Step 3:** Implement. The page renders `<app-meeting-form-modal embedded compact mode="create" [prefill]>` inside the Meeting tab. On `moreOptions`, the page opens today's full meeting form with that draft.
- [ ] **Step 4:** Run the meetings + calendar specs. Expected: PASS. Commit `feat(planner): Meeting tab uses the meeting form in compact mode`.

### Task F5: Work item tab and the Create & plan flow

**Files:**
- Create: `src/app/modules/calendar/state/work-item-plan-flow.service.ts` (+ spec)
- Create: `src/app/modules/calendar/ui/planner-create-popover/work-item-tab.component.ts` (+ spec)
- Modify: `src/app/modules/work/models/dto/task.dto.ts` (`CreateTaskRequestDto` + `assigneeEmployeeId?`, `planStartAt?`, `planEndAt?`)

**Interfaces:**
- Consumes:
  - `TaskApiService.createTask(objectiveId, dto)` (201 `WorkTaskDto` | 202 `PendingApprovalDto`) and `assignTask(taskId, employeeId)`;
  - `PlannerLayerStore.createBlock(taskId, startIso, endIso): Promise<boolean>` and `.error()`;
  - `ProjectApiService.listMine`, `getProjectTree`, `TaskApiService.getCategories`.
- Produces:
  - `export type PlanFlowState = { kind: 'idle' } | { kind: 'busy' } | { kind: 'done'; taskId: string } | { kind: 'pending' } | { kind: 'notPlanned'; taskId: string; shortId: string; reason: string } | { kind: 'unknown' } | { kind: 'error'; message: string };`
  - `WorkItemPlanFlow`:
    - `state: Signal<PlanFlowState>`
    - `createAndPlan(objectiveId: string, dto: CreateTaskRequestDto, slot: CreateSlot): Promise<void>`
    - `retryPlanning(): Promise<void>`
    - `planExisting(taskId: string, shortId: string, slot: CreateSlot): Promise<void>`
  - The tab outputs `moreOptions = output<WorkItemDraft>()`, where `WorkItemDraft = { title; projectId; objectiveId; categoryId; priority; estimatedHours; dueDate; slot }`.

- [ ] **Step 1: Write the failing flow tests:**
  - `201 → assign me → block → done`;
  - `202 → pending, no assign/block call; dto carried assigneeEmployeeId=me and the slot`;
  - `block refused → notPlanned with the store's error; retryPlanning sends only createBlock for the same task, then done`;
  - `a second createAndPlan while busy sends nothing`;
  - `create fails without a status (network) → unknown, no second create`;
  - `retryPlanning failing again keeps notPlanned and never calls createTask`;
  - `assign fails after 201 → notPlanned with the assign error, item kept`.
- [ ] **Step 2: Write the failing tab tests:**
  - `Estimate starts at the slot length and can be changed`;
  - `editing the estimate does not change the plan, and the plan label shows "Plan 9:00–11:00 · 2h"`;
  - `Type lists the project's categories except systemKey meeting, with kind icons`;
  - `Create & plan is disabled with "Turn off All day to plan a work item." when all-day`;
  - `warns (not blocks) when the slot overlaps one of my blocks`;
  - `shows Retry planning and Pick another time when notPlanned` (Pick another time opens the existing block picker for that task).
- [ ] **Step 3:** Run both specs. Expected: FAIL.
- [ ] **Step 4:** Implement. `createAndPlan` always sends `assigneeEmployeeId` (me) and `planStartAt`/`planEndAt`; on 201 it then does assign → block (the backend ignores the fields on 201). On `done`, the page reloads the planner and closes the popover.
- [ ] **Step 5:** Run both specs and `npx ng test --watch=false --include "src/app/modules/calendar/**/*.spec.ts"`. Expected: PASS. Commit `feat(planner): Work item tab creates and plans, with retry safeguards`.

### Task F6: Work item → More options (full form) keeps everything and plans the created item

**Files:**
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts`
- Modify: `calendar-page.component.{ts,html}`
- Test: `task-form-modal.component.spec.ts`, `calendar-page.planner.spec.ts`

**Interfaces:**
- Consumes: `WorkItemDraft` (F5), `WorkItemPlanFlow.planExisting` (F5); the form's `created = output<string>()` (the id; `''` when sent for approval) and `closed`.
- Produces: `export interface TaskFormPrefill { title: string; categoryId: string | null; priority: string | null; estimatedHours: number | null; dueDate: string | null; assigneeEmployeeId: string | null; }` and `prefill = input<TaskFormPrefill | null>(null)` (create mode only).

- [ ] **Step 1:** Confirm in code that `created` fires only after the assignee is saved (around `task-form-modal.component.ts:3666-3715`). If it doesn't, move the emit after the assign, with a test.
- [ ] **Step 2: Write the failing tests:**
  - form: `fills title, category, priority, estimate, due and assignee from prefill`;
  - page: `More options loads the project, then opens the form with projectId, prefillModuleId and prefill`;
  - page: `created(id) plans that id at the slot`;
  - page: `created('') shows "Sent for approval" and plans nothing`;
  - page: `closed without created plans nothing`;
  - page: `a planning refusal because the assignee changed shows "Created …, not planned: assigned to someone else"`.
- [ ] **Step 3:** Run. Expected: FAIL.
- [ ] **Step 4:** Implement.
- [ ] **Step 5:** Run the work + calendar specs. Expected: PASS. Commit `feat(planner): More options on a work item keeps every field and plans the created item`.

### Task F7: Time off tab and its More options

**Files:**
- Create: `src/app/modules/calendar/ui/planner-create-popover/time-off-tab.component.ts` (+ spec)
- Modify: `src/app/modules/leave/ui/new-leave-request-wizard/new-leave-request-wizard.component.ts` (extra initial inputs)
- Modify: `calendar-page.component.{ts,html}` (host the wizard, as `my-balances.component.ts:114-144` does)

**Interfaces:**
- Consumes: `LeaveTypeStore`, `LeaveBalanceStore` (balances + `loadWorkWindow`), `LeaveRequestStore.preview/submit/uploadDocument/clearPreview`.
- Produces:
  - wizard inputs `initialLeaveTypeId = ''`, `initialStartTime = ''`, `initialEndTime = ''`, `initialReason = ''`;
  - tab outputs `submitted`, and `moreOptions = output<{ leaveTypeId: string; startIso: string; endIso: string; reason: string | null }>()`.

- [ ] **Step 1: Write the failing tests:**
  - `lists active leave types with remaining hours`;
  - `previews the policy when type and times are set`;
  - `Submit sends one request with the slot's startAt/endAt`;
  - `a type needing a document for this length disables Submit and points to More options` (uses `requiresDocument` / `documentRequiredAfterHours`);
  - wizard: `fills type, dates, times and reason from the initial inputs`;
  - page: `More options opens the wizard prefilled`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the leave + calendar specs. Expected: PASS. Commit `feat(planner): Time off tab submits a leave request`.

### Task F8: The caller's leave on the Planner

**Files:**
- Create: `src/app/modules/calendar/state/planner-leave-layer.store.ts` (+ spec)
- Modify: `src/app/modules/calendar/ui/calendar-hour-grid/calendar-hour-grid.component.{ts,html}` (input `leaves`)
- Modify: `calendar-page.component.{ts,html}`

**Interfaces:**
- Consumes: `LeaveRequestApiService.listMine({ fromDate, toDate })`.
- Produces:
  - `export interface PlannerLeave { id: string; startAt: string; endAt: string; status: 'pending' | 'approved'; typeName: string; }`
  - `PlannerLeaveLayerStore`: `leaves: Signal<readonly PlannerLeave[]>`, `load(from: string, to: string): Promise<void>`, `startAutoRefresh(): void`, `stopAutoRefresh(): void`.
  - Hour grid input `leaves = input<readonly PlannerLeave[]>([])`.

- [ ] **Step 1: Write the failing tests:**
  - store: `keeps pending and approved, drops rejected and cancelled`;
  - store: `re-reads on visibilitychange to visible and on window focus`;
  - store: `re-reads every 60 s while visible, not while hidden` (fake timers);
  - store: `stops refreshing after stopAutoRefresh`;
  - grid: `draws "Pending leave" outlined and "Leave" solid, with different labels`;
  - page: `loads leave for the visible range, and again after a Time off submit`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. The page calls `startAutoRefresh` while the planner is active and `stopAutoRefresh` on destroy.
- [ ] **Step 4:** Run the full FE suite `npx ng test --watch=false` and `node scripts/check-no-warnings.mjs`. Expected: all pass, `Build is clean`.
- [ ] **Step 5:** Commit `feat(planner): your pending and approved leave on the Planner`. Push the branch.
