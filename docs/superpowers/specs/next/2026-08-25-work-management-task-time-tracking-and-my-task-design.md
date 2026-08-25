# Work Management: Task Clock-in/Push Time Tracking, Edit/Status History, and My Task page (backend) — design

**Status:** next. **Depends on:** cascading Objective ownership
(`2026-08-21-work-management-cascading-objective-ownership-design.md`) for `IsEffectiveManagerAsync`, and
the existing `TaskEditRequest`/`EditTask`/`MoveTaskStatus` machinery below, which this feature extends
rather than replaces.

## 1. What this is

Three related additions to the Work Management Task domain, built in this order:

- **A. Task edit history + status-change history** — every applied change to a `WorkTask` (direct edit,
  approved edit-request, or a status move) gets an audit row, readable by any project member on the task
  detail page.
- **B. Per-task Clock-in/Push time tracking** — an employee can clock into a task, work, then "Push" to log
  a session's duration and report a new completion percentage. Multiple clock-in/push cycles accumulate
  total logged time per task.
- **C. `GET .../my-tasks`** — the read endpoint backing the new frontend "My Task" page (routing/UI is the
  frontend companion spec). Board/List view switching, filters, and rendering are entirely frontend
  concerns; this spec only defines the query.

## 2. Existing building blocks (reuse, do not reimplement)

- `WorkTask.ProgressPercent` (int, already on the entity) is the single stored completion percentage — **no
  new column for this.** This spec adds tables that *log changes to* `ProgressPercent`, not a new percentage
  field.
- `TaskEditRequest` (`Domain/Features/WorkManagement/Tasks/Entities/TaskEditRequest.cs`) is the existing
  non-owner-edit approval workflow (owner/effective-manager edits directly via `EditTaskCommand`; non-owner
  submits `CreateTaskEditRequestCommand`, decided by `ApproveTaskEditRequestCommand`/
  `RejectTaskEditRequestCommand`). This spec **extends its payload**, does not fork a parallel workflow.
- `MoveTaskStatusCommandHandler` **already mutates `ProgressPercent`** as a side effect: moving into a
  status with `MarksTaskComplete = true` sets `ProgressPercent = 100` (and `CompletedHours`/`CompletedAt`);
  moving out resets it to `0`. This is a **third source of percentage change** alongside Push and manual
  edit — §5 covers how it folds into the same log and lock rule without duplicating that handler's logic.
- `AssignTaskCommand`/`UnassignTaskCommand` already manage the task-assignee join backing
  `WorkTaskResponse.AssigneeEmployeeIds` — the My Task query (§7) filters on this, no new assignment
  concept needed.
- `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync` is the existing "owner or cascaded
  owner/member" check — reused for both manual-edit authorization (already wired into `EditTaskCommand`'s
  caller-is-owner path) and for who may decide a `TaskEditRequest`.

## 3. Data model (new tables)

```
TaskEditLog
  Id                Guid PK
  TenantId          Guid
  TaskId            Guid FK -> WorkTask
  EmployeeId        Guid (Employee) — whose edit this represents (requester for an approved
                     request, direct editor otherwise)
  Source            string: "direct" | "approved_request"
  EditRequestId     Guid? FK -> TaskEditRequest, set only when Source = "approved_request"
  OldValuesJson     string — snapshot of the changed fields before
  NewValuesJson     string — snapshot of the changed fields after
  Reason            string?, nullable, optional free-text set at edit time
  ChangedAt         DateTimeOffset

TaskStatusChangeLog
  Id                Guid PK
  TenantId          Guid
  TaskId            Guid FK -> WorkTask
  EmployeeId        Guid (Employee) — who moved the status
  FromStatusId      Guid FK -> TaskStatus
  ToStatusId        Guid FK -> TaskStatus
  ChangedAt         DateTimeOffset

TaskClockingSession
  Id                Guid PK
  TenantId          Guid
  TaskId            Guid FK -> WorkTask
  EmployeeId        Guid (Employee)
  ClockInAt         DateTimeOffset
  ClockOutAt         DateTimeOffset?, null while the session is open
  DurationMinutes   int?, computed and stored at Push time (null while open)
  Reason            string?, nullable, addable after the fact via a small PATCH
  CreatedAt/UpdatedAt per BaseEntity

TaskPercentageLog
  Id                Guid PK
  TenantId          Guid
  TaskId            Guid FK -> WorkTask
  EmployeeId        Guid (Employee) — who caused this percentage change
  PreviousPercent   int
  NewPercent        int
  Source            string: "push" | "manual_edit" | "status_change"
  ClockingSessionId Guid? FK -> TaskClockingSession, set only when Source = "push"
  Reason            string?, nullable, addable after the fact via a small PATCH
  ChangedAt         DateTimeOffset
```

**Invariant enforced at the application layer** (same convention as this module's other join/state
invariants, e.g. the calendar spec's one-active-event membership rule — not a DB constraint except where
noted): a task may have at most one **open** `TaskClockingSession` (`ClockOutAt IS NULL`) at a time,
regardless of employee. **This one IS worth a partial unique index** —
`CREATE UNIQUE INDEX ... ON task_clocking_sessions (task_id) WHERE clock_out_at IS NULL` — because two
concurrent clock-in requests racing past an application-layer check is a realistic failure mode here
(two assignees clicking "Clock In" within the same second), unlike the calendar's event-membership case
which only matters at human editing speed.

`TaskEditRequestPayload` gains one field: `ProgressPercent` (`int?`, nullable, defaults to no-op like the
other optional fields already on that record).

**Migration:** one new EF migration adding all four tables plus the `TaskEditRequestPayload` shape change
(payload is a JSON blob, not a column — no migration needed for that part, just update the DTO and its
`CreateTaskEditRequestCommandValidator`). Follow `20260823172054_AddTaskCategories.cs` as the closest
precedent for a batch of new tenant-scoped tables. **Do not apply this migration** — write it, dry-run
validate with `BEGIN...ROLLBACK`, commit the code, then stop and tell the user the exact command to run
themselves, per this project's standing rule.

## 4. Clock-in / Push state machine

- **Clock In** (`POST /tasks/{id}/clock-in`): caller must be in the task's `AssigneeEmployeeIds`. Reject
  409 if the task already has an open `TaskClockingSession` (any employee) — the partial unique index is
  the backstop, but check-then-insert first for a clean error message. Reject 409 if
  `task.ProgressPercent == 100` (task is locked — see below). Creates the open session row.
- **Push** (`POST /tasks/{id}/push`, body `{percent: int, reason?: string}`): caller must own the task's
  currently-open session (reject 404/409 if there isn't one, or it belongs to someone else — a push can
  only close the session the same caller opened). Validate `percent > task.ProgressPercent` (strictly
  greater — reject 400 otherwise, this is the one rule the user was explicit about). In one transaction:
  - Close the session: `ClockOutAt = now`, `DurationMinutes = (ClockOutAt - ClockInAt)`.
  - Write a `TaskPercentageLog` row, `Source = "push"`, `ClockingSessionId` = this session's `Id`,
    `PreviousPercent`/`NewPercent` from the task's old/new value.
  - Update `task.ProgressPercent = percent`.
  - If `percent == 100`: this is the lock condition (see below) — no separate "auto clock-out" action
    needed beyond closing the session that was already being pushed.
- **Lock rule**: `task.ProgressPercent == 100` blocks `POST .../clock-in` (409, message: "This task is
  complete — reduce its percentage before clocking in again"). It does **not** block a manual edit that
  reduces the percentage — that's the only unlock path (§5), and it does not block `MoveTaskStatus` from
  moving the task to a non-complete status, which resets `ProgressPercent` to 0 as an existing side effect
  and therefore *also* unlocks clocking (§5 covers logging that transition too).
- **Multiple sessions accumulate**: nothing deletes or merges old closed `TaskClockingSession` rows —
  total logged time for a task is `SUM(DurationMinutes)` across all its sessions, computed by the history
  read endpoint (§6), not stored as a running total on `WorkTask` (avoids a second source of truth to keep
  in sync).

## 5. Manual percentage edit, and folding in the existing status-driven flip

**Manual edit** (owner/effective-manager direct via `EditTaskCommand`, or non-owner via
`TaskEditRequest`→approval) may set `ProgressPercent` to any value, up or down — this is the one supported
way to reduce a locked (100%) task's percentage and re-enable clocking. When either path actually writes
`task.ProgressPercent` to a new value, write a `TaskPercentageLog` row with `Source = "manual_edit"`,
`ClockingSessionId = null`, `EmployeeId` = the direct editor, or the *requester* for an approved-request
path (matches §3's `TaskEditLog.EmployeeId` convention — the log attributes changes to whoever's edit it
conceptually was, not necessarily whoever clicked Approve).

**`MoveTaskStatusCommandHandler`'s existing 0/100 flip** (§2) is a third, pre-existing way
`ProgressPercent` changes. Do not change that handler's behavior. Add one thing to it: when it changes
`ProgressPercent`, write a `TaskPercentageLog` row, `Source = "status_change"`, `ClockingSessionId = null`,
`EmployeeId` = the caller moving the status. This means a task marked complete via status move is
immediately locked from clocking (percentage is 100, same as a 100% Push), and moving it back out
immediately unlocks it (percentage resets to 0) — no special-casing needed in the clock-in/push handlers,
they only ever look at the current stored `ProgressPercent`.

**`TaskEditLog` writes** happen in `EditTaskCommandHandler` (`Source = "direct"`) and
`ApproveTaskEditRequestCommandHandler` (`Source = "approved_request"`, `EditRequestId` set) — both already
know the before/after field values at the point they call `SaveChangesAsync`; snapshot `Title`,
`Description`, `Priority`, `DueDate`, `EstimatedHours`, `StoryPoints`, and now `ProgressPercent` into
`OldValuesJson`/`NewValuesJson` (only fields that actually changed — an edit that leaves a field untouched
shouldn't clutter the diff). `Reason` is an optional new field on both `EditTaskRequest` and the
`TaskEditRequestPayload`/`CreateTaskEditRequestCommand`.

**`TaskStatusChangeLog` writes** happen in `MoveTaskStatusCommandHandler`, alongside the
`TaskPercentageLog` write above when applicable — always writes (status moves always change `StatusId`),
whereas the percentage log write is conditional (only when `MarksTaskComplete` actually flips).

## 6. History read endpoint

`GET /tasks/{id}/history` — merges all four logs plus (for context) the two request tables
(`TaskEditRequest`, filtered to `Approved`/`Rejected` — pending ones already show up in the Approvals
page and aren't "history" yet) into one time-sorted feed for the task detail page. Each entry is a
discriminated-union-shaped DTO (`type: "edit" | "status_change" | "clock_session" | "percentage_change"`)
carrying just enough to render one timeline row — resolve `EmployeeId`s to display names the same way
`GetMyProjectMilestonesQueryHandler`/calendar's handler already do (`ICallerIdentityResolver` batch
resolve, not N+1). `[RequirePermission("projects:access")]`, visible to any project member — this is
explicitly **not** owner-gated, per the requirement that all users can see who edited what.

The `TaskPercentageLog.ClockingSessionId` link is how the frontend correlates "this percentage change
happened during this clock session" (render them as one combined entry) vs. a standalone manual edit or
status-driven change (render separately) — the read endpoint should nest the matching `TaskClockingSession`
inline on a `push`-sourced percentage entry rather than making the frontend join two feed entries itself.

## 7. `GET .../my-tasks` (My Task page backend)

`GET /api/v1/work/projects/{projectId:guid}/my-tasks?sprintId={guid?}`, `[RequirePermission
("projects:access")]`. Near-copy of `GetProjectTasksQuery`'s handler shape, with two differences:

- Filters to tasks where the caller's resolved `EmployeeId` is in the task's assignee set (reuse whatever
  join `AssignTaskCommand` already writes to — do not add a second assignee concept).
- Default sort: `DueDate ascending NULLS LAST`, then `Priority descending`
  (`critical` > `high` > `medium` > `low` — this module already has that ordering implicit in
  `WorkTaskPriorities`, formalize it as an explicit `CASE` or a small priority-rank lookup rather than
  relying on string sort order) — nearest deadline first, ties broken by higher priority first.
- Optional `sprintId` query param filters to one sprint; omitted means all sprints (matches the "All
  Sprints" vs. one-sprint filter from the requirement — the frontend's task-name search filter is
  client-side over this same result set, no new query param needed for it).

Response reuses `WorkTaskResponse` unchanged (already carries everything the frontend needs; the new
`ProgressPercent`/history data comes from §6 when a task is opened, not from this list endpoint).

## 8. Explicitly out of scope

- `RequestAllocationExtension`'s manual-trigger gap (currently only reachable automatically from a slack
  conflict at task/objective creation) — flagged during brainstorming, tracked as a separate follow-up,
  not part of this plan.
- A project-wide (not "mine") flat task list — the sub-menu's separate "List" tab is being removed/merged
  into My Task's internal view toggle per the approved design, not built as its own thing.
- Any change to `TimeAttendance`'s employee-level Clock In/Out (org-wide attendance) — unrelated domain,
  do not touch `ClockInPolicies`/`EfClockInPolicyRepository`/etc.

## 9. Testing

Unit-test the state machine directly: open-session uniqueness (409 on double clock-in), push rejects
`percent <= current`, push at exactly 100 locks (`clock-in` then 409s), manual edit below 100 unlocks,
`MoveTaskStatus`'s existing 0/100 flip now also produces a `TaskPercentageLog` row (regression-guard this
with an assertion added to `MoveTaskStatusCommandHandlerTests`, not a new file). Integration-test the full
Clock-in → Push → history-read round trip against the real DB, matching this module's existing repository
test pattern (e.g. `EfSprintRepositoryTests`). Note: `TimeTrackingMutationArchitectureTests` is pinned
specifically to the unrelated `TimeTrackingController` (TimeAttendance module, org-wide attendance) — it is
not a generic WM convention and does not apply here; do not treat it as a template. Check
`tests/ONEVO.Tests.Architecture/` for whichever test(s) actually assert generic WM controller/permission
conventions (e.g. every `[RequirePermission]` shape on `TasksController`) before assuming none apply to the
new clock-in/push/history endpoints.
