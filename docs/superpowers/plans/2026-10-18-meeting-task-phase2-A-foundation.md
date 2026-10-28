# Meetings as Tasks — Phase 2, Plan A: Foundation ("Release A" safe floor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the schema, the fail-closed `ExcludeMeetings` task filter, the database guards, the concurrency-safe occurrence insert and the `job_leases` primitive, with every meeting feature switched off. This becomes the safe floor that Release B (Plans B–E) builds on.

**Architecture:** A meeting occurrence is a `tasks` row with `task_kind = 'meeting'`, plus a 1:1 `meeting_details` row and a `meeting_series` row. In this plan no code path creates meetings outside tests. The plan proves three things on real Postgres:
- existing task queries can never see a meeting;
- the new tables are tenant-isolated;
- the §5.6 insert SQL is correct.

**Tech Stack:** .NET 10, EF Core 10.0.9 + Npgsql 10.0.2 (snake_case), xUnit + Moq, Testcontainers (`MyTeamDb.CreateAsync()` harness), PostgreSQL RLS (`onevo_app` is NOBYPASSRLS).

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` (§2.2, §4, §5.1 architecture tests, §5.5 generator rules, §5.6, §8).

**Branch:** create `feature/meeting-task-phase2-a` from `feature/meeting-phase0-port` (plus this spec commit). Worktree: `C:\Users\User\onexso-worktrees\be-phase2`.

## Global Constraints

- `task_kind` value for a meeting occurrence: `"meeting"` (add `WorkTaskKinds.Meeting = "meeting"`).
- Query filter name constants: `WorkTaskQueryFilters.ExcludeMeetings = "ExcludeMeetings"` and `WorkTaskQueryFilters.TenantScope = "TenantScope"`.
- `UNIQUE (series_id, original_start)` on `meeting_details` is **NOT DEFERRABLE**. Constraint name: `uq_meeting_details_series_original_start`.
- Advisory lock key: `hashtextextended($tenant_id::text || ':' || $series_id::text, 0)`, taken with `pg_advisory_xact_lock`.
- Every new tenant table gets `ENABLE` + `FORCE ROW LEVEL SECURITY` and the `tenant_isolation` policy, in the exact text used by `20261014082513_AddReportingManagerChangeHistory.cs`. `job_leases` is platform-level: no RLS, and it is allow-listed.
- Migrations are additive only. Existing rows must satisfy every new CHECK. Add CHECKs as `NOT VALID`, then `VALIDATE`.
- No feature is reachable in this plan. Every flag defaults to off: `Meetings:Create`, `Meetings:BulkInvite`, `Meetings:Background`, `Meetings:ReadOnly`.
- `Meetings:Attendance` defaults: `Checkpoints = ["00:30:00","02:00:00","24:00:00"]`, `MatchWindowBefore = "01:00:00"`, `LateRecheckWindow = "7.00:00:00"`, `FailedRetryAfter = "3.00:00:00"`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A task query that bypasses the filter.** An existing `IgnoreQueryFilters()` on `WorkTasks` would return meeting rows (as in the restore-by-id path). The expected behaviour is that meetings stay invisible. Pinned in Task 2.
2. **A status or category referenced only by meeting tasks gets deleted.** The FK from meeting tasks must still block the delete. The existing "any task uses this status" checks must keep counting meeting rows. Pinned in Task 2.
3. **A deploy over real data.** Existing calendar rows must satisfy the owner CHECKs, and the migration must not fail on a populated database. Pinned in Task 5 (migration on seeded data).
4. **A generator racing a split.** Two transactions must never leave an orphan meeting task (a task with no `meeting_details` row). Pinned in Task 6, case 3.
5. **A lease that expires mid-run.** A second instance takes over, and the first instance's renewal must fail rather than silently keep running. Pinned in Task 7.

---

## File structure

| Area | Files |
|---|---|
| Domain | `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs` (constants); new `src/ONEVO.Domain/Features/WorkManagement/Meetings/Entities/{MeetingSeries,MeetingDetails,MeetingOccurrenceSuppression,MeetingOccurrenceAttendee,MeetingSeriesLink,MeetingAttendanceSession,MeetingActionItem}.cs`; new `src/ONEVO.Domain/Features/SharedPlatform/Entities/JobLease.cs`; calendar entities `CalendarEventParticipant`, `CalendarEventGuest`, `CalendarEventMeeting`, `CalendarEventMeetingAttendance`, `ExternalCalendarEventLink` (new owner columns) |
| Persistence | `ApplicationDbContext.cs` (named-filter composition, DbSets); `Configurations/WorkManagement/WorkTaskConfiguration.cs`; new `Configurations/WorkManagement/Meetings/*Configuration.cs`; new `Configurations/SharedPlatform/JobLeaseConfiguration.cs`; changes to the calendar configurations; `Repositories/WorkManagement/EfWorkTaskRepository.cs` (bypass sites) |
| SQL | Migration `AddMeetingWorkItemsFoundation` (tables, columns, indexes, CHECKs, triggers, RLS); `ops/meetings/phase2-preflight.sql` |
| Services | `Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingOccurrenceWriter.cs` + `Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfMeetingOccurrenceWriter.cs`; `Application/Common/ServiceInterfaces/IJobLeaseService.cs` + `Infrastructure/Services/SharedPlatform/Jobs/JobLeaseService.cs`; `Application/Features/WorkManagement/Meetings/MeetingsOptions.cs` + validator + `Infrastructure/Services/WorkManagement/Meetings/MeetingsStartupCheck.cs` |
| Tests | `tests/ONEVO.Tests.Architecture/MeetingQueryFilterArchitectureTests.cs`; `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/{ExcludeMeetingsIntegrationTests,MeetingSchemaIntegrationTests,MeetingOccurrenceWriterIntegrationTests}.cs`; `tests/ONEVO.Tests.Integration/SharedPlatform/JobLeaseIntegrationTests.cs`; `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingsOptionsValidatorTests.cs` |

---

### Task 1: The `ExcludeMeetings` named filter

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (composition loop, lines ~357-419)
- Test: `tests/ONEVO.Tests.Architecture/MeetingQueryFilterArchitectureTests.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ExcludeMeetingsIntegrationTests.cs`

**Interfaces:**
- Produces: `WorkTaskKinds.Meeting`; `static class WorkTaskQueryFilters { const string ExcludeMeetings = "ExcludeMeetings"; const string TenantScope = "TenantScope"; }` (in `WorkTask.cs`). A query that must see meetings calls `.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings])` and keeps tenant scoping.

- [ ] **Step 1: Write the failing model test**

In `MeetingQueryFilterArchitectureTests` (reuse `TenantIsolationArchitectureTests`' model-inspection context helper, or copy its `CreateModelInspectionContext`):

```csharp
[Fact]
public void WorkTask_DeclaresTheTenantScopeAndExcludeMeetingsFiltersByName()
{
    using var context = CreateModelInspectionContext();
    var filters = context.Model.FindEntityType(typeof(WorkTask))!.GetDeclaredQueryFilters();
    Assert.Contains(filters, f => f.Key == WorkTaskQueryFilters.ExcludeMeetings);
    Assert.Contains(filters, f => f.Key == WorkTaskQueryFilters.TenantScope);
    Assert.DoesNotContain(filters, f => f.Key is null); // never mix anonymous and named filters
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~MeetingQueryFilterArchitectureTests`
Expected: FAIL. The constants do not exist yet, so this is a compile failure. Add the constants first, then re-run and expect an assertion failure.

- [ ] **Step 3: Implement**

1. In `WorkTaskConfiguration.Configure`, call `builder.HasQueryFilter(WorkTaskQueryFilters.ExcludeMeetings, t => t.TaskKind != WorkTaskKinds.Meeting)`.
2. In `ApplicationDbContext.OnModelCreating`, handle an entity that already declares a named filter (`entityType.GetDeclaredQueryFilters().Any(f => f.Key is not null)`). For such an entity, register the composed tenant/soft-delete lambda with `HasQueryFilter(WorkTaskQueryFilters.TenantScope, composed)` instead of the anonymous overload, and compose it only with the anonymous filter, if any.
3. Leave every other entity on the anonymous path, unchanged. `TenantIsolationArchitectureTests` already enumerate `GetDeclaredQueryFilters()`, so those tests stay valid.

- [ ] **Step 4: Write the failing integration test**

In `ExcludeMeetingsIntegrationTests`, using `MyTeamDb.CreateAsync()`:
- Add a project and module with `h.AddProject` / `h.AddModule`.
- Insert one standard task and one task with `TaskKind = "meeting"` through the EF context.

Assert:
```csharp
Assert.Equal(new[] { standard.Id }, await db.WorkTasks.Select(t => t.Id).ToListAsync());
Assert.Null(await new EfWorkTaskRepository(db).GetByIdForTenantAsync(h.TenantId, meeting.Id));
var both = await db.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).Select(t => t.Id).ToListAsync();
Assert.Equal(2, both.Count);
// Tenant scoping survives ignoring ExcludeMeetings: a second tenant's meeting stays invisible.
```

- [ ] **Step 5: Run both tests, expect PASS; run the full architecture suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture` → Expected: all pass (718 + new).
Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~ExcludeMeetingsIntegrationTests` → Expected: PASS.

- [ ] **Step 6: Commit**

`feat(work-tasks): ExcludeMeetings named query filter; WorkTask keeps tenant scoping as a named filter`

---

### Task 2: Existing filter bypasses and the guard tests

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs`. The bare `IgnoreQueryFilters()` calls are at lines 23, 283 and 290.
- Test: `tests/ONEVO.Tests.Architecture/MeetingQueryFilterArchitectureTests.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/ExcludeMeetingsIntegrationTests.cs`

**Interfaces:**
- Consumes: `WorkTaskQueryFilters` (Task 1).
- Produces: `MeetingQueryFilterArchitectureTests.BareIgnoreAllowList`. Later plans add their own deliberate bypass sites to this allow-list.

- [ ] **Step 1: Write the failing architecture tests**

1. `NoBareIgnoreQueryFiltersOnWorkTasks_OutsideTheAllowList`
   - Scan `src/**/*.cs` (not Migrations) for `WorkTasks` followed by `.IgnoreQueryFilters()` on the same statement.
   - The allow-list is exactly:
     - `EfWorkTaskRepository.AnyActiveByStatusIdAsync`
     - `EfWorkTaskRepository.AnyActiveByCategoryIdAsync`
     - `DapiDueTodayTaskSeeder.cs`
     - `WorkManagementDapiDemoSeeder.Tasks.cs`
   - These keep counting meeting rows, so a status or category that meeting tasks reference cannot be deleted.
2. `IgnoringExcludeMeetings_IsOnlyInAllowListedFiles`
   - Scan for `IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings` or a raw `"ExcludeMeetings"`.
   - The allow-list starts empty. Plans B–E add their files, each paired with `MeetingVisibility.ForCaller` (spec §5.1).
3. `RawSqlOnTasks_IsOnlyInAllowListedFiles`
   - Scan for `ExecuteSql`, `SqlQuery` or `FromSql` whose interpolated text contains `\btasks\b` (case-insensitive).
   - The allow-list starts with `EfMeetingOccurrenceWriter.cs` (Task 6).

- [ ] **Step 2: Run them**

Expected: test 1 FAILS on `EfWorkTaskRepository.cs:23` (`GetTrackedByIdForTenantIncludingDeletedAsync`). Tests 2 and 3 PASS.

- [ ] **Step 3: Implement**

Change `GetTrackedByIdForTenantIncludingDeletedAsync` to `.IgnoreQueryFilters([WorkTaskQueryFilters.TenantScope])`. It still bypasses soft-delete, still re-applies `TenantId` by hand, and keeps excluding meetings. Add a one-line comment above each of the two allow-listed bare calls: they count meeting rows on purpose (FK safety).

- [ ] **Step 4: Add the behaviour tests (integration)**

1. `IncludingDeletedLookup_StillHidesMeetings`: a soft-deleted meeting task → `GetTrackedByIdForTenantIncludingDeletedAsync` returns null.
2. `StatusUsedOnlyByAMeetingTask_CountsAsInUse`: `AnyActiveByStatusIdAsync` is true when the only task using the status is a meeting task.

- [ ] **Step 5: Run the architecture suite and the two integration tests; expect PASS**

- [ ] **Step 6: Commit**

`fix(work-tasks): filter bypasses keep meetings hidden; guard tests for every bypass`

---

### Task 3: Phase 2 entities and EF configurations

**Files:**
- Create: the 7 meeting entities (see File structure) plus `JobLease`.
- Create: their configurations.
- Modify: `ApplicationDbContext.cs` (DbSets).
- Test: `tests/ONEVO.Tests.Architecture/MeetingQueryFilterArchitectureTests.cs` (model assertions).

**Interfaces:**
- Produces: the entities below (all except `JobLease` implement `ITenantOwnedEntity`), with these columns (spec §4.1):
  - `MeetingSeries`: `Id, TenantId, ProjectId, ObjectiveId, OrganizerEmployeeId, Title, Description, Location, IsPrivate, DtStart (DateTimeOffset), DurationMinutes (int), IsAllDay, Timezone, RecurrenceRule (string?), CancelledAt, CreationPath (string, 20), ApprovalRequestId (Guid?), SplitFromSeriesId (Guid?), SplitReason (string?, 20), SplitAt, Revision (int), CreatedAt, CreatedById, UpdatedAt`; constants `MeetingCreationPaths { Direct="direct", Approval="approval", CalendarOffice="calendar_office" }`, `MeetingSplitReasons { EditFollowing="edit_following", MoveForward="move_forward", MoveSingle="move_single" }`.
  - `MeetingDetails`: key `TaskId`; `TenantId, SeriesId, OriginalStart, StartAt, EndAt, IsAllDay, Location, IsPrivate, IsDetached, CancelledAt, Revision, CalendarEventMeetingId (Guid?), AttendanceStatus (string?, 30), AttendanceNextCheckAt, AttendanceCheckpoint (int), AttendanceFinalizedAt, AttendanceLastError (string?, 40)`.
  - `MeetingOccurrenceSuppression`: key (`SeriesId`, `OriginalStart`); `TenantId, Reason (string, 20), MovedToTaskId, CreatedByEmployeeId, CreatedAt`.
  - `MeetingOccurrenceAttendee`: `Id, TenantId, OccurrenceTaskId, AttendeeKind ("employee"|"guest"), EmployeeId?, Email?, Change ("add"|"remove"), ResponseStatus?, ResponseReason?, CreatedAt`.
  - `MeetingSeriesLink`: `Id, TenantId, SeriesId, CalendarEventMeetingId, Origin ("created"|"inherited_on_split"), AttachedAt, ReleasedAt?`.
  - `MeetingAttendanceSession`: `Id, TenantId, CalendarEventMeetingId, ProviderSessionId (string, 255), SessionStart, SessionEnd?, OccurrenceTaskId?, CreatedAt`.
  - `MeetingActionItem`: `Id, TenantId, OccurrenceTaskId, TaskId, CreatedByEmployeeId, CreatedAt`.
  - `JobLease`: key `JobName (string, 100)`; `OwnerInstanceId (string, 100), LeaseUntil, HeartbeatAt`.
- Configurations declare the uniques of spec §4.1:
  - `meeting_details`: `uq_meeting_details_series_original_start` (a plain unique constraint via `HasAlternateKey` or a raw constraint in Task 5; it must be a **constraint**, not just an index, for `ON CONFLICT ON CONSTRAINT`).
  - `meeting_series`: UNIQUE `approval_request_id` filtered NOT NULL.
  - `meeting_series_links`: UNIQUE `series_id` filtered `released_at IS NULL`; UNIQUE (`series_id`, `calendar_event_meeting_id`).
  - `meeting_attendance_sessions`: UNIQUE (`calendar_event_meeting_id`, `provider_session_id`).
  - `meeting_action_items`: UNIQUE `task_id`.
  - `meeting_occurrence_attendees`: UNIQUE (`occurrence_task_id`, `employee_id`) filtered `attendee_kind = 'employee'`; unique on (`occurrence_task_id`, lower(`email`)) in raw SQL in Task 5.
- Composite FKs (`tenant_id`, …) are declared in Task 5's SQL.

- [ ] **Step 1: Write the failing model test**

`MeetingEntities_AreTenantFilteredAndMapToTheSpecTables`. For each of the 7 meeting entity types assert:
- the table names are `meeting_series`, `meeting_details`, `meeting_occurrence_suppressions`, `meeting_occurrence_attendees`, `meeting_series_links`, `meeting_attendance_sessions` and `meeting_action_items`;
- `GetDeclaredQueryFilters().Count > 0`.

Assert that `JobLease` maps to `job_leases` and has no query filter.

- [ ] **Step 2: Run it; expect FAIL** (compile error first, then assertion)

- [ ] **Step 3: Create the entities, configurations and DbSets**

Lengths: as listed above. `TenantIsolationArchitectureTests.EveryTenantOwnedEntity_*` must keep passing; `JobLease` is not `ITenantOwnedEntity`.

- [ ] **Step 4: Run the architecture suite; expect PASS**

- [ ] **Step 5: Commit**

`feat(meetings): Phase 2 entities and EF mappings (no behaviour yet)`

---

### Task 4: Owner columns on the calendar child tables

**Files:**
- Modify: entities and configurations of `CalendarEventParticipant`, `CalendarEventGuest`, `CalendarEventMeeting`, `CalendarEventMeetingAttendance`, `ExternalCalendarEventLink`.
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/*`. The existing unit suite must stay green; no new behaviour.

**Interfaces:**
- Produces:
  - `CalendarEventParticipant.EventId` and `CalendarEventGuest.EventId` become `Guid?`; new `Guid? MeetingSeriesId` on both.
  - `CalendarEventMeeting.CalendarEventId` becomes `Guid?`; new `string OwnerKind` (default `CalendarMeetingOwnerKinds.CalendarEvent = "calendar_event"`; `MeetingSeries = "meeting_series"`).
  - `CalendarEventMeetingAttendance`: new `Guid? OccurrenceTaskId`, `Guid? AttendanceSessionId`, `string? ParticipantKey` (255).
  - `ExternalCalendarEventLink.CalendarEventId` becomes `Guid?`; new `Guid? MeetingTaskId`.

- [ ] **Step 1: Make the property changes and fix every compile error**

Fix each error with `.Value` / `!` only where the code path is legacy-only, i.e. where the owner is a calendar event by construction. Do **not** change any query predicate.

- [ ] **Step 2: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit` → Expected: 6228/6228 (the port baseline) plus any new tests.

- [ ] **Step 3: Commit**

`refactor(calendar): nullable event owner + Phase 2 owner columns on calendar child tables (no behaviour change)`

---

### Task 5: The foundation migration (tables, constraints, triggers, RLS)

**Files:**
- Create: migration `AddMeetingWorkItemsFoundation`. Generate it with `dotnet ef migrations add` (env `ConnectionStrings__MigrationConnection` built from `.env`'s `ONEVO_DB_*` migrator values; never print them), then append raw SQL.
- Create: `ops/meetings/phase2-preflight.sql`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingSchemaIntegrationTests.cs`

**Interfaces:**
- Produces, in the database:
  - Unique indexes `ux_tasks_tenant_id_id`, `ux_objectives_tenant_project_id`, `ux_calendar_event_meetings_tenant_id_id`.
  - Composite FKs per spec §4.1.
  - CHECKs:
    - `ck_calendar_event_participants_one_owner`: `num_nonnulls(event_id, meeting_series_id) = 1`.
    - `ck_calendar_event_guests_one_owner`: the same.
    - `ck_calendar_event_meetings_owner`: (`owner_kind='calendar_event' AND calendar_event_id IS NOT NULL`) OR (`owner_kind='meeting_series' AND calendar_event_id IS NULL`).
    - `ck_external_calendar_event_links_one_owner`.
    - `ck_meeting_occurrence_attendees_kind`.
  - Triggers:
    - `trg_meeting_details_key_immutable` (BEFORE UPDATE; the exact function text is in spec §5.6).
    - `trg_meeting_task_has_details`: a DEFERRABLE INITIALLY DEFERRED constraint trigger on `tasks` AFTER INSERT/UPDATE OF `task_kind`, raising when `task_kind='meeting'` and no `meeting_details` row exists.
    - `trg_meeting_details_has_meeting_task`: deferred, on `meeting_details` AFTER INSERT, raising when the task is not `task_kind='meeting'`.
    - `trg_meeting_placement_matches_series`: deferred, on `tasks` AFTER UPDATE OF `project_id`, `objective_id`, on `meeting_details` AFTER INSERT/UPDATE OF `series_id`, and on `meeting_series` AFTER UPDATE OF `project_id`, `objective_id`; it raises when a meeting task's (`project_id`, `objective_id`) differs from its series.
  - RLS on the 7 meeting tables.

- [ ] **Step 1: Write the failing schema tests** (Testcontainers; the DB is migrated by the harness)

1. `MeetingDetailsUnique_IsNotDeferrable`: `SELECT condeferrable FROM pg_constraint WHERE conname = 'uq_meeting_details_series_original_start'` → `false`.
2. `EveryMeetingTable_HasForcedRowLevelSecurity`: for each of the 7 tables, `relrowsecurity AND relforcerowsecurity` is true in `pg_class`.
3. `OneOwnerChecks_RejectBothOrNeither`: inserting a participant with both owners, or neither, raises `23514`.
4. `ATenantContextSeesOnlyItsOwnMeetingRows`: insert a series per tenant as admin; in tenant A's context, select from `meeting_series` → only A's row.
5. `CompositeFk_RejectsAnotherTenantsSeries`: `meeting_details` pointing at tenant B's series from tenant A → `23503`.
6. `MeetingTaskWithoutDetails_FailsAtCommit`: insert a `tasks` row with `task_kind='meeting'` and no details, then commit → the exception mentions `trg_meeting_task_has_details`.
7. `PlacementMismatch_FailsAtCommit`.
8. `MigrationOverPopulatedCalendarData_Succeeds`:
   - Seed participants, guests, meetings and links with the legacy owner.
   - Run the down migration, then re-run up. Use the EF migrator: `db.GetService<IMigrator>().MigrateAsync("<previous>")` then up.
   - Assert all CHECKs are `convalidated`.

- [ ] **Step 2: Run them; expect FAIL** (missing tables and constraints)

- [ ] **Step 3: Write the migration**

1. The EF-generated part creates tables, columns and simple indexes.
2. Append `migrationBuilder.Sql` blocks for: the composite unique indexes, the composite FKs, the CHECKs (`NOT VALID` then `VALIDATE CONSTRAINT`), the trigger functions and triggers, `uq_meeting_details_series_original_start` as `ALTER TABLE ... ADD CONSTRAINT ... UNIQUE (series_id, original_start)`, and the lower(email) unique index.
3. Add RLS via the same `foreach (var table in TenantTables)` block as `20261014082513_AddReportingManagerChangeHistory.cs`.
4. `Down()` drops everything this migration added and restores `NOT NULL` on the four owner columns. It first raises if any row with a non-null `meeting_series_id`, `meeting_task_id` or `owner_kind = 'meeting_series'` exists. That raise is the production guard, spec §8.

- [ ] **Step 4: Write `ops/meetings/phase2-preflight.sql`**

A read-only script that counts the rows that would violate each new CHECK or unique (expected 0 for each), and prints a one-line summary per check.

- [ ] **Step 5: Run the schema tests; expect PASS. Run the full integration suite**

Run: `dotnet test tests/ONEVO.Tests.Integration` → Expected: 595 + new tests pass. The only failures allowed are the 2 known My Team tests: `LedWorkProgressIntegrationTests.Lead_sees_rolled_up_counts_member_sees_nothing` and `MyTeamCapabilitiesIntegrationTests.CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row`.

- [ ] **Step 6: Run `dotnet ef migrations has-pending-model-changes`; expect "No changes"**

- [ ] **Step 7: Commit**

`feat(meetings): foundation migration - tables, composite FKs, owner CHECKs, deferred triggers, RLS, preflight script`

---

### Task 6: Concurrency-safe occurrence insert (spec §5.6) — closes the open SQL item

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/RepositoryInterfaces/IMeetingOccurrenceWriter.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Meetings/EfMeetingOccurrenceWriter.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingOccurrenceWriterIntegrationTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record MeetingOccurrenceDraft(
    Guid TenantId, Guid SeriesId, Guid ProjectId, Guid ObjectiveId, Guid CategoryId, Guid StatusId,
    string ShortId, string Title, string? Description, Guid CreatedById,
    DateTimeOffset OriginalStart, DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsAllDay,
    string? Location, bool IsPrivate, Guid? CalendarEventMeetingId);

public interface IMeetingOccurrenceWriter
{
    /// Takes the series lock (xact) - call inside a transaction.
    Task LockSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct);
    /// Inserts the occurrence unless (series, original_start) exists or is suppressed.
    /// Returns the task id when it inserted, null when it did nothing.
    Task<Guid?> TryMaterializeAsync(MeetingOccurrenceDraft draft, CancellationToken ct);
    /// Remaps occurrences into an EMPTY target series in one statement (split/move).
    Task RemapIntoSeriesAsync(Guid tenantId, Guid targetSeriesId,
        IReadOnlyList<(Guid TaskId, DateTimeOffset NewOriginalStart, DateTimeOffset NewStartAt, DateTimeOffset NewEndAt)> mapping,
        CancellationToken ct);
}
```
- Also produces `public static class MeetingOccurrenceIds { public static Guid For(Guid seriesId, DateTimeOffset originalStart); }` in `src/ONEVO.Application/Features/WorkManagement/Meetings/MeetingOccurrenceIds.cs`: the deterministic first-materialization id (spec §5.5). It returns `DeterministicGuid.Create(Namespace, $"{seriesId:N}|{originalStart.ToUniversalTime():O}")`, where `Namespace` is a new fixed GUID constant in that class. This is the same scheme as `GetCalendarEventsQueryHandler`'s virtual occurrence ids, and Plan C's calendar merge uses it so a virtual occurrence and its later materialized task share one id. Unit test it in the same task (`MeetingOccurrenceIdsTests`): the same inputs give the same id, a different start or series gives a different id, and the same instant in a different offset gives the same id.
- `TryMaterializeAsync` executes the exact CTE from spec §5.6. In the CTE, `RETURNING id` maps to the method result.
- `RemapIntoSeriesAsync` first checks that the mapping's new starts are strictly increasing and distinct (throws `InvalidOperationException` otherwise), then runs one `UPDATE ... FROM (VALUES ...)`.

- [ ] **Step 1: Write the 8 failing tests** (spec §5.6 list; each on its own fresh database)

1. `Constraint_IsNotDeferrable`. Same check as Task 5 test 1, kept here so this file proves the whole §5.6 contract.
2. `Materialize_Twice_InsertsOnce`: the first call returns the id; the second returns null; exactly one `tasks` row and one `meeting_details` row.
3. `SixteenParallelWriters_WithoutTheLock_OneRow_NoOrphanTask`:
   - Run 16 `Task.Run` transactions, each calling `TryMaterializeAsync` **without** `LockSeriesAsync`, then commit.
   - Retry any transaction that fails with the deferred-trigger error, once.
   - Assert 1 details row and 0 `tasks` rows with `task_kind='meeting'` lacking details.
4. `SixteenParallelWriters_WithTheLock_NoAborts`: the same, with `LockSeriesAsync` first → no exceptions at all.
5. `Remap_WithATimeShift_KeepsTaskIds`:
   - Materialize 7 occurrences on series S1.
   - Create an empty S2.
   - Remap all 7 with starts shifted by +1 h.
   - Assert the same 7 task ids now have `series_id = S2` and the new starts; S1 has 0 rows; a new materialize on S1 at an old start succeeds.
6. `SameSeriesKeyChange_Raises`: an `UPDATE meeting_details SET original_start = original_start + interval '1 day'` → the exception names `trg_meeting_details_key_immutable`.
7. `Remap_RejectsNonIncreasingStarts`: a mapping with duplicate starts → `InvalidOperationException`; nothing changed.
8. `Suppression_BlocksMaterialize_EvenUnderConcurrency`:
   - Insert a suppression for (S, t).
   - 8 parallel writers try to materialize (S, t).
   - All return null; 0 rows.

- [ ] **Step 2: Run them; expect FAIL** (types missing)

- [ ] **Step 3: Implement `EfMeetingOccurrenceWriter`**

Use `Database.SqlQuery<Guid>` with interpolation for the CTE. Register it as a scoped service in `Infrastructure/DependencyInjection.cs`. Add `EfMeetingOccurrenceWriter.cs` to the Task 2 raw-SQL allow-list.

- [ ] **Step 4: Run them; expect all 8 PASS**

Then update spec §5.6's status line to: "Verified 2026-10-xx by `MeetingOccurrenceWriterIntegrationTests` (8/8)". Mark it as frozen in the spec header.

- [ ] **Step 5: Commit**

`feat(meetings): concurrency-safe occurrence insert and remap; spec 5.6 verified on Postgres`

---

### Task 7: `job_leases` primitive

**Files:**
- Create: `src/ONEVO.Application/Common/ServiceInterfaces/IJobLeaseService.cs`
- Create: `src/ONEVO.Infrastructure/Services/SharedPlatform/Jobs/JobLeaseService.cs`
- Test: `tests/ONEVO.Tests.Integration/SharedPlatform/JobLeaseIntegrationTests.cs`

**Interfaces:**
```csharp
public interface IJobLeaseService
{
    /// True when this instance now holds the lease until now + ttl (new or renewed).
    Task<bool> TryAcquireAsync(string jobName, TimeSpan ttl, CancellationToken ct);
    /// Releases only if this instance holds it.
    Task ReleaseAsync(string jobName, CancellationToken ct);
    string InstanceId { get; } // "{MachineName}:{ProcessId}:{Guid}" fixed per process
}
```
- `TryAcquireAsync`:
  1. Runs `INSERT ... ON CONFLICT (job_name) DO NOTHING`.
  2. Then runs `UPDATE job_leases SET owner_instance_id=@me, lease_until=now()+@ttl, heartbeat_at=now() WHERE job_name=@j AND (lease_until < now() OR owner_instance_id=@me) RETURNING 1`.
- Admin tenant-context mode is not needed (the table has no RLS). Each call runs in its own short transaction.

- [ ] **Step 1: Write the failing tests**

1. `FirstCaller_Acquires_SecondInstance_IsRefused`: two services with different `InstanceId`s.
2. `Holder_Renews`: the same instance calling again → true, and `lease_until` moves forward.
3. `ExpiredLease_IsTakenOver_AndTheOldHolderCannotRenew`:
   - ttl = 1 s.
   - Wait for expiry (poll `lease_until < now()`).
   - Instance B acquires → true.
   - Instance A's renew → false.
4. `Release_ByNonHolder_DoesNothing`.

- [ ] **Step 2: Run them; expect FAIL**

- [ ] **Step 3: Implement and register the service** (singleton, using `IServiceScopeFactory` for the DbContext)

- [ ] **Step 4: Run them; expect PASS**

- [ ] **Step 5: Commit**

`feat(platform): job_leases primitive for multi-instance background jobs (SCL-01)`

---

### Task 8: Meetings options, flags and startup checks

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Meetings/MeetingsOptions.cs` (+ `MeetingsOptionsValidator : IValidateOptions<MeetingsOptions>`)
- Create: `src/ONEVO.Infrastructure/Services/WorkManagement/Meetings/MeetingsStartupCheck.cs` (`IHostedService`, StartAsync only)
- Modify: `src/ONEVO.Api/appsettings.json` (the `Meetings` section with the Global Constraints defaults; all flags false)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Meetings/MeetingsOptionsValidatorTests.cs`

**Interfaces:**
```csharp
public sealed class MeetingsOptions
{
    public const string Section = "Meetings";
    public bool Create { get; init; }
    public bool BulkInvite { get; init; }
    public bool Background { get; init; }
    public bool ReadOnly { get; init; }
    public MeetingAttendanceOptions Attendance { get; init; } = new();
}
public sealed class MeetingAttendanceOptions
{
    public TimeSpan[] Checkpoints { get; init; } = [TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)];
    public TimeSpan MatchWindowBefore { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan LateRecheckWindow { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan FailedRetryAfter { get; init; } = TimeSpan.FromDays(3);
}
```

- [ ] **Step 1: Write the failing validator tests**

1. `Defaults_AreValid`.
2. `Checkpoints_MustBeStrictlyIncreasingAndPositive`: `[2h, 30m]` and `[0s]` both fail, and the message names `Meetings:Attendance:Checkpoints`.
3. `EmptyCheckpoints_Fail`.
4. `NonPositiveWindows_Fail` (for each of the three windows).

- [ ] **Step 2: Run them; expect FAIL**

- [ ] **Step 3: Implement**

1. The validator.
2. Bind with `AddOptions<MeetingsOptions>().Bind(...).ValidateOnStart()`.
3. `MeetingsStartupCheck` throws `InvalidOperationException("Meetings:BulkInvite needs the outbox claim fix (P-1)")` when `BulkInvite` is true and `information_schema.columns` has no `outbox_messages.locked_by` column. This is P-1's column, spec §9.
4. Use `IWritableTenantContext.SetAdminMode()` for that query.

- [ ] **Step 4: Run them; expect PASS**

Add one integration test, `BulkInviteWithoutP1_FailsStartup`, using the integration harness DB (no `locked_by` column on this branch) → `StartAsync` throws.

- [ ] **Step 5: Commit**

`feat(meetings): options, flags (all off) and startup checks`

---

### Task 9: Release A verification

- [ ] **Step 1: Run every suite**
  - `dotnet test tests/ONEVO.Tests.Unit` → all pass.
  - `dotnet test tests/ONEVO.Tests.Architecture` → all pass.
  - `dotnet test tests/ONEVO.Tests.Integration` → all pass except the 2 known My Team tests (names in Task 5).

  Write the counts in the commit message of Step 3.

- [ ] **Step 2: Run the preflight script against the local dev database**

  As the migrator, run `ops/meetings/phase2-preflight.sql` → every count is 0.

- [ ] **Step 3: Write the Release A runbook**

  Create `docs/runbooks/meetings-phase2-release.md`, covering:
  - the release order P-1 → A → B;
  - **never roll back below Release A once meeting rows exist**;
  - the preflight script;
  - all flags stay off in A.

  Commit as `docs(meetings): Release A runbook and verification`.

- [ ] **Step 4: Push the branch** (no PR unless asked)
