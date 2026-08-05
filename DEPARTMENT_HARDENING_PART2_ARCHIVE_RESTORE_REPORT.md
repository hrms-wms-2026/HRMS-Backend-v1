# Department Hardening Part 2 Report (Archive Dependency Checks and Restore)

**Task:** Extend the existing Department backend (Part 2A-2D and Part 1, already code-complete) with archive dependency checking (child departments, active employees, positions), a read-only archive-check endpoint, a restore endpoint, and dependency-blocked archive behavior for both `POST .../archive` and the `DELETE` compatibility alias.
**Repository Working Directory:** `C:\onevoNew\HRMS-Backend-v1`
**Branch:** `feature/mkcert-tenant-subdomain-https`
**Date:** 2026-08-04

---

## 1. Files Read

- `C:\onevoNew\Onexo_Department_Position_User_Journey_Validation.md`
- `DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md`
- `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT.md`
- `src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`
- `src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`
- `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs`
- `src/ONEVO.Domain/Common/BaseEntity.cs`, `src/ONEVO.Domain/Common/ITenantOwnedEntity.cs`
- `src/ONEVO.Domain/Lookups/EmploymentStatus.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/Lookups/Common/EmploymentStatusConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/LookupDataSeeder.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/*.cs`, `Queries/GetDepartment/*.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentResponse.cs`, `DepartmentListItemResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentMapper.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs`, `IDateTimeProvider.cs`
- `src/ONEVO.Application/Common/Models/Result.cs`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/CreateDepartmentRequest.cs`, `UpdateDepartmentRequest.cs`
- `src/ONEVO.Api/Filters/RequirePermissionAttribute.cs`
- `src/ONEVO.Api/Middleware/CsrfProtectionMiddleware.cs`
- `src/ONEVO.Api/Program.cs` (middleware pipeline order)
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (global query-filter composition)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs`
- Migration snapshot: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (grepped for `fk_employees_` to confirm `Employee.UserId`/`DepartmentId`/`CreatedById` carry no foreign-key constraint, only a unique index on `UserId`)

---

## 2. Files Changed

**New:**
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentArchiveBlockers.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentArchiveDependencyResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Services/DepartmentArchiveDependencyEvaluator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQuery.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQueryValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/RestoreDepartment/RestoreDepartmentCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/RestoreDepartment/RestoreDepartmentCommandValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/RestoreDepartment/RestoreDepartmentCommandHandler.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2ArchiveRestoreArchitectureTests.cs`
- `docs/superpowers/plans/2026-08-04-department-hardening-part2.md`
- `DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT.md` (this file)

**Modified:**
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs` - added `CountActiveChildrenAsync`, `CountActiveEmployeesAsync`.
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs` - implemented both, block-bodied, `AsNoTracking()`, explicit tenant/legal-entity scoping.
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/ArchiveDepartmentCommandHandler.cs` - runs `DepartmentArchiveDependencyEvaluator` before deactivating; returns `409 Conflict` with the blocker message if blocked.
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` - added `POST .../archive-check` (`org:read`) and `POST .../restore` (`org:manage`).
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs` - 2 new repository tests.
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs` - 14 new tests (check-dependencies, archive-blocks, restore, auth guards) + 1 existing test updated to mock zero blockers.
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs` - 5 new controller tests.
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs` - added the 2 new repository methods to the legal-entity-scoping theory.
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs` - registered `RestoreDepartmentCommand`/`Handler` and `CheckDepartmentArchiveDependenciesQuery`/`Handler` in the command/query/handler arrays and file-location theories.
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs` - added `ArchiveCheckRoute_ExistsAsPost_WithOrgReadPermission`, `RestoreRoute_ExistsAsPost_WithOrgManagePermission`.
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` - 11 new tests (see Section 9).

**Not changed:** Position schema/entities/APIs (none added); `headPositionId` exposure in any request contract or command (still absent, and this task's `RestoreDepartmentCommandHandler` explicitly never assigns it - see Section 6); any Postman file; any `OneVo-HR/` doc; any frontend file; `Department.Code`/`Department.Name` (restore never touches them); Legal Entity code; `tenantId`/`legalEntityId` acceptance anywhere in a request body (still absent - both new endpoints take zero request body).

---

## 3. Exact Routes

| Method | Route | Permission | Success | Notes |
|---|---|---|---|---|
| POST | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}/archive-check` | `org:read` | `200 OK`, `DepartmentArchiveDependencyResponse` | New. Read-only, no mutation. |
| POST | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}/archive` | `org:manage` | `204 NoContent` | Existing (Part 1) - now blocks with `409` if dependencies exist. |
| DELETE | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | `org:manage` | `204 NoContent` | Existing (Part 1) compatibility alias - delegates to the identical `ArchiveDepartmentCommand`, so it inherits the same `409` blocking automatically. Confirmed unconditionally by `DeleteAndArchiveActions_BothDelegateToArchiveDepartmentCommand` (counts exactly 2 `new ArchiveDepartmentCommand(` call sites in the controller source) and directly by `Delete_Blocked_WhenActiveChildExists_Returns409`. |
| POST | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}/restore` | `org:manage` | `204 NoContent` | New. |

**Archive-check response shape** (matches the task's spec exactly, plus one added field explained in Section 4):

```json
{
  "departmentId": "...",
  "canArchive": false,
  "blockers": {
    "activeSubdepartmentCount": 2,
    "activeEmployeeCount": 4,
    "activePositionCount": 0,
    "isUsedAsParent": true,
    "hasActiveEmployees": true,
    "hasActivePositions": false,
    "positionDependencyCheckSupported": false
  },
  "message": "This department cannot be archived yet. Reassign linked subdepartments and employees first."
}
```

`message` is built dynamically from whichever categories actually block (only "subdepartments", "employees", or both are ever named, since positions never block - see Section 4). When `canArchive` is `true`, `message` is exactly `"No active employees, positions, or subdepartments are linked to this department."`, matching the task's literal example text.

---

## 4. Dependency Sources

| Blocker | Source | Method |
|---|---|---|
| `activeSubdepartmentCount` / `isUsedAsParent` | `departments` table | `IDepartmentRepository.CountActiveChildrenAsync` - `WHERE tenant_id, legal_entity_id, parent_department_id = @id, is_active = true`. |
| `activeEmployeeCount` / `hasActiveEmployees` | `employees` joined to `employment_statuses` | `IDepartmentRepository.CountActiveEmployeesAsync` - `WHERE tenant_id, legal_entity_id, department_id = @id AND employment_statuses.code = 'active'`. "Active" is resolved by joining on `Code == "active"`, not the seeded id `1`, so this stays correct even if seed ordering ever changes. `Employee` inherits `BaseEntity`, so EF's automatic global query filter already excludes soft-deleted (`IsDeleted = true`) rows on top of this join - verified empirically by `CountActiveEmployeesAsync_CountsOnlyActiveStatusEmployees_...` returning the correct count of `1` against 4 seeded rows (one per category: active/terminated/wrong-department/wrong-legal-entity). |
| `activePositionCount` / `hasActivePositions` / `positionDependencyCheckSupported` | **None - schema limitation, not a guess** | `Position` (`src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`) has only `Name` and `DefaultRoleId`, plus `BaseEntity`'s `Id/TenantId/CreatedAt/UpdatedAt/CreatedById/IsDeleted/DeletedAt`. There is no `DepartmentId`, `LegalEntityId`, or status/active column, and nothing links a `Position` row to a `Department` (only the reverse pointer `Department.HeadPositionId -> Position` exists, which this task never touches). "How many active positions belong to this department" is therefore not merely unmeasured - it is structurally unrepresentable in the current schema. `DepartmentArchiveDependencyEvaluator` always returns `ActivePositionCount = 0`, `HasActivePositions = false`, `PositionDependencyCheckSupported = false`, and positions are never included in the archive-blocking decision or the blocker message. This follows the task's own explicit fallback ("returned as 0 only if explicitly marked `positionDependencyCheckSupported: false`") rather than inventing a count or halting the entire task over one field. |

**Shared evaluator, not duplicated logic:** both `CheckDepartmentArchiveDependenciesQueryHandler` and `ArchiveDepartmentCommandHandler` call the same static `DepartmentArchiveDependencyEvaluator.EvaluateAsync`/`CanArchive`/`BuildMessage`, so the archive-check preview and the archive endpoint's actual gate can never disagree.

---

## 5. Archive Route Behavior

- `ArchiveDepartmentCommandHandler` now runs `DepartmentArchiveDependencyEvaluator.EvaluateAsync` immediately after fetching the department and before flipping `IsActive`. If `CanArchive` is `false`, it returns `Result<bool>.Conflict(BuildMessage(...))` (surfaced as HTTP `409`) and does **not** call `Update`/`SaveChangesAsync` - proven by `ArchiveDepartment_Blocks_WhenActiveChildDepartmentsExist`/`..._WhenActiveEmployeesExist` explicitly asserting `Times.Never` on both.
- `POST .../archive` and `DELETE` both dispatch the identical `ArchiveDepartmentCommand`, so both inherit this blocking behavior with zero additional code - this was Part 1's design and remains unchanged.
- **Already-archived departments (idempotent re-archive):** unchanged from Part 1/2B - the handler has never special-cased "already inactive," it simply re-runs the same set-`IsActive=false`-and-save flow (now gated by the same dependency check). This is a deliberate continuation of the pre-existing convention rather than a new "already archived -> conflict" rule, per the task's "prefer the project's existing convention and document it" instruction.
- **Regression audit performed before wiring in the check (not discovered after the fact):** every pre-existing archive/delete call in `DepartmentsIntegrationTests.cs` (`Delete_WithOrgReadOnly_NoOrgManage_Returns403`, `Create_Get_Update_Delete_FullLifecycle`, `Update_ParentIsInactive_Returns409`, `Archive_Route_SoftDeactivates_AndListExcludesByDefault`) archives a department with zero children and zero employees at archive time, so none of them regressed. The one pre-existing unit test that archives successfully (`ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt`) was updated to mock both new counts as `0`.

---

## 6. Restore Behavior

- New `RestoreDepartmentCommand`/`Handler`, `POST .../restore`, `org:manage`.
- Fetches via the existing `IDepartmentRepository.GetByIdForLegalEntityAsync` - this method was already implemented with **no** `IsActive` filter (confirmed by reading it before writing any new code), so it already returns archived rows. No new "IncludingInactive" repository method was added; adding one would have been redundant.
- 404 if the department doesn't exist in the tenant/legal entity.
- **Already-active department: idempotent success**, not an error - consistent with `ArchiveDepartmentCommandHandler`'s existing precedent of treating a repeat call as a no-op rather than inventing a new "already restored" conflict. Proven by `RestoreDepartment_IsIdempotent_WhenAlreadyActive` asserting `Update` is never called.
- If `ParentDepartmentId` is set, the parent is re-fetched via the same `GetByIdForLegalEntityAsync` (no new "ParentIsActiveAsync" method needed - this mirrors exactly how `CreateDepartmentCommandHandler`/`UpdateDepartmentCommandHandler` already check parent-active state). Missing or inactive parent -> `409 Conflict`, department is **not** restored (verified: `existing.IsActive` still `false` after the call, and `Restore_Fails_WhenParentIsArchived` proves this end-to-end against real Postgres).
- On success: only `IsActive = true` and `UpdatedAt = _dateTimeProvider.UtcNow` are set. `RestoreDepartment_DoesNotChangeHeadPositionId` proves `HeadPositionId` survives unchanged; nothing in the handler ever touches `Code`, `Name`, or `ParentDepartmentId`; no children are touched (restore never queries or writes any other department row).
- `IDateTimeProvider.UtcNow` is used for `UpdatedAt`, never `DateTimeOffset.UtcNow` directly - enforced by both the pre-existing `DepartmentPart2BArchitectureTests.DepartmentApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly` (which scans the whole `Department` folder including this new file) and this task's own `DepartmentPart2ArchiveRestoreArchitectureTests.RestoreAndCheckArchiveHandlers_DoNotUseDateTimeOffsetUtcNowDirectly`.

---

## 7. Explicit Statements

- **`activePositionCount` is deferred/schema-limited, not invented.** See Section 4 - `Position` has no `DepartmentId`/`LegalEntityId`/status column at all, so `0` with `positionDependencyCheckSupported: false` is the only value that could ever be measured today, not an unverified guess.
- **`headPositionId` remains untouched by this task.** Neither `RestoreDepartmentCommandHandler` nor `CheckDepartmentArchiveDependenciesQueryHandler` reads, writes, or validates `HeadPositionId` (the query handler never even loads it into the response - `DepartmentArchiveDependencyResponse` has no `HeadPositionId` field at all).
- **No Position APIs were added.** No `Position` controller, command, query, or contract was added or modified. Guarded by `DepartmentPart2ArchiveRestoreArchitectureTests.NoPositionController_HasBeenAddedInPart2`.
- **No role/permission-management code was added.** Guarded by `NoRoleOrPermissionManagementCode_WasAddedForThisFeature`, which scans every new file for `RoleTemplate`/`CreateRole`/`PermissionSeeder` references.
- **DELETE is kept as the compatibility alias**, unchanged disposition from Part 1 - still delegates to the identical `ArchiveDepartmentCommand`, so it inherits the new blocking behavior automatically without any code change to the `Delete` action itself.
- **No request body accepts `tenantId`, `legalEntityId`, or `headPositionId`.** Both new endpoints take zero request body (route parameters only).

---

## 8. Tests Added/Updated

**Repository - `EfDepartmentRepositoryTests.cs`** (+2): `CountActiveChildrenAsync_CountsOnlyActiveDirectChildren_ScopedToTenantAndLegalEntity`, `CountActiveEmployeesAsync_CountsOnlyActiveStatusEmployees_ScopedToTenantLegalEntityAndDepartment`.

**Application unit - `DepartmentApplicationUnitTests.cs`** (+14, +1 updated): `CheckArchiveDependencies_ReturnsCanArchiveTrue_WhenAllCountsAreZero`, `CheckArchiveDependencies_ReturnsCanArchiveFalse_WithExactBlockerCounts`, `CheckArchiveDependencies_ReturnsNotFound_WhenDepartmentDoesNotExist`, `ArchiveDepartment_Blocks_WhenActiveChildDepartmentsExist`, `ArchiveDepartment_Blocks_WhenActiveEmployeesExist`, `RestoreDepartment_Succeeds_ForInactiveDepartmentWithNoParent_AndUsesInjectedClockForUpdatedAt`, `RestoreDepartment_DoesNotChangeHeadPositionId`, `RestoreDepartment_Succeeds_ForInactiveDepartmentWithActiveParent`, `RestoreDepartment_Rejects_WhenParentIsInactive`, `RestoreDepartment_Rejects_WhenParentIsMissing`, `RestoreDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist`, `RestoreDepartment_IsIdempotent_WhenAlreadyActive`, `RestoreDepartment_ReturnsForbidden_WhenUnauthenticated`, `CheckArchiveDependencies_ReturnsForbidden_WhenUnauthenticated`; `ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt` updated to mock zero blockers.

**Controller unit - `DepartmentsControllerTests.cs`** (+5): `ArchiveCheck_SendsQuery_WithRouteIds_AndReturnsOk`, `ArchiveCheck_NotFound_ReturnsProblem404`, `Restore_SendsCommand_WithRouteIds_AndReturnsNoContent`, `Restore_ParentInactive_ReturnsProblem409`, `Restore_NotFound_ReturnsProblem404`.

**Architecture** (+17 across 4 files): 2 new `InlineData` in `DepartmentPart2AArchitectureTests`'s legal-entity-scoping theory; `DepartmentPart2BArchitectureTests` - `RestoreDepartmentCommand`/`Handler` and `CheckDepartmentArchiveDependenciesQuery`/`Handler` added to `CommandAndQueryTypes`/`HandlerTypes` (+2 `Handlers_DoNotUseApplicationDbContextDirectly` theory cases) plus 6 new file-location `InlineData` entries; `DepartmentsControllerArchitectureTests` +2 (`ArchiveCheckRoute_ExistsAsPost_WithOrgReadPermission`, `RestoreRoute_ExistsAsPost_WithOrgManagePermission`); new `DepartmentPart2ArchiveRestoreArchitectureTests.cs` +5 (`EfDepartmentRepository_NeverCallsRemoveOnDepartmentsDbSet`, `RestoreAndCheckArchiveHandlers_DoNotUseDateTimeOffsetUtcNowDirectly`, `NoPositionController_HasBeenAddedInPart2`, `NoRoleOrPermissionManagementCode_WasAddedForThisFeature`, `DeleteAndArchiveActions_BothDelegateToArchiveDepartmentCommand`).

**Integration - `DepartmentsIntegrationTests.cs`** (+11, real Testcontainers Postgres, Docker was available): `ArchiveCheck_Unauthenticated_Returns401`, `Restore_Unauthenticated_Returns401`, `ArchiveCheck_Eligible_ReturnsCanArchiveTrue`, `ArchiveCheck_WithOrgRead_Returns200`, `ArchiveCheck_Blocked_ReturnsAccurateCounts_WhenActiveChildExists`, `Archive_Blocked_WhenActiveChildExists_Returns409_AndDoesNotDeactivate`, `Delete_Blocked_WhenActiveChildExists_Returns409`, `Archive_Child_WithNoBlockers_Succeeds_ThenRestore_Succeeds`, `Restore_WithOrgReadOnly_NoOrgManage_Returns403`, `Restore_Fails_WhenParentIsArchived`, `Archive_Blocked_WhenActiveEmployeeExists` (seeds an `Employee` row directly via `ApplicationDbContext` in the same scope, mirroring the file's existing precedent for fixture users where no public creation API exists yet).

**Total new/updated tests this task: 2 + 15 (14 new + 1 updated) + 5 + 17 + 11 = 50.**

---

## 9. Build/Test Results

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded. 0 Errors, 0 Warnings in touched files (1 pre-existing unrelated
     CS8602 warning in AdminAuthController.cs).

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 1213, Skipped: 0, Total: 1213.
     (Baseline before this task: 1192 - net +21, matching Section 8's unit-test count.)

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 425, Skipped: 0, Total: 425.
     (Baseline before this task: 408 - net +17, matching Section 8.)

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~DepartmentsIntegrationTests" --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 10m
  -> Docker was available (docker info succeeded); ran against a real Testcontainers
     PostgreSQL instance, full Kestrel TestServer pipeline (auth, CSRF, RequirePermission,
     MediatR, EF/RLS) - not controller/handler unit tests.
     Test Run Successful. Total tests: 36. Passed: 36.
     (Baseline before this task: 25 - net +11, matching Section 8. Zero regressions among
     the 25 pre-existing tests.)

git diff --check
  -> Exit code 0. Warnings shown are pre-existing CRLF-normalization notices on unrelated
     in-progress LegalEntity files (not touched by this task), matching the same benign
     pattern already noted in the Part 1 report.

ASCII scan (PowerShell Select-String -Pattern '[^\x00-\x7F]', locale-independent - Part 1's
own notes warned that `grep -qP` under Git Bash can silently no-op on a locale error and
produce a false-clean result, so this task used the same PowerShell approach from the start):
  -> All 20 touched/created source and test files: 0 non-ASCII characters, clean on first scan.
  -> This task's own plan document (docs/superpowers/plans/2026-08-04-department-hardening-part2.md)
     contained em-dash characters (prose formatting only, not code) - normalized to ASCII
     double-hyphens and rescanned clean, even though this constraint is really aimed at
     shipped source/test/migration files rather than planning docs.
```

---

## 10. Remaining Gaps (Per the Requirement Document, Out of Scope for This Slice)

- **Search/sort/pagination** on the department list - not implemented; `ListDepartmentsQuery` is unchanged.
- **Department details view/drawer** - not implemented; this is a frontend concern and the underlying `GetDepartmentQuery` already returns full department data, but no read model beyond that was added.
- **Department head-position assignment** - `headPositionId` remains schema-ready/read-only only; not accepted or mutated by any endpoint in this task, including restore.
- **Position foundation** - `Position` still has no `DepartmentId`/`LegalEntityId`/status columns; `activePositionCount` is therefore always `0` with `positionDependencyCheckSupported: false` (see Section 4/7). Building real Position-department linkage is a prerequisite for a true position dependency count and is out of scope here.
- **Management scope** (department/position management-responsibility assignment) - out of scope; not part of the Department entity or this task.
- **Occupant assignment** - out of scope; depends on Position APIs, which are not built.
- **Frontend states** (loading, empty, no-search-results, no-permission, network/server failure, concurrent-update, clickable blocker counts, "View employees"/"View positions"/"Move subdepartments" actions from the validation doc's Archive Department screen spec) - none of this is backend work; the backend now returns the exact counts and booleans a frontend would need to render these states, but no frontend file was touched.
- **Hierarchy "impact summary before confirmation"** for reparenting (unrelated to archive/restore, carried over from Part 1's own gap list) - still not implemented; out of scope for this task.

---

## 11. Verification Commands Run (Exact, Per Task Instructions)

1. `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` - pass.
2. `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal` - 1213/1213 pass.
3. `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal` - 425/425 pass.
4. Docker was available: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~Department" --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 10m` - 36/36 pass.
5. `git diff --check` - exit 0, clean.
6. ASCII scan on all touched files - clean (see Section 9).

No commit or push was made at any point in this task.
