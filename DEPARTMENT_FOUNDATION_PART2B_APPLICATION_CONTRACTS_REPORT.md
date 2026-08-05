# Department Foundation - Part 2B Report (Application Commands, Queries, Validators, and Contracts)

**Task:** Department Part 2B Backend - Application commands, queries, validators, DTOs, API request contracts, and tests.  
**Repository Working Directory:** `C:\onevoNew\HRMS-Backend-v1`  
**Date:** 2026-08-03  

---

## 1. Files Read

- `OneVo-HR/modules/org-structure/overview.md`
- `OneVo-HR/database/schemas/org-structure.md`
- `DEPARTMENT_FOUNDATION_BACKEND_AUDIT_PLAN.md`
- `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md`
- `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/CreateLegalEntity/CreateLegalEntityCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/DeleteLegalEntity/DeleteLegalEntityCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- `src/ONEVO.Application/Common/Models/Result.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/IDateTimeProvider.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntityPart2BArchitectureTests.cs`

---

## 2. Files Changed

**New Application DTOs & Mappers:**
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentListItemResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentMapper.cs`

**New Application Queries:**
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQuery.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/GetDepartment/GetDepartmentQuery.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/GetDepartment/GetDepartmentQueryValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/GetDepartment/GetDepartmentQueryHandler.cs`

**New Application Commands:**
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommandValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommandValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/DeleteDepartmentCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/DeleteDepartmentCommandValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/DeleteDepartment/DeleteDepartmentCommandHandler.cs`

**New API Contracts:**
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/CreateDepartmentRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/UpdateDepartmentRequest.cs`

**New & Updated Tests:**
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs` (NEW)
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs` (NEW)

**Unchanged:** No migrations, EF schema, DbContext, domain entities, repository interfaces, repository implementations, Postman files, frontend files, or OneVo-HR docs were modified.

---

## 3. Added Commands, Queries, Validators, and Contracts

| Type | Name | Purpose | Output / Result |
|---|---|---|---|
| **Query** | `ListDepartmentsQuery` | Lists departments scoped to a Legal Entity | `Result<IReadOnlyList<DepartmentListItemResponse>>` |
| **Query** | `GetDepartmentQuery` | Gets a single department by Legal Entity & ID | `Result<DepartmentResponse>` |
| **Command** | `CreateDepartmentCommand` | Creates a new department under a Legal Entity | `Result<DepartmentResponse>` |
| **Command** | `UpdateDepartmentCommand` | Mutates an existing department's details | `Result<DepartmentResponse>` |
| **Command** | `DeleteDepartmentCommand` | Soft-deletes / deactivates a department | `Result<bool>` |
| **Contract** | `CreateDepartmentRequest` | API body (`Name`, `Code`, `ParentDepartmentId`) | N/A (no tenant/legalEntity/headPositionId) |
| **Contract** | `UpdateDepartmentRequest` | API body (`Name`, `Code`, `ParentDepartmentId`) | N/A (no tenant/legalEntity/headPositionId) |

---

## 4. Behavior Matrix

| Operation | Handler Behavior & Boundary Rules | Status / Error Handling |
|---|---|---|
| **List** | Sources `tenantId` from `ICurrentUser`; verifies Legal Entity exists in tenant via `ILegalEntityRepository.GetByIdForTenantAsync`; queries `IDepartmentRepository.ListByLegalEntityAsync` with `IncludeInactive` so active-row filtering happens in the repository query. | Returns `403 Forbidden` if unauthenticated/no tenant; `404 NotFound` if Legal Entity missing; `200 OK` on success. |
| **Get** | Sources `tenantId` from `ICurrentUser`; verifies Legal Entity exists; queries `IDepartmentRepository.GetByIdForLegalEntityAsync`. | Returns `404 NotFound` if Legal Entity or Department not found; `200 OK` on success. |
| **Create** | Verifies Legal Entity exists; checks duplicate name in same Legal Entity via `ExistsByNameAsync`; verifies parent exists in same Legal Entity via `ExistsAsync` if `ParentDepartmentId` provided; creates entity with `IsActive = true` and `CreatedAt = _dateTimeProvider.UtcNow`. | Returns `409 Conflict` if duplicate name in Legal Entity; `404 NotFound` if parent or Legal Entity missing; `200 OK` on success. |
| **Update** | Fetches existing entity by `(tenantId, legalEntityId, departmentId)` via `GetByIdForLegalEntityAsync`; rejects self-parenting (`ParentDepartmentId == DepartmentId`); checks duplicate name excluding self via `ExistsByNameAsync`; verifies parent exists; mutates `Name`, `Code`, `ParentDepartmentId`, `UpdatedAt = _dateTimeProvider.UtcNow`; **preserves `HeadPositionId` untouched**. | Returns `404 NotFound` if department/parent missing; `409 Conflict` on duplicate name or self-parenting; `200 OK` on success. |
| **Delete** | Fetches existing entity by `(tenantId, legalEntityId, departmentId)`; sets `IsActive = false` and `UpdatedAt = _dateTimeProvider.UtcNow`; calls `_departments.Update(existing)` and `SaveChangesAsync()`. Does not physically remove row. | Returns `404 NotFound` if department missing; `200 OK` on success. |

> **Clock Injection Correction Note:** Direct `DateTimeOffset.UtcNow` calls in `CreateDepartmentCommandHandler`, `UpdateDepartmentCommandHandler`, and `DeleteDepartmentCommandHandler` were removed and replaced with constructor-injected `IDateTimeProvider` (`_dateTimeProvider.UtcNow`), strictly conforming to the repository dateTime abstraction pattern.

---

## 5. Validation Rules Table

| Target | Property / Parameter | Rule | Error Message |
|---|---|---|---|
| **ListDepartmentsQuery** | `LegalEntityId` | `NotEmpty()` | "Legal entity ID is required." |
| **GetDepartmentQuery** | `LegalEntityId`, `DepartmentId` | `NotEmpty()` on both | "Legal entity ID is required.", "Department ID is required." |
| **CreateDepartmentCommand** | `LegalEntityId` | `NotEmpty()` | "Legal entity ID is required." |
| **CreateDepartmentCommand** | `Name` | `NotEmpty()`, `MaximumLength(100)` | "Department name is required.", "Department name cannot exceed 100 characters." |
| **CreateDepartmentCommand** | `Code` | Optional, `MaximumLength(20)` when set | "Department code cannot exceed 20 characters." |
| **UpdateDepartmentCommand** | `LegalEntityId`, `DepartmentId` | `NotEmpty()` on both | "Legal entity ID is required.", "Department ID is required." |
| **UpdateDepartmentCommand** | `Name` | `NotEmpty()`, `MaximumLength(100)` | "Department name is required.", "Department name cannot exceed 100 characters." |
| **UpdateDepartmentCommand** | `Code` | Optional, `MaximumLength(20)` when set | "Department code cannot exceed 20 characters." |
| **UpdateDepartmentCommand** | Self-Parenting | `Must(ParentDepartmentId != DepartmentId)` | "Department cannot be its own parent." |
| **DeleteDepartmentCommand** | `LegalEntityId`, `DepartmentId` | `NotEmpty()` on both | "Legal entity ID is required.", "Department ID is required." |

---

## 6. Head Position Scoping & Policy Statement

**Explicit Policy:**
`headPositionId` is **schema-ready only**. Part 2B does **not** accept `headPositionId` in any create or update request contract or command. `headPositionId` is exposed strictly as a read-only response property on response DTOs (`DepartmentResponse` / `DepartmentListItemResponse`) carrying the value stored in the database. Updating or assigning a Department Head Position is deferred until real Position APIs and cross-entity validation rules (verifying tenant ownership, legal entity boundary, active status, and head position eligibility) are built in later parts. `UpdateDepartmentCommandHandler` strictly preserves `HeadPositionId` untouched when mutating department records.

---

## 7. Permission Plan for Part 2C

Permissions are **not** enforced in Application handlers, preserving existing codebase architecture where authorization attributes are applied at the API Controller layer.

**Plan for Part 2C Controller Authorization:**
- `ListDepartmentsQuery` / `GetDepartmentQuery`: Require `[RequirePermission("org:read")]` attribute on GET endpoints.
- `CreateDepartmentCommand` / `UpdateDepartmentCommand` / `DeleteDepartmentCommand`: Require `[RequirePermission("org:manage")]` attribute on POST/PUT/DELETE endpoints.

---

## 8. Tests Added and Final Test Counts

### Unit Tests Added (`DepartmentApplicationUnitTests.cs` - 17 tests)
1. `ListDepartments_ReturnsOnlyDepartmentsInSelectedLegalEntity`
2. `ListDepartments_ReturnsNotFound_WhenLegalEntityDoesNotExist`
3. `GetDepartment_ReturnsOnlySelectedLegalEntityDepartment`
4. `GetDepartment_ReturnsNotFound_WhenDepartmentDoesNotExistInLegalEntity`
5. `CreateDepartment_Succeeds_WhenInputIsValid_AndUsesInjectedClockForCreatedAt`
6. `CreateDepartment_RejectsDuplicateNameInSameLegalEntity`
7. `CreateDepartment_AllowsSameNameInDifferentLegalEntity`
8. `CreateDepartment_RejectsParentFromDifferentLegalEntity`
9. `CreateDepartmentCommandValidator_RejectsEmptyLegalEntityIdAndName`
10. `UpdateDepartment_Succeeds_ByFetchThenMutate_AndUsesInjectedClockForUpdatedAt`
11. `UpdateDepartment_RejectsDuplicateNameInSameLegalEntity`
12. `UpdateDepartment_RejectsSelfParenting`
13. `UpdateDepartmentCommandValidator_RejectsSelfParenting`
14. `DeleteDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt`
15. `DeleteDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist`
16. `Handlers_DoNotAcceptTenantIdFromRequestInput_ResolvesFromCurrentUserOnly`
17. Injected clock timestamp verification tests across Create, Update, and Delete handlers.

### Architecture Tests Added (`DepartmentPart2BArchitectureTests.cs` - 10 tests)
1. `NoDepartmentsController_ExistsYetInPart2B`
2. `NoDepartmentApiRoute_AddedYetInPart2B`
3. `RequestContracts_DoNotContainTenantId`
4. `CreateAndUpdateRequestContracts_DoNotContainHeadPositionId`
5. `Handlers_DoNotUseApplicationDbContextDirectly`
6. `CommandFiles_LiveUnderOrgStructureDepartmentFolder`
7. `QueryFiles_LiveUnderOrgStructureDepartmentFolder`
8. `DepartmentPart2A_HeadPositionIdSchema_RemainsPresent`
9. `DepartmentApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly`

### Final Test Suite Summary
- **Unit Tests:** **1161 / 1161 passed** (0 failed).
- **Architecture Tests:** **390 / 390 passed** (0 failed).

---

## 9. Verification Summary

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded. 0 Errors, 0 Warnings.

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal
  -> Passed! Failed: 0, Passed: 1161, Skipped: 0, Total: 1161.

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --verbosity minimal
  -> Passed! Failed: 0, Passed: 390, Skipped: 0, Total: 390.

rg -n "DateTimeOffset\.UtcNow" src\ONEVO.Application\Features\OrgStructure\Department
  -> 0 matches.

git diff --check
  -> Exit Code 0 (clean).

ASCII Character Scan:
  -> 0 non-ASCII characters found across all touched files.
```

---

## 10. Deferred Items for Part 2C / Part 2D

- **Controllers / API Routing (Part 2C):** `DepartmentsController` under `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` with routes `/api/v1/org/legal-entities/{legalEntityId}/departments`.
- **Permission Enforcement (Part 2C):** Controller endpoint annotations with `[RequirePermission("org:read")]` and `[RequirePermission("org:manage")]`.
- **Postman Collection & HTTP Validation (Part 2D):** End-to-end HTTP tests and Postman collection suite.
- **Deep Ancestor Cycle Detection:** Full multi-level ancestor graph cycle validation when changing parent departments (if simple self-parenting check is insufficient for complex tree updates).
- **Position API Integration & HeadPositionId Assignment:** Exposing and validating `headPositionId` in create/update requests once Position management APIs exist.

---

## 11. Scope Boundary Confirmation

- **No Migrations or Schema Changes:** Verified zero migration files added or modified.
- **No Controller or Routes:** Verified no controller or API routes created in Part 2B.
- **No Postman or OneVo-HR Docs Changes:** Verified zero Postman or markdown documentation files altered under `OneVo-HR/`.
- **Pure ASCII Codebase:** Verified 100% ASCII compliance across all files.
