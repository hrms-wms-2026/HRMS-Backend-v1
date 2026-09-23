# Sprint Lifecycle Redesign Design

**Status:** Approved (pending final written-spec review) — 2026-09-18
**Repos:** `HRMS-Backend-v1` (this repo) + `Hrms--Web-application---front-end---v1`, both on new branch
`feature/sprint-lifecycle-redesign` off `origin/development` / `origin/Development`.
**Builds on:** `docs/superpowers/specs/next/2026-08-17-work-management-sprint-foundation-design.md`
(the original Sprint foundation — this spec changes its lifecycle and creation flow, not its
existence).

## Summary

Replaces the Backlog page's sprint-as-form-plus-status-dropdown UI with a guided lifecycle: Create
(instant, dateless Draft) → Start (commits dates + goal, becomes Active) → Complete (disposes of
unfinished tasks, becomes Complete). Removes the free-form status dropdown (`SetSprintStatusCommand`)
entirely — every remaining transition is a single purpose-built, owner-only action. Retires the
`Future` and `Incomplete` status values; `Achieved` is untouched.

## Background — current state (verified by direct code reading)

- `Sprint.StartDate`/`EndDate` are non-nullable `DateOnly`, required at creation
  (`CreateSprintCommandHandler.cs:59`) — the handler even computes `initialStatus` as `Active` if
  `StartDate <= today`, i.e. **creating a sprint can already make it active today**, exactly the
  behavior being redesigned away.
- `SprintStatuses`: `Future, Active, Complete, Incomplete, Achieved`. `SprintLifecycleJob` advances
  `Future → Active` (start date reached) and `Active → Incomplete` (end date passed, tasks
  unfinished) on a 5-minute sweep; completion has always been a separate manual action.
- `SetSprintStatusCommandHandler` lets the objective owner jump to any status directly, setting
  `Sprint.IsManuallyOverridden = true` so the sweep permanently ignores that sprint afterward. This
  is the dropdown the redesign removes; nothing else writes or reads `IsManuallyOverridden`.
- `CompleteSprintCommandHandler` currently **blocks with 422** unless every task in the sprint is in
  a `MarksTaskComplete` status. There is no disposition path for leftover tasks today.
- `AchieveObjectiveCommandHandler:76` blocks Objective achievement while
  `sprints.Any(s => s.Status is not (Complete or Achieved))` — adding `Draft` without adjusting this
  would let one abandoned draft sprint permanently block its Objective.
- `Achieved` is load-bearing beyond this feature: `MoveTaskStatusCommandHandler`, `EditTaskCommandHandler`,
  `CreateTaskCommandHandler`, `CreateTaskCreationRequestCommandHandler`, `CreateTaskEditRequestCommandHandler`,
  and `ApproveTaskEditRequestCommandHandler` all check `sprint.Status == Achieved` to freeze task
  edits. None of these change.
- No time-tracking / logged-hours data source exists anywhere in either repo (confirmed by search).
  `WorkTask.estimatedHours` is the only hours field that exists.
- No drag-and-drop exists in the Backlog task table today.
- Frontend `SprintFormComponent`'s "Request sprint" label is dead copy: `TaskBacklogComponent` only
  ever passes `ownedModuleOptions()` into it, so the non-owner/request branch is unreachable from this
  call site.

## Data model (`ONEVO.Domain...Sprints.Entities.Sprint`)

| Field | Change |
|---|---|
| `StartDate`, `EndDate` | `DateOnly` → `DateOnly?` |
| `Goal` | New, `string?`, short free text (cap 500 chars, matches other Work Management free-text fields) |
| `Status` | `SprintStatuses`: `Draft, Active, Complete, Achieved` (`Future`, `Incomplete` removed) |
| `IsManuallyOverridden` | Removed |
| `OverdueNotifiedAt` | New, `DateTimeOffset?` — lets `SprintLifecycleJob` send the overdue notification exactly once per sprint instead of every 5-minute tick |

### Migration

New EF migration:
- Alter `start_date`/`end_date` nullable, add `goal` (nullable), add `overdue_notified_at` (nullable),
  drop `is_manually_overridden`.
- Data backfill: `status = 'future'` → `'active'` (a Future sprint already carries committed dates —
  in the new model, having dates *is* Active; there's no more dateless-but-scheduled holding state).
  `status = 'incomplete'` → `'active'` (its dates and tasks carry over; overdue is now a computed
  display state, not stored). `complete`/`achieved` rows untouched.
- No seeder references `Sprint` directly (confirmed by search) — no seeder changes needed.

## Lifecycle & backend commands

```
Draft --(owner: StartSprintCommand — dates + goal)--> Active --(owner: CompleteSprintCommand — task disposition)--> Complete --(owner, via Objective/Milestone flow, unchanged)--> Achieved
```

- **`CreateSprintCommand`**: signature becomes `(ObjectiveId, Name, Goal?)`. Always creates
  `Status = Draft`, `StartDate = EndDate = null`. Drops the `initialStatus` date computation.
- **`StartSprintCommand`** (new): `(SprintId, StartDate, EndDate, Goal?)`. Owner-only
  (`IsEffectiveManagerAsync`, same check every other sprint command already uses). Rejects if
  `sprint.Status != Draft` (409) or `EndDate < StartDate` (422). Sets dates, optionally overwrites
  `Goal` if provided, `Status = Active`.
- **`CompleteSprintCommand`**: signature becomes
  `(SprintId, Disposition)` where `Disposition` is a small discriminated shape —
  `MoveToBacklog` or `MoveToSprint(Guid TargetSprintId)`. The existing all-tasks-complete 422 gate is
  **removed**. Inside the same transaction as today: for every task in the sprint not currently in a
  `MarksTaskComplete` status, set `task.SprintId = null` (Backlog) or `task.SprintId = TargetSprintId`
  (must belong to the same Objective, validated same as today's sprint-task association rules); then
  `sprint.Status = Complete`, `CompletedAt = now`. Existing `sprint_completed` notification unchanged.
- **`EditSprintCommand`**: signature becomes `(SprintId, Name, Goal, StartDate?, EndDate?)`. Always
  updates `Name`/`Goal`. If the sprint is `Active` and both dates are provided, updates them
  (`EndDate >= StartDate` validated); if the sprint is `Draft`, date params must be null (422 if
  provided — dates only ever get set via Start). Still blocked entirely once `Complete`/`Achieved`,
  as today.
- **`SetSprintStatusCommand`, `SetSprintStatusCommandHandler`, `SetSprintStatusCommand.cs`, its
  controller route (`PATCH sprints/{id}/status`), and `SprintContracts.SetSprintStatusRequest`**: all
  deleted.
- **`AchieveSprintCommand`**: unchanged.
- **`SprintLifecycleJob`**: drops the `Future → Active` branch entirely (nothing dateless to watch).
  Keeps a single sweep responsibility: for `Active` sprints past `EndDate` with incomplete tasks and
  `OverdueNotifiedAt == null`, send the existing `sprint_incomplete`-style notification and stamp
  `OverdueNotifiedAt`. **Never mutates `Status`.** `DetermineNextStatus`'s pure-function shape is
  replaced by a pure `bool ShouldNotifyOverdue(...)` predicate — same testability precedent, smaller
  surface.
- **`AchieveObjectiveCommandHandler:76`** gate changes from
  `sprints.Any(s => s.Status is not (Complete or Achieved))` to
  `sprints.Any(s => s.Status is Active)` — Draft sprints no longer block Objective achievement;
  Active still does, same as today's practical effect.

### API surface (`SprintsController`)

- `POST objectives/{id}/sprints` — body becomes `{ name, goal? }`.
- `POST sprints/{id}/start` — new, body `{ startDate, endDate, goal? }`.
- `PATCH sprints/{id}` — body becomes `{ name, goal, startDate?, endDate? }`.
- `POST sprints/{id}/complete` — body becomes `{ disposition: 'backlog' | 'sprint', targetSprintId? }`.
- `PATCH sprints/{id}/status` — deleted.
- `POST sprints/{id}/achieve`, the two `GET` sprint-list endpoints, `GET sprints/{id}/tasks` — unchanged
  shape, `SprintViewModel`/`SprintResponse` gain `Goal`, `StartDate`/`EndDate` become nullable in the
  DTO.

## Frontend changes (`Hrms--Web-application---front-end---v1`)

- `models/sprint.model.ts`, `models/dto/sprint.dto.ts`: `SprintStatus` →
  `'draft' | 'active' | 'complete' | 'achieved'`; `startDate`/`endDate` → `Date | null`; add `goal:
  string | null`. `CreateSprintRequestDto` → `{ name, goal? }`. New `StartSprintRequestDto { startDate,
  endDate, goal? }`. `EditSprintRequestDto` → `{ name, goal, startDate?, endDate? }`. New
  `CompleteSprintRequestDto { disposition: 'backlog' | 'sprint'; targetSprintId?: string }`.
- `sprint-api.service.ts` / `sprint.mapper.ts`: updated for the above; new `start()` method; `complete()`
  takes the disposition payload.
- `sprint-list.store.ts`: drops `setStatus()`; adds `start()`; `complete()` takes disposition.
- `SprintFormComponent` (Create/Edit): Create mode drops Start/End date inputs, adds optional Goal
  field, drops the "Request sprint" label branch (always "Create sprint"). Edit mode: Name + Goal
  always; Start/End date inputs shown only when `sprint.status === 'active'`.
- New `SprintStartDialogComponent`: task count, summed `estimatedHours`, distinct assignee count from
  the sprint's tasks, start/end date pickers, goal (pre-filled, editable), non-blocking warning text
  for tasks missing an estimate or assignee (computed client-side from already-loaded task list, no
  new endpoint), Cancel/Start actions.
- New `SprintCompleteDialogComponent`: total/complete/incomplete task counts, summed estimated hours
  (no "logged hours" — no data source exists for it), a disposition radio (Move to Backlog / Move to
  another sprint, with a sprint picker scoped to the same Objective's non-Complete/Achieved sprints),
  Cancel/Complete actions.
- `SprintTabComponent`: three distinct renderings by status instead of one form-plus-dropdown —
  Draft (name, goal, "No dates set", counts, `+ Add tasks`, `Start sprint →`), Active (dates,
  days-remaining or red "N days overdue" badge, goal, task-status breakdown, progress bar, `Complete
  Sprint`), Complete (read-only, collapsed by default, summary counts only). The `statusChangeRequested`
  output and its dropdown are removed.
- `TaskBacklogComponent`: `sprintStatusOptions` drops `future`/`incomplete`, adds `draft`; sprint list
  sorts Active first, then Draft, then Complete; `onSprintStatusChangeRequested` and the
  `statusChangeRequested` wiring are removed, replaced by `onSprintStarted`/`onSprintCompleted`
  handlers that open the two new dialogs.
- `sprint-tree-row.component.ts` / `milestone-tree-node.component.ts` (Tree view): compatibility only
  — render `null` dates as "No dates set" and a Draft badge. No lifecycle redesign there.

## Explicitly out of scope

- Pending-approval step for sprint creation (no approval infra exists for sprints today; confirmed
  with you).
- A distinct "Ready" status (your mockups never show one separately from Draft).
- Drag-and-drop from Unsorted into a sprint card (no DnD infrastructure exists in the Backlog task
  table today; confirmed with you — task-form's existing sprint picker and the `+ Add tasks`
  prefilled-create flow cover reassignment instead).
- "Logged hours" display anywhere (no time-tracking data source exists in either repo).
- Any redesign of the Tree view's sprint rendering beyond null-safety.
- Per-task disposition on Complete (the dialog offers one bulk destination for all incomplete tasks,
  matching the single picker in your mockup — not a per-task choice).
