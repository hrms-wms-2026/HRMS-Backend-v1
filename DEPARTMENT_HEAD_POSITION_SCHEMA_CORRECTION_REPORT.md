# Department Head Position Schema Correction Report

**Task:** Correct Department Part 2A schema to include Phase 1 `head_position_id`.  
**Repository Working Directory:** `C:\onevoNew\HRMS-Backend-v1`  
**Date:** 2026-08-03  

---

## 1. Summary of Changes

Department Part 2A was initially implemented without `head_position_id`. In accordance with OneVo-HR Phase 1 documentation, `departments.head_position_id` is defined as a nullable foreign key referencing `positions.id`. This field is mandatory as a database schema column, even though the column value itself is optional (nullable) for departments without a designated head position.

The schema has been corrected by updating the domain entity, EF configuration, EF model snapshot, generating a new corrective migration (`AddDepartmentHeadPositionId`), and adding/updating architecture and unit tests to enforce the constraint.

---

## 2. Files Read

- `src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs`
- `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContextFactory.cs`
- `src/ONEVO.Infrastructure/Migrations/20260803085109_AddDepartments.cs`
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`

---

## 3. Files Changed

1. **`src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`**
   - Added property `public Guid? HeadPositionId { get; set; }`.
   - Kept `ParentDepartmentId`, `TenantId`, `LegalEntityId`, `Name`, `Code`, and `IsActive` unchanged.

2. **`src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs`**
   - Added index `ix_departments_head_position_id` on `HeadPositionId`.
   - Configured foreign key `HeadPositionId` referencing `Position` entity with `DeleteBehavior.Restrict`.
   - Preserved `tenant_id + legal_entity_id + name` unique index and tenant isolation rules.

3. **`src/ONEVO.Infrastructure/Migrations/20260803092715_AddDepartmentHeadPositionId.cs`** *(NEW)*
   - Added EF Migration `AddDepartmentHeadPositionId`.
   - `Up()`: Adds nullable column `head_position_id` (uuid), index `ix_departments_head_position_id`, and foreign key `fk_departments_positions_head_position_id` to `positions(id)` with `DeleteBehavior.Restrict`.
   - `Down()`: Drops foreign key, index, and column cleanly.

4. **`src/ONEVO.Infrastructure/Migrations/20260803092715_AddDepartmentHeadPositionId.Designer.cs`** *(NEW)*
   - Generated designer metadata for `AddDepartmentHeadPositionId`.

5. **`src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`**
   - Updated by `dotnet ef` tooling to include `Department.HeadPositionId`, index, and foreign key metadata.

6. **`tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`**
   - Removed obsolete guards (`DepartmentEntity_HasNoHeadPositionIdProperty` and `AddDepartmentsMigration_DoesNotReferenceHeadPositionId`).
   - Added guards asserting `HeadPositionId` presence (type `Guid?`), corrective migration mapping, EF configuration index and `Restrict` FK, RLS retention, and scoped unique name index.

7. **`tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`**
   - Added focused EF model metadata assertion test `DepartmentModel_ConfiguresHeadPositionId_AsNullableForeignKeyToPosition_WithRestrictDeleteBehavior`.

---

## 4. Exact Schema Correction

### C# Domain Entity (`Department.cs`)
```csharp
public class Department : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public Guid? HeadPositionId { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

### EF Core Configuration (`DepartmentConfiguration.cs`)
```csharp
// Supports lookups for the designated head position of a department.
builder.HasIndex(d => d.HeadPositionId)
    .HasDatabaseName("ix_departments_head_position_id");

// Optional head position reference. Nullable FK to positions.id.
// Restrict (never cascade) so deleting a position that is designated as a
// department head fails loudly instead of silently taking the department down.
builder.HasOne<Position>()
    .WithMany()
    .HasForeignKey(d => d.HeadPositionId)
    .OnDelete(DeleteBehavior.Restrict);
```

---

## 5. Rationale: Why `head_position_id` is Nullable but Required in Phase 1 Schema

- **Schema Requirement:** OneVo-HR Phase 1 domain architecture specifies that every department has a designated head position field (`head_position_id`) in its relational schema to establish organizational leadership structure.
- **Nullability Requirement:** Not all departments immediately have an assigned head position upon creation, and small or newly formed departments may operate temporarily without a designated head position. Making `head_position_id` non-nullable would force artificial dummy position assignments during department bootstrapping.
- **Delete Behavior (`Restrict`):** Deleting a position that happens to be assigned as a department's head position must be prevented by the database foreign key constraint rather than cascading to delete the entire department or silently setting field values without application oversight.

---

## 6. Head Position Schema Readiness & Application Scoping Policy

head_position_id is schema-ready only. Part 2A adds the nullable database column, EF mapping, index, and FK so the Phase 1 schema is aligned with OneVo-HR. Assigning, changing, validating, or exposing a Department Head Position through API requests is deferred until the real Position APIs/model are ready. Part 2B/2C must not accept headPositionId in create/update requests unless Position validation can prove the position belongs to the same tenant and legal entity/company, is active, and satisfies the approved head-position rules.

---

## 7. Migration SQL Evidence

Generated using `dotnet ef migrations script 20260803085232_AddOrgModuleToStarterPlan --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api`:

```sql
START TRANSACTION;
ALTER TABLE departments ADD head_position_id uuid;

CREATE INDEX ix_departments_head_position_id ON departments (head_position_id);

ALTER TABLE departments ADD CONSTRAINT fk_departments_positions_head_position_id FOREIGN KEY (head_position_id) REFERENCES positions (id) ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260803092715_AddDepartmentHeadPositionId', '10.0.9');

COMMIT;
```

### RLS and Constraint Verification from Full Script
From `dotnet ef migrations script 20260731073116_ExpandLegalEntityForGeneralSettings`:
- Table `departments` is created with RLS enabled (`ALTER TABLE departments ENABLE ROW LEVEL SECURITY; ALTER TABLE departments FORCE ROW LEVEL SECURITY;`).
- RLS policy `tenant_isolation` remains intact and unaltered.
- Unique department name constraint `ix_departments_tenant_id_legal_entity_id_name` remains scoped to `(tenant_id, legal_entity_id, name)`.

---

## 8. Verification Results

| Step / Test Suite | Command | Result |
| :--- | :--- | :--- |
| **API Build** | `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | **Succeeded** (0 Errors) |
| **Unit Tests** | `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal` | **Passed** (1144 / 1144 passed) |
| **Architecture Tests** | `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal` | **Passed** (358 / 358 passed) |
| **Migration SQL Check** | `dotnet ef migrations script` | **Confirmed** (Nullable `uuid`, Index created, FK to `positions(id)`, `ON DELETE RESTRICT`) |
| **Git Hygiene** | `git diff --check` | **Clean** (Exit Code 0) |

---

## 9. Confirmation of Scope Boundaries

- **Department Part 2B:** NOT implemented (no commands, handlers, queries, validators, DTOs, or service contracts created).
- **Controllers / APIs:** No Department controllers or API routes created.
- **Position APIs:** No Position APIs created or modified.
- **Documentation / Postman:** No Postman collection files or OneVo-HR markdown documentation modified.
- **RLS Security:** RLS policies and tenant isolation rules strictly preserved without weakening.

---

## 10. Report Reconciliation Result

### Files Changed
1. `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md` (reconciled schema summary, column list, migration details, and added policy section).
2. `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md` (added policy section and reconciliation report section).

### Stale Claims Removed
- Removed all claims asserting that `head_position_id` was omitted from Part 2A or the Department entity.
- Removed claims asserting `AddDepartments` migration never references `head_position_id` without context of the corrective migration.
- Removed claims suggesting `head_position_id` column creation should be deferred to a later feature part.

### Final head_position_id Policy
- `head_position_id` is schema-ready in Part 2A (nullable `uuid`, index `ix_departments_head_position_id`, FK to `positions(id)` with `DeleteBehavior.Restrict`).
- Application validation and API exposure of `headPositionId` are deferred to later parts when Position APIs and cross-entity validation rules exist.

### Verification Commands & Results
- Stale wording scan pattern:
  `Select-String -Path DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md, DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md -Pattern "no HeadPositionId|DoesNotReferenceHeadPositionId|omitted.*head_position_id|head_position_id.*omitted|revisit.*head_position_id"`
  Result: 0 stale claims remaining (all matches eliminated).
- Non-ASCII character scan:
  `Select-String -Path DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md, DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md -Pattern "[^\x00-\x7F]"`
  Result: 0 non-ASCII characters found (100% clean ASCII markdown).
- `git diff --check`: Exit Code 0 (clean).
