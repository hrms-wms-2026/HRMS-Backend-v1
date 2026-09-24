# Department Foundation — Backend Audit & Implementation Plan (Part 1)

**Scope:** Audit only. No code, migrations, tests, Postman, or OneVo-HR docs were changed.
**Repos read:** `C:\onevoNew\HRMS-Backend-v1`, `C:\onevoNew\OneVo-HR`

---

## 1. Executive Summary

**Department is MISSING from the backend.** An exhaustive search (`grep -rli department` across `src/`, `tests/`, and every Postman file in the workspace) found:

- Zero `Department` domain entity, EF configuration, repository, command/query, controller, or test.
- Zero `departments` table in any migration (`src/ONEVO.Infrastructure/Migrations/*.cs` — 47 migrations, none create `departments`).
- One unrelated legacy trace: `Employee.DepartmentId` (`src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs:16`) — a bare nullable `Guid`, not EF-configured with any relationship, not pointed at a real table. Leave it untouched; wiring it is a Core HR change, not Org Structure.
- One correct trace: `PermissionSeeder.cs:92-93` already defines `org:read`/`org:manage` with descriptions that mention "departments," anticipating this feature.

**Department is Phase 1 per OneVo-HR**, unambiguously:
- `database/schemas/org-structure.md:5` — "**Phase:** Phase 1", `departments` is the second of 8 documented tables.
- `modules/org-structure/overview.md:5` — "**Phase:** 1 - Build", lists `departments` among 7 "Key Database Tables" and in the "Features" list.
- `database/phase1-table-inventory.md` cross-references `departments` extensively (FK target for exception-engine, time-attendance, monitoring, work-schedules, etc.).

**Backend alignment vs. drift:** The completed Legal Entity/Company work (Parts 2A–2D) is structurally aligned with OneVo-HR for entity/repository/controller shape, but it already carries two gaps relative to the docs' own rules that Department will inherit unless a decision is made:
1. The docs' "Mandatory API Safety Contract" (`departments/end-to-end-logic.md:8-10`, restated per-endpoint) requires paginated collections, `Idempotency-Key` on mutations, and `If-Match`/xmin optimistic concurrency. `LegalEntitiesController.List` (`LegalEntitiesController.cs:29-37`) is unpaginated; no controller in `OrgStructure` uses `[Idempotent]` or an ETag/xmin check.
2. There is **no backend mechanism for "selected Company" context** at all (see §4). Every Department doc flow depends on resolving this server-side.

Neither gap is unique to Department — they are pre-existing in the one Org Structure feature that already shipped. This report flags them as decisions, not as blockers invented by this audit.

---

## 2. Canonical Model

**Canonical table/entity name:** `departments` (confirmed identically in `database/schemas/org-structure.md:43`, `modules/org-structure/overview.md:98`, `modules/org-structure/departments/overview.md:16`).

**Required relationships:**
- `tenant_id` → `tenants` (every tenant-owned table in this codebase carries this directly, per `ITenantOwnedEntity`; `departments.tenant_id` is documented at `org-structure.md:48`).
- `legal_entity_id` → `legal_entities` (the Company scoping boundary; documented at `org-structure.md:49`).
- `parent_department_id` → `departments` (self-referencing, nullable).
- `head_position_id` → `positions` (nullable; must be `unique`-type position in the same Company — **unenforceable today**, see §10).

**Required fields (from `org-structure.md:43-63` + `departments/overview.md:18` + `departments/end-to-end-logic.md`):**

| Field | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | FK tenants |
| `legal_entity_id` | uuid | FK legal_entities |
| `name` | varchar(100) | required, unique within legal entity |
| `code` | varchar(20) | optional/auto-generated, unique within legal entity, stable once set |
| `parent_department_id` | uuid | nullable self-FK |
| `head_position_id` | uuid | nullable FK positions |
| `is_active` | boolean | logical-delete flag |
| `created_at` | timestamptz | |

Docs do **not** list an `updated_at` column for `departments` (unlike `legal_entities` and `positions`, which both have one). This is a genuine doc silence, not a decision either way — flagged in §10.

**Fields that already exist:** none — the entity does not exist. (`Employee.DepartmentId` is not a Department field; it's an orphaned Employee column.)

**Fields missing:** all of the above — full entity needs to be created from scratch.

**Fields that should NOT be added yet:**
- `head_position_id` — see §10. `Position` (`src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs:5-9`) is a legacy stub (`Name` + `DefaultRoleId` only — no `legal_entity_id`, `position_type`, or `department_id`). The doc's head-position rules ("must be `unique` type," "must belong to same Company") cannot be validated against this stub. Adding the column now means shipping an FK to a table whose shape will change incompatibly once real Position work starts.
- Anything from Position/Position Assignment ("delete blocked if positions remain in department") — same reason.

---

## 3. Relationship Decision

**Department belongs to LegalEntity (Company), and also carries `tenant_id` directly — both are required, not either/or.**

Evidence, consistent across five independent doc files with no contradiction on this point:
- `modules/org-structure/departments/overview.md:12` — "Department management is Company-context first... maps internally to `legal_entity_id`."
- `modules/org-structure/legal-entities/overview.md:20` — "Departments belong to one Company." (Phase 1 Rules)
- `database/schemas/org-structure.md:49,57` — `departments.legal_entity_id` FK, alongside `departments.tenant_id` FK — same dual-FK pattern every other Org Structure table uses (`legal_entities`, `positions`, `position_assignments` all carry both `tenant_id` and their business-scope FK).
- `modules/org-structure/overview.md:15` — "Departments and positions are Company-specific," under a module whose `tenant_id` scoping is separately stated throughout.

This mirrors the shipped `LegalEntity` pattern exactly: `LegalEntity` implements `ITenantOwnedEntity` (carries `TenantId`) and additionally has its own business hierarchy (`ParentLegalEntityId`). Department is one level down: `tenant_id` for tenant isolation/RLS, `legal_entity_id` for the Company business boundary.

No blocker here — docs are unanimous.

---

## 4. API Surface Plan

**Use the codebase's actual route family, not the task's example.** The shipped `LegalEntitiesController` uses `/api/v1/org/legal-entities` (`LegalEntitiesController.cs:20`), and OneVo-HR docs specify `/api/v1/org/departments` verbatim (`departments/overview.md:26-29`, `org-structure/overview.md:225-228`). The task's `/api/v1/org-structure/departments` example does **not** match either source and should not be used.

| Method | Route | Permission | Doc source |
|---|---|---|---|
| GET | `/api/v1/org/departments?view=tree\|flat&page=&page_size=&sort=` | `org:read` | `departments/overview.md:26`, `departments/end-to-end-logic.md:53` |
| GET | `/api/v1/org/departments/{id}` | `org:read` | **Inferred** — not in the docs' endpoint table; only evidence is `IOrgStructureService.GetDepartmentAsync` (`org-structure/overview.md:37`). Flagging as inferred, not doc-mandated. |
| POST | `/api/v1/org/departments` | `org:manage` | `departments/overview.md:27` |
| PUT | `/api/v1/org/departments/{id}` | `org:manage` | `departments/overview.md:28` |
| DELETE | `/api/v1/org/departments/{id}` | `org:manage` | `departments/overview.md:29` |

**Blocking gap for Part 2B/2C — no "selected Company" context mechanism exists in the backend.**

Every Department doc flow says the server resolves "selected Company from topbar context" and maps it to `legal_entity_id` (`departments/end-to-end-logic.md:24,55,110`), and the docs' own integration test spec asserts `422` when that context is missing, with the POST body carrying **no** `legalEntityId` field (`departments/testing.md:247-254`, `Post_MissingCompanyContext_Returns422`).

I checked `ICurrentUser` (`src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs:1-13`) — it exposes only `UserId`, `TenantId`, `Email`, `Permissions`, `IsAuthenticated`, `SessionBinding`, `SessionExpiresAt`. There is no `LegalEntityId`/company-selection property. A repo-wide grep for `SelectedCompany`, `SelectedLegalEntity`, `X-Legal-Entity`, `CompanyContext` returned **no matches** anywhere in `src/`. `LegalEntitiesController.Create` doesn't need this (creating a Company isn't itself Company-scoped), so this gap was never exercised by the Parts 2A–2D work.

**This does not block Part 2A** (repository methods take `legalEntityId` as an explicit parameter, same shape as `ILegalEntityRepository`'s `tenantId` parameter). It **does block Part 2B/2C** until one of these is decided:
(a) build a real selected-Company context service (header/claim/session-backed), or
(b) document a deviation where `legalEntityId` is client-supplied in the request and validated as belonging to the tenant.

---

## 5. Permissions Plan

**Use the existing, already-defined permissions — no new permissions needed.**

- `org:read` — list/get. Defined at `PermissionSeeder.cs:92`, mirrored in `ModuleCatalogSeeder.cs:242`. Matches docs: `permissions-reference.md:71` ("View org structure, departments, hierarchy") and `permissions-reference.md:284` (`departments:read` is explicitly an alias covered by `org:read`).
- `org:manage` — create/update/delete. Defined at `PermissionSeeder.cs:93`, `ModuleCatalogSeeder.cs:243`. Matches docs: `permissions-reference.md:72` and `permissions-reference.md:306` (`org:write` covered by `org:manage`).

**Permission gap found (pre-existing, not introduced by Department):** no production `RoleTemplateSeeder.cs` template grants `org:read` or `org:manage`. Its two seeded templates are `HR Manager` (`attendance:read, employees:read, leave:approve, leave:read` — `RoleTemplateSeeder.cs:53`) and `Workspace Member` (unrelated, Work Management). Only `DevSmokeTestTenantSeeder.cs:91,96` grants `org:read`/`org:manage`, and only to dev/smoke-test tenants. This means **today, no real tenant role can call the Department (or even the already-shipped LegalEntity) endpoints** without a manual permission grant or the `*` Super Admin bypass. This should be fixed (e.g., add `org:manage`/`org:read` to an Admin/Owner-equivalent production role template) before or alongside Department shipping, or the feature is unreachable in practice.

---

## 6. Validation Rules

From `departments/end-to-end-logic.md` and `department-hierarchy.md`:

- `name`: required, unique **within the selected Company** (`legal_entity_id`), not tenant-wide.
- `code`: optional at creation (auto-generated if omitted), unique within the selected Company, stable — renaming a department must not change its `code` without an explicit admin action.
- `parent_department_id`: optional; if provided, parent must exist, be active, and belong to the same Company; updates must reject circular references via ancestor walk (walk from new parent to root, reject if the current department's id appears).
- `head_position_id`: optional; if provided, position must exist, be active, be `position_type = unique`, and belong to the same Company. **Deferred — unenforceable until Position exists properly (see §10).**
- `legal_entity_id` (selected Company): must exist, belong to the tenant, and be active.
- Active/inactive: `is_active` is the only status flag; there is no separate soft-delete column.
- Deletion: blocked if the department has active employees; blocked if positions remain assigned to it (positions must be moved first); deletion re-parents child departments to the deleted department's parent (or promotes them to root if the deleted department was itself root); deletion is logical (`is_active = false`), never a physical row removal — historical/audit records are preserved.
- Cycle prevention: required for `parent_department_id` on both create (parent existence/Company match) and update (full ancestor-chain walk).

---

## 7. Schema/Migration Plan (design only — no migration to be written in this task)

**Table `departments`:**

| Column | Type | Constraint |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL, FK → `tenants` |
| `legal_entity_id` | uuid | NOT NULL, FK → `legal_entities`, `ON DELETE RESTRICT` (matches `legal_entities.parent_legal_entity_id`'s own Restrict precedent — never silently cascade-destroy org structure) |
| `name` | varchar(100) | NOT NULL |
| `code` | varchar(20) | NULL |
| `parent_department_id` | uuid | NULL, self-FK, `ON DELETE RESTRICT` |
| `head_position_id` | uuid | **Recommend omitting from this migration entirely** — see §10; needs explicit approval either way, not a pure audit fact |
| `is_active` | boolean | NOT NULL DEFAULT true |
| `created_at` | timestamptz | NOT NULL DEFAULT now() |
| `updated_at` | timestamptz | NULL — docs don't list this column for `departments`, but `legal_entities`/`positions` both have it; recommend adding for consistency (open decision, flagged in §10) |

**Indexes:**
- `ix_departments_tenant_id` on `(tenant_id)` — matches `legal_entities`' plain tenant index (`LegalEntityConfiguration.cs:65`).
- `ix_departments_legal_entity_id_name` UNIQUE on `(legal_entity_id, name)` — scope is Company, not tenant. **Open decision:** filtered by `is_active` or not? `legal_entities` itself uses an unfiltered unique index on `(tenant_id, name)` (`LegalEntityConfiguration.cs:71-73`) even though it also supports logical deactivation — that's the closest precedent, and it means a deactivated department's name would still block reuse. Flagging as a decision, not assuming.
- `ix_departments_legal_entity_id_code` UNIQUE on `(legal_entity_id, code)` `WHERE code IS NOT NULL` — same filtered-optional-unique pattern as `legal_entities.company_code`/`registration_number` (`LegalEntityConfiguration.cs:74-81`).
- `ix_departments_parent_department_id` on `(parent_department_id)` — supports the recursive CTE tree queries.

**RLS:** `departments` needs the standard `tenant_isolation` policy (admin-bypass + tenant-match), applied via its own dedicated migration using the `AddMissingRlsPolicies.cs` pattern (`ALTER TABLE ... ENABLE/FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ...`) rather than being folded into the original `AddRlsPolicies.cs` migration — that's exactly how `positions` was retrofitted (`AddMissingRlsPolicies.cs:21`), and `departments` is a new table being added after that baseline, so it needs the same treatment `positions` got, not the original list.

**Soft-delete:** No `IsDeleted`/`DeletedAt` columns. Follow the `LegalEntity` precedent exactly: implement `ITenantOwnedEntity` directly (not `BaseEntity`, even though `BaseEntity` offers `IsDeleted`) and use `IsActive` as the only logical-delete flag, consistent with the doc's "deletion is a logical deletion... setting `is_active = false`" rule (`org-structure.md:63`).

---

## 8. Repository/Application Plan

**Part 2A — Schema/Entity/Repository**
- `Department` domain entity (`ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`), implementing `ITenantOwnedEntity` directly (mirrors `LegalEntity`, not `BaseEntity`).
- `DepartmentConfiguration : IEntityTypeConfiguration<Department>` (`ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/`), following `LegalEntityConfiguration.cs`'s check-constraint/index/FK style.
- One migration: `departments` table + FKs + indexes + dedicated `tenant_isolation` RLS policy (per §7).
- `IDepartmentRepository` (`ONEVO.Application/Features/OrgStructure/RepositoryInterfaces/`) with methods scoped by `legalEntityId` (mirroring `ILegalEntityRepository`'s `tenantId`-scoped shape):
  - `ListByLegalEntityAsync(tenantId, legalEntityId, ct)` (flat)
  - `GetByIdForTenantAsync(tenantId, id, ct)`
  - `GetAncestorIdsAsync(tenantId, departmentId, ct)` (for cycle detection)
  - `GetChildrenAsync(tenantId, departmentId, ct)` (for re-parenting on delete)
  - `AddAsync` / `Update`
  - `IsNameUniqueInLegalEntityAsync(legalEntityId, name, excludeId?, ct)`
  - `IsCodeUniqueInLegalEntityAsync(legalEntityId, code, excludeId?, ct)`
  - `HasActiveEmployeesAsync(departmentId, ct)` / `HasPositionsAsync(departmentId, ct)` — these two cannot be meaningfully implemented against real data yet (Employee.DepartmentId is unwired; Position is a stub); decide in Part 2A/2B whether to stub them returning `false` with a tracked TODO, or defer them to Part 2B.
  - `SaveChangesAsync`
- `EfDepartmentRepository : IDepartmentRepository`.

**Part 2B — Application/Contracts**
- Commands: `CreateDepartmentCommand/Handler/Validator`, `UpdateDepartmentCommand/Handler/Validator`, `DeleteDepartmentCommand/Handler/Validator` (FluentValidation, mirroring `CreateLegalEntityCommandValidator.cs`'s style).
- Queries: `ListDepartmentsQuery/Handler` (flat + tree, CTE per `end-to-end-logic.md:63-68`), `GetDepartmentQuery/Handler`.
- DTOs: `DepartmentDto`, `DepartmentTreeDto` (`ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/`), request DTOs under `ONEVO.Api/Contracts/OrgStructure/Departments/`.
- `DepartmentMapper`.
- Outbox integration: `IOutboxWriter.EnqueueAsync(...)` (`ONEVO.Application/Common/ServiceInterfaces/IOutboxWriter.cs`) must be called from the command handler **before** the repository's `SaveChangesAsync`, since `OutboxWriter.EnqueueAsync` only does `_db.Set<OutboxMessage>().AddAsync` on the shared `ApplicationDbContext` (`OutboxWriter.cs:42`) — it relies on the caller's later `SaveChangesAsync` to commit atomically with the business change. This satisfies the doc's "same transaction" mandate (`end-to-end-logic.md:12-14`) only if both writes go through the same `ApplicationDbContext` instance/scope — currently exercised in only two Auth handlers, never in the shipped LegalEntity work, so this will be the first Org Structure use of it.
- Event name to use: **needs a decision** — see §10 (doc conflict between `DepartmentUpdated` and `DepartmentMoved`).
- Selected-Company context resolution: **blocked on §4** decision.

**Part 2C — Controller/Routes**
- `DepartmentsController` under `ONEVO.Api/Controllers/Tenant/OrgStructure/`, same shape as `LegalEntitiesController.cs`: `[Authorize(Policy = "TenantPolicy")]`, `[RequirePermission("org:read"|"org:manage")]` per action, `Problem(result.Error, statusCode: result.StatusCode ?? 400)` on failure.
- Routes per §4.

**Part 2D — Integration/Postman/Manual validation**
- Testcontainers integration tests (per §9).
- Postman collection additions (not part of this task).
- Manual validation against a running tenant.

---

## 9. Testing Plan

**Unit tests** — `departments/testing.md:15-176` already specifies a near-complete `DepartmentServiceTests` suite (create success, legal-entity-not-found, duplicate name in same/different legal entity, duplicate code, parent-in-different-legal-entity, no-code-generates-code, outbox-message-added, circular-reference on update, code-changed-to-existing). Use it close to verbatim in Part 2B/2C.

**Architecture tests** — this repo has a per-feature convention (`LegalEntitiesControllerArchitectureTests.cs`, `LegalEntityGeneralSettingsArchitectureTests.cs`, `LegalEntityPart2BArchitectureTests.cs`). Department should get an equivalent (`DepartmentsControllerArchitectureTests.cs` or similar) covering controller-permission-attribute presence and no controller→DbContext/service bypass. The existing generic `LayerDependencyTests.cs` and `TenantIsolationArchitectureTests.cs` should already cover the new Department code once it exists, without modification.

**Integration tests** (Testcontainers Postgres, per `departments/testing.md:184-303`):
- Create success → appears in tree.
- Duplicate name in same Company → 409.
- Same name in different Company → 201 (proves scope is Company, not tenant).
- Parent in different Company → 422.
- Missing Company/selected-context → 422 (blocked until §4 is resolved).
- Child department appears under parent in tree; 5-level-deep hierarchy renders correctly.
- Cross-Company isolation: departments from another Company in the same tenant do not leak into flat/tree list.
- Cross-tenant isolation: departments from another tenant are invisible (RLS-backed).
- Negative: delete blocked with active employees; delete blocked with positions (deferred until Employee/Position wiring exists — may need to ship as a follow-up test once those land); parent cycle rejected on update.

---

## 10. Risks / Blockers

1. **No selected-Company context mechanism exists anywhere in the backend** (`ICurrentUser` has no company-selection property; no header/claim/service found). Blocks Part 2B/2C exactly as designed by the docs. Needs an explicit decision before Part 2B starts (§4).
2. **No production role template grants `org:manage`/`org:read`.** The feature would ship unreachable for real tenants without a role/permission-seeding fix (§5). Pre-existing gap, not introduced by Department, but Department is the first feature where it will visibly matter to testers.
3. **`Position` is an incompatible legacy stub** (`Name` + `DefaultRoleId` only). The doc's head-position rules cannot be enforced. Recommend omitting `head_position_id` from the Part 2A migration entirely rather than shipping a placeholder FK to a table that will change shape — but this is a schema-shape call needing explicit approval, not a pure audit fact.
4. **Doc self-contradictions found (not resolved — flagging per instructions):**
   - Table count: `modules/org-structure/overview.md:7` says "Tables: 7"; `database/schemas/org-structure.md:5` says "Tables: 8." (Likely just missed being updated when a table was added; doesn't affect the Department relationship decision.)
   - Event name: `Userflow/Org-Structure/department-hierarchy.md:68` lists `DepartmentMoved` as the event for a parent-department change; `modules/org-structure/overview.md:205` lists `DepartmentUpdated` as the event for "Department metadata or hierarchy commits." These may be intended as the same event under two names, or two distinct events — Part 2B needs one canonical answer before wiring the outbox payload type.
5. **Stale task-file link:** `modules/org-structure/overview.md:8` and `current-focus/DEV3-org-structure.md` both point to `current-focus/DEV3.md` as the Org Structure task file, but `DEV3.md`'s actual content (read in full) is entirely about Work Management (workspaces, projects, chat, IDE APIs) with no Org Structure section. Not blocking, but the module doc's own task-file pointer is dead.
6. **Doc silence on `updated_at`** for `departments` (present on sibling tables `legal_entities`/`positions`, absent from the documented `departments` column list). Needs a decision, not an assumption (§7).
7. **`LegalEntitiesController.List` is unpaginated** and no Org Structure controller implements `Idempotency-Key`/`If-Match`, despite the docs mandating both for every collection/mutation endpoint. If Department copies the shipped LegalEntity pattern verbatim, it inherits this gap; fixing it only for Department would create inconsistency within the same module. Needs a one-time decision that then applies to both.
8. **`Employee.DepartmentId`** is an existing, unconfigured, FK-less scalar column. Confirmed it is out of scope for Department Part 2A — do not touch; wiring it is a Core HR change.

None of these block **Part 2A** as scoped in §11. Items 1 and 2 block Part 2B/2C. Item 3 requires a decision before finalizing the Part 2A migration shape specifically for one column.

---

## 11. Final Recommendation

**Part 2A can start now**, scoped exactly as follows, because it has no dependency on the selected-Company-context gap (repository methods take `legalEntityId` as an explicit parameter, same as `ILegalEntityRepository` takes `tenantId`):

- `Department` domain entity (direct `ITenantOwnedEntity`, not `BaseEntity`) with the columns in §7 **except** `head_position_id` (omit pending Position Part 3, or get explicit sign-off to include it as an unenforced nullable FK — this is the one open call before writing the migration).
- `DepartmentConfiguration` (EF) with the indexes/constraints in §7, including a decision on whether the name/code unique indexes are filtered by `is_active`.
- One migration: `departments` table + FKs + indexes + its own dedicated `tenant_isolation` RLS policy (via the `AddMissingRlsPolicies.cs` pattern, not the original `AddRlsPolicies.cs` list).
- `IDepartmentRepository`/`EfDepartmentRepository` per §8, with `HasActiveEmployeesAsync`/`HasPositionsAsync` explicitly stubbed or deferred (call this out in the Part 2A PR description so it isn't mistaken for a real guard).

**Before Part 2B starts**, two decisions need to be made (not by this report):
1. How "selected Company" context is resolved server-side for list/create/update/delete — a new mechanism, or a documented deviation where `legalEntityId` is client-supplied and tenant-validated.
2. Whether to add `org:manage`/`org:read` to a production role template now, so the feature is actually reachable once shipped.

---

## Verification

**Files read (primary; full list is longer via directory listings):**

OneVo-HR:
- `modules/org-structure/departments/overview.md`, `end-to-end-logic.md`, `testing.md`
- `modules/org-structure/overview.md`
- `modules/org-structure/legal-entities/overview.md`
- `database/schemas/org-structure.md`
- `database/phase1-table-inventory.md` (grep)
- `Userflow/Org-Structure/department-hierarchy.md`
- `Userflow/Auth-Access/permissions-reference.md` (grep + read)
- `current-focus/DEV3-org-structure.md`, `current-focus/DEV3.md`

HRMS-Backend-v1:
- `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs`
- `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`
- `src/ONEVO.Domain/Common/BaseEntity.cs`
- `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/LegalEntity/LegalEntityConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/CreateLegalEntity/CreateLegalEntityCommandHandler.cs`, `CreateLegalEntityCommandValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/DeleteLegalEntity/DeleteLegalEntityCommandHandler.cs`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`
- `src/ONEVO.Api/Filters/RequirePermissionAttribute.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs`
- `src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxWriter.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`, `ModuleCatalogSeeder.cs`, `DevSmokeTestTenantSeeder.cs`, `RoleTemplateSeeder.cs`
- `src/ONEVO.Infrastructure/Migrations/20260719180411_AddMissingRlsPolicies.cs`, `20260515022320_AddRlsPolicies.cs` (grep)
- Full migration filename listing (47 files) confirming no `departments` migration exists.
- `tests/ONEVO.Tests.Architecture/` directory listing (confirming per-feature architecture test convention).

**Report path:** `C:\onevoNew\HRMS-Backend-v1\DEPARTMENT_FOUNDATION_BACKEND_AUDIT_PLAN.md`

**Changes made:** none. No code, migrations, tests, Postman collections, or OneVo-HR docs were modified. No `git add`/commit/push. Both repos' worktrees were confirmed clean before and remain untouched (`git status --short` empty in both).

**Is Part 2A safe to start:** **Yes**, with the scope in §11 — one open call remains (include `head_position_id` now vs. defer it), which needs a yes/no from the user before the migration is written.
