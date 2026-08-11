# Position Foundation Part 1: Backend Audit & Staged Implementation Plan

**Repository:** `C:\onevoNew\HRMS-Backend-v1`  
**Phase:** OneVo-HR Phase 1 Org Structure  
**Date:** 2026-08-04  
**Status:** Read-Only Audit & Plan (No code, migration, test, or documentation changes executed)

---

## 1. Executive Summary

This document presents the **Position Foundation Part 1 Backend Audit and Staged Implementation Plan** for `HRMS-Backend-v1`. Following the completion of the Department Foundation and Hardening phases (Parts 1-3), Department schema readiness for `head_position_id` has been established (`departments.head_position_id` exists as a nullable foreign key pointing to `positions.id` with `DeleteBehavior.Restrict`). However, per design policy, `headPositionId` is not exposed or validated in Department APIs until a fully compliant Position model and API family exist.

### Key Audit Findings

1. **Current Position Model is a Legacy Stub:**
   The existing `Position` entity (`src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`) and database table (`positions`, created in migration `20260708080059_AddAdminSessionsAndCsrfBinding.cs`) represent a legacy stub containing only `Id`, `Name`, `DefaultRoleId`, `TenantId`, `CreatedAt`, `UpdatedAt`, `CreatedById`, `IsDeleted`, and `DeletedAt`.
2. **Missing Canonical Phase 1 Position Fields:**
   The current backend `Position` lacks required Phase 1 fields: `LegalEntityId` (company scope), `DepartmentId`, `Code` (stable integration code), `PositionType` (`unique` vs `pooled`), `MaxOccupancy` (capacity), `ReportsToPositionId` (self-referencing reporting hierarchy), and `IsActive` (boolean status).
3. **Missing Ancillary Schema Tables:**
   Five ancillary tables specified in OneVo-HR Phase 1 documentation are completely absent from the backend:
   - `position_reporting_history` (effective-dated position reporting line history)
   - `position_access_templates` (persistence for "Grant system access from this position")
   - `management_coverage_records` (single source for employee visibility and approval routing)
   - `position_assignments` (effective-dated employee placements into positions)
   - `employee_hierarchy_closure` (derived reporting tree closure table)
4. **Missing Application & API Layers:**
   No application commands, queries, handlers, validators, DTOs, or API controllers (`PositionsController`) exist for Position in the backend. No unit, integration, or architecture tests exist for Position feature logic.
5. **Department Head Position Relationship Safety:**
   The existing foreign key `fk_departments_positions_head_position_id` on `departments.head_position_id` references `positions(id)` with `DeleteBehavior.Restrict`. Reconciling the `positions` table to the Phase 1 schema while preserving `id` as `uuid` PK ensures existing schema integrity is preserved.

### Overall Recommendation

Part 2A (Position Schema, Entities, and Repositories) is **safe to start**, provided that the legacy `positions` table is reconciled via EF Core migration rather than dropped/recreated, and that `position_reporting_history` and `management_coverage_records` are created in Part 2A to support reporting hierarchy generation.

---

## 2. Files Inspected

### Backend Files (`HRMS-Backend-v1`)

| Absolute File Path | Line Range / Area | Inspected Aspect |
| :--- | :--- | :--- |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Domain\Features\OrgStructure\Position\Entities\Position.cs` | L1-L10 | Legacy `Position` domain entity (`Name`, `DefaultRoleId`) |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Application\Features\OrgStructure\Position\RepositoryInterfaces\IPositionRepository.cs` | L1-L9 | Repository interface (`GetByIdAsync`) |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\OrgStructure\Position\PositionConfiguration.cs` | L1-L18 | EF Core configuration for `positions` table |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\OrgStructure\Position\EfPositionRepository.cs` | L1-L18 | EF Core repository implementation |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\ApplicationDbContext.cs` | L98 | `DbSet<Position> Positions` registration |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Migrations\20260708080059_AddAdminSessionsAndCsrfBinding.cs` | L60-L77 | Initial `positions` table creation migration |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Migrations\20260719180411_AddMissingRlsPolicies.cs` | L19-L40 | RLS policy `tenant_isolation` on `positions` |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Migrations\20260803092715_AddDepartmentHeadPositionId.cs` | L1-L51 | FK `fk_departments_positions_head_position_id` |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Domain\Features\OrgStructure\Department\Entities\Department.cs` | L1-L30 | Department entity and `HeadPositionId` property |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Domain\Features\OrgStructure\LegalEntity\Entities\LegalEntity.cs` | L1-L40 | LegalEntity entity for company scope boundary |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Domain\Features\CoreHr\Employee\Entities\Employee.cs` | L1-L28 | Employee entity (legacy manager/department fields) |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\CoreHr\Employee\EmployeeConfiguration.cs` | L1-L33 | Employee EF configuration |
| `C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Seeders\PermissionSeeder.cs` | L91-L93 | Seeded permissions `org:read` and `org:manage` |
| `C:\onevoNew\HRMS-Backend-v1\DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md` | L1-L184 | Schema-ready `head_position_id` policy report |

### OneVo-HR Documentation Files (`OneVo-HR`)

| Absolute File Path | Status | Summary of Position Requirements |
| :--- | :--- | :--- |
| `C:\onevoNew\OneVo-HR\database\phase1-table-inventory.md` | **Exists** (L692-L795) | Detailed table inventory for `positions`, `position_access_templates`, `management_coverage_records`, `position_reporting_history`, `position_assignments`, `employee_hierarchy_closure` |
| `C:\onevoNew\OneVo-HR\database\schema-catalog.md` | **Exists** (L96-L101) | Module catalog listing 7 org-structure position tables |
| `C:\onevoNew\OneVo-HR\database\schemas\org-structure.md` | **Exists** (L84-L266) | Full schema definition, FKs, constraints, reporting rules, deletion rules |
| `C:\onevoNew\OneVo-HR\modules\org-structure\overview.md` | **Exists** (L1-L143) | High-level module architecture and legal entity scoping |
| `C:\onevoNew\OneVo-HR\modules\org-structure\positions\overview.md` | **Exists** (L1-L143) | Position concepts, unique vs pooled types, access block |
| `C:\onevoNew\OneVo-HR\modules\org-structure\positions\end-to-end-logic.md` | **Exists** (L1-L233) | API endpoints, command flows, validation logic, cycle detection, error codes |
| `C:\onevoNew\OneVo-HR\Userflow\Org-Structure\position-setup.md` | **Exists** (L1-L237) | Admin UX flow, modal fields, management coverage rules, event catalog |

---

## 3. Current Backend Position Inventory

The current state of Position artifacts in `HRMS-Backend-v1` is summarized below:

```
[ONEVO.Domain]
  └── Features/OrgStructure/Position/Entities/Position.cs
       ├── Inherits: BaseEntity (Id, TenantId, CreatedAt, UpdatedAt, CreatedById, IsDeleted, DeletedAt)
       └── Properties: Name (string), DefaultRoleId (Guid?)

[ONEVO.Application]
  └── Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs
       └── Method: GetByIdAsync(Guid id, CancellationToken ct)

[ONEVO.Infrastructure]
  ├── Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs
  │    └── ToTable("positions"), HasKey(p => p.Id), Property(p => p.Name).HasMaxLength(100).IsRequired()
  ├── Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs
  │    └── Inherits BaseRepository<Position>, implements IPositionRepository
  ├── Persistence/ApplicationDbContext.cs
  │    └── public DbSet<Position> Positions => Set<Position>();
  └── Migrations/
       ├── 20260708080059_AddAdminSessionsAndCsrfBinding.cs (Created positions table)
       ├── 20260719180411_AddMissingRlsPolicies.cs (Enabled & forced RLS tenant_isolation)
       └── 20260803092715_AddDepartmentHeadPositionId.cs (FK from departments.head_position_id to positions.id)

[ONEVO.Api]
  └── Controllers/ Tenant/OrgStructure/ (No PositionsController exists)

[Tests]
  └── No unit, integration, or architecture test files exist for Position feature logic.
```

### Detailed Component Inventory

1. **Entity Class (`Position.cs`):**
   - **Path:** `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`
   - **Namespace:** `ONEVO.Domain.Features.OrgStructure.Entities`
   - **Fields:** `Name` (string), `DefaultRoleId` (Guid?), plus inherited `BaseEntity` properties (`Id`, `TenantId`, `CreatedAt`, `UpdatedAt`, `CreatedById`, `IsDeleted`, `DeletedAt`).
   - **Assessment:** Stub entity. Lacks legal entity context, department association, code, type, capacity, reporting line, and active flag.

2. **EF Core Configuration (`PositionConfiguration.cs`):**
   - **Path:** `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs`
   - **Namespace:** `ONEVO.Infrastructure.Persistence.Configurations.OrgStructure` (deliberately scoped to feature segment to prevent name collision with `Position` entity).
   - **Table Mapping:** `positions`
   - **Constraints:** Primary Key `Id`, `Name` required max length 100.
   - **Assessment:** Missing FKs (`LegalEntityId`, `DepartmentId`, `ReportsToPositionId`), missing indexes (`(tenant_id, legal_entity_id, name)`, `(tenant_id, code)`).

3. **Database Migration & Table Shape:**
   - **First Created:** Migration `20260708080059_AddAdminSessionsAndCsrfBinding.cs` (lines 60-77).
   - **Columns in PostgreSQL:**
     - `id` (`uuid`, PK, NOT NULL)
     - `name` (`character varying(100)`, NOT NULL)
     - `default_role_id` (`uuid`, NULLABLE)
     - `tenant_id` (`uuid`, NOT NULL)
     - `created_at` (`timestamp with time zone`, NOT NULL)
     - `updated_at` (`timestamp with time zone`, NULLABLE)
     - `created_by_id` (`uuid`, NOT NULL)
     - `is_deleted` (`boolean`, NOT NULL)
     - `deleted_at` (`timestamp with time zone`, NULLABLE)
   - **RLS Policy:** Enabled and forced in migration `20260719180411_AddMissingRlsPolicies.cs` (policy `tenant_isolation`).
   - **Foreign Keys:** Referenced by `departments.head_position_id` via FK `fk_departments_positions_head_position_id` with `DeleteBehavior.Restrict` (migration `20260803092715_AddDepartmentHeadPositionId.cs`).

4. **Repository Layer:**
   - **Interface:** `IPositionRepository` (`src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs`). Contains only `GetByIdAsync(Guid id, CancellationToken ct)`.
   - **Implementation:** `EfPositionRepository` (`src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs`). Implements `GetByIdAsync` via `Query().FirstOrDefaultAsync(...)`.

5. **Application Commands/Queries/Handlers:**
   - **Count:** 0. No CQRS commands, handlers, queries, validators, or DTOs exist.

6. **API Controllers & Endpoints:**
   - **Count:** 0. No `PositionsController.cs` exists in `src/ONEVO.Api/Controllers/Tenant/OrgStructure/`.

7. **Tests & Postman:**
   - **Unit Tests:** None.
   - **Integration Tests:** None.
   - **Architecture Tests:** None specific to Position logic (only general `TenantIsolationArchitectureTests`).
   - **Postman Collections:** No Position requests in `postman/collections/ONEVO Organization Admin API/`.

---

## 4. OneVo-HR Phase 1 Position Requirements Found

Based on audit of OneVo-HR documentation (`database/schemas/org-structure.md`, `modules/org-structure/positions/overview.md`, `end-to-end-logic.md`, `position-setup.md`), the required Phase 1 Position specification is documented below:

### 1. `positions` Main Entity Schema

| Field | Type | Required | Constraints & Rules |
| :--- | :--- | :--- | :--- |
| `id` | `uuid` | Yes | Primary Key |
| `tenant_id` | `uuid` | Yes | Foreign Key -> `tenants(id)`. RLS isolated |
| `legal_entity_id` | `uuid` | Yes | Foreign Key -> `legal_entities(id)`. Position is legal-entity-scoped. Immutable after creation |
| `department_id` | `uuid` | Yes | Foreign Key -> `departments(id)`. Department must belong to same legal entity |
| `name` | `varchar(100)` | Yes | Position title. Unique within selected legal entity: `(tenant_id, legal_entity_id, name)` |
| `code` | `varchar(40)` | No | Stable short code for import/integrations. Unique within tenant when provided: `(tenant_id, code)` |
| `position_type` | `varchar(20)` | Yes | `'unique'` or `'pooled'` |
| `max_occupancy` | `int` | Yes | Must be `1` for `unique`; `>= 1` for `pooled`. Cannot be reduced below current active occupancy |
| `reports_to_position_id` | `uuid` | No | Self-referencing FK -> `positions(id)`. Nullable for root positions. Target must be a same-company `unique` position |
| `is_active` | `boolean` | Yes | Status (default `true`). Deletion sets `is_active = false` (logical deletion) |
| `created_at` | `timestamptz` | Yes | Audit timestamp |
| `updated_at` | `timestamptz` | No | Audit timestamp |

### 2. Mandatory Reporting & Governance Rules

1. **Company Boundary Scoping:**
   - Positions, departments, and reporting targets must belong to the same Company (`legal_entity_id`). Cross-company reporting lines are strictly forbidden in Phase 1.
2. **Reporting Target Restrictions:**
   - Only `unique` positions (`max_occupancy = 1`) can be selected as `reports_to_position_id`. `pooled` positions cannot be selected as reporting targets.
3. **Cycle Prevention:**
   - Position updates/creates must validate that setting `reports_to_position_id` does not create a reporting loop/cycle (traversing up from target to root must never encounter the position itself).
4. **Deletion Guard Rules:**
   - Position deletion is logical (`is_active = false`).
   - Deletion is **blocked** if active employee assignments (`position_assignments`) exist for the position.
   - Deletion is **blocked** if the position is currently assigned as `head_position_id` on any Department.
   - When a position with child reporting positions is deleted, child positions are re-parented to the deleted position's `reports_to_position_id` (or become root positions if deleted position was a root).
5. **Head Position Relationship Rules (`departments.head_position_id`):**
   - `departments.head_position_id` points to `positions.id`.
   - The head position must belong to the same tenant and legal entity as the department.
   - A position assigned as a department head must be of type `unique`.

### 3. Ancillary Phase 1 Schema Tables (Required for Position Lifecycle)

1. **`position_reporting_history`:**
   - Tracks effective-dated reporting line changes (`position_id`, `reports_to_position_id`, `effective_from`, `effective_to`).
   - Initial row written when a position is created with a `reports_to_position_id`.
2. **`management_coverage_records`:**
   - Single source of truth for employee management visibility and Phase 1 approval routing.
   - Setting `reports_to_position_id` automatically creates a locked record with `source = 'ReportingStructure'` and `is_locked = true`.
   - Manual coverage added via optional access block.
3. **`position_access_templates`:**
   - Internal persistence for "Grant system access from this position" rules (`role_id`, `requires_approval`, `is_sensitive`).
4. **`position_assignments`:**
   - Placement of employees into positions (`PrimaryEmployment` vs `AdditionalAuthority`).
5. **`employee_hierarchy_closure`:**
   - Derived tree closure table for fast hierarchical manager/subordinate reporting queries.

---

## 5. Current vs Required Mismatch Table & Specific Safety Checks

### Mismatch Table

| Requirement | Current Backend State | Gap Severity | Recommended Action | Staged Part |
| :--- | :--- | :--- | :--- | :--- |
| **Legal Entity Scope (`legal_entity_id`)** | Missing on `Position` entity & table | **Blocker** | Add `LegalEntityId` (uuid, FK to `legal_entities.id`, required) | Part 2A |
| **Department Scope (`department_id`)** | Missing on `Position` entity & table | **Blocker** | Add `DepartmentId` (uuid, FK to `departments.id`, required) | Part 2A |
| **Position Code (`code`)** | Missing on `Position` entity & table | High | Add `Code` (varchar(40), nullable, tenant-unique index) | Part 2A |
| **Position Type (`position_type`)** | Missing on `Position` entity & table | **Blocker** | Add `PositionType` (`unique` \| `pooled`, string/enum) | Part 2A |
| **Max Occupancy (`max_occupancy`)** | Missing on `Position` entity & table | **Blocker** | Add `MaxOccupancy` (int, default 1) | Part 2A |
| **Reporting Line (`reports_to_position_id`)** | Missing on `Position` entity & table | **Blocker** | Add `ReportsToPositionId` (uuid, nullable self-referencing FK) | Part 2A |
| **Active Status (`is_active`)** | Uses legacy `is_deleted` soft-delete | High | Add `is_active` boolean; reconcile status model | Part 2A |
| **Name Uniqueness Index** | No unique index on `Position.Name` | High | Add unique index `(tenant_id, legal_entity_id, name)` | Part 2A |
| **Reporting History Table** | Table does not exist | Medium | Add `position_reporting_history` entity, configuration, table | Part 2A |
| **Management Coverage Table** | Table does not exist | High | Add `management_coverage_records` entity, configuration, table | Part 2A |
| **Position Access Templates Table** | Table does not exist | Medium | Add `position_access_templates` entity, configuration, table | Part 2A |
| **Position Application Layer** | No commands, queries, handlers, DTOs | **Blocker** | Create CQRS handlers, validators, DTOs | Part 2B |
| **Position API Controller** | No `PositionsController` | **Blocker** | Create `PositionsController` with tenant routing & permissions | Part 2C |
| **Postman & Integration Validation** | No position Postman/integration tests | High | Create integration tests & Postman requests | Part 2D |
| **Department Head Assignment API** | Schema-ready only, no API validation | Medium | Expose `headPositionId` in Department API with validation | Part 3 |
| **Employee Position Assignment** | Legacy `Employee.ManagerId` exists | Medium | Implement `position_assignments` & hierarchy closure | Part 4 |

### Specific Safety Checks Audit

1. **Reconcile Stub vs Replace:**
   - *Check:* Should the current `Position` stub be extended/reconciled or dropped/replaced?
   - *Finding:* The `positions` table is already referenced by foreign key `fk_departments_positions_head_position_id` in migration `20260803092715_AddDepartmentHeadPositionId.cs`. Dropping the `positions` table would break database migration continuity. Reconciling via EF migration (`AddPositionFoundationSchema`) to add missing Phase 1 columns while keeping `positions.id` intact is **required and safe**.
2. **Migration & Table Shape Conflict:**
   - *Check:* Do existing columns conflict with Phase 1?
   - *Finding:* The current table contains legacy columns `default_role_id`, `created_by_id`, `is_deleted`, and `deleted_at`. In Part 2A, `is_active` (boolean, default true) will be added for Phase 1 status checks. Legacy columns `default_role_id`, `created_by_id`, `is_deleted`, `deleted_at` should be preserved as nullable/deprecated fields in EF configuration to prevent breaking existing data or raw SQL queries, or cleanly mapped.
3. **Folder & Namespace Scoping:**
   - *Check:* Does Position belong under OrgStructure namespace/folders?
   - *Finding:* Yes. Current files are located in `src/ONEVO.Domain/Features/OrgStructure/Position/`, `src/ONEVO.Application/Features/OrgStructure/Position/`, and `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/`. This location is correct.
4. **Legal Entity Scoping:**
   - *Check:* Is Position scoped by selected Legal Entity?
   - *Finding:* No, currently `LegalEntityId` is completely missing. Part 2A will enforce `LegalEntityId` as a non-nullable foreign key and enforce same-company validation in handlers.
5. **Department `head_position_id` Compatibility:**
   - *Check:* Can `departments.head_position_id` safely reference the positions table?
   - *Finding:* Yes, foreign key `fk_departments_positions_head_position_id` exists in schema. Reconciling `positions` schema maintains foreign key validity.
6. **Employee Assignment Model:**
   - *Check:* Does Employee support multiple positions or is assignment deferred?
   - *Finding:* `Employee` entity currently has legacy fields `ManagerId`, `DepartmentId`, `JobTitleId`. OneVo-HR Phase 1 specifies that manager reporting is derived strictly from `position_assignments` + `positions.reports_to_position_id`. Employee assignment implementation is explicitly deferred to Part 4.
7. **`position_assignments` Status:**
   - *Check:* Does `position_assignments` exist in backend?
   - *Finding:* No. It exists in Phase 1 documentation (`database/schemas/org-structure.md`, line 212) but is absent in backend code/migrations.
8. **Tenant Id Input Safety:**
   - *Check:* Does any existing Position code expose `tenantId` in request bodies?
   - *Finding:* No Position API controllers exist. In Part 2B/2C contracts, `TenantId` will be resolved strictly from `ITenantContext`, never client request payloads.
9. **Direct DbContext Access:**
   - *Check:* Does direct `DbContext` access exist in handlers?
   - *Finding:* No Position handlers exist yet. Part 2B will implement all repository access through `IPositionRepository`.

---

## 6. Blockers and Open Decisions

### Critical Blockers

1. **Legacy Schema Reconciliation:**
   The `positions` database table lacks 7 mandatory Phase 1 columns (`legal_entity_id`, `department_id`, `code`, `position_type`, `max_occupancy`, `reports_to_position_id`, `is_active`). Migration `AddPositionFoundationSchema` must be created in Part 2A.
2. **Missing Ancillary Tables for Reporting & Governance:**
   Setting `reports_to_position_id` requires writing an initial row to `position_reporting_history` and generating locked coverage in `management_coverage_records`. Without these two tables, core position reporting logic cannot fulfill Phase 1 design requirements.

### Open Decisions

1. **Part 2A Ancillary Table Scope:**
   - *Decision:* Should Part 2A include `position_reporting_history` and `management_coverage_records` in addition to `positions`?
   - *Recommendation:* **Yes.** Include `positions`, `position_reporting_history`, and `management_coverage_records` in Part 2A EF configuration and migration. This enables Part 2B position creation handlers to atomically write reporting history and generated management coverage without schema gaps.
2. **Legacy Column Disposition (`default_role_id`, `created_by_id`, `is_deleted`):**
   - *Decision:* How to handle legacy columns in `positions` table during migration?
   - *Recommendation:* Keep `default_role_id` as a nullable property on `Position` (or map to `position_access_templates` compatibility fallback) and retain `created_by_id`, `is_deleted`, `deleted_at` as nullable/deprecated fields in the EF configuration. Do not perform destructive column drops unless explicitly requested.

---

## 7. Recommended Staged Implementation Roadmap

```
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 1: Backend Audit & Staged Implementation Plan (COMPLETED)            │
│ Read-only report POSITION_FOUNDATION_BACKEND_AUDIT_PLAN.md created.        │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 2A: Position Schema, Entity & Repository Hardening                   │
│ - Domain entities: Position, PositionReportingHistory, ManagementCoverage │
│ - EF Configurations & migration AddPositionFoundationSchema              │
│ - IPositionRepository & EfPositionRepository methods                      │
│ - Unit & Architecture tests for repository and EF model                   │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 2B: Position Application Services & Contracts                        │
│ - Commands: CreatePosition, BulkCreatePositions, UpdatePosition, Delete   │
│ - Queries: GetPositionById, ListPositions, GetPositionTree               │
│ - Validators: Same-company, unique vs pooled, reporting cycle check       │
│ - Unit tests for command/query handlers and validators                    │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 2C: Position Controller & Endpoints                                  │
│ - PositionsController under src/ONEVO.Api/Controllers/Tenant/OrgStructure │
│ - Endpoints: POST, POST /bulk, GET, GET /tree, GET /{id}, PUT, DELETE     │
│ - Permissions: org:read, org:manage                                       │
│ - Controller unit & architecture tests                                    │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 2D: Postman & Integration Validation                                 │
│ - Integration tests: PositionsIntegrationTests.cs                         │
│ - Postman collection requests under Organization - Positions              │
│ - E2E verification report                                                 │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 3: Department Head Position Assignment                               │
│ - Expose headPositionId in Department Create/Update DTOs & endpoints      │
│ - Cross-entity validation (same tenant/company/dept, active unique pos)  │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│ Part 4: Employee & Position Assignment (DEFERRED)                         │
│ - position_assignments table & services                                   │
│ - Multi-position assignment model (PrimaryEmployment / AdditionalAuth)    │
│ - Hierarchy closure table employee_hierarchy_closure                      │
└───────────────────────────────────────────────────────────────────────────┘
```

---

## 8. Permission Recommendation

Per audit of `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs` (lines 91-93), the canonical organization structure permissions are:

- **`org:read`** ("View org structure, departments, hierarchy.")
- **`org:manage`** ("Create and edit org structure, departments.")

### Proposed Endpoint Permission Mapping

| Endpoint Route | HTTP Method | Required Permission | Description |
| :--- | :--- | :--- | :--- |
| `/api/v1/org/positions` | `GET` | `org:read` | List positions (paginated, filtered by legal entity / department) |
| `/api/v1/org/positions/tree` | `GET` | `org:read` | Get position reporting tree structure |
| `/api/v1/org/positions/{id}` | `GET` | `org:read` | Get single position details by ID |
| `/api/v1/org/positions` | `POST` | `org:manage` | Create new position |
| `/api/v1/org/positions/bulk` | `POST` | `org:manage` | Bulk create positions |
| `/api/v1/org/positions/{id}` | `PUT` | `org:manage` | Update position details & reporting line |
| `/api/v1/org/positions/{id}` | `DELETE` | `org:manage` | Delete position (logical deletion with re-parenting) |

*Note:* `roles:manage` permission will be required in addition to `org:manage` if an incoming request updates the optional position access grant block ("Grant system access from this position").

---

## 9. Exact Part 2A Prompt Outline

When ready to execute **Part 2A**, the prompt outline below should be provided:

```text
Task: Position Foundation Part 2A - Schema, Entities, and Repositories Implementation

Target Directory: C:\onevoNew\HRMS-Backend-v1

Scope of Work:
1. Update Position Domain Entity:
   - File: src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs
   - Add properties: LegalEntityId (Guid), DepartmentId (Guid), Code (string?), PositionType (PositionType enum/string), MaxOccupancy (int), ReportsToPositionId (Guid?), IsActive (bool), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset?).
2. Create Ancillary Domain Entities:
   - PositionReportingHistory: src/ONEVO.Domain/Features/OrgStructure/Position/Entities/PositionReportingHistory.cs
   - ManagementCoverageRecord: src/ONEVO.Domain/Features/OrgStructure/Position/Entities/ManagementCoverageRecord.cs
3. Update EF Core Configurations:
   - PositionConfiguration: src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs
   - Add PositionReportingHistoryConfiguration and ManagementCoverageRecordConfiguration.
   - Configure FKs, indexes ((tenant_id, legal_entity_id, name), (tenant_id, code)), and DeleteBehavior.Restrict.
4. Add EF Migration:
   - Run dotnet ef migrations add AddPositionFoundationSchema.
   - Verify migration SQL script and ApplicationDbContextModelSnapshot.cs.
5. Update Repository Contracts & Implementation:
   - Update IPositionRepository interface and EfPositionRepository implementation with required query methods.
6. Create Tests:
   - Unit tests for EfPositionRepository and EF metadata mapping in tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/.
   - Architecture tests in tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs.
```

---

## 10. Verification Plan for Future Implementation

When Part 2A (and subsequent parts) are executed, verification must include:

### 1. Build Verification
```powershell
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
```

### 2. Migration Script & Model Verification
```powershell
dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```
- Verify `positions` table columns (`legal_entity_id`, `department_id`, `code`, `position_type`, `max_occupancy`, `reports_to_position_id`, `is_active`).
- Verify indexes `ix_positions_tenant_id_legal_entity_id_name` and `ix_positions_tenant_id_code`.
- Verify RLS `tenant_isolation` policy remains active.

### 3. Test Suite Execution
```powershell
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
```

### 4. Git Hygiene
```powershell
git status --short
git diff --check
```

---

## 11. Explicit Non-Goals

The following activities were **explicitly excluded** from Part 1 and were NOT performed:

- ❌ No C# source code modifications in `HRMS-Backend-v1`.
- ❌ No EF Core migrations generated or modified.
- ❌ No unit, integration, or architecture tests created or modified.
- ❌ No Postman collection files created or modified.
- ❌ No frontend files or documentation modified.
- ❌ No OneVo-HR documentation modified.
- ❌ No Department API exposure of `headPositionId`.
- ❌ No Employee assignment implementation (`position_assignments`).
