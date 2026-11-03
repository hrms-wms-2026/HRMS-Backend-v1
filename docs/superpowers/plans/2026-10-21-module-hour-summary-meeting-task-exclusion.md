# Module Hour Summary — Meeting-Task Exclusion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `wm_module_hour_summaries.task_count` and `unestimated_task_count` from counting meeting occurrences, so the Team Progress dashboard and `GET /api/v1/work/led-progress` report only real work.

**Architecture:** The defect is entirely in PostgreSQL. `wm_refresh_module_hour_summary(uuid)` joins `tasks` without a `task_kind` predicate, so it cannot see EF Core's `ExcludeMeetings` query filter. The fix is a forward EF migration that `CREATE OR REPLACE`s the function body with a `task_kind` predicate on the join, plus a backfill that recomputes every module. No C# production code changes. The trigger wrapper `wm_tasks_refresh_module_hour_summary()` needs no change — it already delegates to the refresh function on insert/delete/update — but the UPDATE trigger's `WHEN` clause must additionally watch `task_kind` so a re-kind of an existing task recomputes the summary.

**Tech Stack:** .NET 10, EF Core 10 migrations with `migrationBuilder.Sql(...)`, Npgsql, PostgreSQL 16 (Testcontainers), xUnit integration tests.

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` (Phase 2, meetings-as-tasks). This plan is the follow-up to the Plan D branch `feature/meeting-task-phase2-d`.

## Global Constraints

- The change is **SQL-only**. No entity, `DbContext`, configuration, repository or service file is modified. `dotnet ef migrations has-pending-model-changes` must remain clean.
- The migration must be **safe on the Plan D branch alone** (where `wm_module_hour_summaries` does not exist) **and correct in the merged tree** (where it does). Guard the DDL with a catalog lookup.
- `task_kind` is `NOT NULL DEFAULT 'standard'` (`20261002180123_AddEmployeeChecklistWorkTasks`), so `IS DISTINCT FROM 'meeting'` is equivalent to `<> 'meeting'` here — but `IS DISTINCT FROM` is chosen so the predicate is also correct if a pre-default row ever lands with `NULL`.
- The exact meeting-kind literal is `'meeting'` (`WorkTaskKinds.Meeting`, `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs:19`).
- Do **not** restore any deleted production API, do not modify `origin/development` directly, and do not re-add `ListTopLevelProgressRowsAsync`.
- A migration timestamp **greater than `20261020133948`** (development's latest, `AddCustomizableDashboards`) is required so the fix sorts after `AddModuleHourSummaries` (`20261020074523`) in the merged migration sequence. `20261020074523 < 20261020133948`, so any timestamp above the latter satisfies it.
- The migration must remain valid when the Plan D branch is deployed without development's `AddModuleHourSummaries`: in that case it is a no-op.

## Review Focus

- A meeting occurrence inserted **after** the fix must not change any summary column — in particular it must not change `task_count` for the module, and it must not create a summary row for a module that has only meeting tasks.
- A module whose **only** task is a meeting occurrence must report `task_count = 0`, not `1`.
- A meeting occurrence whose `due_date` is set must not move `earliest_open_due_date` and must not make the module `AtRisk`.
- A task whose `task_kind` is changed from `'meeting'` to `'standard'` (or back) must cause the summary to recompute — this is why the UPDATE trigger's `WHEN` clause gains `task_kind`.
- A task whose `estimated_hours` is set **on** a meeting occurrence must not move `not_started_hours` / `in_progress_hours` / `completed_hours` — hours are affected by the same predicate, not only the counts.
- The backfill must be **idempotent**: running the migration twice (or the backfill by hand) must leave the same numbers.
- A tenant with meeting occurrences must not affect another tenant's summary row in the same database.

---

### Task 1: Pin the defect with failing regression tests

**Files:**
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ModuleHourSummaryMeetingExclusionIntegrationTests.cs`

**Interfaces:**
- Consumes: `MyTeamDb` (`tests/ONEVO.Tests.Integration/MyTeam/MyTeamDb.cs`) — `CreateAsync()`, `NewContext()`, `AddPlacement(db)`, `AddTask(db, projectId, objectiveId, statusId, progress, due, assigneeEmployeeId, estimatedHours)`, `AddSeries(db, placement)`, `AddOccurrence(db, series, placement)`, `AddTenantAsync()`. The helper `MeetingPlacement` from the My Team namespace.
- Produces: nothing consumed by later tasks; these are the pins the fix must satisfy.

The tests read `wm_module_hour_summaries` directly over `Npgsql` (the same pattern as `tests/ONEVO.Tests.Integration/MyTeam/LedWorkProgressIntegrationTests.cs` uses for its fixtures) so they assert on **database** state, not on an EF projection that could mask the trigger.

- [ ] **Step 1: Write the failing test**

Create the test file with a private helper that opens a `NpgsqlConnection` on `h.ConnectionString` and reads the summary row for an objective:

```csharp
private static async Task<(int Count, int Unestimated, decimal NotStarted, DateOnly? Earliest)?>
    ReadSummaryAsync(MyTeamDb h, Guid objectiveId)
```

returning `null` when no row exists. Then four facts:

**`MeetingOccurrence_DoesNotChange_TaskCountOrUnestimatedCount`**
Seed: a module (`AddPlacement`) with one standard task (`estimatedHours: 5`, `due: today.AddDays(3)`) and one meeting occurrence in the same module (`AddSeries` + `AddOccurrence`). Save, then read the summary.
Assert: `TaskCount == 1` and `UnestimatedTaskCount == 0` and `NotStarted == 5m`.

**`ModuleWithOnlyAMeetingOccurrence_ReportsZeroTasks`**
Seed: a module with only a meeting occurrence. Assert: a summary row exists with `TaskCount == 0` (or no row at all — accept either by asserting `row?.Count ?? 0 == 0`).

**`MeetingOccurrence_WithADueDate_DoesNotMoveEarliestDueDate_OrMakeTheModuleAtRisk`**
Seed: a standard task with `due: today.AddDays(10)` and a meeting occurrence whose `DueDate` is set to `today.AddDays(-2)`. Assert `Earliest == today.AddDays(10)`.

**`TheSummaryOfOneTenant_IsUnaffectedByAnotherTenantsMeetingOccurrences`**
Seed: tenant A with a standard task and a meeting occurrence; tenant B (`h.AddTenantAsync()`) with one standard task only. Assert tenant A's row has `TaskCount == 1` and tenant B's row has `TaskCount == 1`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~ModuleHourSummaryMeetingExclusionIntegrationTests"`

Expected: the first three FAIL (the observed database values are `task_count = 2`, `unestimated_task_count = 1`); the tenant-isolation test PASSES (tenant scoping was never the defect — it is a guard against the fix breaking it).

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ModuleHourSummaryMeetingExclusionIntegrationTests.cs
git commit -m "test(work): pin the module-hour-summary meeting-task leak"
```

---

### Task 2: The forward migration that excludes meeting tasks

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/20261021100000_ExcludeMeetingTasksFromModuleHourSummaries.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/20261021100000_ExcludeMeetingTasksFromModuleHourSummaries.Designer.cs`
- Modify: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` — **no change** (the model snapshot is unaffected by a SQL-only migration; regenerate it and confirm it is byte-identical)

**Interfaces:**
- Consumes: the existing database function `public.wm_refresh_module_hour_summary(uuid)` created by `20261020074523_AddModuleHourSummaries` on development.
- Produces: the same function with the meeting predicate; the UPDATE trigger `trg_tasks_module_hour_summary_update` widened to watch `task_kind`.

- [ ] **Step 1: Write the failing migration test**

Add to `ModuleHourSummaryMeetingExclusionIntegrationTests.cs`:

**`TheRefreshFunction_ExcludesMeetingTasks_And_WatchesTaskKindOnUpdate`**
Query `pg_proc` for `pg_get_functiondef` of `wm_refresh_module_hour_summary` and assert the body contains `task_kind`. Query `pg_trigger` for `trg_tasks_module_hour_summary_update` and assert its `pg_get_triggerdef` contains `task_kind`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~ModuleHourSummaryMeetingExclusionIntegrationTests"`

Expected: FAIL — the live function body contains no `task_kind`.

- [ ] **Step 3: Create the migration**

Create the migration with the EF tool **or** by hand. Timestamp `20261021100000` — greater than development's latest (`20261020133948`), so in the merged tree it sorts after `AddModuleHourSummaries`.

The `Up()` body is two `migrationBuilder.Sql(...)` calls.

**Up, call 1 — replace the function (guarded).** The guard makes the migration a no-op on the Plan D branch alone, where the function does not exist:

```csharp
migrationBuilder.Sql("""
    DO $do$
    BEGIN
        IF to_regclass('public.wm_refresh_module_hour_summary') IS NOT NULL THEN
            EXECUTE $fn$
                CREATE OR REPLACE FUNCTION public.wm_refresh_module_hour_summary(p_objective_id uuid)
                 RETURNS void LANGUAGE sql AS $body$
                    INSERT INTO wm_module_hour_summaries AS s
                        (id, tenant_id, project_id, not_started_hours, in_progress_hours, completed_hours,
                         task_count, unestimated_task_count, earliest_open_due_date,
                         created_at, updated_at, created_by_id, is_deleted)
                    SELECT
                        o.id, o.tenant_id, o.project_id,
                        COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.done AND t.progress_percent <= 0), 0),
                        COALESCE(SUM(t.estimated_hours) FILTER (WHERE NOT x.done AND t.progress_percent > 0), 0),
                        COALESCE(SUM(t.estimated_hours) FILTER (WHERE x.done), 0),
                        COUNT(t.id),
                        COUNT(t.id) FILTER (WHERE COALESCE(t.estimated_hours, 0) = 0),
                        MIN(t.due_date) FILTER (WHERE NOT x.done),
                        now(), now(), '00000000-0000-0000-0000-000000000000', false
                    FROM objectives o
                    LEFT JOIN tasks t
                        ON t.objective_id = o.id AND t.tenant_id = o.tenant_id
                        AND t.parent_task_id IS NULL AND NOT t.is_deleted
                        AND t.task_kind IS DISTINCT FROM 'meeting'
                    LEFT JOIN task_statuses st ON st.id = t.status_id
                    CROSS JOIN LATERAL (
                        SELECT (COALESCE(st.marks_task_complete, false) OR COALESCE(t.progress_percent, 0) >= 100) AS done
                    ) x
                    WHERE o.id = p_objective_id
                    GROUP BY o.id, o.tenant_id, o.project_id
                    ON CONFLICT (id) DO UPDATE SET
                        not_started_hours = EXCLUDED.not_started_hours,
                        in_progress_hours = EXCLUDED.in_progress_hours,
                        completed_hours = EXCLUDED.completed_hours,
                        task_count = EXCLUDED.task_count,
                        unestimated_task_count = EXCLUDED.unestimated_task_count,
                        earliest_open_due_date = EXCLUDED.earliest_open_due_date,
                        updated_at = now();
                $body$;
            $fn$;
        END IF;
    END
    $do$;
    """);
```

The **only** functional difference from the live body is the added line `AND t.task_kind IS DISTINCT FROM 'meeting'` in the `tasks` join. Everything else is copied verbatim from `20261020074523_AddModuleHourSummaries`.

**Up, call 2 — widen the UPDATE trigger (guarded).** `task_kind` is now a column the summary depends on, so a change to it must recompute:

```csharp
migrationBuilder.Sql("""
    DO $do$
    BEGIN
        IF to_regclass('public.trg_tasks_module_hour_summary_update') IS NOT NULL THEN
            DROP TRIGGER IF EXISTS trg_tasks_module_hour_summary_update ON tasks;
            CREATE TRIGGER trg_tasks_module_hour_summary_update
                AFTER UPDATE ON tasks
                FOR EACH ROW
                WHEN (OLD.objective_id IS DISTINCT FROM NEW.objective_id
                    OR OLD.status_id IS DISTINCT FROM NEW.status_id
                    OR OLD.estimated_hours IS DISTINCT FROM NEW.estimated_hours
                    OR OLD.progress_percent IS DISTINCT FROM NEW.progress_percent
                    OR OLD.due_date IS DISTINCT FROM NEW.due_date
                    OR OLD.parent_task_id IS DISTINCT FROM NEW.parent_task_id
                    OR OLD.is_deleted IS DISTINCT FROM NEW.is_deleted
                    OR OLD.task_kind IS DISTINCT FROM NEW.task_kind)
                EXECUTE FUNCTION wm_tasks_refresh_module_hour_summary();
        END IF;
    END
    $do$;
    """);
```

The trigger is dropped and recreated rather than altered because PostgreSQL has no `ALTER TRIGGER ... WHEN`; this is the only way to widen the `WHEN` clause.

**Up, call 3 — the backfill.** Recompute every module so existing rows are corrected:

```csharp
migrationBuilder.Sql("""
    DO $do$
    BEGIN
        IF to_regclass('public.wm_refresh_module_hour_summary') IS NOT NULL THEN
            PERFORM public.wm_refresh_module_hour_summary(o.id) FROM objectives o;
        END IF;
    END
    $do$;
    """);
```

This is idempotent — the refresh is a pure recomputation from `tasks`, so running it twice yields the same row.

- [ ] **Step 4: Generate the Designer file and snapshot**

Run the EF tool to produce the `.Designer.cs` and the snapshot entry. The snapshot must be **unchanged** from the current one — if it differs, the migration was not SQL-only.

Confirm: `git diff --stat src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` → empty.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~ModuleHourSummaryMeetingExclusionIntegrationTests"`

Expected: ALL PASS — the four Task 1 tests and the `pg_proc`/`pg_trigger` test.

- [ ] **Step 6: Verify the whole integration suite**

Run: `dotnet test tests/ONEVO.Tests.Integration`

Expected: the Task 1 tests pass; the two known pre-existing My Team failures remain the only failures (`MyTeamCapabilitiesIntegrationTests.CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row` and development's renamed `LedWorkProgressIntegrationTests.Lead_sees_rolled_up_hours_member_sees_nothing`); no new failure.

- [ ] **Step 7: Verify the EF model is unchanged**

Run: `.superpowers/tools/ef.sh migrations has-pending-model-changes`

Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/20261021100000_ExcludeMeetingTasksFromModuleHourSummaries.cs \
        src/ONEVO.Infrastructure/Migrations/20261021100000_ExcludeMeetingTasksFromModuleHourSummaries.Designer.cs \
        src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs \
        tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ModuleHourSummaryMeetingExclusionIntegrationTests.cs
git commit -m "fix(work): exclude meeting tasks from module hour summaries"
```

---

### Task 3: The end-to-end dashboard assertion

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ModuleHourSummaryMeetingExclusionIntegrationTests.cs`

**Interfaces:**
- Consumes: `GetLedWorkProgressQueryHandler` (`src/ONEVO.Application/Features/WorkManagement/Leadership/Queries/GetLedWorkProgress/`), `EfModuleHourSummaryRepository`, `WorkLeadershipService`, `WorkApprovalEligibility`, `MilestoneMembershipCoordinator`, `OutboxWriter`, `NoOpEncryptionService`, `SystemDateTimeProvider` — the same wiring as `tests/ONEVO.Tests.Integration/MyTeam/LedWorkProgressIntegrationTests.cs`.
- Produces: the assertion that the meeting exclusion reaches the API the team-progress widget mirrors.

- [ ] **Step 1: Write the failing test**

**`TheLedWorkProgressResponse_ExcludesTheMeetingOccurrence`**
Seed: a module with one standard task (`estimatedHours: 4`) and one meeting occurrence. Handle `GetLedWorkProgressQuery` as the module's lead. Assert on the module's `Hours`:
`TaskCount == 1`, `UnestimatedTaskCount == 0`, `NotStartedHours == 4m`, `AtRisk == false`, and `ModulesAtRisk == 0`.

Add a second case in the same test — **defence in depth** — that sets the meeting occurrence's `DueDate` to `today.AddDays(-2)` and asserts `AtRisk` is still `false` and `ModulesAtRisk` is still `0`. (Production never sets a due date on a meeting task; this pins the behaviour if that ever changes.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~ModuleHourSummaryMeetingExclusionIntegrationTests"`

Expected: on the unfixed tree it fails with `TaskCount` found `2`; on the fixed tree it passes.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ModuleHourSummaryMeetingExclusionIntegrationTests.cs
git commit -m "test(work): pin the led-progress response against meeting occurrences"
```

---

### Task 4: Rollback path

**Files:**
- Modify: `src/ONEVO.Infrastructure/Migrations/20261021100000_ExcludeMeetingTasksFromModuleHourSummaries.cs` — the `Down()` body

**Interfaces:**
- Consumes: the pre-fix function body (the `tasks` join **without** the `task_kind` predicate) and the pre-fix UPDATE trigger `WHEN` clause.

- [ ] **Step 1: Write `Down()`**

`Down()` restores the original function body verbatim (no `task_kind` predicate) and the original UPDATE trigger `WHEN` clause (no `task_kind` term), both guarded by the same `to_regclass` check, then re-runs the backfill so the summary rows match the restored definition:

```csharp
migrationBuilder.Sql("""...restored function without the task_kind predicate...""");
migrationBuilder.Sql("""...DROP + CREATE trg_tasks_module_hour_summary_update without the task_kind term...""");
migrationBuilder.Sql("""...PERFORM wm_refresh_module_hour_summary(o.id) FROM objectives o;...""");
```

- [ ] **Step 2: Verify the rollback runs**

Run the migration up, then down, then up again against a disposable database, asserting the numbers each time:
- after **up**: `task_count = 1`
- after **down**: `task_count = 2` (the meeting counts again — the pre-fix behaviour)
- after **up** again: `task_count = 1`

The second `up` must be a clean no-op-safe reapplication, proving `CREATE OR REPLACE` and the backfill are idempotent.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/20261021100000_ExcludeMeetingTasksFromModuleHourSummaries.cs
git commit -m "chore(work): restore the pre-fix module hour summary on rollback"
```

---

### Task 5: Docs and the Plan D ledger

**Files:**
- Modify: `docs/runbooks/meetings-phase2-release.md`
- Modify: `.superpowers/sdd/2026-10-19-meeting-task-phase2-D-action-items-bulk-attendance/progress.md` (git-ignored)

**Interfaces:**
- Consumes: the migration id `20261021100000_ExcludeMeetingTasksFromModuleHourSummaries` and the behaviour change.

- [ ] **Step 1: Update the release runbook**

Add a short section to `docs/runbooks/meetings-phase2-release.md` next to the existing meetings release notes: the Team Progress summary now excludes meeting occurrences, the migration is `20261021100000_ExcludeMeetingTasksFromModuleHourSummaries`, it is a no-op where `wm_module_hour_summaries` does not exist, and it backfills existing module rows on apply.

- [ ] **Step 2: Update the ledger**

Append a Task 7 row to the Plan D progress ledger recording: the defect reproduced against a real disposable database (`task_count = 2`, `unestimated_task_count = 1` with one standard task and one meeting occurrence), the fix migration id, the regression tests, and the verification commands with their results.

- [ ] **Step 3: Commit**

```bash
git add docs/runbooks/meetings-phase2-release.md
git commit -m "docs(meetings): record the module hour summary exclusion in the release runbook"
```

The ledger is git-ignored and is **not** committed.

---

## Rollback Summary

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet ef database update 20261021100000_ExcludeMeetingTasksFromModuleHourSummaries` | `task_count` drops from 2 to 1 |
| 2 | `dotnet ef database update 20261020133948_AddCustomizableDashboards` | `task_count` returns to 2 (pre-fix behaviour) |

Rollback is a plain `Down()` — it restores the prior function body and re-runs the backfill so the summary rows are consistent with the restored definition. No data is lost: the summary table is derived state and is fully recomputable from `tasks` at any time.

## Out of Scope

- Changing the trigger's *frequency* — the refresh is still per-row on insert/delete/update, plus the one-off backfill.
- Any change to the EF model, the `ModuleHourSummary` entity, or the `LedHourRollup` projection. The C# read path is already correct: it reads whatever the trigger wrote.
- The `Meetings:BulkInvite` P-1 outbox blocker, which remains unresolved and independent of this defect.
