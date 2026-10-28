# Position Management Coverage — Level Exclusivity Validation Audit

## 1. Current broken rule

**No backend correctness defect reproduced.** The reported symptom — "backend shows a conflict error, but the UI still lets the user pick Primary Manager when automatic reporting coverage already claims it" — traces to two separate things, and only one of them is a backend concern:

- The **backend already enforces** the exclusivity rule described in the task, including automatic-vs-manual conflicts, before this audit started. Evidence:
  - `HasActiveCoverageConflictAsync` ([EfPositionRepository.cs:383-411](src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs#L383-L411)) filters only on `TenantId`, `LegalEntityId`, `CoveredTargetType`, `CoveredPositionId`/`CoveredDepartmentId`, `OwnerOrder`, and `Status == "active"`. It has **no `Source` predicate and no `OwnerPositionId` predicate**, so a locked, automatically-generated `ReportingStructure` row and a manual row from a different owner both count as "active coverage" for the same slot.
  - `AddManualCoverageRecordCommandHandler` ([AddManualCoverageRecordCommandHandler.cs:82-92](src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommandHandler.cs#L82-L92)) and `UpdateManualCoverageRecordCommandHandler` ([UpdateManualCoverageRecordCommandHandler.cs:66-77](src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommandHandler.cs#L66-L77)) both call it and return 409 on conflict.
  - Self-coverage is rejected in `AddManualCoverageRecordCommandHandler.cs:58-59` (`OwnerPositionId == CoveredPositionId` → 409, message "A position cannot cover itself.").
  - Three partial unique indexes (one per target type: Position/Department/Company), scoped by tenant + legal entity + covered target + `OwnerOrder`, filtered to `status = 'active'`, back this at the DB level ([ManagementCoverageRecordConfiguration.cs:52-71](src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/ManagementCoverageRecordConfiguration.cs#L52-L71)), with Postgres unique-violations translated to a 409 `CoverageOrderConflictException` as a race-safety net.
  - `OwnerOrder` has no upper bound in the validator ([AddManualCoverageRecordCommandValidator.cs:21-22](src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommandValidator.cs#L21-L22)) — Backup N for any N is already supported.
- The **frontend gap the user described is real but out of scope**: the Add-coverage form doesn't pre-filter the "responsibility level" dropdown against automatic coverage that belongs to a *different* owner position (it only excludes orders already used by *this* owner's own records). That is a UI affordance problem, not a validation-correctness problem — the backend still rejects the submission with a 409 either way. Per the task's working-directory scope ("Work only in `HRMS-Backend-v1`… Do not edit frontend"), this was not touched.

This backend logic was already in place before this audit started, evidently from prior same-day work recorded in `POSITION_MANAGEMENT_COVERAGE_UPDATE_LEVEL_REPORT.md` (uncommitted, already present in this working tree). This audit independently re-verified every claim above by reading the code (not by trusting that report), then closed the one real gap it found: **`HasActiveCoverageConflictAsync` had zero tests that actually execute the query** — every existing test replaced it with a Moq stub, so "automatic coverage counts as active" was true by inspection only, never proven by a running test. See §5/§8.

## 2. Final exclusivity rule (as implemented)

For a given `(tenant_id, legal_entity_id, covered_target_type, covered_target_id)`:

- Exactly one row with `status = 'active'` may exist per `owner_order`, regardless of `source` (`Manual` or `ReportingStructure`) and regardless of `owner_position_id`.
- `owner_order = 1` is "Primary Manager"; `owner_order = N` (N ≥ 2) is "Backup Manager `N-1`" — unbounded.
- A manual record may keep its own current `(target, owner_order)` on update; it may not move to a slot occupied by another active record (manual or automatic).
- `owner_position_id == covered_position_id` is always rejected (self-coverage), independent of the exclusivity check.
- `Company` target type is supported by the same mechanism (`covered_position_id` and `covered_department_id` both `NULL`), scoped tenant + legal-entity + `owner_order` only, though the frontend does not currently expose it as a selectable target (pre-existing, documented deferral in the prior report).

## 3. Automatic coverage: stored, not computed

**Automatic ("reporting-structure") coverage is a real, persisted row in `management_coverage_records`**, not something computed on the fly from `Position.ReportsToPositionId`:

- Written by `CreatePositionCommandHandler` when a new position has a `ReportsToPositionId` ([CreatePositionCommandHandler.cs:119-135](src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandHandler.cs#L119-L135)): `OwnerOrder = 1`, `Source = "ReportingStructure"`, `IsLocked = true`, `Status = "active"`.
- Kept in sync by `UpdatePositionCommandHandler` when `ReportsToPositionId` changes ([UpdatePositionCommandHandler.cs:143-192](src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs#L143-L192)): the old locked row is removed, a new one is added for the new manager — **unless** a manual record already occupies order 1 for that covered position, in which case the automatic sync row is silently skipped (documented in-code, see §7 "remaining risks" — this is a pre-existing design decision, not something this audit changed).

Because it's a real row in the same table with the same `status = 'active'` semantics, `HasActiveCoverageConflictAsync`'s ordinary `Source`-agnostic query already sees it. **The DB partial unique indexes do protect this case too** (they're not scoped by `Source` either) — so both the app-level pre-check and the DB constraint cover automatic-vs-manual conflicts.

## 4. DB/index changes

**None.** The three existing partial unique indexes already match the spec exactly:

| Index | Scope | Filter |
|---|---|---|
| `ix_management_coverage_records_active_position_order` | `(tenant_id, legal_entity_id, covered_position_id, owner_order)` | `covered_target_type = 'Position' AND status = 'active'` |
| `ix_management_coverage_records_active_department_order` | `(tenant_id, legal_entity_id, covered_department_id, owner_order)` | `covered_target_type = 'Department' AND status = 'active'` |
| `ix_management_coverage_records_active_company_order` | `(tenant_id, legal_entity_id, owner_order)` | `covered_target_type = 'Company' AND status = 'active'` |

Verified two ways:
1. `ManagementCoverageRecordConfiguration.cs:52-71` (EF model config) and `ApplicationDbContextModelSnapshot.cs:4753-4770` (the checked-in snapshot) declare byte-for-byte identical index names, columns, and filters — no drift between model and snapshot for this entity.
2. The 10 pre-existing Postgres-backed integration tests under `PositionsIntegrationTests` (filter `Coverage`) passed against a real Testcontainers Postgres instance (§5), which only happens if the indexes as migrated actually match what the running code expects.

`dotnet ef migrations has-pending-model-changes` itself could not be run (design-time factory requires a live local Postgres connection string per `ops/postgres/setup-local-db.ps1`, which was not set up in this session) — see §6 skipped checks. The two checks above are the substitute evidence.

## 5. Error messages (item 6)

Refined from a single generic string to level-specific wording, per the task's suggested phrasing, in both `AddManualCoverageRecordCommandHandler` and `UpdateManualCoverageRecordCommandHandler`:

- Pre-check conflict (`HasActiveCoverageConflictAsync` returns true): `"This target already has a Primary Manager."` / `"This target already has a Backup Manager {n}."`
- DB race fallback (`CoverageOrderConflictException` catch): previously returned the exception's own generic message (`"An active coverage record already exists…"`, baked into `EfPositionRepository.cs:446-448` from the constraint name, which has no idea which level was requested). Now each handler discards `ex.Message` and re-derives the same level-specific string from `request.OwnerOrder`, so the message is identical regardless of whether the conflict was caught by the pre-check or the DB constraint.
- Self-coverage: `"A position cannot cover itself."` — already matched the suggested wording, untouched.
- No technical constraint/index names are exposed in any path (verified by reading every `Result.Conflict(...)` call site under the coverage handlers).

New helper: `ManagementCoverageRecordMapper.ResponsibilityLevelLabel(int ownerOrder)` — mirrors the frontend's `formatResponsibilityLabel` convention (order 1 → "Primary Manager", order N → "Backup Manager N-1").

## 6. Tests run

| Command | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal` | **Build succeeded**, 0 warnings, 0 errors |
| `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Coverage"` | **32 passed**, 0 failed |
| `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore` (full suite) | **1982 passed**, 0 failed |
| `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --filter "FullyQualifiedName~Coverage"` | **6 passed**, 0 failed |
| `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore` (full suite) | **571 passed, 3 failed** — all 3 failures (`PositionPart2A_DoesNotExpose_Commands_Queries_Or_RequestContracts`, `PositionPart2C_Introduces_ExactlyOnePositionsController_InExpectedNamespace`, `PositionsController_IntroducedInPart2C_IsTheOnlyPositionController`) are caused by the pre-existing, uncommitted `PositionTemplatePacksController`/`PositionTemplatePacks` feature already in this working tree before this task started (unrelated Position Template Packs work — untracked in `git status` at the start of this session). Not touched by this task. |
| `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~PositionsIntegrationTests&FullyQualifiedName~Coverage"` | **10 passed**, 0 failed, against a real PostgreSQL Testcontainers instance (Docker was available in this session) |
| `git diff --check` (on all files touched by this audit) | Clean — only line-ending (LF→CRLF) notices, no whitespace-conflict errors |

One environment blocker encountered and resolved with user approval: a leftover `ONEVO.Api.exe` process (PID 55268) from an earlier session held a file lock on the build output, failing `dotnet build`/`dotnet test` for the Api and test projects with `MSB3027`. Asked the user how to proceed; approved stopping the process. Terminated it (`taskkill /PID 55268 /F`) and all subsequent builds/tests ran clean.

### Skipped checks

- `dotnet ef migrations has-pending-model-changes` — requires a live local Postgres connection (`MigrationConnection`, set up via `ops/postgres/setup-local-db.ps1`), which was not provisioned in this session. Substituted with a manual snapshot-vs-configuration diff (§4) plus the fact that the real-Postgres integration tests passed, which would not happen if the checked-in migrations didn't match the current model for this entity.
- Frontend tests/build — out of scope per task instructions ("Do not edit frontend").
- Full integration suite — only the coverage-filtered subset was run; the full suite includes unrelated in-progress features (legal entity, position template packs, onboarding, etc.) already dirty in this working tree and was not the target of this audit.

## 7. Tests added

`tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs` — 5 new tests that run the real EF query behind `HasActiveCoverageConflictAsync` (via `UseInMemoryDatabase`, not a Moq stub), closing the gap identified in §1:

- `HasActiveCoverageConflictAsync_ReturnsTrue_WhenAutomaticReportingStructureRecordOccupiesSameOrder` — a locked `Source = ReportingStructure` row blocks a new claim at the same order. This is the one the task's problem statement is specifically about.
- `HasActiveCoverageConflictAsync_ReturnsTrue_WhenDifferentOwnerClaimsSameTargetAndOrder` — exclusivity is per covered target, not per owner.
- `HasActiveCoverageConflictAsync_ReturnsFalse_WhenRequestedOrderIsDifferentFromTakenOrder` — Backup Manager 2 (order 3) is allowed while Backup Manager 1 (order 2) is taken; different levels don't conflict.
- `HasActiveCoverageConflictAsync_ExcludesGivenRecordId_SoEditingItsOwnOrderIsNotAConflict` — `excludingRecordId` correctly excludes the record being edited.
- `HasActiveCoverageConflictAsync_IgnoresRecords_FromOtherTenantOrOtherLegalEntity` — cross-tenant and cross-legal-entity rows (with the same `covered_position_id` by coincidence) never conflict.

Everything else in the task's required test list was already covered by existing tests before this audit (all passing, all still passing after the message-text change since none of them assert on message content, only status code):

| Required scenario | Existing test |
|---|---|
| Manual Primary rejected — another manual Primary exists | `AddManualCoverage_ReturnsConflict_WhenActiveRecordAlreadyClaimsSameOrderForTarget` (handler-level, mocked) + new repo-level test above |
| Same owner cannot duplicate same target/level | same as above |
| Different owner cannot duplicate same target/level | new repo-level test above (not previously proven at the query level) |
| Editing own record without changing target/level succeeds | `UpdateManualCoverage_AllowsSubmittingTheRecordsOwnCurrentOwnerOrder` |
| Editing record to occupied target/level fails | `UpdateManualCoverage_RejectsWithConflict_WhenNewOrderAlreadyUsedByAnotherRecord` |
| Self-coverage fails | `AddManualCoverage_ReturnsConflict_WhenCoveredPositionIsOwnerItself` |
| Conflict returns 409 | all conflict tests assert `result.StatusCode == 409` |
| Cross-tenant/legal-entity isolation | new repo-level test above; also structurally guaranteed by `HasActiveCoverageConflictAsync`'s `TenantId`/`LegalEntityId` predicates and by the partial unique indexes both being tenant/legal-entity scoped |
| Backup Manager 2 allowed when Backup 1 exists / Backup 3 allowed | new repo-level test above; validator has no upper bound (`AddManualCoverageRecordCommandValidator.cs:21-22`) |

## 8. Files changed

- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/AddManualCoverageRecord/AddManualCoverageRecordCommandHandler.cs` — level-specific conflict message on both the pre-check and the DB-race fallback path
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdateManualCoverageRecord/UpdateManualCoverageRecordCommandHandler.cs` — same
- `src/ONEVO.Application/Features/OrgStructure/Position/Mappers/ManagementCoverageRecordMapper.cs` — new `ResponsibilityLevelLabel(int ownerOrder)` helper
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs` — 5 new repository-level tests for `HasActiveCoverageConflictAsync`

No migration, no DB schema change, no frontend change.

## 9. Remaining risks

- **Silent skip on `UpdatePosition`**: if a manual record already claims Primary Manager (order 1) for a covered position, changing that position's `ReportsToPositionId` updates the reporting relationship but silently does *not* create the automatic coverage row for the new manager ([UpdatePositionCommandHandler.cs:154-192](src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs#L154-L192), explicitly commented as intentional). This is a pre-existing design decision, not covered by this task's spec, and was deliberately left untouched — changing it would alter position-update semantics beyond the scope of a coverage-validation audit. Flagging it here because it means "who is the Primary Manager" can silently diverge from "who does this position report to" when a manual override exists.
- **Frontend option-availability gap** (§1): the Add-coverage form does not yet filter out responsibility levels already claimed by another owner's automatic coverage. Functionally safe (backend rejects with 409), but a UX rough edge — out of scope per this task's "do not edit frontend" instruction.
- **`dotnet ef migrations has-pending-model-changes` not run** (§6) — mitigated by the snapshot diff and passing real-Postgres integration tests, but not a substitute for the actual CLI check if a local Postgres environment becomes available later.
- The 3 pre-existing architecture-test failures and the general dirtiness of this working tree (Legal Entity, Position Template Packs, and other in-progress features already uncommitted before this task started) are unrelated to Management Coverage and were left untouched, per instructions not to commit or push.

No files were committed or pushed, per instructions.
