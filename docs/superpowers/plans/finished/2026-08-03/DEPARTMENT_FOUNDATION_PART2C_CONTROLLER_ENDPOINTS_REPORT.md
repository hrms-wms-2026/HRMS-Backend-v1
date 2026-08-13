# Department Foundation - Part 2C Report (API Controller Endpoints)

**Task:** Backend Department Part 2C - API controller endpoints for Department under selected Legal Entity context.  
**Repository Working Directory:** `C:\onevoNew\HRMS-Backend-v1`  
**Date:** 2026-08-03  

---

## 1. Files Read

- `DEPARTMENT_FOUNDATION_BACKEND_AUDIT_PLAN.md`
- `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md`
- `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md`
- `DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`
- `src/ONEVO.Api/Filters/RequirePermissionAttribute.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntitiesControllerTests.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`

---

## 2. Files Changed

**New API Controller:**
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`

**New & Updated Test Files:**
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs` (NEW)
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs` (NEW)
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs` (UPDATED - obsolete pre-Part-2C controller prohibition tests updated/removed)
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs` (UPDATED - obsolete pre-Part-2C controller prohibition tests updated/removed)

**Unchanged:** No migrations, EF schema, DbContext, domain entities, repository interfaces, repository implementations, application handlers, application commands/queries, Postman files, frontend files, or OneVo-HR docs were modified.

---

## 3. Route Table

| HTTP Method | Route Template | Controller Action | Description |
|---|---|---|---|
| **GET** | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments` | `List(Guid legalEntityId, bool includeInactive)` | Lists active departments under selected Legal Entity (or all if `includeInactive=true`). |
| **GET** | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | `Get(Guid legalEntityId, Guid departmentId)` | Retrieves details for a specific department under the selected Legal Entity. |
| **POST** | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments` | `Create(Guid legalEntityId, CreateDepartmentRequest request)` | Creates a new department under the selected Legal Entity. |
| **PUT** | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | `Update(Guid legalEntityId, Guid departmentId, UpdateDepartmentRequest request)` | Updates department details. Route parameters override any body context. |
| **DELETE** | `/api/v1/org/legal-entities/{legalEntityId:guid}/departments/{departmentId:guid}` | `Delete(Guid legalEntityId, Guid departmentId)` | Soft-deactivates a department (`IsActive = false`). |

---

## 4. Request / Response Mapping Table

| Endpoint | HTTP Request Body | CQRS Command / Query Dispatched | HTTP Success Response | Error Problem Mapping |
|---|---|---|---|---|
| **List** | None (Query parameter `includeInactive`) | `ListDepartmentsQuery(legalEntityId, includeInactive)` | `200 OK` with `IReadOnlyList<DepartmentListItemResponse>` | `Problem(result.Error, statusCode)` |
| **Get** | None | `GetDepartmentQuery(legalEntityId, departmentId)` | `200 OK` with `DepartmentResponse` | `Problem(result.Error, statusCode)` |
| **Create** | `CreateDepartmentRequest` (`Name`, `Code`, `ParentDepartmentId`) | `CreateDepartmentCommand(legalEntityId, request.Name, request.Code, request.ParentDepartmentId)` | `201 CreatedAtAction` pointing to `Get` (`/api/v1/org/legal-entities/{legalEntityId}/departments/{id}`) | `Problem(result.Error, statusCode)` |
| **Update** | `UpdateDepartmentRequest` (`Name`, `Code`, `ParentDepartmentId`) | `UpdateDepartmentCommand(legalEntityId, departmentId, request.Name, request.Code, request.ParentDepartmentId)` | `200 OK` with `DepartmentResponse` | `Problem(result.Error, statusCode)` |
| **Delete** | None | `DeleteDepartmentCommand(legalEntityId, departmentId)` | `204 NoContent` | `Problem(result.Error, statusCode)` |

---

## 5. Permission & Authorization Table

| Controller Action | Applied Attributes | Permission Code Required | Scope / Policy |
|---|---|---|---|
| `DepartmentsController` Class | `[ApiController]`, `[Route("...")]`, `[Authorize(Policy = "TenantPolicy")]` | N/A | Tenant authentication policy enforced globally across all endpoints |
| `List` | `[HttpGet]`, `[RequirePermission("org:read")]` | `org:read` | Read-only access to department lists |
| `Get` | `[HttpGet("{departmentId:guid}")]`, `[RequirePermission("org:read")]` | `org:read` | Read-only access to department details |
| `Create` | `[HttpPost]`, `[RequirePermission("org:manage")]` | `org:manage` | Management access to create departments |
| `Update` | `[HttpPut("{departmentId:guid}")]`, `[RequirePermission("org:manage")]` | `org:manage` | Management access to update departments |
| `Delete` | `[HttpDelete("{departmentId:guid}")]`, `[RequirePermission("org:manage")]` | `org:manage` | Management access to deactivate departments |

---

## 6. Context & Security Policy Statements

1. **Selected-Company Context (`legalEntityId`):**
   `legalEntityId` is passed as a route constraint on every endpoint (`/api/v1/org/legal-entities/{legalEntityId:guid}/departments`), representing the company selected by the user in the frontend top-bar selector. Handlers strictly verify that the requested Legal Entity exists within the user's tenant before returning or mutating data.

2. **Tenant ID Non-Exposure:**
   `tenantId` is **never** accepted from HTTP route parameters, query strings, or request bodies. Tenant context is resolved strictly server-side from `ICurrentUser.TenantId` via JWT token/session context.

3. **Head Position ID Policy (`headPositionId`):**
   `headPositionId` is **read-only / schema-ready only**. `CreateDepartmentRequest` and `UpdateDepartmentRequest` do not contain `headPositionId`. `DepartmentsController` does not bind `headPositionId`. Updating or assigning a Department Head Position remains deferred until Position APIs are created in future modules.

---

## 7. Tests Added and Final Test Counts

### Controller Unit Tests Added (`DepartmentsControllerTests.cs` - 12 tests)
1. `List_SendsQuery_WithRouteLegalEntityId_AndIncludeInactiveFalseByDefault`
2. `List_SendsQuery_WithIncludeInactiveTrue_WhenParameterIsTrue`
3. `List_ForbiddenResult_ReturnsProblem403`
4. `Get_SendsQuery_WithRouteIds_AndReturnsOk`
5. `Get_NotFoundResult_ReturnsProblem404`
6. `Create_MapsRequestBodyAndRouteLegalEntityId_IntoCommand_AndReturnsCreatedAtAction`
7. `Create_DuplicateName_ReturnsProblem409`
8. `Update_MapsRequestBodyAndRouteIds_IntoCommand_AndReturnsOk`
9. `Update_Conflict_ReturnsProblem409`
10. `Delete_SendsCommand_WithRouteIds_AndReturnsNoContent`
11. `Delete_NotFound_ReturnsProblem404`
12. `CreateAndUpdateRequests_DoNotExposeHeadPositionId`

### Architecture Tests Added (`DepartmentsControllerArchitectureTests.cs` - 14 tests)
1. `Controller_ExistsIn_TenantOrgStructureNamespace`
2. `Controller_RequiresTenantPolicy`
3. `Controller_HasCorrectBaseRoute_WithLegalEntityIdGuidConstraint`
4. `NoRoute_UsesUnscopedDepartmentsPath`
5. `AllFiveRequiredRoutesAndVerbs_Exist`
6. `GetActions_RequireOrgReadPermission`
7. `MutatingActions_RequireOrgManagePermission`
8. `NoAction_AcceptsTenantIdParameter`
9. `NoAction_AcceptsHeadPositionIdParameter`
10. `RequestContracts_DoNotExposeTenantId_LegalEntityId_OrHeadPositionId`
11. `Controller_InjectsIMediatorOnly`
12. `Controller_DoesNotInjectDbContext_Repositories_OrUserServicesDirectly`
13. `HeadPositionIdSchema_Part2AGuardStillPasses`

### Final Test Suite Summary
- **Unit Tests:** **1173 / 1173 passed** (0 failed).
- **Architecture Tests:** **404 / 404 passed** (0 failed).

---

## 8. Verification Results Summary

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded. 0 Errors, 1 Warning (unrelated AdminAuthController CS8602 warning).

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --verbosity minimal
  -> Passed! Failed: 0, Passed: 1173, Skipped: 0, Total: 1173.

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --verbosity minimal
  -> Passed! Failed: 0, Passed: 404, Skipped: 0, Total: 404.

rg -n "headPositionId|HeadPositionId" src/ONEVO.Api/Contracts/OrgStructure/Departments src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs
  -> 0 matches.

rg -n "tenantId|TenantId" src/ONEVO.Api/Contracts/OrgStructure/Departments src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs
  -> 0 matches.

rg -n "ApplicationDbContext|IDepartmentRepository|ILegalEntityRepository" src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs
  -> 0 matches.

git diff --check
  -> Exit Code 0 (clean).

ASCII Character Scan:
  -> 0 non-ASCII characters found across all touched files.
```

---

## 9. Deferred Items for Part 2D

- **Postman Collection & End-to-End HTTP Validation (Part 2D):** Exporting and running Postman integration tests for Department endpoints.
- **Swagger / Live API Verification (Part 2D):** End-to-end HTTP pipeline verification with actual JWT tokens.
- **Position API Integration & HeadPositionId Assignment:** Enabling head position assignment once Position management APIs are implemented.
