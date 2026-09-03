# Work Management — Event Duration, Hybrid Membership, People Filter

**Design spec — for review**

| | |
|---|---|
| **Status** | Draft for user review |
| **Date** | 2026-09-02 |
| **Author** | kajaatharan2003@gmail.com |
| **Scope** | Work Management only — `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1` |
| **Branch** | `feature/wm-event-duration-hybrid-membership` off `feature/wm-approval-hours-and-component-tuning` (both repos) |
| **Supersedes** | The 2026-08-30 `WM_DEADLINE_EVENTS_SOLUTION` proposal — **rejected** by the hiring official. This is the slimmed-down set of recommendations that survived. |

---

## 1. Background

The 2026-08-30 proposal ("deadlines on Events, not Modules") was presented to the
hiring official and **rejected**: the system is to stay as it is. Modules keep
their dates, the parent-containment check, and the allocation cascade — none of
that is touched.

The official then asked for a **subset of ideas** to be added instead. As relayed:

1. An **Event gets a duration** (a start date + end date), set when the event is created.
2. An **Event's membership becomes hybrid** — it can hold whole **Modules** and/or
   individual **Tasks** drawn from other modules ("two full modules + half of a third").
3. **Multiple events per module** are allowed; a **task** still belongs to **one active event** only.
4. The **event editor** lets the user pick a whole module, or expand a module and tick individual tasks.
5. The **Project Calendar** draws module bars in their event's colour, nested against an
   event band, with partial-module membership shown as a coloured "N of M in event" segment.
6. The **"My Task" page is removed**; its value is replaced by a **People filter on Board + Backlog**.

> **Default decisions taken (flag at review if wrong):**
> - **D-A** The six items above are the complete list of recommendations.
> - **D-B** If a module is linked to an event *as a whole* and a task is later created in
>   that module whose due date is outside the event window, the **task-create is rejected**
>   with "widen event X first" — keeping rule **R2** (below) consistent. Alternative would be
>   to silently keep the new task out of the event.
> - **D-C** Any existing deep-link that pointed at the My Task page (dashboard widget,
>   notification links) is repointed to **Board, filtered to the current user**.
> - **D-D** Branch name `feature/wm-event-duration-hybrid-membership`, one per repo.

---

## 2. Rules

| # | Rule |
|---|---|
| **R1** | **One active event per task.** A task may belong to at most one *active* event (archived/closed events don't count). A **module** has no such limit — it can be a whole-module member of many events. |
| **R2** | **The event date window is authoritative.** For every task that is a member of event `e` — whether picked directly *or* pulled in by a whole-module link — `e.StartDate ≤ task.DueDate ≤ e.EndDate`. A task with no due date cannot be an event member. |
| **R3** | **No silent reshaping.** Adding an out-of-window task, editing a member task's due date out of window, or narrowing an event's window so a member falls outside → **rejected** (`409`) naming the offending task(s). The event is never auto-moved; the user widens the window or removes the task. |
| **R4** | **Whole-module link is live.** A module linked as a whole contributes *its current active tasks* to the event, now and as tasks are added later (subject to R2 via D-B). Its individual task rows are not stored — the module link is. |
| **R5** | **Authorship unchanged.** Whoever can create/edit/close an event today still can; no change to the permission check. |

---

## 3. Current state (what this touches)

### 3.1 `CalendarEvent` — `ONEVO.Domain/.../CalendarEvents/Entities/CalendarEvent.cs`
`Id, TenantId, ProjectId, Name, Color, Status (active|archived), CreatedById, CreatedAt, ArchivedById?, ArchivedAt?` — **no dates**.

### 3.2 `CalendarEventObjective` — link `CalendarEventId → ObjectiveId`, `AddedAt`.
Today: a unique "one active event per objective" constraint (enforced in the create/update handlers + repo). **This constraint is removed** (R3 of the old proposal → now multiple events per module).

### 3.3 `GetProjectCalendarQuery` — `Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs`
Returns **one `ProjectCalendarItemResponse` per Objective**: the objective's own
`StartDate`/`EndDate`, a `CanEdit` flag (effective-manager && !achieved && !default),
and — if the objective is in an active event — a single `CalendarEventId` + `CalendarEventColor`.

### 3.4 Event commands
- `CreateCalendarEventCommand(ProjectId, Name, Color, IReadOnlyList<Guid> ObjectiveIds)`
- `UpdateCalendarEventCommand` (Name?, Color?, ObjectiveIds?)
- `CloseCalendarEventCommand` — status → archived. **Unchanged by this spec.**
- Contracts in `ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`.

### 3.5 "My Task" page (frontend)
`src/app/modules/work/feature/my-task-page/` (route, component, store, specs) +
its nav entry. Backend: served by an existing "tasks assigned to me" query — see §6.

### 3.6 Board + Backlog (frontend + backend)
`feature/task-board/`, `feature/task-backlog/` components + stores; backend
`GetTaskBoardQuery` / `GetProjectBacklogQuery` (names to confirm during plan) already
scope by project and status.

---

## 4. Data model changes (backend)

| Object | Change |
|---|---|
| `calendar_events` | **add** `start_date date NOT NULL`, `end_date date NOT NULL` |
| `calendar_event_objectives` | **kept**. Drop the unique/active-per-objective constraint + supporting index. |
| `calendar_event_tasks` | **new**: `(id uuid pk, calendar_event_id uuid fk→calendar_events cascade, task_id uuid fk→tasks restrict, added_at timestamptz)`, unique `(calendar_event_id, task_id)`, index `task_id`. |
| `objectives` | **untouched.** |

- **One EF migration**, `AddEventDatesAndEventTasks`. Written + dry-run-validated
  (`BEGIN … ROLLBACK`), **applied by the user** (`ops/postgres/setup-local-db.ps1 -RunMigrations`).
- Only `dapi` demo data exists for calendar events. Existing rows get `start_date`/`end_date`
  back-filled in the migration from the min/max of their objectives' dates (so they stay valid
  under R2), or dropped if that is simpler — decide in the plan.
- RLS: `calendar_event_tasks` carries no `TenantId` (child of tenant-owned `calendar_events`,
  same as `calendar_event_objectives`). No new `TenantTables` coverage row. Run
  `dotnet test tests/ONEVO.Tests.Architecture` to confirm the coverage suite stays green
  (see the calendar-events RLS coverage recipe from 2026-08-27 if it flags).
- If `ApplicationDbContextModelSnapshot.cs` looks corrupted after generating the migration,
  it's the known stale-checkout issue — compare line counts before trusting a `database update` error.

---

## 5. Event API changes (backend)

### 5.1 Create — `POST /api/v1/work/projects/{projectId}/calendar-events`
`CreateCalendarEventRequest` / `CreateCalendarEventCommand` gain:
- `StartDate` (DateOnly, **required**)
- `EndDate` (DateOnly, **required**)
- `TaskIds` (`IReadOnlyList<Guid>`, optional) — individual task members
- `ObjectiveIds` (existing, optional) — **whole-module** members (semantics unchanged: live link)

Handler:
1. Validate `EndDate >= StartDate`; colour/name rules unchanged.
2. Resolve the member task set = `distinct( tasks(ObjectiveIds active) ∪ TaskIds )`.
3. All members belong to this project; each has a `DueDate`; each `DueDate ∈ [StartDate, EndDate]` → else `409` listing offenders (**R2/R3**).
4. No member task is already in another **active** event → else `409` listing them (**R1**).
5. Persist `CalendarEvent` (+ dates), one `CalendarEventObjective` per `ObjectiveId`, one `CalendarEventTask` per **directly-picked** `TaskId` (module-linked tasks are *not* stored as task rows — R4).

### 5.2 Update — `PATCH /api/v1/work/calendar-events/{id}`
Same new fields, all optional. Membership replace semantics:
- If **neither** `ObjectiveIds` nor `TaskIds` present → membership unchanged; if a date changed,
  re-validate all current members against the new window (**R3**).
- If **either** present → new membership fully replaces the old set
  (`ObjectiveIds: []` clears module links; `TaskIds: []` clears task links).
- Re-run steps 3–4 from §5.1.

### 5.3 Close — unchanged. Archiving frees its member tasks for a new active event (**R1**).

### 5.4 Read — `GET /api/v1/work/projects/{projectId}/calendar`
Still **one row per Module** (module dates unchanged), but the single
`CalendarEventId`/`CalendarEventColor` pair becomes a **list**:

```
ProjectCalendarItemResponse (per objective, as today) +
  Events: [
    {
      EventId, EventName, EventColor,
      EventStartDate, EventEndDate,
      Membership: "whole" | "partial",
      TasksInEventCount,          // for "partial"
      TaskTotalCount              // module's active task count
    }
  ]
```

Plus a sibling collection on the response envelope for **event bands** (id, name,
colour, start, end, canEdit) so the frontend can draw the scenery band without
re-deriving it. `CanEdit` per event by the same effective-manager test used today
for objectives (**R5**).

### 5.5 Contracts / controllers
- `CalendarContracts.cs` — `CreateCalendarEventRequest` / `UpdateCalendarEventRequest` +
  dates + `TaskIds`; `CalendarEventViewModel` + dates + `taskIds`; new
  `ProjectCalendarEventViewModel` (the per-module event entry) + event-band view model.
- `CalendarController.cs` — command construction + GET response mapping.
- No new endpoints. Post-hoc "add/remove task to event" is `PATCH` with an updated list.

### 5.6 Task edit guard
`PUT /api/v1/work/tasks/{id}` and the task-edit-request approve path: if the task is a
member of an active event (direct or via a whole-module link) and the new `DueDate`
falls outside that event's window → `409` (**R3**). `WorkTaskResponse` gains
`ActiveEventId?`, `ActiveEventName?` so the UI can show "in event X" and bound the
due-date picker.

### 5.7 Task create into a whole-module-linked event (D-B)
`POST /api/v1/work/tasks` (and the creation-request approve path): after resolving the
new task's objective, if that objective is a whole-module member of any active event,
require `DueDate` and validate it against every such event's window → `409` otherwise.
No new `EventId` parameter is added (that was the rejected proposal's idea; not in scope).

---

## 6. My Task removal + People filter

### 6.1 Remove My Task
- **Frontend**: delete `feature/my-task-page/` (component, store, `.spec`s), its route,
  its nav/menu entry, and any `routerLink` to it. Repoint dashboard-widget / notification
  deep-links to `…/board?assignee=<currentEmployeeId>` (**D-C**).
- **Backend**: the "tasks assigned to me" query/endpoint — keep if other screens use it
  (confirm during plan); otherwise remove the endpoint, handler, tests, contract. Do **not**
  remove shared task DTOs.

### 6.2 People filter on Board + Backlog
- **Backend**: `GetTaskBoardQuery` / `GetProjectBacklogQuery` gain optional
  `AssigneeEmployeeIds` (`IReadOnlyList<Guid>?`). When present and non-empty, filter tasks
  to those assignees. Empty/absent → unchanged behaviour. Contract + controller query-param
  binding (`?assigneeEmployeeIds=…&assigneeEmployeeIds=…`).
- **Frontend**: a multi-select "People" control on both Board and Backlog toolbars, options =
  current project members (existing project-members data-access). Selection drives the query
  param; state held in the existing board/backlog store; reflected in the URL so
  `?assignee=<me>` deep-links work. Specs updated.

---

## 7. Frontend — calendar & event editor

### 7.1 Event editor — `ui/calendar-event-modal/`
- Add **Start date** + **End date** inputs (range; end ≥ start client-side).
- Module tree with, per module: a checkbox (whole-module link) + an expand chevron.
  Expanded → task rows with their own checkboxes.
  - Whole-module checked → child task checkboxes shown **locked/ticked** ("included (whole module)").
  - Some tasks checked, module not → module checkbox renders **indeterminate**.
- Right panel "In this event": mixed removable chips — `Module — whole module (live)` and
  `Module / Task` rows. (User approved the two-panel "left" layout; right panel kept as the
  live summary. If the user wants one column, drop the right panel and keep the ticks as state.)
- Surface `409` errors from R1/R2/R3 inline (which task, which event).

### 7.2 Project Calendar timeline — `feature/project-calendar/` + store + utils
Render **Option B (revised)**:
- Event **band** = translucent rectangle across `[event.start, event.end]`, tinted with the
  event colour, labelled.
- **Module bar** stays on its own row at the module's own dates. A module that is a member of
  an event is drawn in **that event's colour**; a module in no event stays **neutral grey**.
- **Partial** module (only some tasks in the event) → a coloured sub-segment on the bar labelled
  `N/M in event`, the remainder dashed-grey.
- A module in **two** events → the bar splits into two coloured segments, one per event colour.
- Dragging a **module bar** still calls `EditObjectiveCommand` (module dates) — unchanged.
  Dragging an **event band** calls `PATCH /calendar-events/{id}` with new dates, re-validated (R3).
- `models/dto/calendar.dto.ts`, `data-access/calendar-api.service.ts`,
  `state/project-calendar.store.ts`, timeline-bar component, `calendar-*.utils.ts` updated to
  the list-of-events shape + band collection. Specs updated.

### 7.3 Task chips
`ui/task-table/`, `ui/task-tree-row/` etc. — a task that is in an active event may show an
"in event" chip (from `ActiveEventName` on `WorkTaskResponse`). No column removals (module
date columns stay — modules are unchanged).

---

## 8. Testing

**Backend (unit + architecture)**
- `CreateCalendarEvent` — happy path (dates + objectiveIds + taskIds); `EndDate < StartDate`;
  R2 boundary on both ends (due date == start, == end, ±1 day); R2 via whole-module link;
  task with no due date; R1 (task already in another active event); R1 does *not* trigger for
  an archived event; multiple events on one module OK.
- `UpdateCalendarEvent` — membership replace (each list independently, `[]` clears);
  date-only edit re-validates members (R3); narrowing window that orphans a member → `409`.
- Read — module with 0 / 1 / 2 events; partial vs whole; `CanEdit` per event.
- Task edit / approve — due-date change that orphans an event member → `409`.
- Task create (D-B) — into a whole-module-linked event, in-window OK, out-of-window `409`,
  missing due date `409`.
- `GetTaskBoard` / `GetProjectBacklog` — `AssigneeEmployeeIds` present (single, multi),
  empty, absent; assignee not on project.
- Architecture suite green (RLS coverage + layering).
- Final **deep per-handler test-coverage audit** part in the plan (boundary both sides,
  negative "must not happen", attribution) — per the standing Manus-audit rule.

**Frontend**
- `calendar-event-modal` — date range validation, whole-module lock, indeterminate module,
  chip add/remove, `409` surfacing.
- `project-calendar` component + store + utils — band render, event-colour bars, partial
  segment, two-event split, event-band drag.
- Board + Backlog — People filter drives query + URL; deep-link `?assignee=me`.
- My Task removal — no dangling route/link; redirected deep-links land on filtered Board.

---

## 9. Order of work (for the plan)

1. **Backend — migration + entities + config** (`calendar_events` dates,
   `calendar_event_tasks`, drop per-objective constraint).
2. **Backend — event create/update** (dates, `TaskIds`, R1/R2/R3, repo methods).
3. **Backend — calendar read reshape** (list-of-events per module + band collection).
4. **Backend — task edit guard + task-create D-B guard + `WorkTaskResponse` fields**.
5. **Backend — Board/Backlog `AssigneeEmployeeIds` filter**.
6. **Frontend — event editor** (date range + hybrid tree + summary panel).
7. **Frontend — calendar timeline** (bands, event-colour bars, partial segments, drag).
8. **Frontend — remove My Task + repoint deep-links**.
9. **Frontend — People filter on Board + Backlog**.
10. **Docs** — `phase1-table-inventory.md` (`calendar_events` +dates, new
    `calendar_event_tasks`), `ARCHITECTURE.md` work/ component list, Postman-request MD for
    Create/Update Calendar Event + Get Project Calendar + Edit Task + Get Task Board/Backlog,
    second-brain `02-work-management/project-calendar.md` + a short ADR.
11. **Verification** — full WM unit suite both repos; architecture suite; manual browser pass
    (create a dated event with a whole module + two tasks from another module; drag the band;
    try an out-of-window task → `409`; Board People filter; My Task gone). Then move the plan
    `next/ → finished/`.

The user runs the migration. No push, no dev-server, no process kills — standing rules.

---

## 10. Not changing

Module (`Objective`) dates and `ObjectiveParentConstraintChecker`; allocation / extend-allocation
cascade; sprints; cascading ownership; task creation flow except the two guards in §5.6–5.7;
the close-event flow; event authorship check.

---

## Appendix — glossary

| UI term | Code / table |
|---|---|
| Module / Sub-module | `Objective` / `objectives` |
| Task | `WorkTask` / `tasks` |
| Event | `CalendarEvent` / `calendar_events` |
| Event ↔ whole Module | `CalendarEventObjective` / `calendar_event_objectives` (kept) |
| Event ↔ single Task | `CalendarEventTask` / `calendar_event_tasks` (**new**) |
| Effective manager | `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync` (cascading-ownership walk) |
