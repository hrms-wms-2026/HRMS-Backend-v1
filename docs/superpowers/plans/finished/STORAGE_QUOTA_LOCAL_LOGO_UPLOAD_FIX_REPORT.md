# Storage Quota Local/Dev Logo Upload Fix Report

**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Scope:** Fix local/dev company logo upload being blocked by `storage_not_entitled` after Cloudflare R2 was configured.

---

## 1. Root Cause

`storage_not_entitled` is returned by `StorageQuotaService.GetTenantStorageLimitAsync`
(`src/ONEVO.Infrastructure/Services/Storage/Quota/StorageQuotaService.cs:43-85`), which resolves
a tenant's storage allowance in this order:

1. Look up the tenant's active subscription (`ITenantSubscriptionRepository.GetLatestActiveByTenantIdAsync`).
2. If found, sum the storage contributed by its selected modules via
   `ModuleStorageAllowanceCalculator.CalculateTotalBytes`, which reads each module's
   `module_catalog.storage_reference` JSON brackets and only counts a module if
   `is_storage_consuming = true`.
3. If that sum is `0`, fall back to `StorageQuota:DefaultStorageLimitGb` (`StorageQuotaOptions`).
4. If neither resolves, **deny** with `storage_not_entitled` (403) — by design, Phase 1 never
   treats an unresolvable limit as "unlimited."

Traced against the local dev seed data (`DevSmokeTestTenantSeeder.cs`) for both `acme` and `dapi`:

- **Step 1 passes.** The seeder creates a `tenant_subscriptions` row with `Status = "trialing"`
  (`DevSmokeTestTenantSeeder.cs:787`). `"trialing"` **is** in
  `SubscriptionStatusRules.ActiveStatuses` (`active`, `trialing`, `maintenance_included`,
  `subscription_included`), so `GetLatestActiveByTenantIdAsync` finds it. Confirmed with a new
  test — see §4.
- **`SelectedModulesJson` is populated.** It is copied from the `starter_51_200` plan's
  `IncludedModulesJson` (17 canonical Phase 1 module keys — `org_structure`, `core_hr`, `leave`,
  `calendar`, `time_attendance`, `activity_monitoring`, etc.), re-derived on every startup
  (`SeedTenantSubscriptionAsync`, `DevSmokeTestTenantSeeder.cs:746-794`).
- **Step 2 resolves to zero, every time, for every tenant.** 16 of the 17 selected module keys
  exist as rows in `module_catalog` (14 inserted by migration
  `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs`, the rest pre-existing via
  `ModuleCatalogSeeder.cs`; only `leave` has no matching row — `ModuleCatalogSeeder` seeds
  `time_off` instead, a pre-existing naming split documented as out of scope in
  `PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md` §7/§11.4). But **every** row in
  `module_catalog`, in every migration and seeder that writes one, has `storage_reference = "[]"`
  and no row anywhere sets `is_storage_consuming = true` — verified by grepping all migrations and
  seeders for `is_storage_consuming`; every match is a schema column-name mapping
  (`.HasColumnName("is_storage_consuming")`) in an EF snapshot/Designer file, never a data seed
  setting it `true`. `ModuleStorageAllowanceCalculator.ResolveStorageGigabytes` returns `0` for an
  empty bracket array (`ModuleStorageAllowanceCalculator.cs:59-96`), so the module-key mismatch on
  `leave` is moot — the sum is `0` regardless.

  This is a **known, previously-flagged gap**, not new: `PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md`
  Remaining Risk #5 explicitly records that the 14 newly-added `module_catalog` rows carry
  "placeholder pricing (`storage_reference = "[]"`) ... since this task's scope was module
  *membership*, not Phase 1 commercial pricing for the 14 new keys, which was never provided."
  A later integration test file independently rediscovered and documented the same gap:
  `LegalEntitiesIntegrationTests.cs:991-1001` grants `core_hr` a real `storage_reference` directly
  in its test database purely so its own logo-upload tests can exercise the real quota path,
  with the comment: *"No module_catalog row carries a real storage_reference in the production
  seed data yet ... so any tenant is storage_not_entitled by default."*

- **Step 3 also resolves to nothing.** `StorageQuota:DefaultStorageLimitGb` is not present in
  `appsettings.Development.json` (nor any `appsettings*.json`) before this fix, so
  `StorageQuotaOptions.DefaultStorageLimitGb` binds to `null`.
- **Step 4 fires: `storage_not_entitled`, 403**, for every tenant, for every upload purpose
  (including `company_logo`), independent of which company size or which modules are selected.

**This bug is not new and not caused by the local database being "out of date."** It has existed
since module membership was corrected in `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs`
(2026-08-04) and was already flagged as a known gap. Wiring up Cloudflare R2 didn't introduce it —
it simply made it possible to reach the code path (`FileStorageService.UploadAsync` →
`BeginReservationAsync` → `_quota.ReserveStorageAsync`) that was blocked on `storage_not_entitled`
before any bytes ever reach the object store, so the failure is the first thing anyone doing a real
end-to-end upload test would see.

## 2. Selected Fix — Option B (local-development-only default)

Added to `src/ONEVO.Api/appsettings.Development.json`:

```json
"StorageQuota": {
  "DefaultStorageLimitGb": 5
},
```

`StorageQuotaOptions.DefaultStorageLimitGb`'s own doc comment already defines exactly this use
case: *"Fallback allowance ... used only when the tenant's active subscription contributes no
storage."* DI wiring for `StorageQuotaOptions` already existed
(`DependencyInjection.cs:381-382`, `services.Configure<StorageQuotaOptions>(...)`), so this is a
config-only change — no code, no migration, no dependency wiring touched.

**Option C (populate real `storage_gb` values in `module_catalog`) was rejected.** There are no
documented per-module, per-company-size storage figures anywhere in the repo. The only storage
figures that appear in project docs (`ONEVO_Backend_Architecture_Document.md:1193`,
`phase1-table-inventory.md`'s `subscription_plan_modules.storage_contribution_gb`) describe a
**different, unbuilt** mechanism (`feature_limits_json`/`plan_storage_bytes`,
`subscription_plan_modules`) — not the `module_catalog.storage_reference` bracket mechanism that
is actually implemented and read by `ModuleStorageAllowanceCalculator`. Inventing bracket values
for the 14 canonical modules would mean shipping fabricated commercial data into a migration that
production would also pick up — exactly the "not priced for sale yet" gap the prior report
deliberately left open. That's a product/commercial decision outside this task's scope.

**Option A did not apply.** The dev-smoke subscription/module seed itself is correct: an active
(`trialing`) subscription with the full canonical module list is created for both `acme` and
`dapi` on every startup, self-healing any stale persisted value
(`SeedTenantSubscriptionAsync`, comment at `DevSmokeTestTenantSeeder.cs:788-791`). There is no
seeder bug to fix — the gap is entirely in `module_catalog`'s storage data, and there are no
canonical values available to seed it with (see rejection of Option C above).

## 3. Why R2 Configuration Was Not The Cause

`FileStorageService.UploadAsync` (`FileStorageService.cs:292-356`) reserves quota **before** any
object storage call:

1. `BeginReservationAsync` → `_quota.ReserveStorageAsync(tenantId, fileSizeBytes, ct)`
   (`FileStorageService.cs:67-71`) — this is where `storage_not_entitled` is raised and returned,
   short-circuiting the method.
2. Only after that succeeds does the method buffer/hash the file and call
   `_objectStorage.PutObjectAsync` (step 3, `FileStorageService.cs:337-351`).

`storage_not_entitled` is emitted exclusively by `StorageQuotaService.GetTenantStorageLimitAsync`
(step 5, `StorageQuotaService.cs:81-84`), which reads `tenant_subscriptions`, `module_catalog`,
and `StorageQuotaOptions` — it never touches `IObjectStorageAdapter`, R2 credentials, or any
platform service key. R2 being newly configured only made the upload flow reachable end-to-end;
it has no code path into quota resolution.

## 4. Tests

### Already covered (no duplication added)
- `Limit_ComesFromActiveSubscriptionModules`, `Limit_FallsBackToPlatformDefault_WhenNoSubscription`,
  `Limit_FallsBackToPlatformDefault_WhenModulesContributeNoStorage` (dev-default-set case),
  `Ensure_Returns403_WhenNotEntitled` — `tests/ONEVO.Tests.Unit/Features/Storage/StorageQuotaServiceTests.cs`.

### Added
- `StorageQuotaServiceTests.Limit_Denied_WhenSubscriptionModulesContributeZeroAndNoDefaultConfigured`
  — reproduces the exact bug shape: active subscription, modules present in the catalog, zero
  contribution, no configured default → 403 `storage_not_entitled`.
- `FileStorageServiceTests.BeginReservationAsync_NotEntitled_PropagatesStorageNotEntitledWith403`
  (`tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs`) — proves
  `storage_not_entitled`/403 propagates unmodified through `FileStorageService.BeginReservationAsync`
  for a `company_logo` upload, not just a generic failure. Required extending
  `FakeStorageQuotaService` (`tests/ONEVO.Tests.Unit/Fakes/FakeStorageQuotaService.cs`) with
  `ReserveFailureError`/`ReserveFailureStatusCode` so the fake can simulate an arbitrary
  `Result.Failure`, not only the hardcoded `storage_quota_exceeded`/409 it previously always
  returned on failure (existing `BeginReservationAsync_QuotaExceeded_PreventsReservation` test
  behavior is unchanged — its defaults reproduce the old hardcoded values exactly).
- `DevSmokeTestTenantSeederTests.SeedAsync_AcmeAndDapiSubscriptionsMatchTheCanonicalPhase1ModuleList`
  extended with an assertion that both seeded subscriptions' `Status` is a member of
  `SubscriptionStatusRules.ActiveStatuses` — guards the "trialing counts as active" fact this
  root-cause analysis depends on, so a future change to either the seeder's hardcoded status
  literal or the active-status list can't silently flip which resolution step actually fails.

## 5. Verification

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj                     → Build succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj      → Build succeeded, 0 errors (pre-existing warnings only)
dotnet build tests/ONEVO.Tests.Architecture/...csproj            → Build succeeded, 0 errors

dotnet test tests/ONEVO.Tests.Unit --filter
  "StorageQuotaServiceTests|FileStorageServiceTests|DevSmokeTestTenantSeederTests|ModuleStorageAllowanceCalculatorTests"
  → 75/75 passed

dotnet test tests/ONEVO.Tests.Unit (full suite)                  → 1984/1984 passed

dotnet test tests/ONEVO.Tests.Architecture (full suite)          → 571/574 passed, 3 pre-existing
  failures in PositionPart2AArchitectureTests / PositionPart2BArchitectureTests
  (PositionTemplatePacksController-related). Verified pre-existing and unrelated: reran the same
  filtered set with `git stash` (my changes fully removed) — identical 3 failures, same assertion
  messages. Not touched by this fix; nothing in Position/PositionTemplatePacks was read or edited.

git diff --check                                                 → clean (only a benign CRLF/LF
  line-ending notice from git, no reported whitespace errors)
```

## 6. Skipped Checks

- **Integration tests were not run.** `StorageQuotaIntegrationTests.cs` and
  `LegalEntitiesIntegrationTests.cs` require Docker/Testcontainers, which was not exercised in this
  session. This is a config-only change confined to `appsettings.Development.json`, which
  integration tests never load: `IntegrationTestEnvironmentScope.cs` sets
  `ASPNETCORE_ENVIRONMENT=Test`, and no `appsettings.Test.json` exists, so only base
  `appsettings.json` (no `StorageQuota` section) plus the explicit environment-variable allowlist
  in `IntegrationTestEnvironmentScope.ManagedKeys` (which does not include any `StorageQuota` key)
  are loaded. `StorageQuotaIntegrationTests.cs` also constructs `StorageQuotaOptions` directly in
  code (`Options.Create(new StorageQuotaOptions { DefaultStorageLimitGb = null })`,
  `StorageQuotaIntegrationTests.cs:183-188`), bypassing configuration entirely. Confirmed by static
  inspection that this change cannot affect either integration test file's assertions.

## 7. Remaining Risks

1. **This fixes local/dev only.** Production and staging still resolve no subscription-derived
   storage allowance (every `module_catalog` row's `storage_reference` is still `"[]"`) and will
   still return `storage_not_entitled` (403) for every tenant's first storage-consuming request,
   including company logo uploads, until real commercial per-module storage figures are decided
   and seeded. This is the same gap flagged as Remaining Risk #5 in
   `docs/superpowers/plans/finished/2026-08-03/PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md`
   — this fix does not close it, it only unblocks local development against it.
2. **`StorageQuota:DefaultStorageLimitGb: 5` in `appsettings.Development.json` must never be
   copied into `appsettings.json` or any non-Development config file** — doing so would silently
   grant every tenant everywhere 5 GB regardless of subscription, masking the real entitlement gap
   in risk #1 rather than surfacing it.
3. The pre-existing `leave` vs `time_off` module-key naming split (documented in the prior
   reconciliation report, §7/§11.4) means even after real storage figures are eventually seeded,
   a `leave`-only contribution would still resolve to zero unless that naming split is also
   reconciled — out of scope here, flagged for whoever picks up risk #1.
