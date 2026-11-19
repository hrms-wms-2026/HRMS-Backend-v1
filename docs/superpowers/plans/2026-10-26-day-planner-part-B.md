# Day Planner (Part B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An employee plans today's work as task blocks on a timeline next to their meetings. They see planned vs actual hours per row and for the day. Their reporting-line managers can view the plan read-only.

**Architecture:**
- **Backend.** A new `planned_task_blocks` table (tenant RLS). A `PlannerDayService` composes one day:
  - blocks;
  - the employee's meeting occurrences;
  - assigned tasks that are due or overdue;
  - actuals: `TaskClockingSession` overlap with the day, and meeting duration if not declined;
  - the attendance clocked total.
- Block commands accept today only. A manager read uses the employee hierarchy closure.
- **Frontend.** A new `/planner` page and a read-only "Today's plan" tab on the employee profile.

**Tech Stack:** .NET 10, EF Core 10, MediatR, PostgreSQL RLS, xUnit/Moq/Testcontainers; Angular 20 standalone + signals, CDK drag-drop, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-26-meeting-hours-and-day-planner-design.md` (Part B).

**Branches:**
- Backend: `feature/day-planner`, cut from `feature/meeting-hours` (Part A) in `be-phase2-d`'s repo.
- Frontend: `feature/day-planner`, cut from `feature/meeting-task-phase2-e`.
- Commit and push both; no PR.

## Global Constraints

- **Web only. Today only.**
  - Writes are refused unless the block's `start_at` and `end_at` fall on today in the employee's zone (the zone `GET /calendar/my-timezone` uses), with 400 `planner_not_today`.
  - Reads take any `date` up to today; a future date returns 400.
- **Blocks:**
  - Same employee, no overlap with each other: 409 `planner_block_overlap`.
  - `end_at > start_at`; minimum 15 min.
  - The task must be assigned to the caller (`task_assignees`) and must not be `task_kind = 'meeting'`: 400 `planner_task_not_plannable`.
- **The plan never changes a task's due date, estimate or status.**
- **Actuals:**
  - Task actual = the sum of each of the caller's sessions on that task, clipped to `[day start, min(day end, now)]`. An open session counts up to now.
  - Meeting actual = `duration` if the employee did not decline and the occurrence has ended; 0 otherwise. Cancelled = 0.
  - Day planned = Σ blocks + Σ non-cancelled meetings not declined; day actual = Σ actuals.
- **Manager view:**
  - Allowed when the caller is in `IEmployeeHierarchyClosureRepository.GetAncestorChainEmployeeIdsAsync(employee)`. Otherwise 404, with the same body as not found.
  - Private meetings appear as "Private meeting" with no title or attendees, the D17 rule used by `EmployeeMeetingAttendanceQuery`.
- **Feature flag:** `Planner:Enabled` (default false). When it is off, every planner endpoint returns 503 `planner_disabled`, and the nav item and profile tab are hidden.
- **New tenant table:** it needs the tenant-isolation RLS policy plus the RLS coverage migration that the architecture tests require. Follow the latest `*RlsPolicyCoverage` migrations.

## Review Focus

1. **A session that starts yesterday and is still open** counts only today's part. Owned by Task 2: `Open_session_from_yesterday_counts_today_only`.
2. **An employee in a timezone ahead of UTC** plans at 08:00 local. "Today" must use their zone, not UTC. Owned by Task 3: `Today_uses_the_employee_zone`.
3. **A manager two levels up** can view; a peer cannot, and gets 404. Owned by Task 4: `Ancestor_can_view_peer_gets_404`.
4. **A task unassigned from the employee after it was planned** still shows its block, with a "no longer assigned" flag. New blocks for it are refused. Owned by Task 3: `Unassigned_task_keeps_its_block_but_cannot_be_planned_again`.
5. **The planner page on a past date** shows no drag handles and no edit controls. Owned by Task 6: `past_day_is_read_only`.

---

### Task 1: Table, entity, flag

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Planner/Entities/PlannedTaskBlock.cs` (`Id, TenantId, EmployeeId, TaskId, StartAt, EndAt, CreatedAt, UpdatedAt`, `ITenantOwnedEntity`).
- Create: EF configuration with:
  - FKs to employees and tasks (composite tenant FKs, like the meeting tables);
  - an index on `(tenant_id, employee_id, start_at)`.
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/PlannerOptions.cs` (`Enabled`), bound in DI.
- Create: migrations `AddPlannedTaskBlocks` and `AddPlannedTaskBlocksRlsPolicyCoverage`.
- Test: an architecture test, plus `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerSchemaIntegrationTests.cs`.

- [ ] **Step 1: Failing tests.**
  - `PlannedTaskBlocks_HasForcedRowLevelSecurity`.
  - `A_tenant_sees_only_its_blocks`: same pattern as `MeetingSchemaIntegrationTests.ATenantContextSeesOnlyItsOwnMeetingRows`.
- [ ] **Step 2: Run. Expect FAIL** (the table is missing).
- [ ] **Step 3: Implement** the entity, configuration, options and both migrations.
- [ ] **Step 4: Run the new tests and `dotnet test tests/ONEVO.Tests.Architecture`. Expect PASS.**
- [ ] **Step 5: Commit** `feat(planner): planned task blocks table`

### Task 2: Day composition (actuals)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerActuals.cs` (pure functions).
- Create: `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerDayService.cs`.
- Create: repository `IPlannedTaskBlockRepository` + `EfPlannedTaskBlockRepository`.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerActualsTests.cs`.

**Interfaces:**
- `PlannerActuals.TaskMinutes(IEnumerable<(DateTimeOffset In, DateTimeOffset? Out)> sessions, DateTimeOffset dayStart, DateTimeOffset dayEnd, DateTimeOffset now) -> int`
- `PlannerActuals.MeetingMinutes(int durationMinutes, bool declined, bool cancelled, bool ended) -> int`
- `PlannerDayService.GetDayAsync(Guid tenantId, Guid employeeId, DateOnly date, bool maskPrivate, CancellationToken ct) -> PlannerDayResponse`
- `PlannerDayResponse(DateOnly Date, string Timezone, IReadOnlyList<PlannerBlockView> Blocks, IReadOnlyList<PlannerMeetingView> Meetings, IReadOnlyList<PlannerDueTaskView> DueTasks, PlannerTotals Totals, decimal AttendanceClockedHours, bool Editable)`
  - `Editable` = the date is today and the caller is the employee.
- Meetings come from the employee's occurrences for the day. Reuse the C2 source `TaskMeetingOccurrenceSource` / `IEmployeeMeetingOccurrenceSource`.
- Due tasks: assigned, not done, `due_date <= date`, not meetings.

- [ ] **Step 1: Failing unit tests:**
  - `Closed_session_inside_the_day`.
  - `Open_session_from_yesterday_counts_today_only`.
  - `Open_session_today_counts_until_now`.
  - `Declined_or_cancelled_meeting_is_zero`.
  - `Meeting_not_ended_is_zero`.
- [ ] **Step 2: Run. Expect FAIL.**
- [ ] **Step 3: Implement** `PlannerActuals`, then `PlannerDayService` using the repositories.
- [ ] **Step 4: Run. Expect PASS.**
- [ ] **Step 5: Commit** `feat(planner): compose a day with planned and actual hours`

### Task 3: Own-plan API (read and today-only writes)

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs`:
  - `GET api/v1/planner/me?date=`;
  - `POST api/v1/planner/me/blocks` `{ taskId, startAt, endAt }`;
  - `PATCH api/v1/planner/me/blocks/{id}` `{ startAt, endAt }`;
  - `DELETE api/v1/planner/me/blocks/{id}`.
- Create: MediatR queries and commands under `Features/WorkManagement/Planner/`, and the error codes `PlannerErrorCodes` (`planner_disabled`, `planner_not_today`, `planner_block_overlap`, `planner_task_not_plannable`, `planner_block_not_found`).
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerOwnPlanIntegrationTests.cs`.

- [ ] **Step 1: Failing tests:**
  - `Plan_a_task_today_and_read_it_back` (the totals include the block's minutes).
  - `Today_uses_the_employee_zone` (Asia/Colombo, a block at 08:00 local).
  - `Yesterday_or_tomorrow_is_refused`.
  - `Overlapping_block_is_409`.
  - `Meeting_or_unassigned_task_is_not_plannable`.
  - `Unassigned_task_keeps_its_block_but_cannot_be_planned_again`.
  - `Planner_disabled_is_503`.
  - `Another_employees_block_is_404_on_patch`.
- [ ] **Step 2: Run. Expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run. Expect PASS.**
- [ ] **Step 5: Commit** `feat(planner): plan today's task blocks`

### Task 4: Manager view and leak coverage

**Files:**
- Modify: `PlannerController` with `GET api/v1/planner/employees/{employeeId}?date=` (read-only, `maskPrivate: true`, `Editable: false`).
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerManagerViewIntegrationTests.cs`.
- Modify: `docs/api/planner-v1.md` (new): the endpoints, codes and rules above.

- [ ] **Step 1: Failing tests:**
  - `Ancestor_can_view_peer_gets_404`.
  - `Private_meeting_is_masked_for_the_manager`.
  - `Self_through_the_employee_route_is_allowed_but_read_only`.
  - `Admin_without_reporting_line_gets_404` (no `*` bypass, same as the meeting tiers).
- [ ] **Step 2: Run. Expect FAIL.**
- [ ] **Step 3: Implement**, and write the doc.
- [ ] **Step 4: Run the planner integration tests and the architecture tests. Expect PASS.**
- [ ] **Step 5: Commit** `feat(planner): reporting-line managers view an employee's day`

### Task 5 (frontend): API service and models

**Files** (frontend repo, `src/app/modules/work/planner/`):
- Create: `models/planner.models.ts` (mirrors `PlannerDayResponse` in camelCase).
- Create: `data-access/planner-api.service.ts` (+ spec): `getMyDay(date)`, `getEmployeeDay(employeeId, date)`, `createBlock`, `moveBlock`, `deleteBlock`. Writes set `SILENT_HTTP_ERRORS`, like `MeetingApiService`.
- Create: `utils/planner-error-messages.ts` (+ spec), with copy for each `PlannerErrorCodes` value.

- [ ] **Step 1: Failing spec:** each method calls its route and verb; writes are silent and reads are not.
- [ ] **Step 2: Run. Expect FAIL.**
  - Run: `npx ng test --include='src/app/modules/work/planner/**/*.spec.ts'`
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run. Expect PASS.**
- [ ] **Step 5: Commit** `feat(planner): API service and models`

### Task 6 (frontend): Planner page

**Files:**
- Create: `feature/planner-page/` (+ spec), routed at `/planner` (lazy, in `app.routes.ts`).
- Modify: `layouts/main-layout/nav/nav-items.config.ts` with `{ id: 'planner', label: 'Planner', icon: 'calendar', path: '/planner' }`, hidden when the API answers 503 `planner_disabled`. Use the same availability pattern as `MeetingsAvailabilityService`.

Layout:
- A day timeline from 06:00 to 22:00 in the employee's zone, showing meetings (read-only, clickable to the occurrence drawer) and blocks.
- A side list of due or overdue tasks; dragging one onto the timeline creates a block.
- Block edges resize; drag moves; ✕ deletes.
- Each row shows planned and actual. The day header shows planned vs actual, plus attendance clocked hours.
- A block that overlaps a meeting gets a conflict marker.
- A date control can go back only. Past days are read-only.

- [ ] **Step 1: Failing spec tests:**
  - `renders meetings and blocks on the timeline`.
  - `dropping a due task creates a block at the drop time`.
  - `shows planned vs actual per row and for the day`.
  - `marks a block that overlaps a meeting`.
  - `past_day_is_read_only`.
  - `overlap error shows the planner_block_overlap copy and keeps the drag undone`.
- [ ] **Step 2: Run. Expect FAIL.**
- [ ] **Step 3: Implement.** Use CDK drag-drop and signals; reuse `MeetingOccurrenceDrawerComponent` for meeting clicks.
- [ ] **Step 4: Run the planner specs, the full suite and `node scripts/check-no-warnings.mjs`. Expect PASS and clean.**
- [ ] **Step 5: Commit** `feat(planner): plan today on a timeline`

### Task 7 (frontend): Manager's "Today's plan" tab

**Files:**
- Create: `modules/people/feature/employee-detail/todays-plan-tab/` (+ spec).
- Modify: `employee-detail.component.ts/html`. The tab starts hidden; the first `getEmployeeDay` decides whether it shows, the same pattern as `meetingTabAvailable`. Reset it when the route's employee changes.

- [ ] **Step 1: Failing spec tests:**
  - `shows the tab only when the day loads`.
  - `is read-only and masks private meetings`.
  - `resets when navigating to another employee`.
- [ ] **Step 2: Run. Expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the specs, the full suite and check-no-warnings. Expect PASS.**
- [ ] **Step 5: Commit** `feat(planner): today's plan on the employee profile`
