# Work Management — Project-Scoped Task Status & Task Category (Design)

**Status:** Approved 2026-08-21 (chat brainstorm with user). Companion frontend changes are in the
frontend repo (§6). One open assumption flagged in §4 — confirm before implementation starts.

## 1. Problem

`TaskStatus` today is **per-Objective**, not per-Project. `CreateProjectCommandHandler` seeds one
Project-level template (`ObjectiveId == null`) plus one copy for the default Objective;
`CreateObjectiveCommandHandler` seeds another full copy for every sub-module created afterward; and
`GetObjectiveTaskStatusesQueryHandler` lazily copies the template onto any Objective that doesn't have
its own copy yet. The result: every Module/sub-Module in a Project can end up with its own
independently-diverged status list. The user wants **one Task Status list per Project**, shared by
every Objective inside it, editable from a Project-level Settings page — not per-Objective anymore.

Separately, `WorkTask.TaskType` is a hardcoded 4-value string enum (`WorkTaskTypes`: Task/Bug/Story/
Feature) — not configurable per project. The user wants this converted into a **Task Category**
mechanism shaped like Task Status (a `TaskCategory` table, one list per Project, editable in the same
new Settings page).

## 2. Task Status: collapse to per-Project

**Data model — no destructive migration.** Every Project already has an `ObjectiveId == null`
template row set (seeded at Project creation). Keep using exactly those rows as *the* status list for
the whole Project. Stop creating any more `ObjectiveId`-scoped copies:

- `CreateProjectCommandHandler.cs:286` — remove the `defaultObjective.Id`-scoped
  `DefaultTaskStatusTemplate.BuildRows` call; keep only the `objectiveId: null` one (line 284).
- `CreateObjectiveCommandHandler.cs:129` — remove the per-sub-module seeding call entirely.
- `GetObjectiveTaskStatusesQueryHandler.cs` — remove the "no rows yet → copy template onto this
  Objective" fallback (lines 38-58). Replace with: look up the Project template rows directly
  (`GetProjectTemplateAsync`, already exists on `ITaskStatusRepository`) and return those. Existing
  per-Objective copies already in the database from before this change are simply never read again —
  no cleanup migration needed, they're orphaned but harmless (not referenced by any FK from `WorkTask`
  status transitions once this handler stops looking for them).
- Rename this query/handler to reflect the new scope — `GetProjectTaskStatuses` (`ProjectId` param,
  not `ObjectiveId`) — since "get an Objective's statuses" no longer makes sense as a concept. Update
  the route from `GET /work/objectives/{objectiveId}/task-statuses` to
  `GET /work/projects/{projectId}/task-statuses`. Grep every caller of the old route/query before
  removing it.

**Command handlers to re-scope** (`CreateTaskStatus`, `EditTaskStatus`, `DeleteTaskStatus`,
`ReorderTaskStatuses`) — currently take an `ObjectiveId` and operate on that Objective's copy. Change
each to take a `ProjectId` and operate directly on the Project-template rows (`ObjectiveId == null`).
Authorization changes too: today these presumably check the Objective's owner (verify each handler
individually — not confirmed during this design's research); the Project-scoped version should check
**Project-level access** — reuse whatever the Project Settings / member-management authorization
already uses elsewhere in this module (e.g. how `EditProjectCommandHandler` or
`AddProjectMemberCommandHandler` gate Project-level changes), not Objective ownership. Load the actual
current checks before writing the plan's tasks — this design doesn't assume a specific existing
pattern here since it wasn't verified.

**`MoveTaskStatusCommandHandler`** — line 64 currently checks `newStatus.ObjectiveId != task.ObjectiveId`
to validate the target status belongs to the task's Objective. Since statuses no longer carry a
meaningful `ObjectiveId`, change this to `newStatus.ProjectId != task.ProjectId` (verify `WorkTask` has
`ProjectId` directly — it does, per `WorkTask.cs`).

**`ObjectiveId` column on `TaskStatus`** stays in the schema (no migration) but becomes effectively
unused going forward — every row written after this change has `ObjectiveId == null`. Note this
explicitly in the entity's doc comment so a future reader doesn't wonder why it's always null.

## 3. Task completion rule (confirmed, already exists — no new work)

The private-status gate in `MoveTaskStatusCommandHandler` (`Visibility == Private` → only the
Objective's effective manager can move a task into it, everyone else can move into any `public`
status) already matches what the user wants. It rides on the cascading-ownership design
(`2026-08-21-work-management-cascading-objective-ownership-design.md`) for the "or a parent Objective's
member" part — no separate work needed here beyond that companion design's §4 change to this same
handler.

## 4. Task Category — converts `TaskType` from hardcoded enum to per-Project table

**Assumption to confirm before implementation:** the user's ask ("already there's a default category,
make it configurable") is interpreted here as *replacing* `WorkTaskTypes`'s 4 hardcoded values
(Task/Bug/Story/Feature) with a Project-configurable list — not adding an unrelated new field
alongside `TaskType`. If that's wrong, flag it before the plan is executed.

**New entity** `TaskCategory` (`src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs`),
same shape as `TaskStatus` minus the `ObjectiveId`/`Visibility`/`RequiresApproval`/`ApproverId`/
`MarksTaskComplete` fields it doesn't need:

```csharp
public class TaskCategory : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
```

**Migration required** (new table, unlike Task Status which reuses existing rows): `task_categories`
table + EF migration. Seed 4 default rows (Task/Bug/Story/Feature, matching today's `WorkTaskTypes`
values so existing tasks have somewhere to land) at Project-creation time, same pattern as
`DefaultTaskStatusTemplate`.

**`WorkTask` change:** add `public Guid CategoryId { get; set; }` (required, mirrors `StatusId`'s
shape), replacing `TaskType` (string). Migration must backfill every existing `WorkTask` row's new
`CategoryId` from its current `TaskType` string value, mapped to that Project's seeded default
category with the matching name — write this backfill as part of the same EF migration, not a
follow-up script, so no task is ever left with a null category mid-migration.

**New CRUD commands/queries**, mirroring Task Status's shape exactly but Project-scoped from the
start (no per-Objective legacy to strip out, unlike Task Status): `GetProjectTaskCategories`,
`CreateTaskCategory`, `EditTaskCategory`, `DeleteTaskCategory`, `ReorderTaskCategories`. Same
authorization question as §2 — Project-level access, verify the actual existing pattern before writing
tasks.

**Every place that currently reads/writes `WorkTask.TaskType`** (task create, task edit, task list/board
column grouping if any, task detail responses) needs to switch to `CategoryId` — grep
`\.TaskType\b` and `WorkTaskTypes` across `src/` before finalizing the task list; not enumerated
exhaustively here since it wasn't traced call-by-call during this design.

## 5. Postman docs

Every new/changed endpoint gets a doc under `docs/postman-request/Work Management/` per
`PROCESS_RULES.md` rule 6 — new Project-scoped Task Status routes, and all new Task Category routes.

## 6. Frontend (companion, `Hrms--Web-application---front-end---v1`)

User confirmed the Task Status editor UI (`BoardStructureEditorComponent`) itself is fine as-is — it
just needs to be driven by the new Project-scoped API instead of the Objective-scoped one, and moved
out of `ObjectiveSettingsComponent` (currently routed under an Objective/`milestoneId`) into a new
**Project-level Settings page** with two tabs: Task Status (existing editor, re-pointed at the new API)
and Task Category (new, same editor pattern reused/adapted for the simpler `TaskCategory` shape — no
`Visibility`/`RequiresApproval`/`ApproverId`/`MarksTaskComplete` fields to edit). Where exactly this
Settings page lives in the Project's navigation (a new tab on the Project detail page? a gear icon
like the tree's existing per-row settings?) is not yet decided — resolve this with the user when
writing the frontend implementation plan, it wasn't specified in the chat brainstorm.

## 7. Out of scope

The cascading-ownership authorization rework (separate design doc) and the tree-tab UI bug-fix bucket
(module filter, missing delete icon, status icon/text overlap) — independent pieces, not covered here.
