# Independent Sprints — Design

**Date:** 2026-09-23
**Status:** Approved (brainstorm with user, 2026-09-23)
**Repos:** `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`
**Module:** Work Management only ("Module" in UI = `Objective` in code; "Milestone/Event" in UI = `CalendarEvent` in code)

## 1. Goal

A Sprint stops belonging to one Module. It becomes a project-level "bunch of tasks" that can hold
tasks from many Modules. Sprint creation gets the same module→task tree picker the Event modal has.
The Event modal gains a **Tree view / Sprint view** switch so tasks can be picked by sprint.

## 2. Decisions (from the brainstorm)

| # | Decision |
|---|---|
| D1 | Any active project member can **create** a sprint (no module required). |
| D2 | A caller can only **put tasks into / take tasks out of** a sprint for tasks whose Module they effectively own (`IsEffectiveOwnerAsync` — own module or any ancestor's owner). Applies to the sprint picker AND to the Backlog "move to sprint" AND to `EditTask`'s `SprintId`. |
| D3 | **Adding a whole Module to a sprint is a one-time bulk tick** of the module's current tasks. No live module↔sprint link. Tasks created later go to the backlog as today. |
| D4 | One task ↔ at most one sprint (existing rule). Picking a task that is already in another sprint **moves** it. Tasks in an **Achieved** sprint are frozen and cannot be picked/moved. Sprints that are Complete/Achieved cannot receive tasks. |
| D5 | **Start / Complete / Achieve / Edit** a sprint: allowed for the sprint's creator, or anyone who `IsEffectiveOwnerAsync` for the Module of **any task currently in the sprint** (so parent-module owners qualify). |
| D6 | Every sprint action is **logged** (`sprint_activity_logs`): created, edited, started, completed, achieved, tasks_added, tasks_removed — who (EmployeeId), when, from/to status, details JSON. Exposed via `GET /sprints/{id}/activity` and shown as a History list in the sprint row. |
| D7 | Event + sprint: **one-time bulk add**. Sprint view in the Event modal only produces `taskIds`; nothing about the sprint is stored on the event. **No event API/DB change.** |
| D8 | Event modal switch: **Tree view** (existing module→task picker, live module membership unchanged) / **Sprint view** (all project sprints in timeline order: `startDate` asc, then Draft (dateless) sprints by creation order; each sprint expandable to pick some tasks, or tick whole). Both views share one task selection. |

## 3. Backend

### 3.1 Data model
- `Sprint.ObjectiveId` **removed**. `Sprint.ProjectId` (already present, non-null) is the only parent.
  Migration first re-syncs `sprints.project_id` from `objectives.project_id` (defensive), then drops
  index `ix_sprints_tenant_id_objective_id_status` and column `objective_id`, then adds
  `ix_sprints_tenant_id_project_id_status`.
- New `SprintActivityLog : BaseEntity` → table `sprint_activity_logs`
  (`sprint_id`, `employee_id`, `action` varchar(30), `from_status` varchar(20)?, `to_status` varchar(20)?,
  `details_json` text?, `occurred_at`), FK → sprints (Restrict), index `(tenant_id, sprint_id, occurred_at)`,
  **tenant_isolation RLS policy in the same migration** (`TenantTables = ["sprint_activity_logs"]`).

### 3.2 Services (Application, `Features/WorkManagement/Sprints/Services`)
- `ISprintAccessService`
  - `CanManageAsync(tenantId, sprint, callerUserId, callerEmployeeId)` — D5.
  - `GetManageableSprintIdsAsync(tenantId, projectId, sprints, callerUserId, callerEmployeeId)` — batched D5 for list queries.
  - `GetAudienceEmployeeIdsAsync(tenantId, sprintId)` — distinct active members of every Module that has a task in the sprint (notification audience).
- `ISprintTaskAssignmentService`
  - `PrepareAsync(tenantId, sprint, addTaskIds, removeTaskIds, callerEmployeeId)` → `Result<SprintTaskChangeSet>` — validates D2/D4, returns tracked tasks.
  - `Apply(changeSet, sprintId)` — mutates `SprintId` on the tracked tasks.
- `ISprintActivityLogRepository` — `AddAsync`, `GetForSprintAsync`.

### 3.3 Commands / queries
| Endpoint | Change |
|---|---|
| `POST /work/projects/{projectId}/sprints` (NEW route; old `objectives/{id}/sprints` POST removed) | `CreateSprintCommand(ProjectId, Name, Goal, TaskIds)`. D1 gate: active project membership **or** `projects:read`/`*` permission. Optional `TaskIds` assigned via `ISprintTaskAssignmentService`. Logs `created` (+ `tasks_added`). |
| `PUT /work/sprints/{id}/tasks` (NEW) | `SetSprintTasksCommand(SprintId, AddTaskIds, RemoveTaskIds)`. Sprint must be Draft/Active. D2 per task. Logs `tasks_added` / `tasks_removed`. |
| `PATCH /sprints/{id}`, `POST .../start`, `.../complete`, `.../achieve` | Gate = `CanManageAsync` (D5). Each logs its action. Complete's `"sprint"` disposition target must be same **project** and Draft/Active. Notifications go to `GetAudienceEmployeeIdsAsync`; the `objectiveName` placeholder now carries the **project name**. |
| `GET /projects/{projectId}/sprints` | Non-`projects:read` callers: any active project member sees **all** project sprints. Adds `canManage`. |
| `GET /objectives/{objectiveId}/sprints` | Semantics change: sprints that **contain at least one task of this module** (keeps the Tree tab's leaf expansion working). |
| `GET /sprints/{id}/tasks` | Access = `projects:read` or active project member (no module walk). |
| `GET /sprints/{id}/activity` (NEW) | Activity rows, oldest first. Same access as `/tasks`. |
| `EditTask` (`SprintId`) | Target sprint must be same **project** and Draft/Active (was: same module). Logs `tasks_added` on target / `tasks_removed` on source. |
| `CreateTask`, `CreateTaskCreationRequest`, `ApproveTaskCreationRequest` | `sprint.ObjectiveId != objective.Id` → `sprint.ProjectId != objective.ProjectId`. |
| `AchieveObjective` | Blocked while **any task of this module sits in an Active sprint** (was: any Active sprint owned by the module). |
| `SprintLifecycleJob` overdue notify | Audience via `GetAudienceEmployeeIdsAsync`; project name placeholder. |

`SprintResponse` / `SprintViewModel` → `(Id, ProjectId, Name, Goal, StartDate, EndDate, Status, CompletedAt, AchievedAt, CanManage)`.

## 4. Frontend

- `Sprint` / `SprintDto`: `objectiveId` → `projectId`, add `canManage`. All `sprint.objectiveId` usages removed.
- `SprintListStore`: `create(projectId, request)`; `start/edit/complete/achieve(sprintId, …, projectId)` always reload via `loadForProject`. `setTasks(sprintId, add, remove, projectId)`. Object-scoped `load()` removed.
- NEW `ModuleTaskPickerComponent` (`work/ui/module-task-picker/`): extracted from `calendar-event-modal`. Inputs `modules`, `moduleMode: 'live' | 'bulk'`, `allowedObjectiveIds: ReadonlySet<string> | null`, `sprintNames`, `frozenSprintIds`, `selectedTaskIds`, `selectedObjectiveIds`. Outputs `selectedTaskIdsChange`, `selectedObjectiveIdsChange`. Lazy-loads tasks per module via `TaskApiService.getTasks`.
- `SprintFormComponent`: Module dropdown removed; create = Name, Goal, picker (bulk, allowed = `isEffectiveOwner` modules). Edit = same + picker seeded with the sprint's current tasks; save sends the add/remove diff to `PUT /sprints/{id}/tasks`.
- Backlog: "Create Sprint" visible whenever the project has modules; move-to-sprint targets = all **Draft + Active** project sprints; blocked only if a selected task's module is not effectively owned; sprint row buttons gated by `sprint.canManage`.
- `task-form-modal`: sprint dropdown lists all Draft/Active project sprints (no module filter); sprint no longer forces the module.
- Tree tab: `loadTasksForSprint` filters sprint tasks to the leaf module (`task.objectiveId === node.parentObjectiveId`).
- NEW `SprintHistoryComponent` (`work/ui/sprint-history/`) inside the expanded sprint row.
- `CalendarEventModalComponent`: Tree view = picker in `live` mode; Sprint view = timeline-ordered sprint list with expand + tri-state tick, loads sprints via `SprintApiService.getByProject(projectId)` and tasks via `TaskApiService.getBySprintId`. Shared `selectedTaskIds`. New input `projectId`.

## 5. Out of scope
- Live module↔sprint link; event↔sprint stored link; sprint deletion; changing notification template texts.

## 6. Testing
Backend xUnit+Moq per handler/service (gates: creator, task-module owner, parent owner, outsider; D4 freeze/move; logging rows; migration builds). Frontend Vitest per component/store. Full suites both repos + architecture suite (RLS coverage) green. Manual browser pass at the end.
