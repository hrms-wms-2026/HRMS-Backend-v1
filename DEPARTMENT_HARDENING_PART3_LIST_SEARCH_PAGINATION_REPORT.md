# Department Hardening Part 3 - List/Search/Sort/Pagination/Tree Report

## Scope

Turn `GET /api/v1/org/legal-entities/{legalEntityId}/departments` into a searchable, sortable,
paginated list endpoint that also supports a `view=tree` hierarchy mode. Read-model only - no
Position schema/API changes, no headPositionId write support, no new migrations.

No commit or push was performed at any point. All work is uncommitted in the working tree, exactly
as instructed.

## Files read (research)

- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQuery.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/GetDepartment/GetDepartmentQuery.cs` and `GetDepartmentQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/CheckDepartmentArchiveDependencies/CheckDepartmentArchiveDependenciesQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentListItemResponse.cs`, `DepartmentResponse.cs`, `DepartmentArchiveBlockers.cs`, `DepartmentArchiveDependencyResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentMapper.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Services/DepartmentArchiveDependencyEvaluator.cs`
- `src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs`
- `src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Department/DepartmentConfiguration.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/CreateDepartmentRequest.cs`, `UpdateDepartmentRequest.cs`
- `src/ONEVO.Application/Common/Models/Result.cs`
- `src/ONEVO.Application/Common/Behaviors/ValidationBehavior.cs`
- `src/ONEVO.Api/Middleware/ExceptionHandlerMiddleware.cs` (confirmed `FluentValidation.ValidationException` -> HTTP 400 ProblemDetails mapping)
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/ArchiveDepartment/ArchiveDepartmentCommandValidator.cs`, `CreateDepartment/CreateDepartmentCommandValidator.cs` (validator style precedent)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs`, `DepartmentPart2BArchitectureTests.cs`, `DepartmentsControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs`
- `src/ONEVO.Infrastructure/DependencyInjection.cs` (confirmed `IDepartmentRepository` -> `EfDepartmentRepository` DI registration needs no change)
- `src/ONEVO.Application/DependencyInjection.cs` (confirmed `AddValidatorsFromAssembly` picks up validator changes automatically)

## Files changed

### Created
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/DepartmentSortBy.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/SortDirection.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/DepartmentPage.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentListPageResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentTreeNodeResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentTreeResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/DTOs/Responses/DepartmentListResult.cs`
- `src/ONEVO.Application/Features/OrgStructure/Department/Mappers/DepartmentTreeMapper.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentTreeMapperTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentPart3ArchitectureTests.cs`
- `docs/superpowers/plans/2026-08-04-department-hardening-part3.md` (implementation plan)

### Modified
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs` - added `ListPageByLegalEntityAsync`, `ListForTreeByLegalEntityAsync`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Department/EfDepartmentRepository.cs` - implemented both methods + private `ApplySort` helper
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQuery.cs` - 2 params -> 9 params, return type `Result<IReadOnlyList<DepartmentListItemResponse>>` -> `Result<DepartmentListResult>`
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryValidator.cs` - full rewrite (page/pageSize/sortBy/sortDirection/view/search rules)
- `src/ONEVO.Application/Features/OrgStructure/Department/Queries/ListDepartments/ListDepartmentsQueryHandler.cs` - full rewrite (flat vs tree dispatch)
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` - `List` action: 1 query param -> 8 query params
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs` - added `ListPageByLegalEntityAsync`/`ListForTreeByLegalEntityAsync` test regions
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs` - replaced `ListDepartments` region, added `ListDepartmentsQueryValidator` region, fixed `Handlers_DoNotAcceptTenantIdFromRequestInput_ResolvesFromCurrentUserOnly`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs` - replaced 3 `List_*` tests with 5 new ones
- `tests/ONEVO.Tests.Architecture/DepartmentPart2AArchitectureTests.cs` - added 2 `InlineData` entries to `IDepartmentRepository_LegalEntityScopedMethods_HaveALegalEntityIdParameter`
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` - fixed 2 response-shape breaks (`.EnumerateArray()` -> `.GetProperty("items").EnumerateArray()`), added 8 new Part 3 tests

**Not touched:** Position schema/API/entity, Employee schema/API/model, LegalEntity schema/API/model, auth/session/legal/MFA/password code, subscription/module seed code, logo/file/assets code, frontend, Postman files, OneVo-HR documentation. No migration file was created or modified.

## Route / query parameter table

| Parameter | Type | Default | Validation |
|---|---|---|---|
| `search` | `string?` | `null` | max length 100; trimmed; empty/whitespace treated as no search |
| `includeInactive` | `bool` | `false` | model-bound bool |
| `parentDepartmentId` | `Guid?` | `null` | ASP.NET model binding rejects a non-Guid value with 400 automatically (no custom rule needed) |
| `view` | `string` | `"flat"` | must be `flat` or `tree`, case-insensitive |
| `sortBy` | `string` | `"name"` | must be `name`, `code`, `createdAt`, or `updatedAt`, case-insensitive |
| `sortDirection` | `string` | `"asc"` | must be `asc` or `desc`, case-insensitive |
| `page` | `int` | `1` | >= 1 |
| `pageSize` | `int` | `25` | 1-100 inclusive |

`legalEntityId` comes from the route only; `tenantId` and `headPositionId` are never accepted from
query or body anywhere in this endpoint.

## Response shape

Flat (`view=flat`, default):
```json
{
  "items": [
    {
      "id": "...",
      "legalEntityId": "...",
      "name": "Engineering",
      "code": "ENG",
      "parentDepartmentId": null,
      "headPositionId": null,
      "isActive": true,
      "createdAt": "...",
      "updatedAt": "..."
    }
  ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 1,
  "totalPages": 1
}
```

Tree (`view=tree`):
```json
{
  "treeItems": [
    {
      "id": "...",
      "legalEntityId": "...",
      "name": "Engineering",
      "code": "ENG",
      "parentDepartmentId": null,
      "headPositionId": null,
      "isActive": true,
      "children": [
        {
          "id": "...",
          "legalEntityId": "...",
          "name": "Backend",
          "code": "BE",
          "parentDepartmentId": "...",
          "headPositionId": null,
          "isActive": true,
          "children": []
        }
      ]
    }
  ]
}
```

The two shapes are never merged into one payload. The handler returns an internal envelope,
`DepartmentListResult(Flat, Tree)`, with exactly one of the two members populated; the controller
inspects the envelope and calls `Ok(...)` with only the populated member, so the wire response is
always exactly one of the two shapes above - never a body with a null `treeItems` or null `items`
field mixed in. Neither shape exposes `tenantId`.

**`GET /departments/{departmentId}` (single department) was deliberately left unchanged.** All
required fields (`id`, `legalEntityId`, `name`, `code`, `parentDepartmentId`, `headPositionId`,
`isActive`, `createdAt`, `updatedAt`) were already present on `DepartmentResponse`. Adding an
archive/dependency summary to every plain GET would mean two extra repository queries
(`CountActiveChildrenAsync` + `CountActiveEmployeesAsync`) on a read that does not need them, and
Part 2's `POST .../archive-check` endpoint already serves that exact need via
`DepartmentArchiveDependencyEvaluator` without duplicating logic. This was a considered decision,
not an oversight.

## Repository filtering strategy

`ListPageByLegalEntityAsync` and `ListForTreeByLegalEntityAsync` both build an `IQueryable<Department>`
starting from an explicit `Where(d => d.TenantId == tenantId && d.LegalEntityId == legalEntityId)`
(not relying solely on the EF global tenant query filter), then chain `.Where(...)` clauses for
`includeInactive`, `search`, and (flat only) `parentDepartmentId`, apply sorting, and only then
`.Skip()/.Take()` (flat only) before a single `.ToListAsync()`. `AsNoTracking()` is used throughout.
Nothing is materialized to a `List<T>` and filtered in memory.

Search matches `Name` or `Code` case-insensitively via `.ToLower().Contains(normalizedSearch)`
rather than `EF.Functions.ILike(...)`. This is a functional requirement, not a style choice:
`EfDepartmentRepositoryTests` runs on `Microsoft.EntityFrameworkCore.InMemory`, and Npgsql-only
functions like `EF.Functions.ILike` throw at runtime under that provider. `.ToLower().Contains(...)`
already had a working precedent in this codebase (`EfDepartmentRepository.ExistsByCodeAsync`) and
translates correctly under both Npgsql and InMemory.

## Tree behavior decision

`view=tree` ignores `parentDepartmentId`, `page`, and `pageSize` entirely - it returns the full
legal-entity hierarchy with `search`/`includeInactive` applied to the node set, not to pagination.
A department whose `ParentDepartmentId` points outside the filtered node set (parent excluded by
`search`, parent inactive while `includeInactive=false`, or genuinely no parent) becomes a root
node rather than being dropped from the response. This keeps every matching department visible in
the tree at all times.

Tested at three levels:
- Unit (mapper): `DepartmentTreeMapperTests.BuildTree_TreatsDepartmentWithParentOutsideSet_AsRoot`
- Unit (handler): `ListDepartments_TreeView_IgnoresParentDepartmentIdAndPagination` - asserts the
  tree branch calls `ListForTreeByLegalEntityAsync` (never `ListPageByLegalEntityAsync`) and that
  `parentDepartmentId`/`page`/`pageSize` on the query have no effect on the result
- Integration: `List_TreeView_ReturnsHierarchyForSelectedLegalEntityOnly` - asserts the tree only
  contains departments from the requested legal entity and correctly nests a real parent/child pair

## Validation rules

All FluentValidation rules run in the existing MediatR `ValidationBehavior` pipeline and surface as
HTTP 400 ProblemDetails via the existing `ExceptionHandlerMiddleware` `FluentValidation.ValidationException`
branch - no new plumbing was needed.

- `LegalEntityId`: not empty
- `Search`: max length 100 (no other constraint - empty/whitespace is valid and means "no search")
- `View`: not empty, and (trimmed, lowercased) must be `flat` or `tree`
- `SortBy`: not empty, and (trimmed, lowercased) must be `name`, `code`, `createdat`, or `updatedat`
- `SortDirection`: not empty, and (trimmed, lowercased) must be `asc` or `desc`
- `Page`: >= 1
- `PageSize`: between 1 and 100 inclusive

## Tests added

**Unit (`ONEVO.Tests.Unit`)**
- `EfDepartmentRepositoryTests.cs`: +16 for `ListPageByLegalEntityAsync` (includes one 8-case
  sort theory), +4 for `ListForTreeByLegalEntityAsync` = 20 new test cases
- `DepartmentTreeMapperTests.cs` (new file): 4 test cases
- `DepartmentApplicationUnitTests.cs`: `ListDepartments` region replaced (2 old tests -> 17 new
  test cases across 11 test methods, 3 of which are theories); new `ListDepartmentsQueryValidator`
  region added (15 test cases across 9 test methods, 4 of which are theories)
- `DepartmentsControllerTests.cs`: 3 old `List_*` tests replaced with 5 new test methods

**Architecture (`ONEVO.Tests.Architecture`)**
- `DepartmentPart2AArchitectureTests.cs`: 2 new `InlineData` cases on an existing theory
- `DepartmentPart3ArchitectureTests.cs` (new file): 3 test methods

**Integration (`ONEVO.Tests.Integration`)**
- `DepartmentsIntegrationTests.cs`: 2 existing tests fixed (response-shape break), 8 new test
  methods (list isolation across legal entities, search scoping, pagination totals, tree hierarchy
  scoping, tree tenantId exclusion, invalid sortBy 400, pageSize>100 400, parentDepartmentId
  direct-children-only)

## Verification results

Commands run in order, exact output:

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
Passed! - Failed: 0, Passed: 1268, Skipped: 0, Total: 1268, Duration: 8 s

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
Passed! - Failed: 0, Passed: 430, Skipped: 0, Total: 430, Duration: 6 s

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "FullyQualifiedName~Department" --verbosity minimal
Passed! - Failed: 0, Passed: 44, Skipped: 0, Total: 44, Duration: 15 m 3 s

git diff --check
Exit code 0. Output was only pre-existing LF/CRLF line-ending warnings on files this plan did not
touch (DeleteLegalEntityCommandHandler.cs, UpdateLegalEntityGeneralSettingsCommandHandler.cs,
LegalEntityGeneralSettingsArchitectureTests.cs, LegalEntityPart2BArchitectureTests.cs,
DeleteLegalEntityCommandHandlerTests.cs, UpdateLegalEntityGeneralSettingsCommandHandlerTests.cs,
DefaultRoleSeederTests.cs - all pre-existing uncommitted work from earlier sessions, not part of
this plan). No whitespace errors were reported. Note: `git diff --check` only inspects tracked,
unstaged changes - every file this plan created is untracked (`git status` shows `??`, not `M`),
so this command did not actually examine the new files. It only covered the modified pre-existing
files listed above, none of which this plan touched.
```

Source scans (all passed):
- `DepartmentPart3ArchitectureTests.ListDepartmentsQuery_DoesNotContainTenantIdOrHeadPositionId` -
  passed; manual grep of `ListDepartmentsQuery.cs` for `TenantId`/`HeadPositionId` also returned
  no matches
- Manual case-insensitive grep of `DepartmentsController.cs` for `tenantId`/`headPositionId` found
  only one hit: a pre-existing doc-comment on the unrelated `Restore` action mentioning
  "Never touches ... HeadPositionId" - no parameter, no accepted input
- `DepartmentPart3ArchitectureTests.PositionEntity_HasNoDepartmentIdProperty` - passed; `git status`
  confirms no file under `src/ONEVO.Domain/Features/OrgStructure/Position/` was modified
- `DepartmentPart3ArchitectureTests.NoNewDepartmentMigrations_WereAddedInPart3` - passed; exactly
  the 3 pre-existing Department migrations (`AddDepartments`, `AddDepartmentHeadPositionId`,
  `AddDepartmentCodeCaseInsensitiveUniqueIndex`) exist, no 4th was added. (Note: `git status` shows
  several other untracked migration files - `AddOrgModuleToStarterPlan`,
  `RenameLegalEntityFirstDayOfWeekToWeekStartDay`, `UpdateStarterPlanToCanonicalPhase1Modules` -
  these are pre-existing uncommitted work from earlier sessions unrelated to Department, not
  created by this plan.)
- Every file created or modified by this plan (11 created + 11 modified = 22 files), plus this
  report itself (23 files total), was grepped individually for non-ASCII bytes using
  `LC_ALL=C grep -n '[^ -~\t]'`; none found. (An earlier pass used `grep -P "[^\x00-\x7F]"` with
  stderr suppressed, which silently errored - "-P supports only unibyte and UTF-8 locales" - on
  every file and produced a false "clean" result without actually checking anything. Re-run with
  the locale-safe form above and confirmed genuinely clean.)
- Grepped `tests/` for `/departments` to bound the blast radius of the response-shape change
  (bare array -> `{items, page, ...}`): every hit outside
  `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` is a
  route-string assertion in `DepartmentsControllerArchitectureTests.cs`, not a call that parses
  the list response body. No other test file was at risk from this change.

## Docker / integration status

Docker was available in this environment (`docker info`/`docker ps` succeeded). The full
`ONEVO.Tests.Integration` Department filter ran against a real `postgres:16-alpine` Testcontainer
(confirmed via `docker ps` showing a freshly started container during the run) through the full
Kestrel TestServer pipeline - Authorize, RequirePermission, MediatR, EF/Postgres/RLS, CSRF
middleware - not mocked. All 44 tests passed in 15m3s. Nothing was skipped.

## Known limitation

Postgres orders `NULL` last on `ASC` / first on `DESC`; EF Core's InMemory provider (LINQ-to-Objects,
used by `EfDepartmentRepositoryTests`) orders `NULL` first on `ASC` always. The
`ListPageByLegalEntityAsync_Sorts_ByEachFieldAndDirection` theory deliberately gives every fixture
row a non-null `Code` and non-null `UpdatedAt` to stay provider-independent - it does not exercise
the two providers' differing null-ordering behavior. This is a test-fidelity gap in what the unit
suite covers, not a known bug in `ApplySort` (the integration suite exercises the real Postgres
ordering, just not specifically with null `Code`/`UpdatedAt` rows).

## Remaining gaps carried forward

- Position foundation still missing - Position has no `DepartmentId` column, so
  `DepartmentArchiveDependencyEvaluator.ActivePositionCount` stays hardcoded to 0 and
  `PositionDependencyCheckSupported` stays `false` (unchanged from Part 2; not addressed here)
- `head_position_id` assignment is still deferred - remains schema-ready, read-only in every
  response shape in this plan (`DepartmentListItemResponse`, `DepartmentResponse`,
  `DepartmentTreeNodeResponse`), never accepted as input
- Frontend Department Management screen is not implemented
- Postman collection was not updated (not included in this plan's scope)
