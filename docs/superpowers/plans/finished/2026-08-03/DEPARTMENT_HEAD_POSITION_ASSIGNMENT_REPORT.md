# Department Part 3 — Department Head Position Assignment

## Summary

Department create/update requests now accept an optional `headPositionId`. Position Foundation
(Parts 2A–2D) is complete, so `Department.HeadPositionId` — schema-ready since the earlier
Department parts — is now validated and settable through the API.

- **Create** rejects any non-null `headPositionId` (409 Conflict). A newly created department has
  no positions belonging to it yet, so the "position must belong to the same department" rule
  cannot be evaluated at create time. This matches the approved requirement doc's explicit
  recommendation (see "Create-time limitation" below) — no guessing was needed.
- **Update** validates and assigns/clears `headPositionId`, scoped to the department being
  updated.

No migrations, frontend, docs, or role/access/user-assignment work was done. Employee/position
assignment to departments (as opposed to *head* position assignment) remains out of scope, as
before.

## Exact files changed

### Contracts
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/CreateDepartmentRequest.cs` — added `Guid? HeadPositionId`
- `src/ONEVO.Api/Contracts/OrgStructure/Departments/UpdateDepartmentRequest.cs` — added `Guid? HeadPositionId`

### Commands
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommand.cs` — added `Guid? HeadPositionId`
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommand.cs` — added `Guid? HeadPositionId`

### Handlers
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment/CreateDepartmentCommandHandler.cs` — rejects non-null `HeadPositionId` (409)
- `src/ONEVO.Application/Features/OrgStructure/Department/Commands/UpdateDepartment/UpdateDepartmentCommandHandler.cs` — injects `IPositionRepository`, validates and assigns/clears `HeadPositionId`

### Controller
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` — passes `request.HeadPositionId` into both commands. Route, permissions (`org:read` / `org:manage`), and verbs unchanged.

### Repository
No new repository method was needed. `IPositionRepository.GetByIdForLegalEntityAsync(tenantId, legalEntityId, positionId, ct)` already existed (Position Foundation Part 2A), already filters by tenant + legal entity in SQL, and already uses `AsNoTracking`. It is reused as-is for the head-position validation lookup — introducing a narrower duplicate method would have been unjustified duplication.

### Tests changed (obsolete Part 2A/2B/2C scope guards updated for Part 3)
These scope-guard tests previously asserted the Department request contracts/commands had **no**
`HeadPositionId`, matching the intentionally schema-only scope of earlier parts. That assertion is
now correctly reversed; each was replaced with a comment (matching this repo's existing convention
for retiring phase-scoped guards) plus new tests that assert the current behavior:
- `tests/ONEVO.Tests.Architecture/DepartmentPart2BArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/DepartmentsControllerArchitectureTests.cs` (narrowed to `RequestContracts_DoNotExposeTenantId_OrLegalEntityId`, since `HeadPositionId` exclusion no longer applies)
- `tests/ONEVO.Tests.Architecture/PositionPart2AArchitectureTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` — `HeadPositionId_IsIgnoredOnCreate_NotAcceptedFromRequestBody` renamed to `Create_WithHeadPositionId_Returns409_AssignmentDeferredToUpdate`; behavior changed from silently-ignored-201 to explicit-409, since the field is now a real, validated part of the contract rather than an unknown JSON property.

### Tests added
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentApplicationUnitTests.cs` — 9 new tests (create x2, update x7)
- `tests/ONEVO.Tests.Architecture/DepartmentPart3ArchitectureTests.cs` — 5 new fact/theory groups
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/DepartmentsControllerTests.cs` — 2 new pass-through tests
- `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` — 7 new integration tests (an 8th, the renamed `Create_WithHeadPositionId_Returns409_...`, is listed above under "Tests changed" since it replaces an existing test rather than adding one)

## Create-time limitation: why headPositionId is rejected on create, not applied

`Onexo_Department_Position_User_Journey_Validation.md` ("2. Create Department") explicitly names
this exact situation and recommends an approach:

> **Head position requires redesign.** A newly created department does not yet have positions
> belonging to it. Use one of these approaches: 1. Create the department first and assign its head
> afterwards, **which is recommended**. 2. Allow an existing position to be moved into the new
> department, but clearly warn the user. **Do not silently assign a position currently belonging to
> another department.**

Given that guidance, `CreateDepartmentCommandHandler` rejects any non-null `headPositionId` with a
409 Conflict and an explanatory message, rather than:
- silently ignoring it (the old, pre-Part-3 behavior when the field didn't exist on the contract), or
- accepting a cross-department position implicitly (explicitly forbidden by the doc), or
- inventing a "move the position into this department" side effect (out of scope — no position
  assignment/move work was authorized for this task).

Department head assignment is supported **only through update**, after the department exists.

## Validation behavior table

| Scenario | Create | Update |
|---|---|---|
| `headPositionId` omitted or `null` | 200/201, `HeadPositionId` stays/becomes `null` | 200, clears any existing head position (full-replace semantics — see below) |
| `headPositionId` provided (any value) | **409 Conflict** — deferred to update | Proceeds to validation below |
| Position does not exist (wrong id, or filtered out by tenant/legal-entity scope) | n/a (rejected before lookup) | **404 Not Found** |
| Position exists, different legal entity | n/a | **404 Not Found** (repository query filters by `legalEntityId`, so a cross-LE position is indistinguishable from "does not exist" — matches the existing `Create_ParentInDifferentLegalEntity_Returns404` precedent for parent department) |
| Position exists, different tenant | n/a | **404 Not Found** (same reasoning — tenant filter in the repository query) |
| Position exists, same tenant+LE, inactive | n/a | **409 Conflict** |
| Position exists, active, `DepartmentId` belongs to a different department | n/a | **409 Conflict** |
| Position exists, active, `DepartmentId` is `null` (no department assigned) | n/a | **409 Conflict** — `Position.DepartmentId` is a nullable "transitional" field per its own entity comment; a position with no department cannot satisfy the same-department rule |
| Position exists, active, `DepartmentId` matches the department being updated | n/a | **200 OK**, `HeadPositionId` set |

## Omitted-vs-null request-model limitation (as required to report)

A plain C# record (`UpdateDepartmentRequest`/`UpdateDepartmentCommand`) bound from a JSON body
cannot distinguish "the client omitted `headPositionId`" from "the client sent `headPositionId:
null`" — both deserialize to `Guid? HeadPositionId = null`. This is not new to `HeadPositionId`:
the existing `ParentDepartmentId` field on the same contracts has the identical limitation, and the
existing handler already resolves it with **full-replace PUT semantics** — `existing.ParentDepartmentId
= request.ParentDepartmentId;` unconditionally overwrites the stored value, omitted or not.

`HeadPositionId` follows the same, already-established convention for consistency:
`existing.HeadPositionId = request.HeadPositionId;` unconditionally. Omitting the field from an
update request therefore **clears** any previously-assigned head position, exactly as sending
`headPositionId: null` would. This is locked in by
`UpdateDepartment_OmittingHeadPositionId_ClearsIt` (unit) and
`Update_OmittingHeadPositionId_ClearsPreviouslyAssignedHead` (integration).

The task's "preserve if omitted" clause was conditional on the API using partial-update semantics —
it does not; every other field on this endpoint is already full-replace. No behavior was invented;
the existing convention was extended.

## Test results

- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` → **0 errors**; one pre-existing, unrelated warning (`AdminAuthController.cs(59,19)` CS8602) present before this task and untouched by it
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal` → **1349 passed, 0 failed** (full suite, not just Department; 161 of these are Department-scoped)
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal` → **525 passed, 0 failed** (full suite)
- `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~Department" --verbosity minimal` → **59 passed, 0 failed** (real PostgreSQL via Testcontainers, ~30 min)
- Cross-fixture check: `grep -ril "headPositionId" tests/ONEVO.Tests.Integration` also matched `PositionsIntegrationTests.cs`, which seeds `Department.HeadPositionId` directly via DbContext (not through `POST/PUT .../departments`), so the create-time behavior change (silently-ignored → 409) does not affect it. Its stale doc comments (which predated Part 3 and claimed no request contract ever exposes `headPositionId`) were corrected for accuracy.
- **Not run: `--filter "FullyQualifiedName~OrgStructure"`.** That filter pulls in the unrelated LegalEntity and Position integration fixtures alongside Department, and each `[Collection]`-isolated fixture in this suite spins up its own Testcontainers PostgreSQL instance sequentially — the combined run stretches into multiple hours. It is not the required verification scope for this feature: the cross-fixture check above already confirms `PositionsIntegrationTests.cs` cannot be affected by the create-time behavior change (it never posts `headPositionId` through the Departments HTTP endpoints), and `LegalEntitiesIntegrationTests.cs` has no `headPositionId` references at all.
- `git diff --check` → no whitespace/conflict-marker errors introduced (pre-existing LF/CRLF autocrlf notices only, on files unrelated to this task)

## Integration coverage (new, Part 3)

All run through the full Kestrel pipeline (Authorize → RequirePermission → MediatR → EF/Postgres/RLS), matching this test class's existing convention:

- `Create_WithHeadPositionId_Returns409_AssignmentDeferredToUpdate`
- `Update_WithHeadPositionId_AssignsHeadPosition_AndResponseIncludesIt`
- `Update_OmittingHeadPositionId_ClearsPreviouslyAssignedHead`
- `Update_HeadPositionId_NotFound_Returns404`
- `Update_HeadPositionId_Inactive_Returns409`
- `Update_HeadPositionId_FromAnotherDepartment_Returns409`
- `Update_HeadPositionId_FromAnotherLegalEntity_Returns404`
- `Update_HeadPositionId_FromAnotherTenant_Returns404_RlsIsolationIntact`

Positions are seeded directly via `ApplicationDbContext` in a DI scope (mirroring the existing
Employee-seeding block already in this test class) since there is no public Positions HTTP
contract exercised by this fixture. Every *assertion* still goes through real HTTP.

## Remaining limitations

- Update-time same-department validation only accepts a position whose `DepartmentId` already
  equals the department being updated. There is no "move this position into this department while
  assigning it as head" convenience — that would be position-assignment/move behavior, explicitly
  out of scope for this task.
- No endpoint returns "eligible head-position candidates" for a given department; the frontend
  would need to filter `GET /positions?departmentId=...` client-side, or a future task would need
  to add that.
- Full-replace semantics mean a client must always resend the current `headPositionId` on an
  unrelated update (e.g. renaming a department) or it will be cleared — same pre-existing behavior
  as `ParentDepartmentId`, not new to this task.
- `Update_HeadPositionId_FromAnotherTenant_Returns404_RlsIsolationIntact` seeds the position
  entirely within tenant B (its own tenant and legal entity), so either the tenant filter or the
  legal-entity filter could account for the 404 — the two are not independently isolated by this
  test. Isolating tenant scoping alone would require a position row whose `tenant_id` and
  `legal_entity_id` belong to different tenants; that combination was not attempted because it
  wasn't verified against `PositionConfiguration`'s FK constraints, and the required verification
  scope for this task did not include a further integration run to check it.

## Explicitly out of scope / not done

- **No employee assignment, position assignment, or department-membership work.** This task only
  wires the already-existing `Department.HeadPositionId` column to the API.
- **No roles, permissions, access templates, or user assignments** were created or modified.
  Verified: no `Role`/`Permission`/`AccessTemplate`/`UserId`/`Employee`-named fields appear in any
  Department request contract or command (see `DepartmentPart3ArchitectureTests.DepartmentContractsAndCommands_ExposeNoRoleOrAccessOrUserAssignmentFields`).
- **No migrations were created.** `git status` confirms the only changes under
  `src/ONEVO.Infrastructure/Migrations/` are pre-existing, untracked files from earlier sessions
  (Position Foundation, dated 2026-08-03/04) — nothing new or modified by this task. The
  `head_position_id` column, its index, and its `Restrict` FK to `positions.id` already existed
  from Department Part 2A (`AddDepartmentHeadPositionId` migration) and were left untouched.
- **No frontend or OneVo-HR docs work** was done, per task constraints.
- `departments.head_position_id`'s FK remains `DeleteBehavior.Restrict` — unchanged, unweakened.
- No `?? Guid.Empty` fallback was introduced anywhere for `HeadPositionId`; nullable Guid is
  threaded through as `Guid?` end-to-end. Verified by
  `DepartmentPart3ArchitectureTests.DepartmentApplicationLayer_DoesNotUseGuidEmptyFallback_ForHeadPositionId`.
- No `DateTimeOffset.UtcNow` was introduced in the Department Application layer;
  `IDateTimeProvider` is used exclusively (unchanged from prior parts, still guarded by
  `DepartmentPart2BArchitectureTests.DepartmentApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly`,
  which scans the whole `Department` Application folder).

## Can the frontend start after this?

**Yes, for the update flow.** `PUT /api/v1/org/legal-entities/{legalEntityId}/departments/{departmentId}`
now accepts and returns `headPositionId`, with the validation table above as the contract. The
frontend should:
- Only offer head-position assignment on the **Edit Department** screen, not Create (matching the
  journey doc's own recommendation for the Create screen — "Assign an active head position after
  creating the department").
- Source head-position candidates from the department's own position list (no dedicated "eligible
  heads" endpoint exists yet — see Remaining Limitations).
- Always resend the department's current `headPositionId` on any update that doesn't intend to
  change it, since this endpoint is full-replace, not a partial patch.
