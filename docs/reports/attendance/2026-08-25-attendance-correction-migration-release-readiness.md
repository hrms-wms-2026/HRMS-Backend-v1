**Date:** 2026-08-24 (initial pass) / 2026-08-25 (corrective index migration + automated regression coverage)
**Scope:** Resolve the two remaining Attendance Correction release blockers — EF model drift and PostgreSQL migration application — with independent verification; then close the two follow-up gaps this report itself flagged (missing FK-support indexes, no automated PostgreSQL regression coverage).
**Repository:** `HRMS-Backend-v1` (work confined to this repo; frontend untouched)

## Release-readiness decision

**Release-ready: model drift is resolved, migrations were applied to PostgreSQL, and focused PostgreSQL integration/RLS checks passed.**

This report went through two verdicts before this one. The first pass (2026-08-24) declared "Release-ready" while leaving five missing indexes as a "documented residual risk" and no automated Postgres tests — on review that was corrected to **"Functionally verified, but not release-ready"** per explicit direction. The corrective work in "Addendum — corrective index migration and automated regression coverage" below then closed both gaps and is independently verified: the fifth-index migration exists and is applied on both the shared dev database and a from-empty fresh database; `has-pending-model-changes` is clean on both; and 11 new Postgres-backed xunit integration tests (persistence, response-mapping, cross-tenant RLS enforcement on both read and write, a from-scratch migration-upgrade backfill test, and index existence) all pass. All four conditions the follow-up instructions required for a release-ready verdict are met.

## Initial state (before this pass)

```
git status --short --branch
```
Branch: `local/reporting-manager-run`. 18 pre-existing modified files and ~25 pre-existing untracked files/directories from unrelated, already-in-progress work (Time Tracking clock-in/break actions, notification contracts, employee list response, several report `.md` files). None of these were touched during this pass — see "Files changed" below for the single file this pass modified.

```
dotnet --list-sdks   → 9.0.314, 10.0.300, 10.0.302
dotnet ef --version  → 10.0.9
git diff --check     → clean (only pre-existing LF/CRLF advisory warnings, no whitespace/conflict errors)
```

## Part A — Approval-snapshot contract

Verified by reading source directly (not by trusting prior reports):

- `AttendanceCorrectionWorkflow.BuildCorrection` ([AttendanceCorrectionWorkflow.cs:417](src/ONEVO.Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs:417)) sets `ApprovalRequired = value.Policy.CorrectionRequiresApproval` once, at creation, independent of `Status`.
- `ToResponse` ([AttendanceCorrectionWorkflow.cs:541](src/ONEVO.Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs:541)) returns `correction.ApprovalRequired` directly for every call site (request, approve, reject, cancel, list-my, list-approvals).
- No occurrence anywhere in the workflow of `Status == Pending`, `ReviewedAt != null`, or `ReviewedById != null` being used to derive `ApprovalRequired`.

**Conclusion: already correct. Not rewritten**, per the instruction to leave correct code alone.

## Part B — EF pending-model-changes: root cause

### Command sequence and evidence

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release        → 0 errors, 0 warnings
dotnet ef migrations list ... --context ApplicationDbContext               → both attendance migrations discovered, (Pending)
dotnet ef migrations has-pending-model-changes ... --context ApplicationDbContext
   → "Changes have been made to the model since the last migration."
```

### Diagnostic migration

Generated a temporary migration, per the task's diagnostic-migration protocol:

```
dotnet ef migrations add DiagnosticPendingModelChanges_DoNotCommit `
  --output-dir Migrations/_DiagnosticDoNotCommit
```

Its `Up()` contained **exactly**:
- 5 `CreateIndex` operations: `ix_attendance_corrections_employee_id`, `_legal_entity_id`, `_presence_session_id`, `_requested_by_id`, `_reviewed_by_id`
- 6 `AddForeignKey` operations: to `attendance_records`, `employees`, `legal_entities`, `presence_sessions`, `users` (×2, for `requested_by_id`/`reviewed_by_id`)

Nothing else — no `CreateTable`, no `AddColumn`, no changes to any other entity.

### Root cause, isolated

Using `dotnet ef migrations remove` to undo the diagnostic migration (which resynchronizes `ApplicationDbContextModelSnapshot.cs` from the immediately-preceding real migration's own `.Designer.cs`) and re-running `has-pending-model-changes` afterward showed **"No changes have been made to the model."** This isolates the defect precisely:

- `20260824154945_AddAttendanceCorrectionApprovalRequired.Designer.cs` (the second migration's own point-in-time model snapshot) **already correctly recorded** the 5 FK-support indexes and the 6-relationship `HasOne(...)` block for `AttendanceCorrection` — confirmed by direct inspection.
- The shared `ApplicationDbContextModelSnapshot.cs` (the cumulative snapshot EF actually diffs against for `has-pending-model-changes`) had drifted out of sync with that migration's own Designer and was missing that same index/relationship metadata for `AttendanceCorrection`.
- `20260824120000_AddAttendanceCorrections.Designer.cs` (the **first** migration's own point-in-time snapshot) was independently found to be **incomplete in the same way**: it has the `AttendanceCorrection` property/column block but has *zero* `b.HasIndex` entries for the 5 FK columns and **no relationship (`b.HasOne`) block at all** — even though that migration's real `Up()` DOES create the FK constraints inline via `table.ForeignKey(...)` in `CreateTable`.

**Verdict:** the drift belongs entirely to the Attendance Corrections feature (no unrelated entity was involved — the diagnostic migration touched only `attendance_corrections`). It is bookkeeping/metadata drift in the migration history's model-snapshot files, not a defect in the actual applied-to-Postgres schema shape for columns/PK/FKs, which migration 1's real DDL already gets right.

### Fix applied

No hand-editing of the two real migrations or their Designer files was needed or performed. The single corrective action was the `dotnet ef migrations add` (diagnostic) → `dotnet ef migrations remove` round-trip, which is a supported EF CLI operation and which resynchronized `ApplicationDbContextModelSnapshot.cs`'s `AttendanceCorrection` block from `20260824154945_...Designer.cs`'s already-correct target model. The diagnostic migration's own files (`.cs`/`.Designer.cs`) and its output directory were fully removed; only the shared snapshot file's `AttendanceCorrection` section changed.

**Verification (post-fix, matches the task's required final check):**

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release        → 0 errors
dotnet ef migrations has-pending-model-changes ... --no-build
   → "No changes have been made to the model since the last migration."
dotnet ef migrations list ... --no-build
   → both attendance migrations discovered; grep count of "(Pending)" = 0 (after Part E's apply)
```

`git status --short` / `find ... -iname "*Diagnostic*"` confirm no diagnostic artifact remains; `git diff --check` remains clean.

## Files changed

### Initial pass (2026-08-24)

| File | Change |
|---|---|
| `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | `AttendanceCorrection` entity block corrected (5 FK-support indexes + 6-relationship block added) via `dotnet ef migrations add`/`remove` round-trip. No other entity in this file was touched. |

### Follow-up pass (2026-08-25) — see Addendum

| File | Change |
|---|---|
| `src/ONEVO.Infrastructure/Migrations/20260824180229_AddAttendanceCorrectionForeignKeyIndexes.cs` | New additive migration; hand-added the 5 `CreateIndex`/`DropIndex` operations after `dotnet ef migrations add` produced an empty `Up()`/`Down()` (expected, since the model already declared the indexes). |
| `src/ONEVO.Infrastructure/Migrations/20260824180229_AddAttendanceCorrectionForeignKeyIndexes.Designer.cs` | Generated by `dotnet ef migrations add`; unmodified. |
| `tests/ONEVO.Tests.Integration/Features/TimeAttendance/AttendanceCorrectionsIntegrationTests.cs` | New: 10 Postgres-backed xunit integration tests (items 1–7, 9–11 of the fix plan). |
| `tests/ONEVO.Tests.Integration/Features/TimeAttendance/AttendanceCorrectionsMigrationUpgradeTests.cs` | New: 1 Postgres-backed xunit integration test (item 8, migration-upgrade backfill). |

No other file was created, edited, or deleted by either pass. All 18 pre-existing modified files and ~25 pre-existing untracked files/directories from the prior, unrelated in-progress work were inspected and left untouched throughout.

## Part C — Migration SQL validation

Generated via `dotnet ef migrations script 20260822063849_AddBreakRecordOpenUniqueness 20260824154945_AddAttendanceCorrectionApprovalRequired` (the real migration-ID range discovered by EF) and inspected directly:

| Requirement | Result |
|---|---|
| Adds `approval_required boolean NOT NULL` | ✅ via `ADD COLUMN ... DEFAULT FALSE` then `DROP DEFAULT` |
| Safely handles existing rows before enforcing final non-null | ✅ default value populates existing rows first, backfill runs before `DROP DEFAULT` |
| Backfill: pending/rejected/cancelled/reviewed → `true`; else `false` | ✅ `WHERE status IN ('pending','rejected','cancelled') OR reviewed_by_id IS NOT NULL OR reviewed_at IS NOT NULL` |
| Does not infer future response behavior from workflow status | ✅ this SQL only classifies historical rows; confirmed in Part A that application code never derives the field from status |
| Does not drop/recreate `attendance_corrections` | ✅ table created once via `CREATE TABLE`, then only `ALTER TABLE ADD COLUMN` |
| Does not weaken RLS/FKs/unique index/audit fields | ✅ RLS enable+force+policy SQL unchanged from migration 1; all 6 FKs are `ON DELETE RESTRICT`; `ux_attendance_corrections_pending_record_type` unchanged |
| No unintended permanent default | ✅ confirmed live: `column_default` for `approval_required` is empty after migration |

## Part D — PostgreSQL authentication blocker (28P01)

**This blocker did not reproduce in this session.** Root-caused as follows:

- The prior reports' `28P01: password authentication failed for user "onevo_migrator"` and the earlier `GRANT onevo_auth_base_login_fn_owner TO onevo_migrator` failure were both tied to an incomplete/placeholder local `.env` and/or a not-yet-bootstrapped `onevo_migrator` role in that prior session's environment.
- In this session, the repo-root `.env` (gitignored, not committed) already contains non-placeholder values for `ONEVO_DB_HOST`, `ONEVO_DB_PORT`, `ONEVO_DB_NAME`, `ONEVO_DB_ADMIN_USER/PASSWORD`, `ONEVO_DB_APP_USER/PASSWORD`, `ONEVO_DB_MIGRATOR_USER/PASSWORD`.
- Before applying anything, the following was proven and is reported here with credentials masked:
  - **Target server:** `localhost:5432` (PostgreSQL 18.3), confirmed via `inet_server_addr()`/`inet_server_port()` — loopback only.
  - **Target database:** `OnevoDb` — the only non-template, non-`postgres` database on this server (confirmed via `pg_database`), matching `.env`'s `ONEVO_DB_NAME`. This is unambiguously the intended local development database, not staging/production.
  - **Migration role:** `onevo_migrator` (masked password), constructed exactly as `ops/postgres/setup-local-db.ps1` builds it: `Host=<host>;Port=<port>;Database=<database>;Username=onevo_migrator;Password=<masked>`.
  - **Migration role authenticated successfully** — `dotnet ef migrations list` with this connection string read `__EFMigrationsHistory` and returned 121 already-applied migrations plus 11 pending ones, before any change was made.
- Because the role and grants already resolve cleanly against this local server, **no role rotation, `pg_hba.conf` edit, or privilege escalation was performed or was necessary.** The one-time `local-bootstrap-roles.sql` re-run (idempotent, via the supported script) emitted a benign `NOTICE: role "onevo_migrator" has already been granted membership in role "onevo_auth_base_login_fn_owner"` — expected on a re-run, not an error.

## Part E — Migration application

Applied via the repository-supported flow, unmodified:

```powershell
powershell -ExecutionPolicy Bypass -File ops\postgres\setup-local-db.ps1 -RunMigrations
```

This applied **all 11 pending migrations** (not only the two Attendance Correction ones — the local dev database had fallen behind on an unrelated backlog from other in-progress work: `AddBiometricEnrollment`, `AddBiometricAndMeetingSignalsRlsPolicyCoverage`, `AddExceptions`, `AddExceptionsRlsPolicyCoverage`, `AddIdleThresholdMinutes`, `AddAttendanceReadModel`, `AddLeaveManagementSchema`, `AddLeaveManageToHrManagerTemplate`, `AddBreakRecordOpenUniqueness`, `AddAttendanceCorrections`, `AddAttendanceCorrectionApprovalRequired`). All 11 applied cleanly with no failures; the script's post-migration grants step also completed successfully.

### `__EFMigrationsHistory` verification

```sql
SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 5;
```
```
20260824154945_AddAttendanceCorrectionApprovalRequired
20260824120000_AddAttendanceCorrections
20260822063849_AddBreakRecordOpenUniqueness
20260821155743_AddLeaveManageToHrManagerTemplate
20260821155653_AddLeaveManagementSchema
```
Both Attendance Correction migrations are present. `dotnet ef migrations list --no-build` afterward shows **zero** remaining `(Pending)` entries.

### Live schema verification

| Check | Result |
|---|---|
| `approval_required` column | `boolean`, `is_nullable = NO` ✅ |
| Column default after migration | empty/none ✅ (no unintended permanent default) |
| Row count | 0 (fresh table; no employee data exposed) |
| Indexes present | `ix_attendance_corrections_attendance_record_id`, `ix_attendance_corrections_tenant_employee_work_date_type`, `ix_..._tenant_legal_entity_employee_created_*`, `ix_..._tenant_legal_entity_status_created_at`, `pk_attendance_corrections`, `ux_attendance_corrections_pending_record_type` — all 6 present |
| Foreign keys | all 6 present, all `ON DELETE RESTRICT`, referencing `attendance_records`, `employees`, `legal_entities`, `presence_sessions`, `users` (×2) |
| RLS enabled/forced | `relrowsecurity = t`, `relforcerowsecurity = t` |
| RLS policy | `tenant_isolation` present, identical predicate pattern to every other tenant table in this codebase |

### RLS enforcement proof (live, with test-created rows only)

Two throwaway rows were inserted (as the `postgres` superuser, which bypasses RLS) referencing real FK targets from the existing local dev seed tenants `acme` and `dapi` (synthetic dev/test accounts, not real employee PII — see prior session memory on these seeded dev tenants). Queried as the restricted `onevo_app` role with `SET app.tenant_context_mode = 'tenant'`:

| Session tenant context | Rows visible |
|---|---|
| `acme`'s tenant id | 1 (the `acme` row only) |
| `dapi`'s tenant id | 1 (the `dapi` row only) |
| An unrelated random tenant id | 0 |
| Cross-tenant `UPDATE` attempt (session=`dapi`, targeting the `acme` row's id) | `UPDATE 0` — silently filtered, not an error |

Both rows were then deleted as `postgres`; a final `SELECT count(*)` confirms 0 rows remain in `attendance_corrections` — the database was left exactly as found.

### Restricted-role privilege check

```sql
SELECT privilege_type FROM information_schema.role_table_grants
WHERE table_name='attendance_corrections' AND grantee='onevo_app';
```
→ exactly `DELETE, INSERT, SELECT, UPDATE` — no DDL, no `TRUNCATE`/`REFERENCES`/`TRIGGER`. `pg_roles` confirms neither `onevo_app` nor `onevo_migrator` is superuser or `BYPASSRLS`.

## Part F — PostgreSQL integration coverage

**Superseded by the Addendum below.** At the time this section was first written, Docker was unreachable in this environment and the verification described here was done by hand against live PostgreSQL instead of via xunit. Docker subsequently became available mid-session; the "Addendum" section documents the 11 xunit integration tests that were then written and are now passing, closing the gap this section originally flagged. The manual verification below remains valid and is kept for the record.

This pass performed the equivalent verification **directly against live PostgreSQL** (documented above under Part E): schema/column/index/FK shape, RLS enforcement with real cross-tenant proof and write-path isolation, and restricted-role privilege boundaries — all with test-created, cleaned-up data, no real employee data exposed.

## Part G — Unit and architecture tests

Fresh Release build performed first (not `--no-build` from stale binaries):

```
dotnet build src\ONEVO.Domain\ONEVO.Domain.csproj --configuration Release             → 0 errors
dotnet build src\ONEVO.Application\ONEVO.Application.csproj --configuration Release   → 0 errors
dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --configuration Release → 0 errors
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release                   → 0 errors, 2 pre-existing unrelated warnings (PositionsController.cs, AdminAuthController.cs)
```

| Suite | Result |
|---|---|
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release` (full suite) | **2938 passed**, 0 failed |
| Focused `FullyQualifiedName~AttendanceCorrection` (unit) | **17 passed**, 0 failed |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release` (full suite) | **658 passed**, 0 failed |
| Focused `FullyQualifiedName~AttendanceCorrection` (architecture) | **7 passed**, 0 failed |

Existing `AttendanceCorrectionsArchitectureTests.cs` already covers: controller tenant scoping/route, approval-permission gating on review actions, self-service actions not requiring approval permission, no tenant/employee accepted from body or route, no EF Core dependency in the Application assembly, the approval-snapshot migration adding a non-null column with backfill, and the migration declaring RLS + the pending-unique index. `AttendanceCorrectionNotificationTests.cs` already covers creation-true/false, pending/approved/rejected/cancelled preserving `true`, auto-approved staying `false`, and response mapping never deriving from status. These already satisfy the Part G invariants; **not rewritten**, per the instruction to leave correct code alone.

`dotnet ef migrations has-pending-model-changes --no-build` and `dotnet ef migrations list --no-build` re-run after these builds (see Part B) confirm the fix is stable against fresh binaries, not stale ones.

## Final verification commands run

```
dotnet build src\ONEVO.Domain\ONEVO.Domain.csproj --configuration Release
dotnet build src\ONEVO.Application\ONEVO.Application.csproj --configuration Release
dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --configuration Release
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release
dotnet ef migrations list --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj --context ApplicationDbContext --no-build
dotnet ef migrations has-pending-model-changes --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj --context ApplicationDbContext --no-build
git diff --check
git status --short
```
All passed / clean, as detailed above.

## Skipped checks and exact blockers

None remaining. The two items originally listed here (Docker unreachable; no xunit Postgres integration suite) were both resolved once Docker became available mid-session — see the Addendum.

## Remaining risks

1. The local dev database (`OnevoDb`) had 11 unrelated pending migrations queued behind the two Attendance Correction ones, from other concurrently in-progress feature work on the same branch. Applying them was unavoidable (EF applies pending migrations in order) and all succeeded, but that work's own correctness is outside this task's scope.
2. The corrective index migration (`20260824180229_AddAttendanceCorrectionForeignKeyIndexes`) was added *after* the original two Attendance Correction migrations, all three of which are still unapplied to any database except this session's local `OnevoDb` and disposable throwaway containers. If any other environment already applied only the first two migrations before this fix landed, it will pick up the third additively with no conflict — confirmed by the from-empty fresh-database test in the Addendum, which applies all three in sequence with no manual intervention.

See the Addendum immediately below for the full account of the corrective index migration and the new automated test suite.

---

## Addendum — corrective index migration and automated regression coverage (2026-08-25)

This addendum documents the follow-up pass that closed the two gaps the original report left as "residual risk": the five missing FK-support indexes, and the absence of automated PostgreSQL integration tests. It follows the five-part fix plan given for this follow-up exactly.

### Part 1 — Corrective index migration

```powershell
dotnet ef migrations add AddAttendanceCorrectionForeignKeyIndexes `
  --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj `
  --startup-project src\ONEVO.Api\ONEVO.Api.csproj `
  --context ApplicationDbContext
```

As expected, this produced an **empty** `Up()`/`Down()` (the model already declares the indexes, so there was nothing left to diff) and left `ApplicationDbContextModelSnapshot.cs` byte-for-byte unchanged — confirmed via `git diff --stat` before and after, both showing the same 182-line diff against `HEAD` as before this migration was added. The five `CreateIndex`/`DropIndex` operations were then added to `Up()`/`Down()` by hand, matching exactly the operations the earlier diagnostic migration had revealed:

- `ix_attendance_corrections_employee_id`
- `ix_attendance_corrections_legal_entity_id`
- `ix_attendance_corrections_presence_session_id`
- `ix_attendance_corrections_requested_by_id`
- `ix_attendance_corrections_reviewed_by_id`

Before adding them, `pg_indexes` was checked against the live `OnevoDb` (already migrated through the first two Attendance Correction migrations at that point) and confirmed none of the five existed under any name. The two existing migrations were not modified; no duplicate indexes were created; no foreign keys, RLS, workflow code, or API contracts were touched.

Applied to `OnevoDb`:
```
dotnet ef database update ... → Applying migration '20260824180229_AddAttendanceCorrectionForeignKeyIndexes'. Done.
```
`pg_indexes` afterward shows all 11 indexes on `attendance_corrections` (the original 6 plus these 5). `dotnet ef migrations has-pending-model-changes --no-build` remains clean.

### Part 2 — Reproducibility on a genuinely fresh database

Docker became available partway through this session (it had been unreachable during the initial 2026-08-24 pass; `docker version`/`docker ps` now both succeed against a running Docker Desktop 4.84.0 engine). A disposable container was used to prove the full migration chain applies cleanly from empty, not just to the already-partially-migrated `OnevoDb`:

```bash
docker run -d --name onevo-migration-verify -e POSTGRES_PASSWORD=<masked> -p 55433:5432 postgres:16-alpine
```

Roles (`onevo_migrator`, `onevo_app`, `onevo_auth_base_login_fn_owner`) and the same grants `ops/postgres/local-bootstrap-roles.sql` applies were bootstrapped by hand against a fresh `onevo_verify` database, then:

```powershell
dotnet ef database update --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj
```

applied **the entire migration history from empty** (well over 100 migrations, ending in the new index migration) with no failures, including `20260724174557_AddAuthLookupBaseLoginCandidatesFunction`'s `ALTER FUNCTION ... OWNER TO onevo_auth_base_login_fn_owner` step that a missing role/grant would have broken.

Verified on this fresh database:

| Check | Result |
|---|---|
| All 5 corrective indexes exist | ✅ (11 total indexes on `attendance_corrections`, same as `OnevoDb`) |
| Both Attendance Correction migrations + the index migration in `__EFMigrationsHistory` | ✅ all 3 present, in order |
| `approval_required` non-null | ✅ `boolean`, `is_nullable = NO` |
| All 6 FKs remain `ON DELETE RESTRICT` | ✅ |
| Pending-request unique index remains | ✅ `ux_attendance_corrections_pending_record_type`, same definition |
| RLS enabled and forced | ✅ `relrowsecurity = t`, `relforcerowsecurity = t` |
| `tenant_isolation` policy installed | ✅ |
| `dotnet ef migrations has-pending-model-changes --no-build` | ✅ clean |

The throwaway container was removed afterward (`docker rm -f onevo-migration-verify`); `OnevoDb` was not affected by this part.

### Part 3 — Automated PostgreSQL integration tests

Two new xunit test classes were added under `tests/ONEVO.Tests.Integration/Features/TimeAttendance/`, both real-Postgres-backed via Testcontainers (no EF InMemory, no mocked persistence):

**`AttendanceCorrectionsIntegrationTests`** (10 tests, HTTP-fixture-based) reuses — rather than duplicates — the minimal, already-proven tenant-provisioning and direct-DbContext employee-seeding helpers from `LegalEntitiesIntegrationTests`/`LeaveTypesIntegrationTests` (hand-rolling a fresh tenant/legal-entity/user/employee risks silently missing one of the many invariants the real provisioning endpoint already enforces — normalized email generation, seeded reference data, RBAC wiring). It does **not** replicate that fixture's ~1200 lines of company/department/position CRUD coverage. Attendance-correction rows are seeded directly through a DbContext rather than driven through `AttendanceCorrectionWorkflow.RequestAsync`, because that workflow's approval-routing/schedule/clock-in-policy decision logic is already covered by the existing unit tests with fakes; what those unit tests cannot prove — and what this class does — is whether the real Postgres column round-trips correctly, whether the API response layer reads the stored value instead of deriving it, and whether RLS actually blocks cross-tenant access:

| # | Test | Proves |
|---|---|---|
| 1 | `ApprovalRequiredRequestPersistsApprovalRequiredTrue` | item 1 |
| 2 | `AutoApprovedRequest_PersistsApprovalRequiredFalse` | item 2 |
| 3 | `Approval_PreservesApprovalRequiredTrue` | item 3 |
| 4 | `Rejection_PreservesApprovalRequiredTrue` | item 4 |
| 5 | `Cancellation_PreservesApprovalRequiredTrue` | item 5 |
| 6 | `Reload_ThroughFreshDbContext_PreservesBothValues` | item 6 |
| 7 | `ApiResponse_UsesStoredApprovalRequiredValue_NotDerivedFromStatus` | item 7 — two rows share the same `status` ("approved") but differ in the persisted `approval_required` column; the API must return different values for each, which it does |
| 9 | `CrossTenant_CannotReadOtherTenantsCorrection` | item 9 |
| 10 | `CrossTenant_CannotUpdateOtherTenantsCorrection` | item 10 |
| 11 | `FreshlyMigratedDatabase_HasAllFiveCorrectiveIndexes` | item 11 |

Items 9–10 needed a context most of this test suite doesn't use: every other `ApplicationDbContext` in this class comes from the `WebApplicationFactory`'s DI container, which (see `E2ETestFactory.ConfigureWebHost`) is wired to the raw Testcontainers **superuser** connection, bypassing RLS entirely — this is true of every existing integration test in the repo, not something introduced here. For a real RLS proof, these two tests instead open an independent connection as the restricted `onevo_app` role with the production `TenantRlsInterceptor` wired to a fixed tenant context, scoped to tenant A, and confirm tenant B's row is invisible to a `SELECT` and that an `UPDATE` targeting it silently affects 0 rows.

**`AttendanceCorrectionsMigrationUpgradeTests`** (1 test, its own throwaway Testcontainers instance, no `WebApplicationFactory`) covers item 8, which needs precise control over the migration timeline that the shared, fully-migrated fixture database cannot give:
1. Migrate to exactly `20260822063849_AddBreakRecordOpenUniqueness` (the migration immediately before `attendance_corrections` exists).
2. Seed a tenant/legal-entity/employee/requester-user/reviewer-user via normal EF `SaveChangesAsync` (safe here — those tables have existed for many migrations).
3. Migrate to exactly `20260824120000_AddAttendanceCorrections` (table now exists, no `approval_required` column yet).
4. Insert one raw-SQL row per historical outcome — pending, approved-with-reviewer-evidence, approved-with-no-reviewer-evidence, rejected, cancelled — since the *current* compiled `AttendanceCorrection` entity always includes `approval_required` and so cannot be used to write a row that predates the column.
5. Migrate to `20260824154945_AddAttendanceCorrectionApprovalRequired` (runs the real backfill `UPDATE`).
6. Assert: pending → `true`, approved-with-reviewer → `true`, approved-without-reviewer → `false`, rejected → `true`, cancelled → `true`.

#### Debugging notes (kept for anyone re-running or extending these tests)

Getting these two classes green required discovering and fixing four real gaps between "what the repository's existing Testcontainers helpers set up" and "what these specific tests needed" — recorded here because they're non-obvious and would bite the next person too:

1. `PrivilegedRoleTestBootstrap` (used by every existing integration test) creates the `onevo_migrator`/`onevo_app`/`onevo_auth_base_login_fn_owner` **roles** but grants them nothing — every existing test sidesteps this by migrating as the Testcontainers superuser, not as `onevo_migrator`. `AttendanceCorrectionsMigrationUpgradeTests` deliberately migrates as `onevo_migrator` to mirror production, so it had to additionally run the schema/default-privilege/role-membership grants `ops/postgres/local-bootstrap-roles.sql` performs in real deployments.
2. Because `AdminTestFactory`/`E2ETestFactory` always migrate as the superuser (see `ConfigureWebHost`), `onevo_app` ends up with **zero** table grants in every existing Testcontainers-backed test in this suite — RLS is not actually exercised anywhere else in this codebase's integration tests via the DI-resolved context. `AttendanceCorrectionsIntegrationTests` grants `onevo_app` explicit `SELECT, INSERT, UPDATE, DELETE` on `attendance_corrections` itself for its two RLS tests.
3. `ApplicationDbContext` instances built outside the DI container (as both new test classes do for their `onevo_migrator`/`onevo_app` connections) don't get `TenantRlsInterceptor` unless it's added explicitly — `ApplicationDbContextFactory` (used by real `dotnet ef` commands) never adds it either, which is fine for pure-DDL migrations but not for the DML seeding these tests also perform. Fixed by wiring the interceptor with an admin-mode tenant context for EF-based seeding, and by manually issuing `SELECT set_config('app.tenant_context_mode', 'admin', false)` on the plain `NpgsqlConnection`s used for the raw-SQL historical-row insert and read-back.
4. The first draft of `CrossTenant_*` fetched tenant B's legal entity through a freshly-seeded fixture employee that had no permissions at all, which 403'd; fixed by reusing the legal-entity id already fetched (with the tenant's owner, who does have permissions) during `InitializeAsync`.

None of these were pre-existing repository bugs — they're all specific to what these two new test classes needed that no existing test happened to need.

### Part 4 — CI verification

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release                                    → 0 errors
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore         → 2938 passed, 0 failed
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore → 658 passed, 0 failed
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AttendanceCorrection"
                                                                                                          → 11 passed, 0 failed
dotnet ef migrations has-pending-model-changes ... --no-build                                            → "No changes have been made to the model since the last migration."
git diff --check                                                                                         → clean (only pre-existing LF/CRLF advisory warnings)
git status --short                                                                                       → only the files listed above are new/changed; all pre-existing unrelated WIP untouched
```

### Part 5 — Verdict correction

The verdict at the top of this report was changed from "Release-ready" (2026-08-24, premature) to "Functionally verified, but not release-ready" (interim, written and saved to disk before starting this addendum's work, per the practice of never leaving a known-false claim on record while further work is in progress) to the final **"Release-ready"** now that all four conditions are independently met: the corrective index migration exists and is applied on two independent databases (the shared dev `OnevoDb` and a from-empty fresh container); `has-pending-model-changes` is clean on both; and the automated PostgreSQL persistence/backfill/RLS test suite (11 tests) passes.
