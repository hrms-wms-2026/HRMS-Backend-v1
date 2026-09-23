# Part 3: `WorkTask.CategoryId` migration, backfill, and call-site wiring

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-project-scoped-task-status-and-category-design.md`
§4. **Hard prerequisite: Part 2 in this same folder must be shipped first** — this Part's backfill reads
the `TaskCategory` rows Part 2's migration/seeding creates.

**Scope guard:** Work Management module only.

## Goal

Replace `WorkTask.TaskType` (a free-text string, validated against the hardcoded `WorkTaskTypes`
constants) with `WorkTask.CategoryId` (`Guid`, FK to the new `TaskCategory` table from Part 2). Every
existing task must end up with a non-null `CategoryId` pointing at its Project's category row whose
`Name` matches its old `TaskType` value exactly (`Task`/`Bug`/`Story`/`Feature`) — the migration must not
leave any task with a null category mid-flight.

## Current state (verified)

`WorkTask.TaskType` is `string`, default `WorkTaskTypes.Task`
(`src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs:34,36`).
`grep -rln "TaskType\|WorkTaskTypes" src/` (run this again before starting — Part 2 may have shifted line
numbers in files this grep also matches, e.g. if `WorkTaskTypes` constants get referenced anywhere new)
currently returns these **31** files. Of them, 9 are historical migration `.Designer.cs` files —
**never edit past migrations**, they're excluded from this Part's work entirely:
`20260818085904_AddSprintManualOverride.Designer.cs`, `20260818000001_AddTaskEditRequestsRlsPolicy.Designer.cs`,
`20260817000004_AddTaskEditRequests.Designer.cs`, `20260817000003_AddWorkTaskSprintId.Designer.cs`,
`20260817000002_AddSprints.Designer.cs`, `20260817000001_AddTaskStatusVisibility.Designer.cs`,
`20260816190309_AddNotificationFoundation.Designer.cs`, `20260816184051_AddTaskCreationRequests.Designer.cs`,
`20260816182551_AddTaskFoundationTables.Designer.cs`, plus `ApplicationDbContextModelSnapshot.cs` (this
one **does** get regenerated, but automatically by the `dotnet ef migrations add` command in Task 1
below — never hand-edit it).

**The remaining 21 real call sites**, grouped by what kind of change each needs:

- **Entity + config (2):** `WorkTask.cs`, `WorkTaskConfiguration.cs`.
- **Create/Edit Task command chain (6):** `Commands/CreateTask/CreateTaskCommand.cs`,
  `CreateTaskCommandHandler.cs`, `CreateTaskCommandValidator.cs`, `Commands/EditTask/EditTaskCommandHandler.cs`,
  `Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommand.cs`,
  `CreateTaskCreationRequestCommandHandler.cs`, `CreateTaskCreationRequestCommandValidator.cs`,
  `DTOs/TaskCreationRequestPayload.cs`, `Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs`,
  `Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs` (this group is larger than "6"
  suggests — count every file in the list above under this bullet, there are 10).
- **API contracts + response shapes (4):** `Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`,
  `TaskCreationRequestContracts.cs`, `WorkTaskViewModelMapper.cs`,
  `Application/.../DTOs/Responses/WorkTaskResponse.cs`.
- **Read queries (2):** `Queries/GetSprintTasks/GetSprintTasksQueryHandler.cs`,
  `Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`.
- **Controller (1):** `Api/Controllers/Tenant/WorkManagement/TasksController.cs`.
- **Demo data seeders (2):** `Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs`,
  `WorkManagementDapiDemoData.cs` — these seed the `dapi` demo tenant referenced elsewhere in this
  project's docs; update them to assign a real seeded `TaskCategory.Id` instead of a `TaskType` string,
  don't skip these just because they're demo-only, a broken seeder blocks every future demo-data session.

## Task 1: Migration — add `CategoryId`, backfill, drop `TaskType`

Add `Guid CategoryId` to `WorkTask.cs` (`src/ONEVO.Domain/.../Entities/WorkTask.cs`), remove
`TaskType`/`WorkTaskTypes`. Update `WorkTaskConfiguration.cs` — remove any `TaskType` column config,
add:
```csharp
builder.HasIndex(t => new { t.TenantId, t.ProjectId, t.CategoryId });
```
(read the existing file first for the exact index-naming convention this repo uses on `WorkTaskConfiguration`
and match it, rather than inventing a different name than the rest of the file's indexes use).

Generate the migration:
```bash
dotnet ef migrations add AddWorkTaskCategoryId --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

This produces an `AddColumn` for `category_id` (nullable at first, since it can't be `NOT NULL` before
the backfill runs) and a `DropColumn` for `task_type`. **Manually edit the generated migration's `Up()`**
to insert a backfill `migrationBuilder.Sql(...)` call between the `AddColumn` and the (separately added,
see below) `NOT NULL`-enforcing `AlterColumn`:

```sql
UPDATE tasks t
SET category_id = c.id
FROM task_categories c
WHERE t.project_id = c.project_id
  AND c.name = CASE t.task_type
    WHEN 'task' THEN 'Task'
    WHEN 'bug' THEN 'Bug'
    WHEN 'story' THEN 'Story'
    WHEN 'feature' THEN 'Feature'
  END;
```

Confirm the exact stored casing of `task_type` values in the database matches this `CASE` mapping before
relying on it — `WorkTaskTypes.Task = "task"` etc. are lowercase constants (already confirmed from
`WorkTask.cs`), while `DefaultTaskCategoryTemplate`'s seeded `TaskCategory.Name` values are capitalized
(`"Task"`, `"Bug"`, ...) — this mapping is required, not optional, the two are not the same string.

After the backfill `Sql(...)` call, add `migrationBuilder.AlterColumn<Guid>("category_id", ..., nullable:
false)` to enforce `NOT NULL` now that every row has a value, then the `DropColumn("task_type", ...)`.
Order matters: `AddColumn(category_id, nullable: true)` → backfill `Sql` → `AlterColumn(category_id,
nullable: false)` → `DropColumn(task_type)`, in that exact sequence within `Up()`.

Apply the migration locally against a database that has real seeded task data (not an empty one — an
empty database can't catch a backfill bug) and verify with a manual query that no `tasks` row has a null
`category_id` afterward.

## Task 2: Entity + config (already covered in Task 1 — this Task is the commit boundary)

Commit `WorkTask.cs` + `WorkTaskConfiguration.cs` + the migration files together as one commit — they're
one atomic schema change, don't split them.

## Task 3: Create/Edit Task command chain

Work through the 10 files listed under that bullet above, one commit per logical unit (e.g. Create path
together, Edit path together, the two Request/Approve pairs together — group by which files change
together for one user-facing action, matching this plan folder's Part 1 style). Worked example for the
Create path (`CreateTaskCommand.cs`/`CreateTaskCommandHandler.cs`) — every other file in this group
follows the same shape (`TaskType` param/field → `CategoryId`, `Guid`):

- `CreateTaskCommand.cs:8` — `string TaskType` → `Guid CategoryId` in the record's parameter list.
- `CreateTaskCommandHandler.cs:100` — `TaskType = request.TaskType` → `CategoryId = request.CategoryId`
  in the `WorkTask` object initializer; `:111` — `task.TaskType` → `task.CategoryId` in the response
  construction.
- `CreateTaskCommandValidator.cs` — replace whatever rule currently validates `TaskType` against
  `WorkTaskTypes`'s allowed values with a check that `CategoryId` resolves to a real `TaskCategory` row
  for this task's Project (this validator likely can't do a DB lookup itself — check whether validation
  of "does this ID actually exist" happens in the validator or the handler elsewhere in this codebase's
  convention, e.g. how `SprintId`/`StatusId` existence is validated on the same command, and match that
  exact pattern rather than inventing a new one).

Apply the same three-part shape (command param, handler construct+respond, validator existence-check) to
each of the other 9 files in this group, reading each one directly before editing — don't assume they're
identical to the Create path, `TaskCreationRequestPayload.cs` in particular stores this as serialized JSON
payload data (same pattern Part 5 of the tree/sprint/task unified view plan already handled for
`SprintId` in this exact file — read that Part's Task 2 for the precedent on how a payload field type
change was handled here before).

## Task 4: API contracts + response shapes

`TaskContracts.cs`, `TaskCreationRequestContracts.cs`, `WorkTaskViewModelMapper.cs`,
`WorkTaskResponse.cs` — same `string TaskType` → `Guid CategoryId` shape, read each file directly, this
is the API-boundary layer so also check whether the frontend DTO needs a category **name** alongside the
id for display (if `WorkTaskResponse` currently only exposes `TaskType` as a raw string with no separate
display-name lookup, decide during this Task whether the response should include `CategoryName` the same
way `TaskStatusResponse` doesn't need one — Category names aren't fixed strings anymore after this
change, so a raw `CategoryId` alone forces the frontend to cross-reference the categories list; check how
`StatusId`/status-name is currently handled on the same response type and mirror that exact pattern).

## Task 5: Read queries + controller

`GetSprintTasksQueryHandler.cs`, `GetObjectiveTasksQueryHandler.cs`, `TasksController.cs` — same
mechanical change, read each directly.

## Task 6: Demo seeders

`WorkManagementDapiDemoSeeder.Tasks.cs`, `WorkManagementDapiDemoData.cs` — these must run **after** Part 2's
category-seeding logic exists for the `dapi` tenant's projects (the seeders create their own Projects,
which go through the same `CreateProjectCommandHandler`/seeding path as any other project, so the
categories already exist by the time these files assign one — confirm this by reading how the seeder
currently references `TaskStatus` rows for the same tenant, and mirror that lookup pattern for
`TaskCategory`).

## Task 7: Full regression pass

1. `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`.
2. `dotnet build`.
3. `grep -rn "TaskType\|WorkTaskTypes" src/ONEVO.Application/ src/ONEVO.Api/ src/ONEVO.Domain/
   src/ONEVO.Infrastructure/Persistence/Configurations/ src/ONEVO.Infrastructure/Persistence/Seeders/`
   (deliberately excludes `src/ONEVO.Infrastructure/Migrations/` — old migrations legitimately still
   reference the old shape and must never be touched) — should return nothing.
4. Update every Postman doc under `docs/postman-request/Work Management/` whose request/response example
   shows `taskType` as a string — `grep -rln "taskType" "docs/postman-request/Work Management/"`.

## Definition of done

- Task 1's migration applies cleanly on a database with real task data and leaves zero null
  `category_id` rows.
- Tasks 2-6 committed (grouped sensibly, one commit per logically-atomic change).
- Task 7's regression pass is clean, including the exclusion-aware grep returning nothing.
