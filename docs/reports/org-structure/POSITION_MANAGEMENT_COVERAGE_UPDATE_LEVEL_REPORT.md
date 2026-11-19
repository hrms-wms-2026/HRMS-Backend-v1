# Position Management Coverage — Edit-Level Support & UX Redesign

## 1. Root cause

The Management Coverage feature (Position Foundation Part 2C/2D) shipped with:

- A raw database-style table (`Source`, `Lock Status` as primary columns) instead of a task-oriented view.
- No `PUT` endpoint or command to change a manual coverage record's `ownerOrder` after creation — the only path to change a responsibility level was delete + re-add, which loses the record's id/audit trail and briefly leaves the target uncovered.
- A hardcoded three-level (`Primary`/`Backup 1`/`Backup 2`) affordance in the original design intent, though by the time this task started the frontend's `ownerOrderOptions` logic had already been made dynamic (see §9).
- No guard against a position being asked to cover itself.

Most of the backend correctness work (duplicate-order rejection, DB-level partial unique indexes, tenant/legal-entity scoping, locked-record protection on remove) was **already implemented** before this task started. The delta actually needed was: self-coverage rejection, the update endpoint/command/handler, a repository method to persist the mutation, and the frontend UX redesign + edit flow. See §9 for what was pre-existing vs. new.

A repository defect was found and fixed as part of this work: `GetCoverageRecordByIdAsync` returns an `AsNoTracking()` (detached) entity. Mutating `OwnerOrder` on that instance and calling `SaveChangesAsync()` alone would have been a **silent no-op** — nothing would persist. `RemoveManualCoverageRecordCommandHandler` avoided this because `DbSet.Remove()` attaches a detached entity as `Deleted` regardless of tracking state. The new update path required an explicit `IPositionRepository.UpdateCoverageRecord(record)` (mirroring `UpdateReportingHistory`/`UpdateAccessTemplate`), which calls `DbSet.Update()` to attach the entity as `Modified`. This is verified by an integration test that updates via `PUT` and then re-reads via a fresh `GET` call (not the same in-memory instance).

## 2. Backend rule table

| Rule | Enforced by | Status |
|---|---|---|
| `PUT /api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/coverage/{coverageId}`, body `{ ownerOrder }` only | `PositionsController.UpdateCoverage`, `UpdateCoverageRecordRequest` | New |
| Tenant resolved from current user only; legalEntityId/positionId from route | `UpdateManualCoverageRecordCommandHandler` | New |
| `coverageId` must exist, belong to the same tenant/legal entity/owner position | `GetCoverageRecordByIdAsync` + explicit checks → 404 | New |
| Record must be `Source == Manual` and `IsLocked == false` | Explicit check → 409 (same reporting-structure wording as remove) | New |
| `ownerOrder >= 1` | `UpdateManualCoverageRecordCommandValidator` | New |
| No duplicate active `ownerOrder` for the same covered target (current record's own order is allowed) | `HasActiveCoverageConflictAsync(..., excludingRecordId: record.Id)` → 409; `CoverageOrderConflictException` catch as race fallback | New (reuses existing repo method) |
| Covered target / owner position / source / lock state / status cannot change via this command | Command only carries `ownerOrder`; handler mutates only `OwnerOrder` + `UpdatedAt` | New |
| `UpdatedAt` set via `IDateTimeProvider`, never `DateTimeOffset.UtcNow` | `UpdateManualCoverageRecordCommandHandler` | New |
| Self-coverage rejected (`CoveredTargetType == Position && CoveredPositionId == owner.Id`) | `AddManualCoverageRecordCommandHandler` → 409 | New |
| Duplicate active `ownerOrder` on **add** rejected | `HasActiveCoverageConflictAsync` | Pre-existing |
| Covered position/department must be active | `AddManualCoverageRecordCommandHandler` | Pre-existing |
| Same tenant / same legal entity | `GetByIdForLegalEntityAsync` scoping throughout | Pre-existing |
| `Source = Manual`, `IsLocked = false` fixed server-side on add | `AddManualCoverageRecordCommandHandler` | Pre-existing |
| Locked/reporting-structure records rejected on remove | `RemoveManualCoverageRecordCommandHandler` → 409 | Pre-existing |
| DB-level partial unique index per covered target × `ownerOrder` (Position, Department, Company) where `status = 'active'` | `ManagementCoverageRecordConfiguration` + migration `20260808075909_AddManagementCoverageRecordUniqueOrderIndexes` | Pre-existing |

## 3. Frontend behavior table

| Behavior | Where | Status |
|---|---|---|
| Modal shows "Automatic reporting coverage" (locked, read-only cards, `Automatic` badge, no actions) and "Manual coverage" (`Manual` badge, `Edit level` / `Remove`) sections instead of a table | `position-coverage-modal.component.html/css` | New (redesign) |
| Subtitle: "Controls what this position can manage for visibility and approval routing." | same | New |
| Manual-section empty state: "No manual coverage rules added." | same | New |
| "Add manual coverage" button; form collapsed by default | `showAddForm` control (already defaulted `false`) | Pre-existing default; button copy updated |
| `formatResponsibilityLabel` (1 → Primary Manager, n → Backup Manager n-1) | `position-coverage-modal.component.ts` | Pre-existing |
| Dynamic `ownerOrderOptions` for **add** (excludes orders already used for the selected target, always offers next order, unlimited backups) | same, refactored into `usedOrdersForTarget` + `ownerOrderOptionsFor` helpers | Pre-existing logic, refactored (no behavior change) for reuse by edit |
| Dynamic `editOwnerOrderOptions(record)` for **edit** — same rule, but excludes the record itself so its current order stays offered | `position-coverage-modal.component.ts` | New |
| Inline "Edit level" per manual record → select + Save/Cancel → emits `updateCoverage` → `PositionStore.updateCoverage` → `PUT` | component + store + `position-management.component.ts` | New |
| Owner position excluded from the covered-position dropdown when adding | `positionOptions` getter | Pre-existing |
| Legacy self-coverage rows (from old data) shown read-only with a warning badge, no Edit/Remove | `isSelfCoverage()` check in template | New |
| Company target not exposed in the UI | `AddCoverageRecordRequest`/model union stays `'Position' \| 'Department'` | Deferred by design (§6) |

## 4. DB uniqueness decision

No new migration was needed. The partial unique indexes required by the spec already existed (added in a prior, uncommitted change to this working tree — migration `20260808075909_AddManagementCoverageRecordUniqueOrderIndexes`):

- `ix_management_coverage_records_active_position_order` — unique `(tenant_id, legal_entity_id, covered_position_id, owner_order)` where `covered_target_type = 'Position' AND status = 'active'`
- `ix_management_coverage_records_active_department_order` — unique `(tenant_id, legal_entity_id, covered_department_id, owner_order)` where `covered_target_type = 'Department' AND status = 'active'`
- `ix_management_coverage_records_active_company_order` — unique `(tenant_id, legal_entity_id, owner_order)` where `covered_target_type = 'Company' AND status = 'active'`

`EfPositionRepository.SaveChangesAsync` translates a Postgres unique-violation against any of these three index names into `CoverageOrderConflictException`, which both the add and (new) update handlers catch and turn into a 409 — the authoritative race-safety net behind the `HasActiveCoverageConflictAsync` pre-check. RLS was not touched. Company target is DB- and validator-supported but intentionally not exposed by the frontend (see §6).

## 5. Exact tests run and counts

### Backend

| Command | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal` | **Build succeeded**, 0 errors |
| `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal` | **1462 passed**, 0 failed (includes 6 new tests: self-coverage rejection on add, 4 update-handler tests, 1 update-validator test) |
| `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal` | **542 passed, 1 failed** (`LegalEntityGeneralSettingsArchitectureTests.LegalEntity_HasExactlyTheInventoryColumns` — pre-existing failure from unrelated in-progress Legal Entity work already in this working tree, not touched by this task). Isolated re-run of the 5 new `PositionCoverageUpdateArchitectureTests` → **5/5 passed** |
| `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~PositionsIntegrationTests&FullyQualifiedName~Coverage"` | **10 passed, 0 failed** (8 pre-existing coverage tests + 2 new: `UpdateCoverage_ChangesOwnerOrder_AndPersists`, `UpdateCoverage_LockedReportingStructureRecord_Returns409_AndIsNotChanged`) against a real PostgreSQL Testcontainers instance |

No EF migration was added (§4), so no `dotnet ef migrations script` step was needed.

### Frontend

| Command | Result |
|---|---|
| `npm test -- --watch=false` (full suite) | **299 passed, 21 failed** across the whole app. All 21 failures are in files this task did not touch (`landing-page`, `landing-header`, `side-navbar`, `top-navbar`, `organization.routes` — pre-existing failures from other in-progress work in this working tree, several tied to an `NG04002` route-matching error unrelated to coverage) |
| Isolated re-run of `position-coverage-modal.component.spec.ts` + `position-api.service.spec.ts` + `position.store.spec.ts` via `ng test --include=...` | **21/21 passed** (18 in the modal spec — 7 pre-existing + 11 new; 1 new in the api-service spec; 2 new in the store spec) |
| `npm run build` | **Succeeded** (only pre-existing-style CSS budget warnings, including a new 863-byte-over warning on the coverage modal's own CSS — consistent with several other components already over budget in this codebase) |
| `npm run build:staging` | **Succeeded** |
| `rg` for `Backup Manager 2` / fixed 3-option arrays under `src/app/modules/organization` | **No matches** — confirmed absent (this logic was already made dynamic before this task started; nothing to remove) |

## 6. Remaining limitations

- **Company target is deferred in the UI.** The backend validator, repository conflict-check, and DB unique index all support `CoveredTargetType == "Company"`, but the frontend's `AddCoverageRecordRequest`/`ManagementCoverageRecordResponse` types and the Add form only expose `Position`/`Department`, per the spec's explicit permission to defer it. No frontend changes are needed to add Company support later — the backend is ready.
- **Legacy self-coverage rows.** If pre-existing data contains a manual record where a position covers itself, it renders read-only with a "Self-coverage" warning badge in the Manual section (no Edit/Remove). New self-coverage can never be created (add-form excludes the owner from `positionOptions`; the add handler now also rejects it server-side with 409), but no backfill/cleanup of any existing legacy rows was performed — out of scope for this task.
- **Live manual check (spec step 10) was started but not completed by an automated browser pass.** Both dev servers were started and confirmed healthy (backend `https://localhost:7229` with `acme`/`dapi` dev tenants seeded; frontend `https://localhost:4200`), but the Chrome browser-automation extension was not connected in this environment, so the click-through (open Position Management → Coverage modal → add/edit/duplicate-reject/backup-beyond-3) was not performed by the agent. Both servers were intentionally left running per the user's direction so this can be done manually.
- The pre-existing architecture-test failure (`LegalEntity_HasExactlyTheInventoryColumns`) and the 21 pre-existing frontend test failures are from other in-progress work already present in this working tree before this task began; they are unrelated to Management Coverage and were left untouched.

## 7. Files changed

### Backend (`HRMS-Backend-v1`)

- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommandHandler.cs` — self-coverage rejection (409)
- `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs` — `UpdateCoverageRecord(record)` method
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs` — `UpdateCoverageRecord` implementation
- `src/ONEVO.Api/Contracts/OrgStructure/Positions/UpdateCoverageRecordRequest.cs` — **new**, `{ ownerOrder }` only
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs` — new `PUT {positionId}/coverage/{coverageId}` action
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommand.cs` — **new**
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommandValidator.cs` — **new**
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommandHandler.cs` — **new**
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/PositionCoverageHandlerTests.cs` — 6 new tests
- `tests/ONEVO.Tests.Architecture/PositionCoverageUpdateArchitectureTests.cs` — **new**, 5 tests
- `tests/ONEVO.Tests.Integration/OrgStructure/Position/PositionsIntegrationTests.cs` — 2 new integration tests

### Frontend (`Hrms--Web-application---front-end---v1`)

- `src/app/modules/organization/models/position.model.ts` — `UpdateCoverageRecordRequest` interface
- `src/app/modules/organization/data-access/position-api.service.ts` — `updateCoverage(...)` method
- `src/app/modules/organization/data-access/position-api.service.spec.ts` — **new**
- `src/app/modules/organization/state/position.store.ts` — `updateCoverage(...)` method
- `src/app/modules/organization/state/position.store.spec.ts` — **new**
- `src/app/modules/organization/ui/position-coverage-modal/position-coverage-modal.component.ts` — automatic/manual split, edit-level flow, self-coverage detection, parameterized order-options helper
- `src/app/modules/organization/ui/position-coverage-modal/position-coverage-modal.component.html` — table → card-section redesign
- `src/app/modules/organization/ui/position-coverage-modal/position-coverage-modal.component.css` — table styles → card/section styles
- `src/app/modules/organization/ui/position-coverage-modal/position-coverage-modal.component.spec.ts` — 11 new tests
- `src/app/modules/organization/feature/position-management/position-management.component.ts` — `onUpdateCoverage(...)` handler
- `src/app/modules/organization/feature/position-management/position-management.component.html` — `(updateCoverage)` binding

No files were committed or pushed, per instructions.
