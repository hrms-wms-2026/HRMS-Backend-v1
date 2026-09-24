# Employee Onboarding Dev Seat Policy Fix Report

## Root cause

`DevSmokeTestTenantSeeder.SeedTenantSubscriptionAsync` (in
[src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs](src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs))
creates and updates the Acme/Dapi `tenant_subscriptions` row without ever setting
`IncludedSeats` or `OverageAllowed`. Both columns are nullable (added by migration
`20260810154000_AddTenantSeatPolicyContract`), so they were left `null` on both the
create branch and the update branch.

`SeatEntitlementService.EvaluateAsync`
([src/ONEVO.Infrastructure/Services/CoreHr/SeatEntitlement/SeatEntitlementService.cs:47](src/ONEVO.Infrastructure/Services/CoreHr/SeatEntitlement/SeatEntitlementService.cs#L47))
treats a subscription with `IncludedSeats is null || OverageAllowed is null` as
`SeatDecisionStatus.Undetermined` — deliberately, since it must never infer seat
capacity from anything but an explicit billing policy.

Two call sites turn `Undetermined` into a block on the actual user-facing flows:
- `SaveOnboardingDraftCommandHandler` stamps the draft `Status = Draft`,
  `DraftReason = "seat_configuration_required"` on every save while the tenant's
  subscription is `Undetermined` — this is the literal string surfaced to the
  frontend and referenced in the task description.
- `FinalizeOnboardingDraftCommandHandler` (line 279-283) and
  `ApproveAccessGrantRequestCommandHandler` (line 208-210) independently re-check the
  same `Undetermined` status at finalize/approve time and return
  `UnprocessableEntity` with a prose message (no message key) — finalization was
  blocked for the same underlying reason even after the save-time symptom.

Both surfaces trace back to the same root cause: the dev smoke seeder never
populated a seat policy for Acme/Dapi.

Note on `SeatEntitlementService`'s employee count: despite the field name
`ActiveEmployeeCount`, the query at line 30 counts **all** `Employees` rows for the
tenant with no status filter — there is no "active" filter in this codebase today.
This did not change and is not part of this fix; it's called out here only so the
seeded headroom below is understood correctly (raw employee row count, not a
status-filtered count).

## Fix

`SeedTenantSubscriptionAsync` now sets an explicit, local-dev-only seat policy:

- **`IncludedSeats: 25`** — Acme seeds 3 employees, Dapi seeds 1; 25 leaves generous
  headroom for manual smoke testing (creating/onboarding several more employees)
  without hitting the cap. This is not a production default — it exists only because
  billing has not defined a real seat policy for these fixed-GUID smoke tenants, and
  the seeder needs *some* explicit value so `SeatEntitlementService` never returns
  `Undetermined` for them.
- **`OverageAllowed: false`** — no product decision exists to allow local dev to
  exceed included seats, so this defaults to the conservative/explicit value per the
  task's constraint ("do not make production defaults... unless product explicitly
  defines them" applied the same way here: don't invent permissive behavior either).

Two constants (`DevSmokeIncludedSeats = 25`, `DevSmokeOverageAllowed = false`) were
added near the other smoke-test-only literals in the seeder.

**Create branch:** the new `TenantSubscription` object now sets both fields directly.

**Update branch (idempotency / self-heal for existing dev databases):** the fix uses
null-coalescing assignment —
```csharp
subscription.IncludedSeats ??= DevSmokeIncludedSeats;
subscription.OverageAllowed ??= DevSmokeOverageAllowed;
```
This only fills in a missing policy; it never overwrites a non-null value someone
(or a future seeder revision) already set on the row. This matters in practice: any
developer's existing local Acme/Dapi database already has a subscription row from
before this fix, with both columns null — only the update-branch `??=` self-heals
that row on the next backend startup. The create branch alone only helps a brand
new database.

Selected-modules seeding logic, `Status`, and every other field on
`SeedTenantSubscriptionAsync` are unchanged.

## Files changed

- [src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs](src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs) —
  added `DevSmokeIncludedSeats`/`DevSmokeOverageAllowed` constants; set both fields on
  create; `??=` both fields on update.
- [tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs](tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs) —
  added 4 new tests (see below).
- [tests/ONEVO.Tests.Unit/Features/CoreHr/SeatEntitlement/SeatEntitlementServiceTests.cs](tests/ONEVO.Tests.Unit/Features/CoreHr/SeatEntitlement/SeatEntitlementServiceTests.cs) —
  added 1 new test mirroring the seeded dev policy.

No other files were touched by this task. `git status` at the start of this session
already showed unrelated pre-existing uncommitted work (`appsettings.Development.json`,
`CloudflareR2ObjectStorageAdapter.cs`, `FakeStorageQuotaService.cs`,
`FileStorageServiceTests.cs`, `StorageQuotaServiceTests.cs`,
`CloudflareR2ObjectStorageAdapterTests.cs`, and two unrelated `.md` reports) — none of
that was modified, read for logic, or relied upon by this fix.

## Tests

### New tests

**`DevSmokeTestTenantSeederTests.cs`:**
1. `SeedAsync_AcmeAndDapiSubscriptionsHaveExplicitDevSeatPolicy` — after a normal
   seed run, both Acme and Dapi subscriptions have `IncludedSeats = 25`,
   `OverageAllowed = false`.
2. `SeedAsync_FillsInMissingSeatPolicy_OnAPreExistingSubscriptionRowFromBeforeThisFix` —
   simulates a dev database seeded by the *pre-fix* seeder (subscription row exists,
   both seat columns manually nulled), re-runs the seeder, and asserts both columns
   are filled in. This is the test that actually proves existing local Acme/Dapi
   databases self-heal, not just fresh ones.
3. `SeedAsync_NeverOverwritesAnExplicitNonNullSeatPolicyAlreadyOnTheRow` — sets a
   custom non-default policy (`IncludedSeats = 3`, `OverageAllowed = true`) on the
   row, re-runs the seeder, and asserts the custom values survive untouched.
4. `SeedAsync_AcmeSubscriptionSeatEntitlementApproves_BecauseSeededHeadroomExceedsSeededEmployees` —
   runs the real `SeatEntitlementService` (not a mock) against the seeded Acme
   tenant and asserts `SeatDecisionStatus.Approved`, closing the loop from seeder
   output to the actual entitlement decision onboarding finalization depends on.

**`SeatEntitlementServiceTests.cs`:**
5. `EvaluateAsync_ReturnsApproved_ForDevSmokeTenantSeatPolicy_WhenEmployeeCountIsBelowIncludedSeats` —
   explicit `IncludedSeats: 25, OverageAllowed: false` (mirroring the seeded dev
   values) with 3 employees; asserts `Approved` with `AvailableSeats == 22`.

### Existing tests confirmed still green (missing-seat-config coverage preserved)

- `SeatEntitlementServiceTests.EvaluateAsync_ReturnsUndetermined_WhenNoSubscriptionRecordExists`
- `SeatEntitlementServiceTests.EvaluateAsync_ReturnsUndetermined_WhenSubscriptionPolicyIsIncomplete`
- `SeatEntitlementServiceTests.EvaluateAsync_ReturnsBlocked_WhenCapacityIsExhaustedAndOverageIsDisabled`
- `FinalizeOnboardingDraftCommandHandlerTests.Handle_CreatesNothing_WhenSeatUndetermined`
- `FinalizeOnboardingDraftCommandHandlerTests.Handle_CreatesNothing_WhenSeatBlocked`

None of these construct data through the seeder — they build `TenantSubscription`/mock
`SeatDecision` objects directly — so the fix does not touch their inputs. All were run
and pass (see below).

### Verification run

- `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj` — succeeded, 0
  errors, 0 warnings.
- `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` — succeeded, 0 errors
  (11 pre-existing warnings, all in files this task did not touch:
  `TenantRlsInterceptorTests.cs`, `GetPositionTreeQueryHandlerTests.cs`,
  `PermissionSeederTests.cs`, `AdminAuthController.cs`, plus one pre-existing NuGet
  advisory on `SQLitePCLRaw.lib.e_sqlite3`).
- `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter
  "FullyQualifiedName~SeatEntitlementServiceTests|FullyQualifiedName~DevSmokeTestTenantSeederTests|FullyQualifiedName~FinalizeOnboardingDraftCommandHandlerTests"` —
  **64/64 passed**.
- `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` (full unit suite) —
  **1994/1994 passed**, 0 failed.
- `git diff --check` on the 3 changed files — clean (one benign LF→CRLF
  line-ending notice from Git, no actual whitespace errors).

## Skipped checks

- **Integration tests** (`ONEVO.Tests.Integration`) were not run. They require
  Docker (`Testcontainers.PostgreSql`) and were out of scope for this fix's
  validation surface — the relevant coverage (seeder + `SeatEntitlementService`
  composition) was instead added as an in-memory-SQLite unit test in
  `DevSmokeTestTenantSeederTests.cs`, following the same pattern the file already
  uses for `SelectedModulesJson` self-healing. One existing integration test,
  `OnboardingDraftsIntegrationTests.Handle_AlwaysResultsInADraftStatus_NeverFinalized`,
  asserts `DraftReason == "seat_configuration_required"` for a tenant with **no**
  subscription row at all (a different tenant than Acme/Dapi, created ad hoc in that
  test's `InitializeAsync`) — this fix does not touch that code path
  (`subscription is null` branch) and that test was not run but should be
  unaffected by inspection.
- Full solution-wide `dotnet build` (no `.sln` file exists in this repo; builds are
  per-project, consistent with prior sessions' notes on this codebase).
- A running `ONEVO.Api.exe` dev server process (PID 26864) was locking
  `Infrastructure.dll`, blocking the Tests.Unit build (it project-references
  `ONEVO.Api`). Stopped with explicit user confirmation before building; the user
  will need to restart their dev server.

## Remaining risks

- **Not a production seat policy.** `IncludedSeats: 25` / `OverageAllowed: false` is
  hardcoded as local-dev-only inside `DevSmokeTestTenantSeeder`, which only runs in
  `Development`/`Test` environments (`StartAsync` early-returns otherwise). No
  production or staging tenant is affected by this change.
- **Self-heal is startup-triggered, not automatic mid-session.** A developer with an
  already-running backend and a pre-fix null-seat subscription row will only get the
  fix applied on the *next* backend startup (the seeder runs in `StartAsync`), not
  by hot-reload. This matches the existing pattern used for the
  `SelectedModulesJson` self-heal already in this seeder.
- **`ActiveEmployeeCount` naming is misleading** (counts all employee rows, not
  status-filtered) — pre-existing behavior, unchanged by this fix, but worth flagging
  since it affects how close to the 25-seat cap a local dev tenant can get during
  heavy smoke testing (e.g. repeated onboarding-then-offboarding cycles that leave
  stale employee rows would count against the cap even if the developer considers
  those employees "inactive").
