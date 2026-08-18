# Sub-agent prompt: dapi demo Task data (copy-paste this whole block)

Repo: `HRMS-Backend-v1`, branch `feature/work-management-milestone-membership` (current branch — stay
on it unless told otherwise). Scope guardrail: **Work Management module only** — don't touch
`organization`, `layouts/main-layout`, or any other module; a teammate owns those.

## Goal

The `dapi` dev tenant already has 22 demo employees and 5 fully-built Projects with 5-layer Objective
trees, seeded by `WorkManagementDapiDemoSeeder` (+ its `WorkManagementDapiDemoSeeder.Objectives.cs`
partial, driven by pure data in `WorkManagementDapiDemoData.cs`, all in
`src/ONEVO.Infrastructure/Persistence/Seeders/`). None of that has any Task data — `WorkTask`,
`TaskStatus`, and `TaskAssignment` are completely unseeded for all 5 projects. Extend the same seeder
with realistic demo Tasks (with statuses, types, priorities, assignees) across all 5 projects/Objective
trees so the Task Foundation feature (Board/Backlog/Approvals, shipped 2026-08-16/17) has real data to
demo instead of empty boards.

## Read this first — background you need before designing anything

- **Existing seeder pattern** (read all three files in full before writing anything):
  `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.cs`,
  `WorkManagementDapiDemoSeeder.Objectives.cs`, `WorkManagementDapiDemoData.cs`. Convention: pure
  declarative data in the `Data.cs`-style file, tree-walk seeding logic in a `partial class
  WorkManagementDapiDemoSeeder`, every inserted row keyed by a deterministic MD5 Guid via the
  existing `DeterministicGuid(string seed)` helper (seed strings look like
  `"dapi-demo:task:{projectKey}:{objectivePath}:{n}"`) so the whole thing is idempotent and safe to
  re-run on every dev boot (`IHostedService.StartAsync`, `!_environment.IsDevelopment() &&
  !_environment.IsEnvironment("Test")` early-return guard already in place — don't duplicate it,
  extend the existing `SeedAsync`).
- **Original design doc for the pattern this extends:**
  `docs/superpowers/plans/next/2026-08-12-work-management-dapi-demo-data-seeding.md` — read it for
  the full rationale (hours/date-containment algorithm, idempotency tests, etc.) even though it predates
  Task entities and doesn't cover them.
- **Constants already declared in `WorkManagementDapiDemoSeeder.cs`** — reuse, don't redeclare:
  `DapiTenantId = 6b0874ab-71db-401f-859f-bdd50c1317fb`, `DapiOwnerUserId =
  cd49a0c2-e978-4055-b8be-7d46a3727e94`, `DapiLegalEntityId = 57fecfe8-1c1e-4a82-be4b-2c8451436420`.
- **Entities** (`src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/`):
  - `WorkTask` (table `tasks`, C# name avoids colliding with `System.Threading.Tasks.Task`):
    `ProjectId`, `ParentTaskId` (nullable, subtasks — optional, skip unless you want extra realism),
    `ObjectiveId`, `ShortId` (format `"{Project.Identifier}-{n}"`, e.g. `"EPOS-1"` — see below),
    `Title`, `Description`, `TaskType` (`WorkTaskTypes.Task/Bug/Story/Feature`), `StatusId`,
    `Priority` (`WorkTaskPriorities.Low/Medium/High/Critical`), `StoryPoints` (nullable int),
    `DueDate` (nullable `DateOnly`), `EstimatedHours` (nullable decimal), `CompletedHours`,
    `ProgressPercent`, `StartedAt`/`CompletedAt` (nullable, set on "in-progress"/"done" demo rows for
    realism).
  - `TaskStatus` (aliased `TaskStatusEntity` in application code, table `task_statuses`):
    `ProjectId`, `ObjectiveId` (**nullable** — null = project-level template, set = that objective's
    own copy — see the critical gotcha below), `Name`, `DisplayOrder`, `RequiresApproval`,
    `ApproverId` (nullable), `MarksTaskComplete`.
  - `TaskAssignment` (table `task_assignments`, **not** a `BaseEntity` — no `TenantId`/`CreatedAt`
    etc., just `Id, TaskId, UserId, EmployeeId, AssignedById, AssignedAt`).
  - `ApplicationDbContext` DbSets: `db.WorkTasks`, `db.TaskStatuses`, `db.TaskAssignments`.

- **Critical gotcha — per-objective TaskStatus, not project template:**
  `CreateTaskCommandHandler` (`src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs:64-67`)
  calls `_statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct)` which queries strictly
  `WHERE ObjectiveId == objectiveId` (`EfTaskStatusRepository.cs`) — **no fallback to the
  project-level template**. The *only* place that fallback+lazy-copy exists is
  `GetObjectiveTaskStatusesQueryHandler` (called when the Board tab loads), which copies the
  project template into per-objective rows on first fetch. Since this seeder bypasses both handlers
  and inserts via EF directly, you must seed **both**:
  1. A project-level template (4 rows, `ObjectiveId = null`) per project — mirror exactly what
     `CreateProjectCommandHandler.cs` does for real projects (see lines ~252-258): `"To Do"`
     (DisplayOrder 0), `"In Process"` (1), `"Review"` (2), `"Done"` (3, `MarksTaskComplete = true`).
     The dapi seeder bypassed `CreateProjectCommandHandler` when it created these 5 Projects, so this
     template is currently **missing entirely** for all 5 — seed it now.
  2. A per-objective copy of those same 4 rows (`ObjectiveId` set) for every Objective that will
     receive Tasks, with fresh deterministic Guids — same `Name`/`DisplayOrder`/`MarksTaskComplete`
     values, mirroring `GetObjectiveTaskStatusesQueryHandler.cs:44-49`'s copy shape.

- **Allocation slack constraint** (`IObjectiveAllocationSlackCalculator`,
  `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ObjectiveAllocationSlackCalculator.cs`):
  `slack = objective.AllocatedHours - sum(active child Objectives' AllocatedHours) - sum(active Tasks'
  EstimatedHours under that objective)`. The real `CreateTaskCommandHandler` blocks
  (409/`InsufficientAllocationResponse`) if a new task's `EstimatedHours` would exceed this. The
  seeder bypasses that check too, but **keep demo data internally consistent with the same formula
  anyway** — only add Tasks under leaf Objectives (the depth-5 nodes with no children in
  `WorkManagementDapiDemoData.ProjectTrees` — `ComputeChildHours`/`ComputeChildDates` in
  `WorkManagementDapiDemoSeeder.Objectives.cs` already gives every leaf its own `AllocatedHours`), and
  keep `sum(EstimatedHours)` per leaf Objective comfortably under that Objective's `AllocatedHours` so
  the demo doesn't already look "full" (leaves room to demo the 409 case live by adding one more task
  through the UI).
- **`ShortId`/task numbering:** real creation calls
  `_projects.IncrementAndGetNextTaskNumberAsync(tenantId, objective.ProjectId, ct)`
  (`EfProjectRepository.cs:77`, increments `Project.NextTaskNumber` starting at 1) then formats
  `$"{project.Identifier}-{taskNumber}"`. Seeder should replicate this numbering deterministically
  (e.g. assign sequential numbers per project as you walk its leaf Objectives in a fixed order) and
  **update each seeded `Project.NextTaskNumber`** to `(last seeded number) + 1` so that the first task
  a real user creates through the UI afterward doesn't collide with a seeded `ShortId`.
- **Validator constraints** to respect for realism (`CreateTaskCommandValidator.cs`): `Title` non-empty
  ≤500 chars; `TaskType` ∈ {task, bug, story, feature}; `Priority` ∈ {low, medium, high, critical};
  `EstimatedHours` ≥ 0 if set.
- **Assignees:** use `WorkManagementDapiDemoData.Persons`/`PersonsByKey` (22-person roster, already
  built) — assign each seeded Task to 1 (occasionally 2) people who are already `ProjectMember`s of
  that Task's Objective (the existing `SeedProjectMemberAsync` already added the owner + any
  `ExtraMemberKeys` as members — pick assignees from among those, don't invent new members). Insert
  one `TaskAssignment` row per assignee (`AssignedById = DapiOwnerUserId`, `AssignedAt = now`).

## What to build

1. Follow **superpowers:brainstorming** then **superpowers:writing-plans** first — don't jump straight
   to code. Produce a short design covering: how many Tasks per leaf Objective (suggest 3-6, mixed
   types/priorities), the status distribution (some "To Do", some "In Process", a few "Done" with
   `CompletedAt`/`ProgressPercent = 100`/`CompletedHours = EstimatedHours` set for realism), and the
   `ShortId` numbering scheme. Save the plan under
   `docs/superpowers/plans/next/2026-08-17-work-management-task-data-seeding.md` (today's date),
   same format/rigor as the existing `2026-08-12-work-management-dapi-demo-data-seeding.md` (TDD
   task breakdown, idempotency tests, exact file diffs).
2. Implement as a new partial-class file, e.g.
   `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Tasks.cs`, wired into
   the existing `SeedAsync` (called once for the whole 5-project set after
   `SeedProjectsAndObjectivesAsync` in `WorkManagementDapiDemoSeeder.cs`, or inline within the same
   per-project loop in `.Objectives.cs` — your call, whichever reads cleaner). Add matching demo
   data (leaf-Objective → Task list) to `WorkManagementDapiDemoData.cs` following the existing
   `DemoObjectiveNode`-style declarative pattern.
3. Tests: extend `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs`
   (same `SqliteTestApplicationDbContext` harness already used there) with assertions for: project
   template TaskStatus count (4 × 5 projects = 20), per-objective TaskStatus rows exist for every leaf
   Objective that has Tasks, Task counts/ShortId uniqueness/sequential numbering per project,
   `Project.NextTaskNumber` correctly advanced, every Task's `EstimatedHours` ≤ its Objective's
   `AllocatedHours`, TaskAssignment rows only reference people who are already that Objective's
   `ProjectMember`s, and a re-run-twice idempotency test (mirror
   `SeedAsync_IsIdempotent_RunningTwiceProducesSameCounts`).
4. Run `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
   and the full `WorkManagement`-filtered suite before committing; commit with a message in the same
   style as the existing seeder commits (see `git log --oneline` in this repo for examples).

Use **superpowers:subagent-driven-development** or **superpowers:executing-plans** to run the plan
task-by-task once it's written and approved.
