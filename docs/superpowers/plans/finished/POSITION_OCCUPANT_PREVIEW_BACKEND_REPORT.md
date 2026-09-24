# Position Occupant Preview — Backend Report

Scope: `HRMS-Backend-v1` only. No frontend changes. No database migration (all new data is
computed at query time from existing tables).

## Response contract

`PositionListItemResponse` (`GET /api/v1/org/legal-entities/{legalEntityId}/positions`) and
`PositionTreeNodeResponse` (`GET /api/v1/org/legal-entities/{legalEntityId}/positions/tree`) each
gained three fields:

```jsonc
{
  // ...existing fields (id, name, maxOccupancy, etc.) unchanged...
  "assignedCount": 2,
  "occupantPreview": [
    {
      "employeeId": "uuid",
      "displayName": "Jane Smith",
      "initials": "JS",
      "avatarFileId": "uuid-or-null",
      "avatarUrl": null
    }
  ],
  "remainingAssignedCount": 0
}
```

- `assignedCount` — total active **primary** assignments for the position (see "Assignment
  source" below for why primary-only).
- `occupantPreview` — the first `assignedCount` employees, capped at a hard backend limit of
  **4** (`PositionMapper.OccupantPreviewLimit`), ordered by `EffectiveFrom` then assignment `Id`.
- `remainingAssignedCount` = `assignedCount - occupantPreview.Count`.
- `maxOccupancy` is unchanged (already existed); the frontend computes empty seats as
  `maxOccupancy - assignedCount`.
- `initials` falls back to `"?"` if both first and last name are blank (defensive; should not
  happen in practice).

**`CurrentOccupancy` / `CurrentOccupancyCheckSupported` were deliberately left untouched** (still
always `null` / `false`) on `PositionListItemResponse`, even though real data is now available and
computing it would have been free alongside `assignedCount`. Reason: `PositionResponse` (returned
by `GET /positions/{id}`, via `GetPositionByIdQueryHandler`) was out of scope for this task and
still reports the same pair as unsupported. Populating only the list endpoint would make the same
position report contradictory occupancy-support between two endpoints for the same tenant.
`assignedCount` / `occupantPreview` / `remainingAssignedCount` are the real, populated successor
fields. **Recommend a follow-up pass** that retires `CurrentOccupancy`/`CurrentOccupancyCheckSupported`
everywhere, including `PositionResponse` and `PositionArchiveBlockers.ActiveOccupants` (not
touched here).

## Assignment source

`assignedCount`/`occupantPreview` come only from `position_assignments` rows where:
- `tenant_id` matches the server-derived current tenant (and the joined `employees.tenant_id`
  as well, redundantly, as defense in depth),
- `assignment_kind == PrimaryEmployment` (excludes `AdditionalAuthority`),
- `assignment_status == Active` (excludes `ended`, `cancelled`, `planned`).

No inference from `users`, position hierarchy, reporting manager, management coverage, or seed
shortcuts. `EffectiveFrom`/`EffectiveTo` are **not** filtered — this mirrors every existing query
in the codebase (`GetActivePrimaryAsync`, `CountActiveAsync`, `EfEmployeeRepository.ListVisibleAsync`),
all of which treat `AssignmentStatus` as authoritative and ignore the date window. Adding a date
filter here would have made this count disagree with the rest of the codebase for no product
benefit.

### Capacity rule (resolved): `CountActiveAsync` now counts active primary assignments only

A follow-up task required resolving the divergence flagged in the original version of this
section: `CountActiveAsync` (used by `ApproveAccessGrantRequestCommandHandler` and
`FinalizeOnboardingDraftCommandHandler` to enforce `max_occupancy`) counted **all** active
assignments regardless of kind, while `assignedCount`/`occupantPreview` were primary-only.

**Final rule: position capacity counts active `PrimaryEmployment` assignments only.
`AdditionalAuthority` does not consume a seat.** `EfPositionAssignmentRepository.CountActiveAsync`
now filters `AssignmentKind == PrimaryEmployment` in addition to the existing tenant/position/
`AssignmentStatus == Active` filters — the same filter `GetOccupancyPreviewsAsync` already used.
`assignedCount` and capacity enforcement are now guaranteed to match for every position; the
frontend can safely compute available seats as `maxOccupancy - assignedCount` with no separate
`availableSeatCount`/`occupiedCountForCapacity` field needed.

**Evidence used to choose this rule over "keep capacity as-is, add distinct fields":**
- `positions` is documented in `phase1-table-inventory.md` as "First-class org seats defining the
  reporting hierarchy" and the seeded-reference-tables note says "Position is the canonical
  seat/job model" — the seat/capacity concept is tied to the position's primary occupancy, not to
  secondary authority grants.
- `position_assignments` has a **database-level** partial unique index,
  `ix_position_assignments_one_active_primary_per_employee`, filtered on
  `assignment_kind = 'PrimaryEmployment' AND assignment_status = 'active'` — only
  `PrimaryEmployment` is structurally seat-constrained at the schema level; `AdditionalAuthority`
  has no equivalent constraint, because it isn't a seat placement.
- `GetActivePrimaryAsync`/`HasActivePrimaryInLegalEntityAsync` (pre-existing, unmodified by this
  feature) already define "is this employee currently seated in a position" as primary-only —
  this is the established pattern elsewhere in the codebase, not a new convention invented here.
- `PositionAssignmentKind.AdditionalAuthority` has **zero creation call sites** anywhere in `src`
  or `tests` (confirmed by repo-wide search) — nothing today actually grants additional authority,
  so this change cannot alter behavior for any live flow; it's a correctness alignment between two
  read paths that were both already primary-only in intent (`GetOccupancyPreviewsAsync`) or by
  omission-of-a-filter bug (`CountActiveAsync`), not a semantics flip.

**Both capacity-enforcing handlers use the same rule by construction, not by convention**: neither
`FinalizeOnboardingDraftCommandHandler.cs:206` nor
`ApproveAccessGrantRequestCommandHandler.cs:198` contains any assignment-counting logic of its
own — both call `_positionAssignmentRepository.CountActiveAsync(tenantId, position.Id, ct)`
directly and compare the result to `position.MaxOccupancy`. There is one capacity rule, defined
once in `EfPositionAssignmentRepository.CountActiveAsync`; both flows inherit it automatically.
`PositionOccupantPreviewArchitectureTests.CapacityEnforcingHandler_OnlyCountsAssignmentsThrough_CountActiveAsync`
guards against either handler growing a second, divergent counting path in the future.

## Avatar / file handling decision

No file-serving/download endpoint exists anywhere in this codebase today (confirmed: no
`FilesController` or equivalent under `src/ONEVO.Api/Controllers`, no download/read-URL generation
service). Per the corrected instructions, `occupantPreview[].avatarFileId` passes through
`employees.avatar_file_id` (`Guid?`) as-is; `avatarUrl` is **always `null`**. No `file_records`
table is queried at all for this feature — there's nothing safe to derive from it yet, so it's
simply not touched. `raw file_records.storage_key` is never exposed; enforced by
`PositionOccupantPreviewArchitectureTests.OccupantPreviewTypes_NeverExposeStorageKey` (reflection
check across the new response/model types).

Frontend should render initials/gray placeholders until a tenant-authenticated avatar-serving
endpoint exists; when it does, only `PositionOccupantPreviewResponse.AvatarUrl` needs to change
(the contract shape already has the field).

## Query batching approach

`IPositionAssignmentRepository.GetOccupancyPreviewsAsync(tenantId, positionIds, previewLimit, ct)`
(new method) is called once per request in both `ListPositionsQueryHandler` and
`GetPositionTreeQueryHandler`, after the position page/tree is already fetched, with all
returned position IDs collected into one collection. The EF implementation
(`EfPositionAssignmentRepository`) does a single query: join `PositionAssignments` to `Employees`,
filter by tenant + kind + status + `positionIds.Contains(...)`, order by
`(PositionId, EffectiveFrom, Id)`, materialize with `AsNoTracking().ToListAsync()`, then group by
`PositionId` and `Take(previewLimit)` per group **in memory**. This is one DB round trip
regardless of how many positions are in the result set (no N+1), and avoids relying on
per-group `TOP`/window-function SQL translation, which isn't reliably portable across EF
providers. `PositionTreeMapper.BuildTree` takes the resulting dictionary as an optional second
parameter (defaults to empty) so the 3 pre-existing single-argument call sites in
`PositionTreeMapperTests` kept working unchanged.

## Tenant/security

- Tenant is server-derived from `ICurrentUser.TenantId` in both handlers, exactly as before —
  no controller or query change; `PositionsControllerArchitectureTests.NoAction_AcceptsTenantIdParameter`
  (pre-existing, unmodified) already covers this and still passes.
- `GetOccupancyPreviewsAsync` filters by `tenantId` on both the assignment and the joined employee
  row.
- All new queries use `AsNoTracking()`.

## Tests run

```
dotnet build src/ONEVO.Application/ONEVO.Application.csproj        -> succeeded, 0 errors
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj  -> succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release          -> succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release -> succeeded, 0 errors

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --no-build --filter "FullyQualifiedName~Position"
  -> Passed: 188, Failed: 0   (186 from the original feature + 2 new: CountActiveAsync excludes
     AdditionalAuthority, CountActiveAsync matches GetOccupancyPreviewsAsync.AssignedCount)

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --no-build --filter "FullyQualifiedName~Onboarding"
  -> Passed: 120, Failed: 0   (full onboarding suite, including FinalizeOnboardingDraft/
     ApproveAccessGrantRequest handler tests that mock CountActiveAsync - run to confirm the
     CountActiveAsync filter change didn't regress any existing onboarding scenario)

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release --no-build --filter "FullyQualifiedName~PositionOccupantPreview"
  -> Passed: 9, Failed: 0    (7 from the original feature + 2 new: capacity-enforcing handlers
     only count assignments through CountActiveAsync)

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release --no-build --filter "FullyQualifiedName~Position"
  -> Passed: 124, Failed: 3 (pre-existing, unrelated - see below; unchanged from before this task)

git diff --check -> only CRLF/LF normalization notices (repo .gitattributes), no real whitespace errors
```

Note: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` (Debug config) failed with `MSB3021`/`MSB3027`
file-lock errors copying `ONEVO.Application.dll`/`ONEVO.Infrastructure.dll` into `ONEVO.Api`'s
Debug output — a **live `ONEVO.Api.exe` process (PID 46328) already running** on this machine has
those DLLs open. This is a pre-existing environment condition, not a compile error (no `CS####`
errors were reported for `ONEVO.Api`; only the debug-output copy step failed). I did not stop that
process. Building/testing in `-c Release` (a separate output directory) sidesteps the lock and
compiles cleanly, which is how all builds/tests above were run. If a definitive Debug-config
build is needed, stop the running `ONEVO.Api.exe` process first.

The 3 pre-existing architecture-test failures
(`PositionPart2BArchitectureTests.PositionsController_IntroducedInPart2C_IsTheOnlyPositionController`,
`PositionPart2AArchitectureTests.PositionPart2A_DoesNotExpose_Commands_Queries_Or_RequestContracts`,
`PositionPart2AArchitectureTests.PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace`)
are caused by an unrelated, already-uncommitted `PositionTemplatePacksController` in the working
tree (present before this task started — see `git status` at session start). None of them
reference anything this change touches; verified by running only the new
`PositionOccupantPreviewArchitectureTests` (all pass) and by inspecting the failure output, which
names only `PositionTemplatePacksController`/`PositionTemplatePacks` types.

New/changed test coverage (all in `ONEVO.Tests.Unit` / `ONEVO.Tests.Architecture`):
- `EfPositionAssignmentRepositoryTests` — 7 tests for `GetOccupancyPreviewsAsync` (from the
  original feature): empty input, position with no assignments, one active assignment (fields
  returned correctly), ended/`AdditionalAuthority` assignments excluded, preview capped at limit
  while `assignedCount` stays full, multiple positions batched correctly in one call, cross-tenant
  assignment not leaked. Plus 2 new tests for this follow-up: `CountActiveAsync` excludes an
  active `AdditionalAuthority` assignment on the same position, and `CountActiveAsync` matches
  `GetOccupancyPreviewsAsync(...)[positionId].AssignedCount` for identical seeded data (one
  active primary, one ended primary, one active additional-authority) — the regression guard
  tying capacity enforcement and the occupant preview together.
- `PositionMapperTests` (new file) — display name/initials computation, `remainingAssignedCount`
  math, missing-from-dictionary defaults, blank-name fallback to `"?"`.
- `PositionTreeMapperTests` — 2 new tests: default (no-occupancy-argument) shape, and occupancy
  propagated to the correct node only.
- `ListPositionsQueryHandlerTests` / `GetPositionTreeQueryHandlerTests` — updated for the new
  constructor dependency; 1 new test each proving the preview fields flow from the batched
  repository call into the response.
- `PositionsControllerTests` — fixed a direct `PositionTreeNodeResponse` construction to supply
  the 3 new positional arguments.
- `PositionOccupantPreviewArchitectureTests` — 7 tests from the original feature (no `StorageKey`
  property on any of the new/touched response or model types, exact property-set check on
  `PositionOccupantPreviewResponse`, presence of the 4 contract fields on both list and tree
  responses), plus 2 new tests for this follow-up: `FinalizeOnboardingDraftCommandHandler.cs` and
  `ApproveAccessGrantRequestCommandHandler.cs` each call `CountActiveAsync` for capacity and
  contain no other `PositionAssignments` reference — guards against either handler growing a
  second, divergent counting path that would silently break the assignedCount/capacity match
  proven above.

## Skipped checks

- **Integration tests** (`PositionsIntegrationTests`, Testcontainers/Postgres/full HTTP pipeline)
  were **not** added or run for this feature. The task's own "Run:" list only calls for
  `dotnet build` + focused unit tests + focused architecture tests + `git diff --check`, and the
  spec's required scenarios (no-assignment, one-assignment, avatar mapping, exclusions, preview
  cap, `remainingAssignedCount`, list/tree shape, cross-tenant isolation, no `tenantId` param) are
  all covered at the unit level instead, using the same `EF InMemory` pattern the existing
  `EfPositionAssignmentRepositoryTests` already use. I did fix the stale class-doc comment on
  `PositionsIntegrationTests` (it claimed `position_assignments` doesn't exist in the schema,
  which is no longer true) to point at the unit-level coverage instead of leaving it misleading.
  A follow-up could add real HTTP-level `List`/`Tree` occupant-preview scenarios there if desired.
- **EF InMemory vs. real Npgsql translation**: the batched query (join → `positionIds.Contains(...)`
  → `orderby` → anonymous-type projection → `ToListAsync`, with all grouping/`Take` done after
  materialization) is written in the same shape `EfEmployeeRepository` documents as the *safe*
  pattern for Postgres translation (see that file's comment on why constructor-projection
  mid-query breaks translation). `dotnet build` confirms it compiles; it was not run against a
  real PostgreSQL instance (no Testcontainers run in this task).
- `dotnet build` on `ONEVO.Api.csproj` in **Debug** configuration — blocked by the already-running
  `ONEVO.Api.exe` process holding Debug-output DLLs open (see Tests run section above). Verified
  clean via Release configuration instead, which exercises the same source and produced 0 errors.

## Remaining risks

1. **Resolved this pass**: `assignedCount` and capacity enforcement (`CountActiveAsync`) now use
   the identical rule (active `PrimaryEmployment` assignments only) — see "Capacity rule
   (resolved)" above. No remaining divergence. If a future feature introduces the first real
   `AdditionalAuthority` assignment, re-verify this decision against how that feature actually
   uses the kind (this report's evidence is that nothing creates one today).
2. **`CurrentOccupancy`/`CurrentOccupancyCheckSupported` are now stale by construction** — real
   data (`assignedCount`) sits right next to a hardcoded `(null, false)` pair on the same DTO.
   Left as-is deliberately (see contract section) to avoid inconsistent occupancy-support
   reporting between `GET /positions` and `GET /positions/{id}`. Needs a dedicated cleanup pass
   across both endpoints plus `PositionArchiveBlockers.ActiveOccupants`.
3. No avatar-serving endpoint exists yet, so `avatarUrl` is always `null` today — expected and
   called out in the corrected instructions, not a defect.
4. This repo currently has substantial **unrelated uncommitted WIP** already in the working tree
   (`PositionTemplatePacksController`, `AddPositionCodeMaxLength` migration, Create/UpdatePosition
   command changes, etc. — present at session start, not touched by this task). The 3 pre-existing
   architecture-test failures come from that WIP, not from this change; they'll need to be
   resolved independently of this feature before the branch is otherwise clean.

Nothing was committed or pushed, per instructions.
