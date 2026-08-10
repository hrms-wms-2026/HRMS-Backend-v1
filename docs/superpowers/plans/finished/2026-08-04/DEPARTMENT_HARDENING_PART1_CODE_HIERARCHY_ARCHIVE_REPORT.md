# Department Hardening Part 1 Report (Code Rules, Hierarchy Safety, Archive Wording)

**Task:** Harden the existing Department backend (Part 2A-2D, already code-complete) with department-code validation rules, DB-level case-insensitive code uniqueness, parent-hierarchy cycle/inactive-parent prevention, and a rename of "delete" to "archive" in public naming/messages.
**Repository Working Directory:** `C:\onevoNew\HRMS-Backend-v1`
**Branch:** `feature/mkcert-tenant-subdomain-https`
**Date:** 2026-08-04

---

## 1. Files Read

- `C:\onevoNew\Onexo_Department_Position_User_Journey_Validation.md`
- `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md`
- `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT.md`
- `src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/*.cs` (pre-rename)
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/*/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentMapper.cs`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/CreateDepartmentRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/UpdateDepartmentRequest.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/IDateTimeProvider.cs`
- `src/ONEVO.Application/Common/Models/Result.cs`
- `src/ONEVO.Infrastructure/Migrations/20260803085109_AddDepartments.cs`
- `src/ONEVO.Infrastructure/Migrations/20260719180411_AddMissingRlsPolicies.cs`
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContextFactory.cs`
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (Department block)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs`
- Codebase convention precedents: `CreateRoleCommandValidator.cs` / `UpdateRoleCommandValidator.cs` (regex validation style)

---

## 2. Files Changed

**New:**
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/ArchiveDepartmentCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/ArchiveDepartmentCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/ArchiveDepartmentCommandValidator.cs`
- `src/ONEVO.Infrastructure/Migrations/20260804053523_AddDepartmentCodeCaseInsensitiveUniqueIndex.cs` (+ `.Designer.cs`)
- `docs/superpowers/plans/2026-08-04-department-hardening-part1.md`
- `DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT.md` (this file)

**Modified:**
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs` - added `ExistsByCodeAsync`, `IsDescendantAsync`.
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs` - implemented `ExistsByCodeAsync` (case-insensitive `.ToLower()` comparison) and `IsDescendantAsync` (recursive CTE via `Database.SqlQuery<Guid>`).
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs` - removed the case-sensitive `ix_departments_tenant_id_legal_entity_id_code` fluent index; replaced with a comment pointing at the raw-SQL expression index.
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommandValidator.cs` - added code regex (`^[A-Za-z0-9_-]{1,20}$`, applied to trimmed value).
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommandHandler.cs` - code trim/null normalization, duplicate-code check, parent active check.
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommandValidator.cs` - same code regex as Create.
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommandHandler.cs` - code trim/null normalization, duplicate-code check (excluding self), parent active check, parent-descendant cycle check.
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` - added `POST .../archive`; `Delete` kept as documented compatibility alias, both now send `ArchiveDepartmentCommand`.
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` - regenerated by `dotnet ef migrations add` (Department block lost the `ix_departments_tenant_id_legal_entity_id_code` `HasIndex` call; nothing else changed).
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs` - 4 new `ExistsByCodeAsync` tests.
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs` - renamed Delete tests to Archive, added code/hierarchy tests (see Section 8).
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs` - renamed Delete mock references to `ArchiveDepartmentCommand`; added `Archive_*` tests.
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs` - added `ExistsByCodeAsync`/`IsDescendantAsync` to the legal-entity-scoping theory; added the migration-SQL architecture test for the case-insensitive index.
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs` - `DeleteDepartmentCommand`/`Handler` references replaced with `ArchiveDepartmentCommand`/`Handler`; `CommandFiles_LiveUnderOrgStructureDepartmentFolder` InlineData updated to `Commands/ArchiveDepartment`.
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs` - added `ArchiveRoute_ExistsAsPost_WithOrgManagePermission` and `DeleteDepartmentCommand_TypeNoLongerExists_ArchiveWordingUsedInstead`.
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` - 7 new tests (code rules, hierarchy safety, archive route).

**Deleted:**
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/DeleteDepartmentCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/DeleteDepartmentCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/DeleteDepartmentCommandValidator.cs`

**Not changed:** Position schema/entities/APIs (none exist, none added); `headPositionId` exposure in any request contract or command (still absent); any Postman file; any `OneVo-HR/` doc; any frontend file; Legal Entity code (only read, for the `GetByIdForTenantAsync` precedent); `tenantId` acceptance anywhere in request bodies (still absent).

---

## 3. Exact API Routes Before/After

| Method | Route | Before | After |
|---|---|---|---|
| GET | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments` | existed | unchanged |
| GET | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | existed | unchanged |
| POST | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments` | existed | unchanged (now also runs code regex + duplicate-code + parent-active checks) |
| PUT | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | existed | unchanged route (now also runs code regex + duplicate-code + parent-active + parent-cycle checks) |
| DELETE | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | soft-deactivated via `DeleteDepartmentCommand` | **kept**, now delegates to `ArchiveDepartmentCommand` - documented as a deprecated compatibility alias in the controller XML doc comment |
| POST | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}/archive` | did not exist | **new** - `org:manage`, sends `ArchiveDepartmentCommand`, `204 NoContent` on success |

## 4. DELETE Route Disposition

**Kept as a documented compatibility alias**, not removed. Both `Delete` (HTTP `DELETE`) and the new `Archive` (HTTP `POST .../archive`) controller actions send the identical `ArchiveDepartmentCommand` to MediatR. The controller's XML doc comment on `Delete` explicitly states it is "Deprecated compatibility alias for Archive ... delegates to the same ArchiveDepartmentCommand ... never a physical delete. Prefer POST .../archive for new integrations." This was a judgment call within the task's explicit "keep DELETE if backward compatibility is already required, and say so" allowance - since the endpoint was already live in Part 2C/2D with real integration test coverage, backward compatibility was treated as already required rather than invented.

---

## 5. Department Code Rules (Implemented)

- Leading/trailing whitespace trimmed before persistence (`request.Code?.Trim()`).
- Empty or whitespace-only code becomes `null` (`string.IsNullOrEmpty(trimmedCode) ? null : trimmedCode`).
- Maximum length 20 characters.
- Allowed characters: `^[A-Za-z0-9_-]{1,20}$` (uppercase/lowercase letters, digits, hyphen, underscore), enforced by `FluentValidation.Must(code => CodePattern.IsMatch(code!.Trim()))` on both `CreateDepartmentCommandValidator` and `UpdateDepartmentCommandValidator`.
- Casing is **preserved** - never uppercased or lowercased for storage/display. Only the duplicate-check comparison lowercases both sides in memory (`.ToLower()`), matching the existing codebase convention (`CreateRoleCommandValidator`/`UpdateRoleCommandValidator` already use `Matches("^[A-Za-z0-9 _-]+$")` for a similar field).
- Duplicate comparison is case-insensitive, scoped to `(tenant_id, legal_entity_id)`.
- Same code may exist in another legal entity or another tenant (scoping excludes both from the comparison).
- Null code may repeat freely (both the application-level `ExistsByCodeAsync` and the DB expression index have `WHERE code IS NOT NULL`).

---

## 6. DB-Level Uniqueness Strategy

The pre-existing `ix_departments_tenant_id_legal_entity_id_code` unique index (from `20260803085109_AddDepartments`) was **case-sensitive** - `"OPS"` and `"ops"` could both exist at the DB layer even after this task's application-level check blocks it, since only the DB constraint is the final line of defense against races/direct-DB writes. EF Core / Npgsql has no fluent-API support for PostgreSQL expression indexes (`lower(code)`), so this was implemented as raw SQL in a dedicated migration, `20260804053523_AddDepartmentCodeCaseInsensitiveUniqueIndex`:

1. `dotnet ef migrations add` auto-generated `DropIndex(name: "ix_departments_tenant_id_legal_entity_id_code", ...)` in `Up()` (because the corresponding fluent-API `HasIndex` call was removed from `DepartmentConfiguration.cs` first).
2. A hand-added `DO $$ ... RAISE EXCEPTION ...` precheck block runs next, counting `(tenant_id, legal_entity_id, lower(code))` groups with more than one row and failing loudly before the new index is created if any exist.
3. A hand-added `CREATE UNIQUE INDEX ux_departments_tenant_legal_entity_code_lower ON departments (tenant_id, legal_entity_id, lower(code)) WHERE code IS NOT NULL;` follows.
4. `Down()` drops the new index first, then restores the original plain index via the auto-generated `CreateIndex` call - fully symmetric.

This new index is intentionally **not** part of EF's declarative model (no fluent-API equivalent exists); `DepartmentConfiguration.cs` carries a comment explaining this instead of a `HasIndex` call. `DepartmentPart2AArchitectureTests.CodeUniqueIndexMigration_PrechecksDuplicatesAndCreatesCaseInsensitiveExpressionIndex` asserts the migration's raw SQL shape directly (precheck, `lower(code)`, index name, filter, and `Down()` drop) since there is no EF model diff to assert against.

---

## 7. Parent Hierarchy / Cycle Prevention Strategy

- **Existence + tenant/legal-entity scoping + active check**: both `CreateDepartmentCommandHandler` and `UpdateDepartmentCommandHandler` now call `IDepartmentRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, parentId, ct)` (instead of the previous `ExistsAsync` boolean check) to fetch the full parent row in one query. `null` -> `404 Parent department not found`; `IsActive == false` -> `409 Parent department is inactive`.
- **Cycle prevention**: `UpdateDepartmentCommandHandler` additionally calls the new `IDepartmentRepository.IsDescendantAsync(tenantId, legalEntityId, existing.Id, proposedParentId, ct)`. If the proposed parent is anywhere in the subtree rooted at the department being edited, the update is rejected with `409 Cannot set parent: would create a circular hierarchy.` Self-parenting (`ParentDepartmentId == DepartmentId`) is unchanged from Part 2B/2D - still rejected at both the validator (400, wins) and handler (409, unreachable for that exact input) layers.
- **`IsDescendantAsync` implementation**: a PostgreSQL `WITH RECURSIVE` CTE walking down from the department's children, run via `_db.Database.SqlQuery<Guid>($"...")` composed with `.AnyAsync(id => id == possibleDescendantId, ct)`. The scalar-column contract required aliasing the CTE's `id` column as `"Value"` (EF Core's `SqlQuery<T>` convention for primitive result types) - this was caught and fixed via the real-database integration test (`Update_ParentIsDescendant_Returns409` initially failed with `42703: column s.Value does not exist` / HTTP 500, confirming the InMemory unit tests alone could not have caught this).
- **No silent reparenting**: neither handler ever mutates any department's `ParentDepartmentId` other than the one named in the request.

---

## 8. Explicit Statements

- **`headPositionId` remains schema-ready only and is not accepted or changed by this task.** Verified: `rg -n "headPositionId|HeadPositionId" src/ONEVO.Api/Contracts/OrgStructure/Departments` -> 0 matches (unchanged from Part 2C's own check). No handler in this task reads, writes, or validates `HeadPositionId`; `UpdateDepartmentCommandHandler` still never assigns to it.
- **Position APIs are not built in this task.** No `Position` controller, command, query, or contract was added or modified. No Position schema/entity change was made.

---

## 9. Tests Added/Updated

**Unit - `EfDepartmentRepositoryTests.cs`** (+4): `ExistsByCodeAsync_ReturnsTrue_WhenCodeMatchesCaseInsensitively`, `..._ReturnsFalse_WhenSameCodeOnlyExistsInAnotherLegalEntity`, `..._ExcludesGivenId_ForUpdateSelfCheck`, `..._ReturnsFalse_WhenNoDepartmentHasThatCode`.

**Unit - `DepartmentApplicationUnitTests.cs`** (+11 net; 2 renamed Delete->Archive, 1 rewritten to use `GetByIdForLegalEntityAsync`): `CreateDepartmentCommandValidator_RejectsInvalidCodeCharacters` (theory, 3 cases), `CreateDepartment_TrimsCode`, `CreateDepartment_ConvertsWhitespaceCodeToNull`, `CreateDepartment_RejectsDuplicateCodeCaseInsensitivelyInSameLegalEntity`, `CreateDepartment_AllowsSameCodeInDifferentLegalEntity`, `UpdateDepartment_RejectsDuplicateCodeCaseInsensitivelyExcludingSelf`, `UpdateDepartment_RejectsInactiveParentDepartment`, `UpdateDepartment_RejectsDescendantParentSelection`, `ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt` (renamed), `ArchiveDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist` (renamed).

**Unit - `DepartmentsControllerTests.cs`** (+2, 2 renamed): `Archive_SendsCommand_WithRouteIds_AndReturnsNoContent`, `Archive_NotFound_ReturnsProblem404`; existing `Delete_*` tests updated to assert against `ArchiveDepartmentCommand`.

**Architecture** (+5): `CodeUniqueIndexMigration_PrechecksDuplicatesAndCreatesCaseInsensitiveExpressionIndex`, `ArchiveRoute_ExistsAsPost_WithOrgManagePermission`, `DeleteDepartmentCommand_TypeNoLongerExists_ArchiveWordingUsedInstead`, plus `ExistsByCodeAsync`/`IsDescendantAsync` added to the existing legal-entity-scoping `[Theory]`; `CommandAndQueryTypes`/`HandlerTypes`/InlineData in `DepartmentPart2BArchitectureTests` updated to `ArchiveDepartment`.

**Integration - `DepartmentsIntegrationTests.cs`** (+7): `Create_WithCode_Returns201_AndCodeIsPreserved`, `Create_DuplicateCodeCaseInsensitiveInSameLegalEntity_Returns409`, `Create_SameCodeInDifferentLegalEntity_IsAllowed`, `Create_InvalidCodeCharacters_Returns400`, `Update_ParentIsInactive_Returns409`, `Update_ParentIsDescendant_Returns409`, `Archive_Route_SoftDeactivates_AndListExcludesByDefault`.

---

## 10. Build/Test Results

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded. 0 Errors, 0 Warnings (in touched files; 1 pre-existing unrelated CS8602 warning in AdminAuthController.cs).

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 1192, Skipped: 0, Total: 1192.

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 408, Skipped: 0, Total: 408.

dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~DepartmentsIntegrationTests" --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 10m
  -> Total tests: 25, Passed: 25, Failed: 0.
     (First run surfaced 1 real bug: Update_ParentIsDescendant_Returns409 got HTTP 500 /
      Npgsql 42703 "column s.Value does not exist" from IsDescendantAsync's raw SQL -
      fixed by aliasing the CTE's final SELECT as `id AS "Value"`, per EF Core's
      SqlQuery<T> scalar-column convention. Rerun after the fix: 25/25 green.)

dotnet ef migrations script --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
  -> Confirmed: departments table + RLS (tenant_isolation policy) created by AddDepartments,
     untouched by any later migration. AddDepartmentCodeCaseInsensitiveUniqueIndex drops
     ix_departments_tenant_id_legal_entity_id_code, runs the RAISE EXCEPTION precheck, then
     creates ux_departments_tenant_legal_entity_code_lower on lower(code), scoped and filtered
     as specified.

git diff --check
  -> Exit code 0 (clean; pre-existing CRLF-normalization warnings on unrelated in-progress
     LegalEntity files are informational, not whitespace errors).

ASCII scan across all Part 1 touched/created source, test, and migration files
  -> 0 non-ASCII characters found (final state). First pass via `grep -qP` under Git Bash
     silently no-op'd on a locale error and produced a false-clean result; redone with a
     locale-independent tool and this caught 6 pre-existing Unicode box-drawing comment
     separators ("--- Auth/permission matrix ---" etc., originally U+2500/U+2501) in
     DepartmentsIntegrationTests.cs - 5 pre-existing (from Part 2D) plus 1 this task added
     matching that same convention. Since this file is touched by Part 1, all 6 were
     normalized to plain ASCII hyphens. Rebuilt and confirmed 0 build errors after the fix.
```

---

## 11. Remaining Gaps Mapped to the Requirement Document

Per `Onexo_Department_Position_User_Journey_Validation.md`, explicitly out of scope for this Part 1 slice:

- **Dependency archive checks** ("Disable the Archive action when the department has employees, positions, or unresolved dependencies") - not implemented. `ArchiveDepartmentCommandHandler` unconditionally deactivates; it does not check for active employees, positions, or child departments before archiving.
- **Restore archived department** - no endpoint or command exists to reactivate (`IsActive = true`) an archived department.
- **Search/sort/pagination** on the department list - not implemented; `ListDepartmentsQuery` is unchanged from Part 2B (returns the full scoped list, client-side sort only).
- **Position management** - out of scope per this task's explicit constraints; no Position APIs, entities, or schema changes were made.
- **Management scope** (department/position management-responsibility assignment) - out of scope; not part of the Department entity or this task.
- **Occupant assignment** - out of scope; depends on Position APIs, which are not built.
- **Hierarchy "impact summary before confirmation"** (UX-level warning shown before reparenting a subtree) - this is a frontend/UX requirement; the backend now blocks the unsafe case (cycle/inactive parent) but does not compute or return an "N subdepartments will move" impact preview.

Also noted as intentional, not an oversight:

- **`IDepartmentRepository.ExistsAsync`** is no longer called by `CreateDepartmentCommandHandler`/`UpdateDepartmentCommandHandler` (both now use `GetByIdForLegalEntityAsync` to get the full entity for the `IsActive` check in the same round trip). The method was **not removed** from the interface/implementation because `DepartmentPart2AArchitectureTests` pins it by name via `[InlineData(nameof(IDepartmentRepository.ExistsAsync))]`, and removing it was outside this task's stated scope of additions.
