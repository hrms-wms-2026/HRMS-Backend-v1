# Position Foundation Part 2A Report: Schema, Entities & Repository

**Repository Working Directory:** `C:\onevoNew\HRMS-Backend-v1`  
**Phase:** OneVo-HR Phase 1 Org Structure  
**Date:** 2026-08-04  
**Scope Delivered:** Position domain entity reconciliation, ancillary entities (`PositionReportingHistory`, `ManagementCoverageRecord`), EF Core configurations, DbSets, EF Migration (`20260804102821_AddPositionFoundationSchema`), RLS policies, repository interface & EF implementation, unit tests, architecture tests, and PostgreSQL integration tests.  
**Explicit Non-Goals Observed:** No controllers, routes, commands, queries, validators, request DTOs, Postman files, frontend files, OneVo-HR doc edits, Department `headPositionId` API exposure, or employee assignment logic (`position_assignments`, `employee_hierarchy_closure`).

---

## Correction: Transitional Nullable legal_entity_id / department_id

### Root Cause & Unsafe Initial Implementation
The initial Part 2A migration attempt added `positions.legal_entity_id` and `positions.department_id` as non-nullable `uuid` columns with default value `00000000-0000-0000-0000-000000000000` (`Guid.Empty`), followed immediately by adding foreign keys to `legal_entities.id` and `departments.id`.

This design was unsafe for existing databases. If legacy position stub rows existed prior to running this migration, the foreign key creation would fail because `00000000-0000-0000-0000-000000000000` does not correspond to a valid legal entity or department record. Using placeholder fake IDs was explicitly prohibited.

### Migration Fix Strategy
1. `20260804102821_AddPositionFoundationSchema.cs` was corrected to add `positions.legal_entity_id` and `positions.department_id` as **nullable** `uuid` columns without default values.
2. Nullable foreign keys were added to `legal_entities.id` and `departments.id` with `DeleteBehavior.Restrict`.
3. `Position.cs` domain entity properties were declared as `Guid? LegalEntityId` and `Guid? DepartmentId`.
4. `PositionConfiguration.cs` explicitly sets `Property(p => p.LegalEntityId).IsRequired(false)` and `Property(p => p.DepartmentId).IsRequired(false)`.
5. Unique indexes on `(tenant_id, legal_entity_id, code)` and `(tenant_id, legal_entity_id, name)` include `filter: "legal_entity_id IS NOT NULL"`, ensuring legacy orphan rows with `NULL` `legal_entity_id` do not violate uniqueness constraints.
6. Part 2B validators will enforce non-null `LegalEntityId` and `DepartmentId` for all newly created positions via API. Legacy orphan positions will be backfilled and hardened in a future follow-up migration.

### Follow-up Correction: Removal of Fake Guid.Empty Helper Surface

A subsequent review found that `Position.cs` still exposed two computed helper properties left over from the unsafe initial attempt:

```csharp
public Guid LegalEntityIdValue => LegalEntityId ?? Guid.Empty;
public Guid DepartmentIdValue => DepartmentId ?? Guid.Empty;
```

These were dangerous: they could silently reintroduce `Guid.Empty` fake-ID semantics into any code that read them, defeating the point of making `LegalEntityId`/`DepartmentId` nullable. A repo-wide search (`rg -n "LegalEntityIdValue|DepartmentIdValue" src tests`) confirmed these two properties had zero call sites anywhere in `src/` or `tests/` - they were dead code. Both properties were removed outright. **No `Guid.Empty` fallback remains anywhere in the `Position` entity.**

`PositionPart2AArchitectureTests.cs` gained a new test, `PositionSurface_DoesNotReintroduce_FakeGuidEmptyIdHelpers`, which scans every `.cs` file under the Position domain entity, application, EF repository, and EF configuration directories and fails if `LegalEntityIdValue`, `DepartmentIdValue`, or a `?? Guid.Empty`-shaped fallback (matched via regex, tolerant of extra whitespace) appear anywhere in that surface. Each of the four scanned directories is asserted to exist and the collected file list is asserted non-empty, so the guard cannot silently pass by scanning zero files if the directory layout ever changes.

This guard was negative-controlled: `LegalEntityIdValue => LegalEntityId ??  Guid.Empty` was temporarily reintroduced into `Position.cs`, the test was re-run and confirmed to fail with the exact offending line reported, then the change was reverted and the suite re-run green (443/443). This is a standing guard against the fake-ID pattern being reintroduced later, verified to actually fire.

### Follow-up Correction: Migration Safety Test Was Too Weak

The original `PositionMigrationSafetyIntegrationTests` ran the full `MigrateAsync()` (applying **all** migrations, including `AddPositionFoundationSchema`, up front) and only afterward inserted a legacy-shaped row directly via SQL. That order proves nothing about migration safety: it never actually replayed `AddPositionFoundationSchema` against a database that already contained a pre-existing legacy row, so it could not have caught a migration that tried to backfill `Guid.Empty` or otherwise failed against real legacy data.

This test has been replaced with `Migration_ReplaysCleanly_OverLegacyPositionRow_InsertedBeforeFoundationSchemaMigration`, which proves the real sequence against a fresh Testcontainers PostgreSQL instance:

1. Bootstrap privileged roles (`PrivilegedRoleTestBootstrap`), then apply migrations only up to `20260804053523_AddDepartmentCodeCaseInsensitiveUniqueIndex` (the migration immediately before `AddPositionFoundationSchema`) via `IMigrator.MigrateAsync("<migration id>")` - not the full model.
2. Insert a legacy `positions` row using only pre-foundation columns (`id, tenant_id, name, created_at, created_by_id, is_deleted`) - no `legal_entity_id`, no `department_id`, no `Guid.Empty` placeholder.
3. Apply the remaining migrations, including `AddPositionFoundationSchema`, via `IMigrator.MigrateAsync()` (latest).
4. Assert: migration succeeds; the legacy row still exists with its original `tenant_id`/`name`; `legal_entity_id` and `department_id` are both `NULL`; no row anywhere in `positions` has `legal_entity_id` or `department_id` equal to `00000000-0000-0000-0000-000000000000`; `legal_entity_id`/`department_id` are nullable per `information_schema.columns`; foreign keys from `positions.legal_entity_id`/`positions.department_id` exist with a non-`CASCADE` delete rule; and the RLS state on `positions` (`relrowsecurity`, `relforcerowsecurity`, and the full `pg_policies` definition) is captured before the migration and asserted byte-for-byte equal after - proving `AddPositionFoundationSchema` does not touch the pre-existing `positions` RLS policy at all (it only adds RLS to the two new tables, `position_reporting_history` and `management_coverage_records`). The "before" snapshot is itself asserted non-empty (`RowSecurityEnabled` true, `Policies` non-empty) so the equality check cannot pass vacuously by comparing two empty snapshots.

This test passed against real PostgreSQL via Testcontainers (not mocked, not source-text-only).

---

## Unique Name Constraint Review

Per `OneVo-HR/database/schemas/org-structure.md` line 93:
> `name` | `varchar(100)` | `Position name; unique within legal entity`

Because the OneVo-HR specification explicitly requires position names to be unique within a legal entity, the unique constraint on `(tenant_id, legal_entity_id, name)` was preserved with filtered index `legal_entity_id IS NOT NULL`.

---

## 1. Files Read

- `POSITION_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md`
- `POSITION_FOUNDATION_BACKEND_AUDIT_PLAN.md`
- `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md`
- `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs`
- `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs`
- `src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs`
- `tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs`

---

## 2. Files Changed

### Created / Modified Source Files

| Action | File Path | Description |
| :--- | :--- | :--- |
| **Modified** | `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs` | Reconciled `Position` domain entity with Phase 1 fields (`LegalEntityId` Guid?, `DepartmentId` Guid?, `Name`, `Code`, `PositionType`, `MaxOccupancy`, `ReportsToPositionId`, `IsActive`, legacy `DefaultRoleId`); later corrected to remove the dead `LegalEntityIdValue`/`DepartmentIdValue` `?? Guid.Empty` helper properties |
| **Created** | `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/PositionReportingHistory.cs` | Domain entity for effective-dated reporting line history |
| **Created** | `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/ManagementCoverageRecord.cs` | Domain entity for management visibility and approval routing |
| **Modified** | `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs` | EF configuration mapping `positions` table, transitional nullable FKs (`Restrict`), indexes, and unique constraints |
| **Created** | `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionReportingHistoryConfiguration.cs` | EF configuration mapping `position_reporting_history` table, FKs (`Restrict`), and indexes |
| **Created** | `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/ManagementCoverageRecordConfiguration.cs` | EF configuration mapping `management_coverage_records` table, FKs (`Restrict`), and indexes |
| **Modified** | `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` | Added DbSets for `PositionReportingHistories` and `ManagementCoverageRecords` |
| **Modified** | `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs` | Updated interface with foundation query/command/history/coverage methods |
| **Modified** | `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs` | Implemented `IPositionRepository` methods using `AsNoTracking`, block-bodied methods, explicit filters, and recursive CTE cycle helper |
| **Created** | `src/ONEVO.Infrastructure/Migrations/20260804102821_AddPositionFoundationSchema.cs` | EF Core Migration adding nullable `legal_entity_id` / `department_id` to `positions`, creating `position_reporting_history` and `management_coverage_records`, and applying RLS policies |
| **Created** | `src/ONEVO.Infrastructure/Migrations/20260804102821_AddPositionFoundationSchema.Designer.cs` | EF Core Migration designer metadata |
| **Modified** | `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | EF Core Model Snapshot updated by EF tooling |
| **Created** | `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs` | Unit tests for `EfPositionRepository` behavior, cross-tenant/legal-entity isolation, legacy orphan filtering, and EF metadata assertions |
| **Created / Modified** | `tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs` | Architecture tests enforcing migration safety, RLS coverage, block-bodied methods, explicit parameter scoping, and absence of deferred elements; later extended with `PositionSurface_DoesNotReintroduce_FakeGuidEmptyIdHelpers` forbidding `LegalEntityIdValue`, `DepartmentIdValue`, and `?? Guid.Empty` anywhere in the Position domain/application/repository/configuration surface |
| **Modified** | `tests/ONEVO.Tests.Architecture/DepartmentPart3ArchitectureTests.cs` | Updated `PositionEntity_HasDepartmentIdProperty` assertion to reflect Phase 1 entity reconciliation |
| **Created / Corrected** | `tests/ONEVO.Tests.Integration/OrgStructure/Position/PositionMigrationSafetyIntegrationTests.cs` | PostgreSQL Testcontainers integration test; corrected to actually replay `AddPositionFoundationSchema` over a legacy row inserted *before* that migration runs (via `IMigrator.MigrateAsync`), rather than inserting the legacy row after all migrations were already applied |
| **Modified** | `POSITION_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md` | This summary report |

---

## 3. Database Schema Changes & Migration Strategy

### Legacy Shape Before Part 2A

Table `positions`:
- `id` (`uuid`, PK)
- `name` (`character varying(100)`)
- `default_role_id` (`uuid`, nullable)
- `tenant_id` (`uuid`)
- `created_at` (`timestamptz`)
- `updated_at` (`timestamptz`, nullable)
- `created_by_id` (`uuid`)
- `is_deleted` (`boolean`)
- `deleted_at` (`timestamptz`, nullable)

### New Canonical Shape After Part 2A

Table `positions`:
- `id` (`uuid`, PK) - Preserved (maintains `departments.head_position_id` FK validity)
- `tenant_id` (`uuid`, NOT NULL, FK -> `tenants`)
- `legal_entity_id` (`uuid`, NULLABLE TRANSITIONAL, FK -> `legal_entities` [Restrict]) - Added
- `department_id` (`uuid`, NULLABLE TRANSITIONAL, FK -> `departments` [Restrict]) - Added
- `name` (`character varying(100)`, NOT NULL)
- `code` (`character varying(40)`, NULLABLE) - Added
- `position_type` (`character varying(20)`, NOT NULL, DEFAULT `'unique'`) - Added
- `max_occupancy` (`integer`, NOT NULL, DEFAULT `1`) - Added
- `reports_to_position_id` (`uuid`, NULLABLE, FK -> `positions` [Restrict]) - Added
- `is_active` (`boolean`, NOT NULL, DEFAULT `TRUE`) - Added
- `created_at` (`timestamptz`, NOT NULL)
- `updated_at` (`timestamptz`, NULLABLE)
- `default_role_id` (`uuid`, NULLABLE) - Preserved (legacy/deprecated)
- `created_by_id` (`uuid`, NOT NULL) - Preserved (legacy)
- `is_deleted` (`boolean`, NOT NULL, DEFAULT `FALSE`) - Preserved (legacy)
- `deleted_at` (`timestamptz`, NULLABLE) - Preserved (legacy)

Indexes created on `positions`:
- `ix_positions_tenant_id` (`tenant_id`)
- `ix_positions_legal_entity_id` (`legal_entity_id`)
- `ix_positions_department_id` (`department_id`)
- `ix_positions_reports_to_position_id` (`reports_to_position_id`)
- `ix_positions_tenant_id_legal_entity_id` (`tenant_id, legal_entity_id`)
- `ix_positions_tenant_id_legal_entity_id_department_id` (`tenant_id, legal_entity_id, department_id`)
- `ix_positions_tenant_id_legal_entity_id_name` (`tenant_id, legal_entity_id, name`) WHERE `legal_entity_id IS NOT NULL` - UNIQUE
- `ix_positions_tenant_id_legal_entity_id_code` (`tenant_id, legal_entity_id, code`) WHERE `code IS NOT NULL AND legal_entity_id IS NOT NULL` - UNIQUE

### Ancillary Tables Created

1. **`position_reporting_history`**:
   - `id` (`uuid`, PK)
   - `tenant_id` (`uuid`, NOT NULL)
   - `position_id` (`uuid`, NOT NULL, FK -> `positions` [Restrict])
   - `reports_to_position_id` (`uuid`, NULLABLE, FK -> `positions` [Restrict])
   - `effective_from` (`date`, NOT NULL)
   - `effective_to` (`date`, NULLABLE)
   - `change_reason` (`character varying(250)`, NULLABLE)
   - `created_at` (`timestamptz`, NOT NULL)
   - `created_by_user_id` (`uuid`, NULLABLE)
   - Indexes: `ix_position_reporting_history_tenant_id`, `ix_position_reporting_history_position_id`, `ix_position_reporting_history_reports_to_position_id`, `ix_position_reporting_history_tenant_position_effective` (`(tenant_id, position_id, effective_from, effective_to)`).

2. **`management_coverage_records`**:
   - `id` (`uuid`, PK)
   - `tenant_id` (`uuid`, NOT NULL)
   - `legal_entity_id` (`uuid`, NOT NULL, FK -> `legal_entities` [Restrict])
   - `owner_position_id` (`uuid`, NOT NULL, FK -> `positions` [Restrict])
   - `covered_target_type` (`character varying(20)`, NOT NULL)
   - `covered_position_id` (`uuid`, NULLABLE, FK -> `positions` [Restrict])
   - `covered_department_id` (`uuid`, NULLABLE, FK -> `departments` [Restrict])
   - `owner_order` (`integer`, NOT NULL, DEFAULT `1`)
   - `source` (`character varying(30)`, NOT NULL)
   - `is_locked` (`boolean`, NOT NULL, DEFAULT `TRUE`)
   - `status` (`character varying(20)`, NOT NULL, DEFAULT `'active'`)
   - `created_at` (`timestamptz`, NOT NULL)
   - `updated_at` (`timestamptz`, NULLABLE)
   - Indexes: `ix_management_coverage_records_tenant_id`, `ix_management_coverage_records_legal_entity_id`, `ix_management_coverage_records_owner_position_id`, `ix_management_coverage_records_covered_position_id`, `ix_management_coverage_records_covered_department_id`, `ix_management_coverage_records_tenant_legal_entity_owner` (`(tenant_id, legal_entity_id, owner_position_id)`).

---

## 4. Verification Results

| Verification Step | Command Executed | Result |
| :--- | :--- | :--- |
| **API Build** | `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | **PASSED** (0 Errors, 1 pre-existing unrelated warning in `AdminAuthController.cs`) |
| **Unit Tests** | `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal` | **PASSED** (1276 / 1276 passed, 0 failed) |
| **Architecture Tests** | `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal` | **PASSED** (443 / 443 passed, 0 failed - includes the new fake-helper guard test) |
| **Position Integration Tests** | `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~Position"` | **PASSED** (2 / 2 passed via real PostgreSQL Testcontainers, including the corrected pre-migration legacy-row replay test) |
| **Fake Helper Search** | `rg -n "LegalEntityIdValue\|DepartmentIdValue" src tests` | **PASSED** (0 matches) |
| **EF Migration Script** | `dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api` | **PASSED** (`AddPositionFoundationSchema` block adds `legal_entity_id`/`department_id` as nullable `uuid` with no default; no `00000000-0000-0000-0000-000000000000` literal or `UPDATE positions` statement anywhere in that block; FKs to `legal_entities(id)` and `departments(id)` both `ON DELETE RESTRICT`; positions RLS untouched - only the two new tables get RLS SQL) |
| **Guid.Empty Scan (Position scope)** | Search `Guid\.Empty\|00000000-0000-0000-0000-000000000000` under Position domain/application/repository/migration dirs and tests | **PASSED** (0 active fallback usages; the only hits outside Position scope are unrelated pre-existing `tenant_id` default-value literals in earlier, unrelated migrations) |
| **Git Hygiene Scan** | `git diff --check` | **PASSED** (Exit Code 0, clean whitespace) |
| **Non-ASCII Scan** | Non-ASCII character scan over touched files | **PASSED** (0 non-ASCII characters found) |

---

## 5. Remaining Blockers & Next Steps

1. **Part 2B (Application Layer):** Position CQRS commands, queries, validators, and DTOs to be created.
2. **Part 2C (API Layer):** Position controllers and API endpoints to be created.
3. **Part 3 (Department Head Position Exposure):** Exposure of `headPositionId` in Department requests once Position API exists.
4. **Part 4 (Employee Position Assignment):** `position_assignments` and `employee_hierarchy_closure` explicitly deferred.

### Part 2B Constraint: legalEntityId / departmentId Must Not Be Untrusted Body Fields

Because `legal_entity_id` and `department_id` are nullable transitional columns with `DeleteBehavior.Restrict` foreign keys, Part 2B's create/update handlers are the enforcement point that keeps new rows honest. Part 2B **must not** accept `legalEntityId` or `departmentId` as arbitrary fields on the request body. They must be resolved exclusively from the approved selected-company (legal entity) context and route-scope model already used by the Department endpoints - the same pattern that prevents a caller from writing an arbitrary/cross-tenant legal entity or department ID into a position. Accepting these as free-form body fields would reopen the same class of unsafe-ID problem this correction just closed, just moved from the migration layer to the request layer.

### Safe to Proceed
**Part 2B (Position Application Services & Contracts) is 100% safe to start**, subject to the untrusted-body-field constraint above.
