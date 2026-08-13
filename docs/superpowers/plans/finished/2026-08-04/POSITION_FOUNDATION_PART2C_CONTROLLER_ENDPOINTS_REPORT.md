# Position Foundation Part 2C - Controller & Endpoint Tests

## Scope

Part 2C only: PositionsController, controller unit tests, architecture guards. No migrations,
schema, Department/LegalEntity/Auth code, frontend, Postman, or OneVo-HR docs were touched.
Position Part 2A (schema/repository) and Part 2B (application layer: commands, queries,
contracts) were already complete and were read but not modified.

## Files read

- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs`
- `src/ONEVO.Api/Filters/RequirePermissionAttribute.cs`
- All Position Part 2B application-layer files under
  `src/ONEVO.Application/Features/OrgStructure/Position/` (Queries, Commands, Responses,
  RepositoryInterfaces, Mappers, Services)
- `src/ONEVO.Api/Contracts/OrgStructure/Positions/CreatePositionRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Positions/UpdatePositionRequest.cs`

## Files changed

**Created:**
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`
- `tests/ONEVO.Tests.Unit/Controllers/Tenant/OrgStructure/PositionsControllerTests.cs` (16 tests)
- `tests/ONEVO.Tests.Architecture/PositionsControllerArchitectureTests.cs` (15 tests)
- `POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` (this file)

**Modified (test-only, no production/schema changes):**
- `tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs` - the Part 2B guard
  `NoPositionsController_ExistsYetInPart2B` asserted zero PositionsController types. Renamed to
  `PositionsController_IntroducedInPart2C_IsTheOnlyPositionController` and changed the
  assertion from "empty" to "exactly one, in the expected namespace" now that Part 2C has
  introduced it.
- `tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs` - same fix inside
  `PositionPart2A_DoesNotExpose_Controllers_Commands_Queries_Or_RequestContracts`: the
  controller-absence assertion is now a controller-identity assertion. The CQRS/request-contract
  portion of that test was left unchanged (still passes - Part 2B's actual namespaces never
  contain the literal `OrgStructure.Position` segment the guard checks for).
- `tests/ONEVO.Tests.Architecture/DepartmentPart2ArchiveRestoreArchitectureTests.cs` - same fix
  inside `NoPositionController_HasBeenAddedInPart2`, now asserts the controller exists in the
  expected namespace instead of asserting its absence.

These three were pre-existing guards from earlier parts that explicitly encoded "no
PositionsController yet" as their pass condition; leaving them unchanged would have made them
permanently red the moment any PositionsController was added, regardless of correctness. Each
edit is a narrow assertion swap (empty -> single/identity), not a scope or behavior loosening.

## Endpoint table

| # | Method | Route | Permission | Command/Query | Success status |
|---|--------|-------|------------|----------------|-----------------|
| 1 | GET | `api/v1/org/legal-entities/{legalEntityId:guid}/positions` | `org:read` | `ListPositionsQuery` | 200 OK (`PositionPageResponse`) |
| 2 | GET | `.../positions/tree` | `org:read` | `GetPositionTreeQuery` | 200 OK (`IReadOnlyList<PositionTreeNodeResponse>`) |
| 3 | GET | `.../positions/{positionId:guid}` | `org:read` | `GetPositionByIdQuery` | 200 OK (`PositionResponse`) |
| 4 | POST | `.../positions` | `org:manage` | `CreatePositionCommand` | 201 Created (`CreatedAtAction` -> `Get`) |
| 5 | PUT | `.../positions/{positionId:guid}` | `org:manage` | `UpdatePositionCommand` | 200 OK (`PositionResponse`) |
| 6 | POST | `.../positions/{positionId:guid}/archive-check` | `org:read` | `CheckPositionArchiveCommand` | 200 OK (`PositionArchiveBlockers`) |
| 7 | POST | `.../positions/{positionId:guid}/archive` | `org:manage` | `ArchivePositionCommand` | 204 No Content |
| 8 | POST | `.../positions/{positionId:guid}/restore` | `org:manage` | `RestorePositionCommand` | 204 No Content |

All failure paths return `Problem(result.Error, statusCode: result.StatusCode ?? 400)`, matching
the `DepartmentsController` convention exactly.

Route ordering: `Tree` (`GET tree`) is declared before `Get` (`GET {positionId:guid}`) in the
controller source. In practice the `:guid` constraint on `Get` already prevents `tree` from ever
matching it, so ordering is not load-bearing, but the literal route is placed first per the task
instruction.

## Key statements

- **legalEntityId comes from the route only.** Every command/query is constructed with the
  `legalEntityId` route parameter; no request contract (`CreatePositionRequest`,
  `UpdatePositionRequest`) declares a `LegalEntityId` property. Verified by both architecture
  tests and the `rg` search below (0 matches).
- **tenantId is never accepted from the request body.** No controller parameter, route segment,
  or request contract references `tenantId`/`TenantId` anywhere in this feature.
- **No role/access-role/permission creation exists in Position endpoints.** The only appearances
  of `permission` in the controller are the `[RequirePermission("org:read"|"org:manage")]`
  attributes that gate access; no endpoint creates, returns, or accepts role/permission/
  access-role data.
- **Department head assignment remains deferred.** No Position endpoint reads, writes, or
  exposes `Department.HeadPositionId`; verified by an architecture test (`NoEndpoint_
  AcceptsOrMutatesHeadPositionId`) and the `rg` search below.
- **Archive uses `POST .../archive`, no DELETE endpoint.** No `[HttpDelete]` attribute exists on
  `PositionsController`; archiving and restoring are both `POST` actions returning `204 No
  Content`, matching Department's terminology decision for new integrations (Position has no
  legacy DELETE clients to maintain compatibility for, so no delete alias was added).
- **Active occupant count remains unsupported.** `PositionArchiveBlockers.ActiveOccupants` stays
  nullable with `ActiveOccupantsCheckSupported = false` (unchanged from Part 2B) because
  `position_assignments` does not exist yet; the controller simply forwards whatever the handler
  returns and does not fabricate a count.

## Tests added/updated

- `PositionsControllerTests.cs` - 16 tests: List defaults, List explicit params, Get success/404,
  Tree success, Create success/201/409, Update success/409, ArchiveCheck success/404, Archive
  success/404, Restore success/404, constructor-injection guard.
- `PositionsControllerArchitectureTests.cs` - 15 tests: namespace, `[Authorize(Policy =
  "TenantPolicy")]`, base route template, all 8 routes/verbs, `org:read`/`org:manage` permission
  assignment per action, IMediator-only constructor, no DbContext/repository/ICurrentUser/tenant
  service (both reflection- and source-text-based), no `tenantId` parameter, request contracts
  free of TenantId/LegalEntityId/HeadPositionId and free of role/permission fields, no
  `[HttpDelete]`, no dead `ArchivePositionRequest`/`RestorePositionRequest` contracts, no
  HeadPositionId exposure anywhere in the controller source.
- 3 pre-existing architecture tests updated (see "Files changed" above) so their "controller does
  not exist yet" assertions reflect Part 2C reality instead of going permanently red. Two of the
  three were also renamed (`PositionsController_FromPart2C_LivesInTenantOrgStructureNamespace` in
  the Department file; the Part 2A file's combined controller+CQRS fact was split into
  `PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace` and
  `PositionPart2A_DoesNotExpose_Commands_Queries_Or_RequestContracts`) so each test's name matches
  what it actually asserts, since the original names ("No...", "DoesNotExpose...Controllers...")
  would otherwise contradict a passing assertion that the controller exists.

## TDD process followed

1. Wrote `PositionsControllerTests.cs` first against a controller that did not exist.
2. Confirmed RED: `dotnet build tests\ONEVO.Tests.Unit\...` failed with `CS0246: The type or
   namespace name 'PositionsController' could not be found` - the exact and only expected error.
3. Implemented `PositionsController.cs` to satisfy the tests, following
   `DepartmentsController`'s exact conventions (constructor, action shapes, `Problem(...)`
   mapping).
4. Confirmed GREEN: all 16 new controller tests passed.
5. Added `PositionsControllerArchitectureTests.cs` and fixed the three stale Part 2A/2B guards
   that had encoded "the controller doesn't exist" as their success condition.

## Build/test results

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  Build succeeded. 0 Warning(s) 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  Passed! - Failed: 0, Passed: 1339, Skipped: 0, Total: 1339

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  Passed! - Failed: 0, Passed: 516, Skipped: 0, Total: 516
```

## Focused search results

**`rg -n "tenantId|TenantId|legalEntityId|LegalEntityId" src/ONEVO.Api/Contracts/OrgStructure/Positions`**
-> 0 matches (expected 0, confirmed).

**`rg -n "CreateRole|roleName|permission|permissions|accessRole|DefaultRoleId|org:read|org:manage" src/ONEVO.Api/Contracts/OrgStructure/Positions src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`**
-> Matches only `org:read`/`org:manage` inside `[RequirePermission(...)]` attributes on the
controller (8 hits, one per action). No contract file matched anything. Expected shape confirmed.

**`rg -n "DELETE|HttpDelete|ArchivePositionRequest|RestorePositionRequest|HeadPositionId|headPositionId" src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs src/ONEVO.Api/Contracts/OrgStructure/Positions`**
-> One harmless match: the doc comment on `Archive` says "...not a physical delete." No
`HttpDelete`, no `ArchivePositionRequest`/`RestorePositionRequest`, no `HeadPositionId`. Expected
shape confirmed.

**`rg -n "DbContext|Repository|ICurrentUser|ITenant|ApplicationDbContext" src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`**
-> 0 matches (expected 0, confirmed).

**ASCII scan** (`rg -n "[^\x00-\x7F]"` equivalent via PowerShell `Select-String`) over the
controller, the Positions contracts folder, the new unit test file, the new architecture test
file, and this report -> 0 matches.

**`git diff --check`** -> only pre-existing CRLF/LF warnings on files outside this task's scope
(LegalEntity handlers, Position repository/domain/config files from prior uncommitted work); no
whitespace errors introduced by this task's new files (new files are untracked, so `git diff
--check` does not scan them - they were separately ASCII/format verified above by direct
inspection during authoring).

## Migration guard (item 13 of the architecture checklist)

The task's architecture checklist item "No migrations were added in this task... scoped to Part
2C touched files, not brittle 'latest migration' assertions" was not implemented as an automated
test. A non-brittle version of this guard is hard to write generically (it would need to know
which migration files existed before this specific task ran, which isn't something reflection or
a file-system scan can determine on its own). Instead it is verified manually: this task's edits
were confined to `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs`,
`tests/ONEVO.Tests.Unit/Controllers/Tenant/OrgStructure/PositionsControllerTests.cs`,
`tests/ONEVO.Tests.Architecture/PositionsControllerArchitectureTests.cs`, three pre-existing
architecture test files (assertion-only edits, listed above), this report, and
`POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md` (a documentation note). No file
under `src/ONEVO.Infrastructure/Migrations/` was read, created, or modified in this task -
confirmed by `git status --porcelain` showing the same set of pre-existing migration files
(all already untracked from prior sessions) before and after this task's edits.

## Remaining risks

- **No HTTP/integration proof yet.** Only unit (mocked `IMediator`) and architecture (reflection)
  tests were added in this task, per scope. No integration test exercises the real HTTP pipeline,
  `[Authorize]`, or `RequirePermissionAttribute` end-to-end for `PositionsController`.
- **Active occupant count remains unsupported** until `position_assignments` exists;
  `PositionArchiveBlockers.ActiveOccupants` stays nullable and `ActiveOccupantsCheckSupported =
  false`, unchanged from Part 2B.
- **The working tree contains substantial prior uncommitted Department/Position/LegalEntity work**
  (migrations, domain entities, repositories, seeders, and several `*_REPORT.md` files from
  earlier sessions), confirmed still present via `git status --porcelain` at the end of this
  task. Nothing in that pre-existing uncommitted state was touched by Part 2C beyond the three
  architecture-test assertion fixes listed above.

## Confirmation

No migrations, schema, Postman collection, frontend code, or OneVo-HR docs were touched. No
Department, LegalEntity, or Auth/System Config code was touched (only three architecture *test*
files needed a stale-assertion fix because they explicitly asserted the Position controller's
non-existence). Nothing was committed or pushed - the working tree still has all changes
unstaged.
