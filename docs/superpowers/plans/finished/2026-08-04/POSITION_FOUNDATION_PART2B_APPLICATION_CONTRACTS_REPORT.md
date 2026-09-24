# Position Foundation Part 2B - Application & Contracts Report

## Update (Part 2C)

This report originally stated (in three places below) that no Position controller existed and
none had been added in Part 2B. That is no longer current: Position Foundation Part 2C added
`src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`, wiring every command and
query documented in this report to HTTP routes under
`api/v1/org/legal-entities/{legalEntityId:guid}/positions`. See
`POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` for the full endpoint table. The
"no controller yet" sentences below are left as-is (not rewritten) since they were accurate at
the time Part 2B was written; this note is the update.

## Correction pass (Part 2B correction)

This report was corrected after review found two issues:

1. **Inactive legal entities were not blocked in create/update.** `CreatePositionCommandHandler` and `UpdatePositionCommandHandler` validated that the legal entity exists but did not check `IsActive`, unlike `RestorePositionCommandHandler`, which already blocked inactive legal entities. Both handlers now return `Result.Conflict` ("Cannot create position: the legal entity is inactive." / "Cannot update position: the legal entity is inactive.") immediately after the existing not-found check, before any department or position lookup. `RestorePositionCommandHandler` was unchanged (its check was already correct).
2. **This report contained non-ASCII/mojibake characters** (em dashes `-`, right arrows `->`) even though the original Test results section claimed an ASCII scan had passed. All such characters have been replaced with ASCII equivalents (`-`, `->`) throughout this file. A fresh ASCII scan now returns zero matches.

Test counts have been refreshed below to reflect the two new unit tests (one per handler) and one new architecture guard test added for this correction.

## Files read

- `Onexo_Department_Position_User_Journey_Validation.md` (corrected UX/business rules for Position screens)
- Department Part 2B application layer (as the pattern template): `CreateDepartmentCommand`/`Validator`/`Handler`, `UpdateDepartmentCommand`/`Validator`/`Handler`, `ArchiveDepartmentCommand`/`Handler`, `RestoreDepartmentCommand`/`Handler`, `ListDepartmentsQuery`/`Validator`/`Handler`, `GetDepartmentQuery`/`Handler`, `CheckDepartmentArchiveDependenciesQuery`/`Handler`, `DepartmentArchiveDependencyEvaluator`, `DepartmentMapper`, `DepartmentTreeMapper`, all `DTOs/Responses/*.cs`, `IDepartmentRepository`, `EfDepartmentRepository`
- Position Part 2A: `Position.cs` (domain entity), `PositionConfiguration.cs`, `IPositionRepository.cs`, `EfPositionRepository.cs`, `PositionReportingHistory.cs`, `ManagementCoverageRecord.cs`, `PositionPart2AArchitectureTests.cs`
- `Department.cs`, `LegalEntity.cs`, `BaseEntity.cs`, `ITenantOwnedEntity.cs` (domain entities)
- `Result.cs`, `ICurrentUser.cs`, `IDateTimeProvider.cs` (Application common)
- `ILegalEntityRepository.cs`
- `CreateDepartmentRequest.cs`, `UpdateDepartmentRequest.cs` (existing Department API contracts, precedent for excluding `legalEntityId`/`tenantId` from request bodies)
- `DepartmentPart2BArchitectureTests.cs` (template for the new `PositionPart2BArchitectureTests.cs`)
- `DepartmentsController.cs` (route template: `api/v1/org/legal-entities/{legalEntityId:guid}/departments`, confirms the route-scope precedent for `legalEntityId`)
- `EfPositionRepositoryTests.cs` (pre-existing Part 2A test file - extended, not replaced)
- `EfDepartmentRepositoryTests.cs` (in-memory DB test-fixture pattern, `BuildInMemoryDb()`)

## Files changed

### Created

**Response DTOs** (`src/ONEVO.Application/Features/OrgStructure/Position/Responses/`)
- `PositionResponse.cs`, `PositionListItemResponse.cs`, `PositionTreeNodeResponse.cs`, `PositionPageResponse.cs`, `PositionArchiveBlockers.cs`

**Mappers** (`src/ONEVO.Application/Features/OrgStructure/Position/Mappers/`)
- `PositionMapper.cs`, `PositionTreeMapper.cs`

**Repository-level type** - `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/PositionPage.cs`

**Service** - `src/ONEVO.Application/Features/OrgStructure/Position/Services/PositionArchiveDependencyEvaluator.cs`

**Queries** (`src/ONEVO.Application/Features/OrgStructure/Position/Queries/`)
- `GetPositionById/{GetPositionByIdQuery,GetPositionByIdQueryValidator,GetPositionByIdQueryHandler}.cs`
- `ListPositions/{ListPositionsQuery,ListPositionsQueryValidator,ListPositionsQueryHandler}.cs`
- `GetPositionTree/{GetPositionTreeQuery,GetPositionTreeQueryValidator,GetPositionTreeQueryHandler}.cs`

**Commands** (`src/ONEVO.Application/Features/OrgStructure/Position/Commands/`)
- `CreatePosition/{CreatePositionCommand,CreatePositionCommandValidator,CreatePositionCommandHandler}.cs`
- `UpdatePosition/{UpdatePositionCommand,UpdatePositionCommandValidator,UpdatePositionCommandHandler}.cs`
- `ArchivePosition/{ArchivePositionCommand,ArchivePositionCommandValidator,ArchivePositionCommandHandler}.cs`
- `RestorePosition/{RestorePositionCommand,RestorePositionCommandValidator,RestorePositionCommandHandler}.cs`
- `CheckPositionArchive/{CheckPositionArchiveCommand,CheckPositionArchiveCommandValidator,CheckPositionArchiveCommandHandler}.cs`

**API contracts** (`src/ONEVO.Api/Contracts/OrgStructure/Positions/`)
- `CreatePositionRequest.cs`, `UpdatePositionRequest.cs`

**Tests**
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/PositionTreeMapperTests.cs` (3 tests)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/GetPositionByIdQueryHandlerTests.cs` (3 tests)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/ListPositionsQueryHandlerTests.cs` (3 tests)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/GetPositionTreeQueryHandlerTests.cs` (2 tests)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/CreatePositionCommandHandlerTests.cs` (12 tests)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/UpdatePositionCommandHandlerTests.cs` (6 tests)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/ArchiveRestoreCheckPositionCommandHandlerTests.cs` (8 tests)
- `tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs` (52 test cases: Theory + Fact combined)

### Modified

- `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs` - appended `ListPageAsync(...)` and `CountHeadDepartmentReferencesAsync(...)`. No existing signature changed.
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs` - appended implementations of the two new methods plus a private `ApplySort` helper. No existing method changed. Block-bodied throughout (verified by `PositionPart2AArchitectureTests.EfPositionRepository_HasNoExpressionBodiedMembers`, still passing).
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs` - this file already existed from Part 2A (8 tests covering schema config and the pre-existing repository surface). Appended 8 new tests for `ListPageAsync` and `CountHeadDepartmentReferencesAsync`, plus one test proving cross-tenant/cross-legal-entity exclusion on the existing `ListByLegalEntityAsync`. Nothing removed or altered from the Part 2A tests.

### Modified (correction pass)

- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandHandler.cs` - added `if (!legalEntity.IsActive) return Result<PositionResponse>.Conflict("Cannot create position: the legal entity is inactive.");` immediately after the existing legal-entity not-found check.
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs` - added `if (!legalEntity.IsActive) return Result<PositionResponse>.Conflict("Cannot update position: the legal entity is inactive.");` immediately after the existing legal-entity not-found check.
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/CreatePositionCommandHandlerTests.cs` - added `Handle_ReturnsConflict_WhenLegalEntityIsInactive` (asserts 409, department lookup never called, `AddAsync`/`SaveChangesAsync` never called).
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/UpdatePositionCommandHandlerTests.cs` - added `Handle_ReturnsConflict_WhenLegalEntityIsInactive` (asserts 409, position and department lookups never called, `Update`/`SaveChangesAsync` never called).
- `tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs` - added `CreateUpdateAndRestorePositionHandlers_BlockInactiveLegalEntity`, a text-scan guard asserting all three handler source files contain the `legalEntity.IsActive` check.
- `POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md` - this file: added this correction section, replaced all non-ASCII em dash/arrow characters with ASCII equivalents, refreshed test counts.

## Commands, queries, and contracts added

| Type | Namespace | Purpose |
|---|---|---|
| `CreatePositionCommand` | `...Commands.CreatePosition` | Create a position scoped to tenant + legal entity + department |
| `UpdatePositionCommand` | `...Commands.UpdatePosition` | Update name/code/type/capacity/department/reports-to |
| `ArchivePositionCommand` | `...Commands.ArchivePosition` | Soft-deactivate (`IsActive = false`), blocked by dependency evaluator |
| `RestorePositionCommand` | `...Commands.RestorePosition` | Reactivate, blocked if department/reports-to inactive |
| `CheckPositionArchiveCommand` | `...Commands.CheckPositionArchive` | Read-only blocker-count check (named Command per the task's own required naming, functions as a query) |
| `GetPositionByIdQuery` | `...Queries.GetPositionById` | Single position with department/reports-to names + child count |
| `ListPositionsQuery` | `...Queries.ListPositions` | Paginated, filtered, sorted list |
| `GetPositionTreeQuery` | `...Queries.GetPositionTree` | Full reporting-hierarchy tree for a legal entity |
| `CreatePositionRequest` / `UpdatePositionRequest` | `ONEVO.Api.Contracts.OrgStructure.Positions` | HTTP request bodies (no controller wired yet) |

All Application-layer namespaces deliberately stop at `OrgStructure` (e.g. `ONEVO.Application.Features.OrgStructure.Commands.CreatePosition`, never `...OrgStructure.Position.Commands...`) - this is the same convention `IDepartmentRepository`/`IPositionRepository`/`ILegalEntityRepository` already use, and it is what keeps `PositionPart2AArchitectureTests.PositionPart2A_DoesNotExpose_Controllers_Commands_Queries_Or_RequestContracts` passing unchanged even though this task adds dozens of new Application types.

## Validation rules

- `LegalEntityId`, `DepartmentId` (create/update), `PositionId` (update/archive/restore/check): `NotEmpty`.
- `Name`: `NotEmpty`, max 100 chars (matches `PositionConfiguration.Name` schema).
- `Code`: `NotEmpty`, max 40 chars, `^[A-Za-z0-9_-]{1,40}$` (matches `PositionConfiguration.Code` schema max length). The "required" and "max length" rules are unconditional; only the regex rule carries `.When(!string.IsNullOrWhiteSpace(x.Code))` - a trailing `.When()` in FluentValidation scopes to every rule in the same chain, so keeping `NotEmpty`/`MaximumLength` in a separate chain from the regex is what makes "code is required" actually enforce (an earlier draft had this bug; caught before implementation via review and fixed in both `CreatePositionCommandValidator` and `UpdatePositionCommandValidator`, each covered by a dedicated `Validator_RejectsEmptyCode` test).
- `PositionType`: `NotEmpty`, must equal `Position.TypeUnique` ("unique") or `Position.TypePooled` ("pooled") - matched case-sensitively, never normalized/trimmed before comparison; `"Unique"` or `" unique"` are rejected by design.
- `MaxOccupancy`: exactly `1` when `PositionType == "unique"`; `>= 1` when `PositionType == "pooled"`.
- `ReportsToPositionId` (update only, format-level): must not equal `PositionId`. Existence, active-status, and cross-legal-entity/cycle checks are handler-level (DB-backed), matching Department's split between validator (format) and handler (business/DB rules).
- `ListPositionsQuery`: `Search` max 100 chars; `SortBy` allowlist (`name, code, department, reportsTo, type, capacity, status, createdAt, updatedAt`); `SortDirection` allowlist (`asc, desc`); `Page >= 1`; `PageSize` in `[1, 100]`. No default is applied inside this layer - bounds are enforced but a future Part 2C controller is expected to supply defaults before constructing the query, mirroring how `ListDepartmentsQuery` is invoked today.

## Route-scope / selected-company rule for legalEntityId

`legalEntityId` is a property on every Position command/query, populated exclusively by a future controller from the URL route segment (mirroring `DepartmentsController`'s `api/v1/org/legal-entities/{legalEntityId:guid}/departments` pattern) - never accepted from a request body. No Position controller was added in this task; `CreatePositionRequest`/`UpdatePositionRequest` intentionally have no `LegalEntityId` property at all.

## tenantId statement

tenantId is never accepted from any request contract, command, or query. Every handler resolves it exclusively from `ICurrentUser.TenantId`, matching every existing Department Part 2B handler. Verified by `rg -n "tenantId|TenantId" src/ONEVO.Api/Contracts/OrgStructure/Positions` -> zero matches, and by `PositionPart2BArchitectureTests.RequestContracts_DoNotContainForbiddenOwnershipOrRoleFields`.

## Role/access statement

No Position command, query, handler, validator, or contract creates, mutates, or references security roles, permission codes, or access-role assignment. Position screens do not create roles. `Position.DefaultRoleId` (a Part 2A legacy field) is never read or written by any Part 2B command or handler. Verified by `rg -n "CreateRole|roleName|permission|permissions|org:read|org:manage"` -> zero matches, and by the architecture tests' forbidden-property-name scans (`Role`, `Permission`).

## Deferred scope

- **Occupant assignment / `position_assignments`**: not implemented. No such table or entity exists anywhere in this codebase (confirmed by repo-wide search for `PositionAssignment`, `position_assignments`, `Occupant`). `CheckPositionArchiveCommand` reports `ActiveOccupants` as `null` with `ActiveOccupantsCheckSupported = false` rather than a fabricated zero.
- **Access approval**: not implemented - no access-role concept is touched by Position in this task.
- **Department head assignment**: remains deferred. No Position contract or command exposes a way to set `departments.head_position_id`; `Department.HeadPositionId` continues to be read-only wherever it already surfaces (Department's own response DTOs), unchanged by this task.
- **Management scope / `ManagementCoverageRecord`**: `IPositionRepository` already had `AddManagementCoverageRecordAsync`/`GetLockedReportingStructureCoverageAsync` from Part 2A, but no Part 2B command creates or lists management-scope records - reporting-line changes (`UpdatePositionCommand`) only mutate `ReportsToPositionId`, nothing else.

## Schema limitations found

- No `position_assignments`/employee-position table exists, so active-occupant counts cannot be measured. Documented in `PositionArchiveBlockers.ActiveOccupants` (nullable, `null` = unverifiable) and `ActiveOccupantsCheckSupported` (`false`), consistent with `DepartmentArchiveDependencyEvaluator`'s precedent for `PositionDependencyCheckSupported`. `ArchivePositionCommandHandler`'s blocking gate (`PositionArchiveBlockers.CanArchive`) intentionally excludes the unverifiable occupant count - it only blocks on the two counts that are actually measurable (`ActiveChildPositions`, `HeadOfDepartments`).

## Test results (refreshed by correction pass)

- **Full unit test suite**: `1323 / 1323` passed (`dotnet test tests/ONEVO.Tests.Unit`), 0 failed, 0 skipped. (Was `1321 / 1321`; +2 from this correction pass: `Handle_ReturnsConflict_WhenLegalEntityIsInactive` in `CreatePositionCommandHandlerTests.cs` and in `UpdatePositionCommandHandlerTests.cs`.)
  - New Position-specific unit tests added this task: **45** across 7 new test files, plus **8** appended to the pre-existing `EfPositionRepositoryTests.cs` (16 total in that file now).
- **Full architecture test suite**: `496 / 496` passed (`dotnet test tests/ONEVO.Tests.Architecture`), 0 failed, 0 skipped - including every pre-existing `PositionPart2AArchitectureTests` and `DepartmentPart2BArchitectureTests` fact, neither of which was modified by this task. (Was `495 / 495`; +1 from this correction pass: `CreateUpdateAndRestorePositionHandlers_BlockInactiveLegalEntity` in `PositionPart2BArchitectureTests.cs`.)
  - New: `PositionPart2BArchitectureTests.cs`, 53 test cases (Theory expansions + Facts).
- **API build**: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` - 0 errors, 0 warnings from this task's files (one pre-existing unrelated CS8602 warning in `AdminAuthController.cs`).

## Verification command output

- `rg "tenantId|TenantId" src/ONEVO.Api/Contracts/OrgStructure/Positions` -> no matches.
- `rg "legalEntityId|LegalEntityId" src/ONEVO.Api/Contracts/OrgStructure/Positions` -> no matches.
- `rg "CreateRole|roleName|permission|permissions|org:read|org:manage" src/ONEVO.Api/Contracts/OrgStructure/Positions src/ONEVO.Application/Features/OrgStructure/Position` -> no matches.
- `rg "DateTimeOffset\.UtcNow|DateTime\.UtcNow" src/ONEVO.Application/Features/OrgStructure/Position` -> no matches (also enforced by `PositionApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly`).
- `rg "Guid\.Empty|00000000-0000-0000-0000-000000000000|LegalEntityIdValue|DepartmentIdValue"` over the Position Domain/Application/Infrastructure/test surface -> only `if (tenantId == Guid.Empty)` guard comparisons in every new handler (expected/correct - an equality check, not a fallback default). Zero `?? Guid.Empty` fallbacks, zero `LegalEntityIdValue`/`DepartmentIdValue` identifiers.
- `rg "enum .*Position|PositionType|SortDirection|PositionSort" src/ONEVO.Application/Features/OrgStructure/Position src/ONEVO.Api/Contracts/OrgStructure/Positions` -> all matches are plain-`string` property/field usages of `PositionType`/`SortDirection` (record properties, validator rules, mapper assignments). Zero `enum` declarations, zero `PositionSort` identifier. No C# enum was introduced anywhere in Position's Application or API-contract surface.
- ASCII scan (`rg "[^\x00-\x7F]"`) over every file created/modified by this task -> no matches.
- `git diff --check` on the two modified files -> no whitespace/conflict-marker errors (only informational CRLF-normalization notices, which are not check failures).

## Remaining risks

- The pre-existing working tree (on branch `feature/mkcert-tenant-subdomain-https`) already contains substantial *uncommitted* prior work - the entire Department feature, Position Part 2A entities/repository, and several migrations - none of it committed to git yet. This task's changes were made directly in that same working tree and were **not committed**, per the original task instruction. Anyone continuing this branch should be aware the full history of how Part 2A/Department work arrived is not yet in git log.
- `ListPositionsQuery`/`GetPositionTreeQuery` do not populate `DepartmentName`/`ReportsToPositionName`/`ChildCount` on list rows (only on the single-item `GetPositionByIdQuery` and, for `ChildCount` only, on tree nodes) - this avoids N+1 queries in the paginated list, but a future Part 2C controller wiring a "Positions - List View" screen that needs a Department column will need either a SQL-side join added to `ListPageAsync` or a client-side lookup against the tree/legal-entity data already fetched elsewhere.
- `CheckPositionArchiveCommand` is named a Command (per the task's explicit required naming) even though it performs no mutation - functionally and structurally it is a query (`IRequest<Result<PositionArchiveBlockers>>`, no `SaveChangesAsync` call). This mirrors the task's literal instruction rather than Department's own naming (`CheckDepartmentArchiveDependenciesQuery`), which is a deliberate deviation, not an oversight.
- No controller exists yet, so none of this layer has been exercised through an actual HTTP request/response cycle or through MediatR's DI pipeline at runtime - only via `dotnet build`, unit tests (direct handler instantiation with mocks), and the in-memory-EF repository tests.
