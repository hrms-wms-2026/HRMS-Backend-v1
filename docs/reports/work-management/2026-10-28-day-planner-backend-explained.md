# Day Planner — Backend Explained

**Written:** 2026-10-28, from the code on `chore/uat-hardening` after pulling `origin/development` (d9d172fb).
**Built by:** Dapiyshanth-19 (commits `2779ccbb` … `418bff5b`, 2026-10-26).
**Related docs:**
- API contract: `docs/api/planner-v1.md`
- Design: `docs/superpowers/specs/2026-10-26-meeting-hours-and-day-planner-design.md` (Part B)
- Plan: `docs/superpowers/plans/2026-10-26-day-planner-part-B.md`

> **Heads-up:** a newer version (v1.1, "Planner = Calendar") is on `origin/feature/planner-calendar-merge` and is **not** in
> `development` yet. This document explains what is in `development` now (v1). The last section lists what v1.1 changes.

---

## 1. What the Planner does (in one minute)

The Planner is a "my day" screen for an employee:

1. **Planned task blocks:** the employee drags one of their tasks into a time slot today, for example "Write report, 10:00–12:00".
   This is only a plan. It **never** changes the task's due date, estimate or status.
2. **Meetings** of that day appear automatically. These are the Phase 2 meeting tasks the employee organizes or attends.
3. **Due tasks:** a side list of their tasks that are due today or overdue, ready to drag in.
4. **Planned vs actual:** for each block, each meeting and the whole day.
   - Planned = the block length or the meeting length.
   - Actual = clocked minutes on the task (task clock-in), or the meeting's length once it has ended.
5. **Attendance minutes:** the attendance clock for the day, shown beside it for reference.

Who can do what:

| Who | Can |
|---|---|
| The employee | View any past day or today. Create, move and delete blocks **today only**. |
| Their manager (anyone above them in the reporting line) | View the employee's today and past days, read-only. Private meeting titles are hidden as "Private meeting". |
| Anyone else | Gets 404, the same as "does not exist". |

**Feature flag:** `Planner:Enabled` (default **false**). While it is off, every endpoint returns **503 `planner_disabled`**.
It is not in `appsettings.json`, so to try it locally add:

```json
"Planner": { "Enabled": true }
```

---

## 2. Files and layers

The code follows the usual Clean Architecture layers (Api → Application → Domain ← Infrastructure).

| Layer | File | Job |
|---|---|---|
| Api | `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs` | 5 endpoints. Sends MediatR requests and turns a failed `Result` into `ProblemDetails` with a `code`. |
| Application | `Features/WorkManagement/Planner/Commands/PlannerCommands.cs` | The 5 MediatR request records plus **one** handler, `PlannerRequestHandler`, which finds the caller's EmployeeId and calls `PlannerService`. |
| Application | `Features/WorkManagement/Planner/Contracts/PlannerContracts.cs` | Request and response DTOs, and the error codes. |
| Application | `Features/WorkManagement/Planner/Services/PlannerService.cs` | **All business rules:** access, timezone, "today only", overlap, composing the day. |
| Application | `Features/WorkManagement/Planner/Services/PlannerActuals.cs` | Pure maths for actual minutes (task sessions and meetings). Unit tested. |
| Application | `Features/WorkManagement/Planner/PlannerOptions.cs` | The `Planner:Enabled` flag. |
| Application | `Features/WorkManagement/Planner/RepositoryInterfaces/IPlannerRepository.cs` | The repository interface plus small read models (`PlannerTaskInfo`, `PlannerSession`, `PlannerAttendance`). |
| Domain | `src/ONEVO.Domain/Features/WorkManagement/Planner/Entities/PlannedTaskBlock.cs` | The only new entity. |
| Infrastructure | `Persistence/Configurations/WorkManagement/Planner/PlannedTaskBlockConfiguration.cs` | EF mapping: table, check constraint, index, foreign keys. |
| Infrastructure | `Persistence/Repositories/WorkManagement/Planner/EfPlannerRepository.cs` | EF queries plus the Postgres advisory lock. |
| Infrastructure | `Migrations/20261025224930_AddPlannedTaskBlocks.cs` | Creates the table. |
| Infrastructure | `Migrations/20261025224945_AddPlannedTaskBlocksRlsPolicyCoverage.cs` | Row-level security (tenant isolation) policy. |
| Infrastructure | `DependencyInjection.cs` (lines ~426–459) | Binds `PlannerOptions` and registers `IPlannerRepository → EfPlannerRepository` and `PlannerService` (scoped). |
| Shared (Meetings) | `Meetings/RepositoryInterfaces/IMeetingRepository.cs` → `ListEmployeeDayMeetingsAsync` and the `EmployeeDayMeeting` record | Reads the day's meetings for the Planner. Implemented in `EfMeetingRepository.cs`. |

Tests:
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerActualsTests.cs`
- `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerSchemaIntegrationTests.cs`
- `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerServiceIntegrationTests.cs`

---

## 3. Tables

### 3.1 The new table: `planned_task_blocks`

One row is one task placed in one time slot of one employee's day.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK `pk_planned_task_blocks` |
| `tenant_id` | uuid | no | From `BaseEntity`; RLS uses it |
| `employee_id` | uuid | no | FK → `employees.id`, **ON DELETE CASCADE** |
| `task_id` | uuid | no | FK → `tasks.id` (`WorkTask`), **ON DELETE CASCADE** |
| `start_at` | timestamptz | no | Stored in UTC |
| `end_at` | timestamptz | no | Stored in UTC |
| `created_at` | timestamptz | no | |
| `updated_at` | timestamptz | yes | Set on move |
| `created_by_id` | uuid | no | The **employee** id of the caller (not the user id) |
| `is_deleted` | boolean | no | Soft delete (see below) |
| `deleted_at` | timestamptz | yes | |

Constraints and indexes:
- `ck_planned_task_blocks_end_after_start`: `end_at > start_at`.
- `ix_planned_task_blocks_tenant_id_employee_id_start_at`: the main lookup, "this employee's blocks in this time range".
- `ix_planned_task_blocks_employee_id` and `ix_planned_task_blocks_task_id`: the FK indexes.
- **RLS policy `tenant_isolation`**, enabled and forced. A row is visible only when `app.current_tenant_id` matches, or in admin mode.

What is **not** in the database (it is checked in code instead):
- "Blocks don't overlap" is checked in `PlannerService` under an advisory lock. There is no exclusion constraint.
- "At least 15 minutes" and "today only" are also checked in code.

**Soft delete:** `DeleteBlock` calls `_db.PlannedTaskBlocks.Remove(...)`, but the shared `SoftDeleteInterceptor` turns every
`BaseEntity` delete into `is_deleted = true, deleted_at = now`. That is why every planner query filters `!b.IsDeleted`.

### 3.2 Existing tables the Planner reads (it never writes them)

| Table / DbSet | Read for | Where |
|---|---|---|
| `tasks` (`WorkTasks`) | Task title, due date, estimate, deleted flag | `GetTasksAsync`, `ListDueAssignedTasksAsync` |
| `task_assignments` (`TaskAssignments`) | "Is this task assigned to this employee?" | Same two methods |
| `task_statuses` (`TaskStatuses`) | `MarksTaskComplete`, to leave done tasks out of the due list | `ListDueAssignedTasksAsync` |
| `task_clocking_sessions` (`TaskClockingSessions`) | Clock-in and clock-out per task, for **actual** minutes | `ListSessionsAsync` |
| `attendance_records` (`AttendanceRecords`) | `ActualStart`, `ActualEnd`, `WorkedMinutes`, `BreakMinutes` for the day | `GetAttendanceAsync` |
| `meeting_details` + `meeting_series` + meeting `tasks` | The day's meeting occurrences | `IMeetingRepository.ListEmployeeDayMeetingsAsync` |
| `calendar_event_participants` | Series attendees and their RSVP (`ResponseStatus`), for "declined" | Same |
| `meeting_occurrence_attendees` | Per-occurrence add or remove overrides and RSVP | Same |
| `employees` + reporting-line closure | Manager check (`ListAncestorChainEmployeeIdsAsync`) and the employee's `UserId` | `IEmployeeRepository` |
| Employee `DisplayTimezone` / legal entity `Timezone` | The employee's timezone | `ICalendarTimezoneResolver.ResolveForUserAsync` |

### 3.3 How the tables relate

```
employees ─┬─< planned_task_blocks >── tasks ──< task_assignments >── employees
           │                             │
           │                             ├──< task_clocking_sessions   (actual minutes)
           │                             └── task_statuses             (done or not)
           │
           ├──< attendance_records                       (attendanceMinutes)
           │
           └── meetings: meeting_series ──< meeting_details (1 row per occurrence, TaskId → tasks)
                         calendar_event_participants  (series attendee + RSVP)
                         meeting_occurrence_attendees (per-occurrence add/remove + RSVP)
```

Things to know:
- A block points at a **task**, not at an assignment. If the task is unassigned later, the block **stays**, and the response
  marks it `stillAssigned: false`.
- Deleting the employee or the task (a hard DB delete) cascades to the blocks. A soft-deleted task is filtered out on read.
- Meetings are tasks too (meetings-as-tasks Phase 2). The default query filter on `WorkTasks` hides meeting tasks, which is
  why the planner's task reads never return meetings. The meeting repository deliberately uses
  `IgnoreQueryFilters(ExcludeMeetingsOnly)` to read them.

---

## 4. Models and DTOs

### 4.1 Domain entity

```csharp
public class PlannedTaskBlock : BaseEntity   // Id, TenantId, CreatedAt, UpdatedAt, CreatedById, IsDeleted, DeletedAt
{
    public Guid EmployeeId { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
}
```

### 4.2 Request DTOs (from the client)

| DTO | Fields | Used by |
|---|---|---|
| `CreatePlannedBlockRequest` | `TaskId`, `StartAt`, `EndAt` | `POST /planner/me/blocks` |
| `MovePlannedBlockRequest` | `StartAt`, `EndAt` | `PATCH /planner/me/blocks/{blockId}` |

### 4.3 MediatR requests (Application)

| Request | Built from |
|---|---|
| `GetMyPlannerDayQuery(DateOnly? Date)` | `GET /me?date=` |
| `GetEmployeePlannerDayQuery(Guid EmployeeId, DateOnly? Date)` | `GET /employees/{id}?date=` |
| `CreatePlannedBlockCommand(CreatePlannedBlockRequest)` | `POST /me/blocks` |
| `MovePlannedBlockCommand(Guid BlockId, MovePlannedBlockRequest)` | `PATCH /me/blocks/{id}` |
| `DeletePlannedBlockCommand(Guid BlockId)` | `DELETE /me/blocks/{id}` |

All five return `Result<PlannerDayResponse>`, and all five are handled by **one** class, `PlannerRequestHandler`.

### 4.4 Response DTOs (to the client)

**`PlannerDayResponse`**, returned by every endpoint:

| Field | Type | Meaning |
|---|---|---|
| `Date` | DateOnly | The day shown |
| `Timezone` | string | The zone the day was computed in (IANA id, or "UTC" as a fallback) |
| `Editable` | bool | True only for **your own today** |
| `Blocks` | `PlannerBlockView[]` | Planned blocks, ordered by start |
| `Meetings` | `PlannerMeetingView[]` | The day's meetings |
| `DueTasks` | `PlannerDueTaskView[]` | Assigned tasks that are open and due on or before that day |
| `Totals` | `PlannerTotals` | `PlannedMinutes`, `ActualMinutes` for the day |
| `AttendanceMinutes` | int | The attendance clock for the day |

**`PlannerBlockView`:** `Id, TaskId, TaskTitle, StartAt, EndAt, PlannedMinutes, ActualMinutes, StillAssigned, OverlapsMeeting`
- `ActualMinutes` is the task's clocked minutes for the **whole day**. Two blocks of the same task show the same number.
- `OverlapsMeeting` is true when the block clashes with a meeting the employee is going to (not cancelled, not declined, not all-day).
  It is only a warning; the save is still allowed.

**`PlannerMeetingView`:** `TaskId, SeriesId, OriginalStart, StartAt, EndAt, Title, IsPrivate, Cancelled, Declined, PlannedMinutes, ActualMinutes`
- `Title` is `"Private meeting"` when a manager views someone else's private meeting.

**`PlannerDueTaskView`:** `TaskId, Title, DueDate, EstimatedHours`

**`PlannerTotals`:** `PlannedMinutes, ActualMinutes`

### 4.5 Internal read models (repository → service; not sent to the client)

| Record | Fields | From |
|---|---|---|
| `PlannerTaskInfo` | `TaskId, Title, DueDate, EstimatedHours, IsMeeting, AssignedToEmployee` | `tasks` + `task_assignments` |
| `PlannerSession` | `TaskId, ClockInAt, ClockOutAt?` | `task_clocking_sessions` |
| `PlannerAttendance` | `ActualStart?, ActualEnd?, WorkedMinutes, BreakMinutes` | `attendance_records` |
| `EmployeeDayMeeting` | `TaskId, SeriesId, OriginalStart, StartAt, EndAt, IsAllDay, Title, IsPrivate, Cancelled, Declined` | Meeting tables (lives in the Meetings feature) |

### 4.6 Error codes (`PlannerErrorCodes`, sent as `ProblemDetails.extensions.code`)

| HTTP | Code | When |
|---|---|---|
| 503 | `planner_disabled` | The flag is off |
| 400 | `planner_not_today` | Asking for a future date, or writing a block that is not on today |
| 400 | `planner_invalid_time` | The block is shorter than 15 minutes, or ends before it starts |
| 400 | `planner_task_not_plannable` | The task is missing, deleted, a meeting, or not assigned to you |
| 409 | `planner_block_overlap` | It overlaps another of your blocks |
| 404 | `planner_not_found` | The block or day doesn't exist **or** you can't see it (same answer on purpose) |

---

## 5. How a request flows

```
HTTP ─► PlannerController ─► MediatR ─► PlannerRequestHandler
                                           │  1. ICurrentUser → (TenantId, UserId)
                                           │  2. ICallerIdentityResolver → caller EmployeeId (none → 404)
                                           ▼
                                      PlannerService ──► IPlannerRepository (EfPlannerRepository) ─► Postgres
                                           │         ──► IMeetingRepository.ListEmployeeDayMeetingsAsync
                                           │         ──► IEmployeeRepository (manager chain, UserId)
                                           │         ──► ICalendarTimezoneResolver (zone)
                                           │         ──► IUnitOfWork (transaction + SaveChanges)
                                           ▼
                                      Result<PlannerDayResponse> ─► 200 JSON or ProblemDetails
```

The repository **never** calls `SaveChangesAsync`. `PlannerService` saves through `IUnitOfWork`.

### 5.1 Reading a day: `GET /planner/me` or `GET /planner/employees/{id}`

`PlannerService.GetDayAsync(tenant, caller, employee, date)`:

1. Is the flag off? → 503.
2. **Access:** if caller ≠ employee, the caller must be in `ListAncestorChainEmployeeIdsAsync(employee)` (any manager above them). Otherwise → 404.
3. **Timezone:** employee → `UserId` → `ResolveForUserAsync`. The order is the employee's `DisplayTimezone`, then the legal entity's
   `Timezone`, then "UTC". An unknown zone id falls back to UTC instead of failing.
4. `today` = now converted to that zone. `day = date ?? today`. A future `day` → 400 `planner_not_today`.
5. `ComposeAsync(day, editable: own && day == today, maskPrivate: !own)`. See 5.4.

### 5.2 Creating a block: `POST /planner/me/blocks`

Runs inside `IUnitOfWork.ExecuteInTransactionAsync`:

1. **`LockPlanAsync`**: `SELECT pg_advisory_xact_lock(hashtextextended('planner:{tenant}:{employee}', 0))`.
   This serializes writes **per employee**: if two devices save at once, the second one waits, then sees the first one's block
   in its overlap check. The lock is released automatically at commit or rollback.
2. `CheckWriteAsync`:
   - flag on → otherwise 503
   - zone found → otherwise 404
   - length ≥ 15 minutes → otherwise 400 `planner_invalid_time`
   - `start` and `end − 1 tick` both on **today** in the employee's zone → otherwise 400 `planner_not_today`
   - no overlap with their other non-deleted blocks (`start < other.end && end > other.start`) → otherwise 409
3. Task check: it exists, is not deleted, is not a meeting, and is assigned to the caller → otherwise 400 `planner_task_not_plannable`.
4. Add a `PlannedTaskBlock` with UTC times and `CreatedById = caller EmployeeId`, then `SaveChanges`.
5. Return today's composed day.

### 5.3 Moving and deleting

- **Move** (`PATCH`): flag check → transaction → lock → load **your** block (otherwise 404) → `CheckWriteAsync` with
  `exceptBlockId = this block`, so it doesn't clash with itself → the block's **current** start must also be today (you can't
  pull an old block into today) → update `StartAt`, `EndAt`, `UpdatedAt` → save → return today.
- **Delete** (`DELETE`): flag check → load your block → it must be on today → `Remove` (becomes a soft delete) → save → return today.
  Delete takes no lock and opens no explicit transaction; it doesn't need them, because removing a block can't create an overlap.

### 5.4 Building the day: `ComposeAsync`

Day bounds = local midnight to the next local midnight in the employee's zone, converted to UTC. It then loads, for that window:

| Step | Data | Rule |
|---|---|---|
| 1 | Blocks | Non-deleted blocks overlapping the window, ordered by start |
| 2 | Meetings | `ListEmployeeDayMeetingsAsync`: materialized occurrences where the employee is the organizer, a series participant, or added to that occurrence. Skipped if they were removed from that occurrence. `Declined` = their RSVP is `Rejected` (an organizer never counts as declined). `Cancelled` = the occurrence or its series was cancelled. |
| 3 | Clock sessions | All of the employee's task clocking sessions overlapping the day |
| 4 | Task info | Title and "still assigned" for each block's task |
| 5 | Due tasks | Assigned, not deleted, not a meeting, `due_date ≤ day`, `progress < 100`, and status not `MarksTaskComplete`. Ordered by due date, then title. |
| 6 | Attendance | That date's attendance record |

Then it calculates:

- **Task actual** (`PlannerActuals.TaskMinutes`): for each session of the task, clip it to `[dayStart, min(dayEnd, now)]`.
  An open session (no clock-out) counts up to now. Sum the minutes and round.
- **Meeting planned** (`MinutesInDay`): only the part of the meeting **inside this day**. An all-day meeting = **480 minutes (8 h)**
  per day (`MeetingPersonHours.AllDayMinutesPerDay`). It is 0 if cancelled or declined.
- **Meeting actual** (`PlannerActuals.MeetingMinutes`): the same minutes, but only once `now ≥ EndAt`. 0 if declined or cancelled.
- **Block planned** = `EndAt − StartAt` in minutes.
- **OverlapsMeeting:** does the block clash with any meeting that is not cancelled, not declined and not all-day?
  An all-day meeting covers the whole day, so it is not treated as a clash.
- **Totals:**
  - planned = all block minutes + all meeting planned minutes
  - actual = clocked minutes of each **distinct** task that has a block (so a task with 2 blocks is counted once) + all meeting actual minutes
  - Clocked time on tasks that have **no block** is not counted in the day's actual.
- **AttendanceMinutes:** if clocked in and not yet out → `(now − ActualStart) − BreakMinutes` (never below 0). Otherwise the
  stored `WorkedMinutes`. 0 when there is no record.

---

## 6. Where the Planner meets other modules

| Module | What the Planner uses | Direction |
|---|---|---|
| **Tasks** (WM) | `WorkTask`, `TaskAssignment`, `TaskStatus`, `TaskClockingSession` | Reads only. A block never changes a task. |
| **Meetings** (WM Phase 2) | `IMeetingRepository.ListEmployeeDayMeetingsAsync`, `MeetingStatusCalculator`, `MeetingPersonHours` | Reads only |
| **Meeting hours (Part A)** | Same 8 h all-day rule; meeting actual = its duration once ended | Shared rule. Part A stamps person-hours on the meeting **task** for module roll-ups; the Planner computes its own per-employee view and doesn't read those stamped hours. |
| **Attendance** (HR) | `AttendanceRecords` for the day | Reads only |
| **Core HR / reporting line** | `IEmployeeRepository.ListAncestorChainEmployeeIdsAsync` (the same rule as the D16 attendance view) | Reads only |
| **Calendar** | `ICalendarTimezoneResolver` (the same zone `GET /calendar/my-timezone` returns) | Reads only |
| **Identity** | `ICurrentUser`, `ICallerIdentityResolver` (UserId → EmployeeId) | Reads only |

Nothing else writes to `planned_task_blocks`. No background job, outbox message or notification is involved in v1.

---

## 7. Things worth knowing (gotchas)

1. **The flag is off by default.** A 503 from every endpoint means config, not a bug.
2. **"Today" is the employee's own timezone**, not the server's and not UTC. Around midnight, a block can be "today" for one person and "tomorrow" for another.
3. **A 404 can mean "you're not their manager".** This is deliberate, so outsiders can't probe which employees exist.
4. **No DB constraint against overlap.** It relies on the advisory lock inside a transaction. `LockPlanAsync` throws if it is called without a transaction.
5. **`StillAssigned: false` blocks stay.** Unassigning a task does not delete old blocks.
6. **Clocked time on unplanned tasks** is not counted in the day's actual total.
7. **Meetings that are still virtual** (never materialized as a task row) don't show up. Only materialized occurrences in `meeting_details` are read.
8. **Migrations to run:** `20261025224930_AddPlannedTaskBlocks` and `20261025224945_AddPlannedTaskBlocksRlsPolicyCoverage`
   (run `dotnet ef database update` with the other pulled meeting migrations).

---

## 8. What v1.1 changes (`origin/feature/planner-calendar-merge`, not merged yet)

From the 2026-10-27 commits (`901d1a4d`, `c09bb29f`, `e632bac1`, `f9dc9f88`, `3ae8cbc0`). Same table, no new migration:

- **Plan ahead:** editable from today up to **today + 28 days**. Past days are read-only.
  New errors: `planner_day_read_only`, `planner_beyond_horizon`. `planner_not_today` is no longer returned.
- **A move can go to another day.** The write returns the day the block was written to.
- **`GET /planner/me/range?from=&to=`** (at most 42 days): calendar-grid data per day — blocks with `projectId`, `blockMinutes`,
  and a **work window** from the legal entity schedule (`PlannerWorkWindows.cs`).
- **`GET /planner/me/focus`:** a ranked "what to work on" list in 4 groups — `overdue`, `due_today`, `due_this_week`, `backlog`.
  Each item has remaining, logged and planned-ahead minutes and a `fullyPlanned` flag (`PlannerFocus.cs`).
- `dueTasks` stays in the response for compatibility. The merged Planner page uses `/me/focus` instead.

When that branch merges, re-check sections 4.6, 5.2 and 5.3 of this document.
