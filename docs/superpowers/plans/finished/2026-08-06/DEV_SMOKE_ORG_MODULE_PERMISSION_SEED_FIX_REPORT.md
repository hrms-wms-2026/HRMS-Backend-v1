# Dev-Smoke Tenant Org Module/Permission Seed Fix Report

**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Scope:** Fix `DevSmokeTestTenantSeeder` so the acme/dapi dev-smoke tenants' subscribed module list is never hardcoded/stale, so `org_structure` (and the rest of the canonical Phase 1 product module list) reaches `GET /api/v1/auth/me`'s `active_modules`, and so `org:read`/`org:manage` survive `PermissionResolver`'s active-module gate for the seeded owner.

---

## 1. Root Cause

`DevSmokeTestTenantSeeder.SeedTenantSubscriptionAsync` (in
`src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`) hardcoded

```csharp
SelectedModulesJson = """["integrations","work_management"]""",
```

on **both** the insert path (new `TenantSubscription`) and the update path (existing row), and it ran this way on every backend startup — including against an already-seeded dev database. Any manual DB fix to `tenant_subscriptions.selected_modules` was therefore overwritten back to the stale 2-item literal on the next restart.

This literal predates a prior, separate fix (`PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md`, 2026-08-04) that corrected the **`starter_51_200` subscription *plan*'s** `included_modules_json` to the canonical 17-key Phase 1 product module list (migration `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules`, confirmed applied on the local dev DB — see §6). That earlier fix never touched `DevSmokeTestTenantSeeder`, which sets the **per-tenant subscription's** `selected_modules` independently of the plan, via its own hardcoded literal — so the dev-smoke tenants kept reading the pre-Aug-4 stale value regardless of the plan-level fix.

### Downstream effect (traced, not assumed)

- `ModuleEntitlementService.GetActiveModuleKeysForTenantAsync` (`src/ONEVO.Infrastructure/Services/SharedPlatform/ModuleEntitlement/ModuleEntitlementService.cs:43-71`) reads `active_modules` **directly from `TenantSubscription.SelectedModulesJson`** — not from the plan. So `/auth/me`'s `active_modules` was exactly `["integrations","work_management"]`, missing `org_structure`.
- `PermissionResolver.ResolveAsync` (`src/ONEVO.Infrastructure/Security/PermissionResolver.cs:64-70`) filters every granted `RolePermission` row by `activeModules.Contains(row.Module)` for any user who isn't holding the `"*"` bypass code. The dev-smoke Tenant Owner role is deliberately **not** given `"*"` (see `DevSmokeTestTenantSeeder.ResolveRolePermissionsAsync`, which explicitly excludes it, mirroring `DefaultRoleSeeder`'s production Owner). So even though the Owner role already had `org:read`/`org:manage` granted as `RolePermission` rows (they're in the "every non-`*` permission" set), those two codes were being silently stripped from the effective permission set at request time because `org_structure` wasn't in `activeModules`.

One hardcoded literal was therefore the root cause of **both** symptoms reported (`active_modules` missing `org_structure`, and no `org:*` permissions on `/auth/me`) — no second bug.

Everything else in the required architecture was already correct and needed no change:
- `PlatformBaselineModules.Keys = ["auth", "configuration", "roles", "notifications"]` (`src/ONEVO.Application/Features/Auth/Permission/Helpers/PlatformBaselineModules.cs`) already keeps these four out of subscribed product modules.
- `org:read`/`org:manage` are already owned by module `"org_structure"` in `PermissionSeeder` (confirmed by `OrgPermissionSeedTests.cs`).
- HR Manager (`org:read`, `org:manage`) and Work Manager (`org:read`) explicit permission code lists were already correct.

## 2. Files Changed

| File | Change |
|---|---|
| `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` | `SeedTenantSubscriptionAsync`: both the insert-path and update-path `SelectedModulesJson` assignments changed from the hardcoded `["integrations","work_management"]` literal to `plan.IncludedModulesJson ?? "[]"` — deriving from the `starter_51_200` plan row already loaded in that method. This is the exact pattern already used by production tenant creation (`CreateTenantCommandHandler.cs:160`: `SelectedModulesJson = plan.IncludedModulesJson ?? "[]"`), so dev-smoke tenants now stay in sync with whatever the plan's canonical module list is, self-correcting on every restart instead of re-pinning a stale value. |
| `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs` | Added a `SeedSubscriptionPlanAsync` helper (seeds a `starter_51_200` `SubscriptionPlan` row with the canonical 17-module `IncludedModulesJson`, called from `RunSeederAsync`) and 4 new tests (see §4). |

No other files were touched. Nothing in `Hrms--Web-application---front-end---v1`, OneVo-HR docs, Postman files, or unrelated auth/session/legal/entity/department/position code was modified.

## 3. Before/After `/auth/me` Payload

**Before (reported live symptom, matches the stale `SelectedModulesJson` literal):**
```json
{
  "active_modules": ["integrations", "work_management"],
  "permissions": [/* ... no org:read / org:manage ... */]
}
```

**After — verified live**, not just asserted: the backend was restarted (see §6), and a real login → session-exchange → `GET /api/v1/auth/me` flow was run against the dev-smoke Acme owner (`siyasiyamala932@gmail.com`) on the local dev DB. Actual response:

```json
{
  "authenticated": true,
  "active_modules": [
    "org_structure", "core_hr", "leave", "calendar", "time_attendance",
    "activity_monitoring", "discrepancy_engine", "identity_verification",
    "exception_engine", "productivity_analytics", "desktop_agent_gateway",
    "worksync_foundation", "projects", "objectives_milestones", "tasks",
    "boards", "planning_sprints"
  ],
  "permissions": [
    "... , "org:manage", "org:read", "roles:manage", "roles:read", ...
  ]
}
```

`active_modules` contains `org_structure`; `permissions` contains both `org:read` and `org:manage`. Frontend Organization nav gating (`org_structure` + `org:read`/`org:manage`) is satisfied.

## 4. Tests Added

All in `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`:

1. `SeedAsync_AcmeAndDapiSubscriptionsMatchTheCanonicalPhase1ModuleList` — both acme and dapi tenant subscriptions' `SelectedModulesJson` deserialize to exactly the canonical 17-module list, contain `org_structure`, and never contain `auth`/`configuration`/`roles`/`notifications` (platform-baseline keys must never be subscribed product modules).
2. `SeedAsync_CorrectsAStaleSelectedModulesJsonAlreadyPersistedInTheDevDatabase` — seeds normally, hand-plants the old stale `["integrations","work_management"]` literal directly onto the persisted `TenantSubscription` row (simulating a dev DB seeded before this fix), reruns the seeder, and asserts the row is corrected back to the canonical list on rerun. This is the idempotent self-healing behavior requirement (#7 in the task) and directly reproduces + fixes the reported symptom.
3. `SeedAsync_AcmeOwnerHasOrgReadAndOrgManagePermissionCodes` — explicit assertion that the seeded Acme Owner's granted `RolePermission` codes include `org:read` and `org:manage` (previously only covered implicitly via the "all non-`*` permissions" count test).
4. `SeedAsync_DoesNotDuplicateTenantSubscriptionsAcrossRepeatedRuns` — after two full seeder runs, exactly one `TenantSubscription` row exists per tenant (acme, dapi).

"No duplicate users/employees/legal entities are created" (task requirement #8) is already covered by pre-existing tests in this file (`SeedAsync_IsIdempotentAcrossTenantsUsersAndRoles`, `SeedAsync_IsIdempotentAcrossRepeatedRunsForEmployees`, `SeedAsync_AcmeHasExactlyThreeLegalEntitiesAfterRepeatedSeeding`, `SeedAsync_DapiHasExactlyOneLegalEntityAfterRepeatedSeeding`) — these were not new but were re-verified green after this change.

## 5. Tests Run

- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` → **Build succeeded, 0 errors.**
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal` → **1410/1410 passed** (includes the 4 new tests).
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal` → **531/531 passed.**
- Focused integration test: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ApiBootTests"` → **2/2 passed** (boots the real API host end-to-end, including the `DevSmokeTestTenantSeeder` hosted service, against a real Postgres/Testcontainers instance — confirms the seeder runs cleanly with the fix under real startup conditions, not just the SQLite unit-test harness). No dev-smoke-specific `/auth/me` integration test exists in the suite (`TenantProvisioningE2ETests` covers freshly-provisioned tenants, not the dev-smoke seeder), so live verification was done directly instead (§3, §6).
- `git diff --check` → clean (only benign LF→CRLF line-ending warnings, no whitespace errors).

## 6. Live Verification Performed

Beyond the required commands, the fix was verified end-to-end against the real local dev database:

1. Confirmed via `dotnet ef migrations list` (using the `onevo_migrator` credentials from `.env`) that migration `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules` **is already applied** to the dev DB — i.e. `subscription_plans.included_modules_json` for `starter_51_200` already holds the canonical 17-key list, so this fix's `plan.IncludedModulesJson` reference resolves correctly rather than pulling forward a still-old plan row.
2. Stopped the already-running `ONEVO.Api` dev process (PID 23536) — it was holding a file lock on `ONEVO.Infrastructure.dll` that blocked the `dotnet build`/`dotnet test` verification steps above (with explicit user approval).
3. Restarted the backend in Development mode. Startup log confirmed `Development smoke-test tenants seeded: acme, dapi` and an actual `UPDATE tenant_subscriptions SET selected_modules = ...` statement executing against the existing Acme subscription row.
4. Ran a real login → session-exchange → `GET /api/v1/auth/me` flow against `siyasiyamala932@gmail.com` (Acme dev-smoke owner) directly against the running backend. Result shown in §3 — `active_modules` and `permissions` both correct.
5. **The backend was left running** (Development mode, `https://localhost:7202`) after this verification so the user can complete step 5 of the task's manual verification checklist (reload the frontend and confirm Organization appears in the sidebar) without needing to restart it again.

## 7. Remaining Risks

1. **Two unrelated EF migrations are pending** on the local dev DB: `20260805045300_AddActivityMonitoring` and `20260805090249_RemoveLegacyEmployeeJobTitleAndManagerFields` (confirmed via `dotnet ef migrations list`). Neither relates to subscription modules, `org_structure`, or permissions, and the backend started and seeded successfully without them applied — but they are outstanding schema drift. Not fixed here (out of this task's explicit scope), flagged for awareness.
2. **`GetAssignablePermissionsForTenantAsync`** (the tenant-admin "which permissions can I assign to a custom role" catalog) still filters strictly by the tenant's subscribed product-module list and does not get the `PlatformBaselineModules` treatment `PermissionResolver` gets — this was already a known, separately-flagged gap from the prior Aug-4 plan-module report, unrelated to and unaffected by this fix.
3. **`PlanId` on the update path is not refreshed.** `SeedTenantSubscriptionAsync`'s update branch doesn't reassign `subscription.PlanId = plan.Id` if it somehow drifted from the current `starter_51_200` plan row's `Id`. Not a currently-observed problem (the seeder always resolves the plan by the same `Code`, and `PlanId` was already out of this fix's scope), but noted for completeness since `SelectedModulesJson` and `PlanId` are logically coupled.
4. This fix only affects the **dev-smoke seeder path**. It does not retroactively repair any *other* tenant's `tenant_subscriptions.selected_modules` that might have been seeded stale by a different code path; scope was intentionally limited to `DevSmokeTestTenantSeeder.cs` per the task.

No commits were made; all changes are in the working tree only.
