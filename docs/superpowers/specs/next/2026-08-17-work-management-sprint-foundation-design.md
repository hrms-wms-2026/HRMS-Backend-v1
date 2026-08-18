# Work Management — Sprint Foundation & Task Status Governance Design

**Status:** Approved (pending final written-spec review) — 2026-08-17
**Companion (deferred, separate spec):** Per-Objective Calendar — depends on Sprint's timeline data
model, designed as a follow-up once this ships. Not covered here.

## Summary

Adds a Sprint subsystem to Work Management (Backlog → create Sprint → tasks belong to a Sprint →
Active Sprint tasks appear on the Board), plus the governance layer Sprint completion depends on:
per-status task visibility (Public/Private), a default status template applied at Objective
creation, an owner-only "Objective Settings" page to customize it, and a rollup of completed task
hours onto the Objective. Also fixes two confirmed authorization gaps found while researching this
(`AssignTaskCommandHandler` and `MoveTaskStatusCommandHandler` have no ownership/membership checks
today) and adds a task-detail popup and create-then-assign UX to the frontend, since neither exists
yet.

## Background — current state (verified by direct code reading, not assumed)

- **No Sprint concept exists anywhere in the codebase** — confirmed zero hits for "sprint"
  (case-insensitive) across both repos' Work Management-scoped folders. A tentative 5-table Sprint
  Planning design exists only in `docs/superpowers/project_ core/phase1-table-inventory.md` (never
  implemented) — its `sprints.objective_id` is nullable there; this design makes it required instead,
  matching how `WorkTask` already denormalizes both `ProjectId` and `ObjectiveId`. Its
  `sprint_daily_snapshots`/`sprint_reports`/`sprint_report_contributors` tables (burndown charts,
  velocity reports) are explicitly **out of scope** — nothing in this spec asked for reporting/
  analytics, and building it now would be scope creep.
- **`AssignTaskCommandHandler`** (`src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AssignTask/AssignTaskCommandHandler.cs`)
  checks only: authenticated, task exists, assignee is an active tenant employee, not already
  assigned. **No check that the caller is the Objective's owner** — any authenticated user with the
  module-level `projects:access` permission can currently assign any task to anyone.
- **`MoveTaskStatusCommandHandler`** (`.../Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`)
  checks only authentication — **no membership or ownership check of any kind**. It sets
  `task.CompletedAt`/`task.ProgressPercent = 100` when the new status has `MarksTaskComplete = true`,
  but **never writes `task.CompletedHours` or `objective.CompletedHours`**.
- **`Objective.CompletedHours`** is written exactly once in the entire application layer — hardcoded
  to `0m` at creation (`CreateObjectiveCommandHandler.cs:118`) — and never recalculated anywhere
  afterward. It's read-only decoration in every mapper/DTO today.
- **`CreateObjectiveCommandHandler`** (sub-objective / "Create sub Module" creation) does not seed any
  `TaskStatus` rows. The only status-provisioning paths today are: `CreateProjectCommandHandler`
  seeding a 4-status **project-level** template (`ObjectiveId = null`) when a Project is created, and
  `GetObjectiveTaskStatusesQueryHandler`'s lazy copy-on-first-Board-visit into a per-Objective copy.
  A newly-created sub-Objective has **zero** TaskStatus rows until its Board tab is opened once.
- **No task-detail popup exists in the frontend.** Neither `TaskCardComponent` nor
  `TaskTableComponent` has any click handler today — clicking a task card or row does nothing.
- **No chained sequential API-call pattern exists** in any `work` module store today (`Promise.all`
  for parallel calls, yes; sequential create-then-act, no). The task-create-then-assign flow this spec
  adds is the first of its kind here.

## Data model

### New entity: `Sprint` (inherits `BaseEntity` — `Id, TenantId, CreatedAt, UpdatedAt, CreatedById, IsDeleted`)

| Field | Type | Notes |
|---|---|---|
| `ProjectId` | `Guid` | Denormalized, same pattern as `WorkTask.ProjectId` |
| `ObjectiveId` | `Guid` | Required — the owning Objective. Not nullable, unlike the old tentative design |
| `Name` | `string` | |
| `StartDate` | `DateOnly` | |
| `EndDate` | `DateOnly` | Must be `>= StartDate` (validator rule) |
| `Status` | `string` | `SprintStatuses`: `Future`, `Active`, `Complete`, `Incomplete`, `Achieved` |
| `CompletedAt` | `DateTimeOffset?` | Set when moved to `Complete` |
| `AchievedAt` | `DateTimeOffset?` | Set when moved to `Achieved` |

**`Achieved` is a status value, not a use of `BaseEntity.IsDeleted`.** The user described Achieve as
"soft delete," but using the generic `IsDeleted` flag would mean every existing repository's default
`!IsDeleted` filter silently hides Achieved sprints — including from the owner's own "all sprints"
Backlog view and from the Objective-achieve gate check (below), which both need to see Achieved
sprints. This mirrors how Objective's own existing Achieve action already works (`IsAchieved`/
`AchievedAt` fields, not `IsDeleted`) — same precedent, same reasoning.

### `WorkTask.SprintId` (new, nullable `Guid?`)

**Nullable at the database level**, even though every *newly created* task must have one. A
non-nullable column would require inventing a placeholder Sprint for every existing task on
migration — real risk, no real benefit. The **application layer** enforces the requirement: task
creation validators reject a missing `SprintId` going forward. Pre-existing sprint-less tasks (there
are none in production yet, but the column must still be safely addable) simply show in a "no
sprint" bucket in Backlog.

### `TaskStatus.Visibility` (new, `string`, `TaskStatusVisibilities.Public` / `.Private`, default `Public`)

Existing rows default to `Public` on migration — **not** retroactively guessed at per-tenant. Only
the new default template (below) assigns `Private` to anything, and only for objectives created after
this ships. `Public` = any active Objective member can move a task into this status. `Private` = only
the Objective owner can.

### Default status template (applied eagerly at Objective creation, not lazily)

`To Do` [Public] → `In Process` [Public] → `Review` [Public] → `Done` [Private, `MarksTaskComplete = true`].
Applied in **two** places going forward: `CreateProjectCommandHandler` (the project-level template,
already exists — just add the `Visibility` values) and `CreateObjectiveCommandHandler` (new — eager
per-objective copy, closing today's "zero statuses until Board is first opened" gap). The existing
lazy-copy fallback in `GetObjectiveTaskStatusesQueryHandler` stays as a safety net for any objective
that somehow still lacks statuses, and must also copy `Visibility` (currently doesn't, since the field
doesn't exist yet).

## Sprint lifecycle

```
Future --(StartDate reached)--> Active --(owner Completes, all tasks MarksTaskComplete)--> Complete
                                    |
                                    +--(EndDate passed, tasks unfinished)--> Incomplete

Future / Active / Complete / Incomplete --(owner Achieves, any time)--> Achieved
```

- **Future → Active** and **Active → Incomplete** are automatic, date-driven, run by a new
  `SprintLifecycleJob : BackgroundService` — follows the same established pattern as the existing
  `AgentCommandExpiryJob` (periodic `BackgroundService`, same polling style). At sprint *creation*,
  the initial status is computed directly from `StartDate` vs. today (immediately `Active` if the
  start date is today or earlier) rather than always starting `Future` and waiting for the next job
  tick — avoids latency on same-day-start sprints.
- **Active → Complete**: owner-only, manual (`CompleteSprintCommand`). Blocked (`422`) unless every
  task in the sprint is currently in a status with `MarksTaskComplete = true`.
- **Any state → Achieved**: owner-only, manual (`AchieveSprintCommand`). Freezes the sprint's tasks —
  implemented as a check in `MoveTaskStatusCommandHandler`/`EditTaskCommandHandler`: if a task's
  Sprint is `Achieved`, block further status moves/field edits on it. No separate `IsFrozen` column on
  `WorkTask` — the Sprint's own status is the single source of truth, avoiding a second field that
  could drift out of sync. **Assignment (`AssignTask`/`UnassignTask`) is deliberately not frozen** —
  reassigning who's on record for a completed/archived task is a bookkeeping action, not scope
  creep on the work itself, so it stays available after Achieve.
- **Incomplete has no cascading behavior in this spec** — explicitly deferred, per your instruction.
  It is a terminal-until-Achieved status with no further automated action.
- **New cross-cutting guard on the existing Objective-Achieve flow**: an Objective can only be
  Achieved once every one of its Sprints is `Complete` or `Achieved`. Added as a check wherever
  Objective-achieve requests are created/approved (the existing `ObjectiveChangeRequestTypes.Achieve`
  path in `ApproveObjectiveChangeRequestCommandHandler`, plus wherever the request is first raised).

**Assumption carried from the design conversation, not yet re-confirmed**: Achieve is reachable from
any state, not only from Complete/Incomplete — an owner can abandon a Future or Active sprint early.
Flag now if that's wrong.

## Permissions summary

| Action | Who |
|---|---|
| Create / Edit / Complete / Achieve Sprint | Objective owner only |
| View all Sprints (any status), in Backlog | Objective owner |
| View Active Sprint(s) only, in Backlog | Objective members (non-owner) |
| View the Board (its *content* is now scoped to Active Sprint(s) only — a behavior change, not a permission change) | Anyone with objective access — same as today |
| Create / Edit / Delete a `TaskStatus` | Objective owner only (matches existing `EditTaskStatus` rule) |
| Move a task into a **Public** status | Any active Objective member (checked via the existing `IMilestoneMembershipCoordinator.IsActiveMemberAsync(tenantId, objectiveId, employeeId, ct)` — no new membership-check mechanism needed) |
| Move a task into a **Private** status | Objective owner only |
| Assign a task to someone | Objective owner only (**fixes today's missing check**) |
| View / edit "Objective Settings" page | Objective owner only — tab hidden entirely for non-owners |

## Objective hours rollup (fills today's gap)

On every `MoveTaskStatusCommandHandler` call, compare the **old** and **new** status's
`MarksTaskComplete` flags (not just the new one, to stay correct across repeated/reversed moves):

- Old not-complete → New complete: `task.CompletedHours = task.EstimatedHours ?? 0m`;
  `objective.CompletedHours += task.CompletedHours`.
- Old complete → New not-complete (a task un-completed): `objective.CompletedHours -= task.CompletedHours`;
  `task.CompletedHours = 0m`.
- Old complete → New complete (e.g. Done → a different Private complete-flagged status, if the owner
  ever configures two): no change to hours, only `task.StatusId` moves.
- Neither old nor new is complete: no hours change (today's behavior, unaffected).

## New/changed backend commands

- `CreateSprintCommand(ObjectiveId, Name, StartDate, EndDate)` — owner-only; computes initial Status.
- `EditSprintCommand(SprintId, Name, StartDate, EndDate)` — owner-only; blocked once Complete/Achieved.
- `CompleteSprintCommand(SprintId)` — owner-only; validates all-tasks-complete precondition.
- `AchieveSprintCommand(SprintId)` — owner-only; sets Status, freezes tasks (via the status-check
  added to Move/Edit task handlers, not a new field).
- `CreateTaskStatusCommand(ObjectiveId, Name, DisplayOrder, Visibility, MarksTaskComplete, RequiresApproval, ApproverId)` —
  owner-only, new (no such command exists today — only Edit does).
- `DeleteTaskStatusCommand(StatusId)` — owner-only, new. Blocked if any `WorkTask` currently
  references the status (simplest safe rule — owner must move tasks out first, no forced
  reassignment logic).
- `EditTaskStatusCommand` — existing, extended with the new `Visibility` field.
- `AssignTaskCommandHandler` — add the owner-only check.
- `MoveTaskStatusCommandHandler` — add membership + Private-status-owner-only checks, plus the hours
  rollup above.
- `CreateTaskCommand` / `CreateTaskCreationRequestCommand` — add required `SprintId`.

## Frontend changes

- **New "Settings" tab** in the objective sub-menu (`milestone-sub-menu.component.ts`'s `TABS` array
  + a new route in `work.routes.ts`, following the exact existing lazy-loaded-component pattern) —
  hidden entirely for non-owners. Houses Task Status list/create/edit/delete/reorder and
  Public/Private toggling.
- **Backlog**: gains a Sprint section — Create Sprint action (owner-only), sprint list (all sprints
  for owner, Active-only for members), tasks groupable/filterable by sprint.
- **Board**: scoped to only the Objective's currently-Active Sprint(s)' tasks (today it shows every
  task on the objective regardless of grouping).
- **Task-detail popup** (new — nothing like this exists today): a "View" affordance added to both
  `TaskCardComponent` and `TaskTableComponent`, opening a new `TaskDetailModalComponent` showing all
  task fields, assignees, and status/priority/hours — this spec treats it as read-view with an Edit
  entry point into the existing edit flow, not a from-scratch editor.
- **Task-create modal**: add an optional "Assign to" field. On submit: call the existing Create Task
  API first; only if an assignee was picked, then call the Assign Task API with the returned task's
  ID, sequentially (not parallel) — matches your explicit instruction. New chained-call pattern for
  this store, following no prior precedent in this codebase (confirmed none exists), so this
  introduces the pattern rather than reusing one.

## Notifications (in-app only — mail via Outbox stays deferred, as already decided)

Reasonable default set, using the existing `INotificationDispatcher.SendTemplatedAsync` pattern (new
templates, same mechanism as Task Foundation's notifications — no new dispatch pipeline):
`sprint_completed`, `sprint_incomplete`, `sprint_achieved` — sent to the sprint's Objective members.
Sprint creation/activation intentionally does **not** notify (would be noisy — members already see
Active sprints directly in Backlog/Board). Adjust if you want creation notified too.

## Explicitly out of scope for this spec

- Per-Objective Calendar (separate, follow-up spec).
- Sprint reporting/velocity/burndown (`sprint_daily_snapshots`, `sprint_reports` from the old tentative
  design) — not asked for, not building it.
- Outbox-based email notifications for this feature's events (in-app only, per your answer).
- Any cascading behavior when a Sprint becomes Incomplete (deferred, per your answer).
- A picker for choosing *which* Active Sprint to view when multiple are active on one Objective's
  Board — this spec shows all Active Sprints' tasks together by default; a filter can be added later
  without being a breaking change.
