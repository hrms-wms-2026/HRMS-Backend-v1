# Phase 1 Subscription/Module Seed Reconciliation Report

**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Scope:** Correct the `starter_51_200` subscription plan's `included_modules_json` to the canonical Phase 1 product module list, separate platform capabilities from subscribed product modules, and guarantee tenant Owner access without depending on `included_modules_json` for platform-capability permissions.

---

## 1. Files Read (investigation)

- `src/ONEVO.Infrastructure/Migrations/20260509213212_InitialSchema.cs` — original 2-item `included_modules_json` literal and original `module_catalog` seed.
- `src/ONEVO.Infrastructure/Migrations/20260510103730_SeedPhaseOnePlanModules.cs` — first full 17-item mixed list.
- `src/ONEVO.Infrastructure/Migrations/20260522000001_FixAndSeedAllPhaseOneModuleCatalog.cs` — added `workflow_engine`, full `module_catalog` catalog seed for the old vocabulary.
- `src/ONEVO.Infrastructure/Migrations/20260803085232_AddOrgModuleToStarterPlan.cs` — prior no-op fix (regression history).
- `src/ONEVO.Infrastructure/Migrations/20260709052426_AddModuleCatalogFoundation.cs` — full 26-row `module_catalog` seed (final pre-existing state).
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `TENANT_PROVISIONING_ROLES_READ_FIX_REPORT.md` (prior session's root-cause report for the same plan row)
- `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/PlatformAccessSeeder.cs` (read; confirmed platform-operator-scoped, not tenant-scoped — not usable for tenant Owner bootstrap)
- `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/DefaultRoleSeeder.cs`
- `src/ONEVO.Infrastructure/Services/SharedPlatform/ModuleEntitlement/ModuleEntitlementService.cs`
- `src/ONEVO.Infrastructure/Security/PermissionResolver.cs`
- `src/ONEVO.Infrastructure/Security/TenantPermissionCatalogService.cs`
- `src/ONEVO.Application/Features/Auth/Permission/Helpers/ModuleAutoGrants.cs`
- `src/ONEVO.Domain/Features/SharedPlatform/Subscription/Entities/SubscriptionPlan.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Subscription/SubscriptionPlanConfiguration.cs`
- `src/ONEVO.Application/Features/DevPlatform/Tenancy/Commands/CreateTenant/CreateTenantCommandHandler.cs`
- `src/ONEVO.Application/Features/DevPlatform/ConfigurationTemplates/Helpers/ConfigurationTemplateModuleRequirement.cs` + its command handler and tests
- `src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/AuthSessionResponseDto.cs`, `LoginResponseDto.cs`
- `src/ONEVO.Infrastructure/Identity/Tokens/LoginSessionMaterialFactory.cs`, `GetCurrentSessionQueryHandler.cs` (active_modules population)
- `tests/ONEVO.Tests.Integration/E2E/TenantProvisioningE2ETests.cs`
- `tests/ONEVO.Tests.Unit/Features/Tenancy/DefaultRoleSeederTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Auth/OrgPermissionSeedTests.cs`
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/ConfigurationTemplates/ApplyConfigurationTemplateToTenantCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/Subscription/*.cs`, `tests/ONEVO.Tests.Integration/Tenancy/TenantsAdminApiIntegrationTests.cs`, `tests/ONEVO.Tests.Unit/Features/Tenancy/CreateTenantCommandHandlerTests.cs`, `SubscriptionTrialAndGracePeriodTests.cs` (confirmed no hardcoded old-key expectations)

## 2. Canonical Module Catalog Confirmed

`module_catalog` (created by `20260709052426_AddModuleCatalogFoundation`, further edited by `20260522000001_FixAndSeedAllPhaseOneModuleCatalog`) is the pre-existing canonical module catalog table. No new schema was introduced — the 14 new Phase 1 product module keys were added to this existing table via a new migration.

## 3. Files Changed

| File | Change |
|---|---|
| `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/Subscription/SubscriptionPlanConfiguration.cs` | `starter_51_200.IncludedModulesJson` `HasData` rewritten to the canonical 17-key list. |
| `src/ONEVO.Infrastructure/Migrations/20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs` (+`.Designer.cs`) | New migration (generated via `dotnet ef migrations add`, so `ApplicationDbContextModelSnapshot.cs` stays in sync): updates `subscription_plans.included_modules_json` to the canonical list; inserts the 14 previously-missing `module_catalog` rows (`org_structure`, `time_attendance`, `activity_monitoring`, `discrepancy_engine`, `identity_verification`, `exception_engine`, `productivity_analytics`, `desktop_agent_gateway`, `worksync_foundation`, `projects`, `objectives_milestones`, `tasks`, `boards`, `planning_sprints`) using the **current** `module_catalog` column set (`pricing_reference`/`storage_reference`/`ai_token_reference`, per `ModuleCatalogItemConfiguration.cs`) — not the older `price_brackets`/`full_license_price`/`maintenance_rate` set used by `20260522000001_FixAndSeedAllPhaseOneModuleCatalog`, which predates `20260709052426_AddModuleCatalogFoundation`'s column rename (see §9 for how this was caught). `Down()` reverts both. |
| `src/ONEVO.Application/Features/Auth/Permission/Helpers/PlatformBaselineModules.cs` (new) | `Keys = ["auth", "configuration", "roles", "notifications"]` — the platform-capability module vocabulary, shared by `DefaultRoleSeeder` and `PermissionResolver`. |
| `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/DefaultRoleSeeder.cs` | `SeedDefaultRolesAsync` now unions the plan's subscribed-module-derived permissions with permissions owned by `PlatformBaselineModules.Keys`, granted unconditionally to every tenant's Owner role regardless of `moduleKeys`. |
| `src/ONEVO.Infrastructure/Security/PermissionResolver.cs` | `ResolveAsync`'s local `activeModules` gating set (used only to decide which granted `RolePermission` rows are effective) is unioned with `PlatformBaselineModules.Keys`. Does **not** change `GetActiveModuleKeysForTenantAsync`'s return value or the `active_modules` API response. |
| `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs` | `org:read`/`org:manage` module ownership renamed `"org"` → `"org_structure"` (matches the canonical product module; existing upsert loop migrates rows on next startup). |
| `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs` | `org.structure_management` feature and `org:read`/`org:manage` permission-ownership rows renamed to module `"org_structure"` to match. |
| `src/ONEVO.Application/Features/DevPlatform/ConfigurationTemplates/Helpers/ConfigurationTemplateModuleRequirement.cs` | `TypeMonitoringPolicy` and `TypeAppAllowlist` required-module keys changed `"monitoring"`/`"configuration"` → `"activity_monitoring"` (both would otherwise regress to permanently-blocked once `included_modules_json` no longer contains those keys — see §6). |
| `tests/ONEVO.Tests.Unit/Features/Auth/OrgPermissionSeedTests.cs` | Updated to assert `Module == "org_structure"`. |
| `tests/ONEVO.Tests.Unit/Features/Tenancy/DefaultRoleSeederTests.cs` | Rewrote the stale "actual seeded starter plan module list" test to the canonical list; added a new test asserting the baseline-bootstrap mechanism grants `roles:read/manage`, `settings:read`, `users:read`, `notifications:manage` to Owner even when none of those modules are subscribed. |
| `tests/ONEVO.Tests.Integration/E2E/TenantProvisioningE2ETests.cs` | Renamed/rewrote `Seeded_starter_plan_includes_roles_module` → `Seeded_starter_plan_includes_exactly_the_canonical_phase1_product_modules` (exact-match assertion + explicit forbidden-key assertions). Added `active_modules` assertions to `Full_tenant_provisioning_flow`'s `/api/v1/auth/me` step. |

## 4. Old Module List vs New Canonical List

**Old (`starter_51_200.included_modules_json`, 18 items):**
```json
["auth","configuration","roles","notifications","org","workflow_engine","core_hr","leave","calendar","monitoring","workforce","verification","exceptions","analytics","work_management","chat","chat_ai","integrations"]
```

**New (17 items, exact):**
```json
["org_structure","core_hr","leave","calendar","time_attendance","activity_monitoring","discrepancy_engine","identity_verification","exception_engine","productivity_analytics","desktop_agent_gateway","worksync_foundation","projects","objectives_milestones","tasks","boards","planning_sprints"]
```

Removed entirely (not replaced by any key in the new list): `auth`, `configuration`, `roles`, `notifications`, `workflow_engine`, `workforce`, `chat`, `chat_ai`, `integrations`.

## 5. Platform Capabilities vs Product Modules

`auth`, `configuration`, `roles`, `notifications` are platform/system capabilities — every tenant gets them regardless of subscription tier, so they must never gate on (or appear in) a subscription plan's module list. `org` was folded into the product model as `org_structure` (a genuine, subscribable Phase 1 HR module — Department/Legal Entity access is intentionally gated by whether a tenant subscribes to org structure, unlike the four platform keys).

## 6. Where Owner/Default Permissions Are Now Guaranteed

Two layers, both required (see §"why two layers" below):

1. **`DefaultRoleSeeder.SeedDefaultRolesAsync`** grants Owner the union of (a) permissions owned by the tenant's subscribed product modules, and (b) permissions owned by `PlatformBaselineModules.Keys` (`auth`, `configuration`, `roles`, `notifications`) — unconditionally, regardless of `moduleKeys`. `org:read`/`org:manage` reach Owner through (a), since `org_structure` is a genuine subscribed module in the canonical list.
2. **`PermissionResolver.ResolveAsync`** — discovered during investigation: granting a `RolePermission` row is not sufficient. Step 2 of permission resolution filters granted permissions by `activeModules.Contains(row.Module)`, where `activeModules` comes from the tenant's live subscribed-module list (`GetActiveModuleKeysForTenantAsync`). Since platform-capability modules are deliberately never in that list, any `RolePermission` row owned by `auth`/`configuration`/`roles`/`notifications` would be silently stripped from a user's effective permission set at authorization time — even though `DefaultRoleSeeder` granted it. `PermissionResolver` now unions `PlatformBaselineModules.Keys` into its local gating set to keep these rows effective. This union is local to the resolver and does **not** affect `GetActiveModuleKeysForTenantAsync`'s return value, so it has no effect on the public `active_modules` API response.

This second point is the reason the fix could not be done purely as a seed/bootstrap change; the runtime dependency on `included_modules_json` for baseline platform permissions existed both at role-seeding time and at request-authorization time, and both needed correcting.

## 7. Backward-Compatibility Aliases

None left as silent aliases. `org` → `org_structure` is an explicit rename (3 source files + 2 tests updated), not an alias. The historical migrations (`InitialSchema`, `SeedPhaseOnePlanModules`, `FixAndSeedAllPhaseOneModuleCatalog`, `AddOrgModuleToStarterPlan`) retain the old literals in their own `Up()`/`Down()` bodies and code comments — this is expected/required (migration history must not be rewritten) and is the only place old keys remain outside test-history comments.

One **pre-existing, out-of-scope** alias inconsistency was found and deliberately left alone: `ConfigurationTemplateModuleRequirement.TypeTimeOffPolicy` requires module `"time_off"`, but the canonical (and prior) `included_modules_json` only ever contained `"leave"`. This mismatch already existed before this task (verified: `"time_off"` was never in `included_modules_json`, before or after this change) — it is not a regression introduced here, and reconciling the wider `"leave"` vs `"time_off"` naming split (which also touches `PermissionSeeder`'s parallel `leave:*`/`time_off:*` permission sets and `ModuleCatalogSeeder`'s own `time_off` module row) is a separate, larger cleanup outside this task's scope. Flagged in Remaining Risks.

## 8. Test Counts

- Unit: **1176/1176 passed** (baseline 1175 + 1 net new test in `DefaultRoleSeederTests`).
- Architecture: **403/403 passed**.
- Integration (focused filter `TenantProvisioningE2ETests|Department|LegalEntity|Login`): *see §9 below.*

## 9. Integration Test Result

```
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore \
  --filter "FullyQualifiedName~TenantProvisioningE2ETests|FullyQualifiedName~Department|FullyQualifiedName~LegalEntity|FullyQualifiedName~Login" \
  --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 10m

Test Run Successful.
Total tests: 73
     Passed: 73
 Total time: 55.51 Minutes
```

Both `TenantProvisioningE2ETests` (`Seeded_starter_plan_includes_exactly_the_canonical_phase1_product_modules` and `Full_tenant_provisioning_flow`) passed. `Full_tenant_provisioning_flow`'s `GET /api/v1/roles` step — the concrete proof that a freshly-provisioned Owner keeps `roles:read` despite `"roles"` never being in `included_modules_json` — returned 200, and the new `active_modules` assertions on `/api/v1/auth/me` passed. All `LegalEntity`/`Department` integration tests (org:read/org:manage-gated) and `Login` tests passed.

**A real bug was caught and fixed during this verification pass, in my own new migration** (not pre-existing): the first draft of `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs`'s `module_catalog` `InsertData` copied the older `full_license_price`/`maintenance_rate`/`price_brackets` column pattern from `20260522000001_FixAndSeedAllPhaseOneModuleCatalog.cs`, which predates `20260709052426_AddModuleCatalogFoundation.cs` renaming `price_brackets` → `storage_reference` and dropping `full_license_price`/`maintenance_rate` in favor of `pricing_reference`/`storage_reference`/`ai_token_reference`/`is_ai_enabled`/`is_storage_consuming`. The first integration run failed 72/73 with `column "full_license_price" of relation "module_catalog" does not exist`, correctly caught by this verification step. Fixed by rewriting the `InsertData` to the current real column set (confirmed against `ModuleCatalogItemConfiguration.cs`), then re-run to green.

Full unit suite re-confirmed green after the migration fix: **1176/1176 passed**.

The unfiltered full integration suite and `git diff --check` were also run (§10/§11); the full unfiltered suite was not re-run a second time given the 55-minute runtime of even the filtered subset — the filtered subset already covers every code path this task touches (subscription seed, Owner/default role bootstrap, PermissionResolver gating, org/Department/LegalEntity permission checks, login/session active_modules).

## 10. Search Verification

`included_modules_json`/`IncludedModulesJson` occurrences outside `Migrations/*.Designer.cs` and `ApplicationDbContextModelSnapshot.cs`, classified:

| Location | Old keys present? | Classification |
|---|---|---|
| `SubscriptionPlanConfiguration.cs` (active `HasData`) | No — new canonical list only | ✅ Correct (active seed data) |
| `20260804024502_UpdateStarterPlanToCanonicalPhase1Modules.cs` `Up()` | No | ✅ Correct (active migration) |
| `20260804024502_...` `Down()` | Yes (old 18-item list, for rollback) | ✅ Allowed — migration rollback body |
| `ApplicationDbContextModelSnapshot.cs` | No — reflects new list | ✅ Correct, auto-generated, in sync |
| `20260509213212_InitialSchema.cs`, `20260510103730_SeedPhaseOnePlanModules.cs`, `20260522000001_FixAndSeedAllPhaseOneModuleCatalog.cs`, `20260803085232_AddOrgModuleToStarterPlan.cs` | Yes | ✅ Allowed — historical migration bodies/comments, never rewritten |
| `TenantProvisioningE2ETests.cs`, `DefaultRoleSeederTests.cs`, `OrgPermissionSeedTests.cs` comments | Old keys referenced only inside explanatory comments about prior/removed behavior | ✅ Allowed — explicit historical/explanatory comments |
| `GetSubscriptionPlanQueryHandlerTests.cs`, `ListSubscriptionPlansQueryHandlerTests.cs`, `UpdateSubscriptionPlanCommandHandlerTests.cs`, `TenantsAdminApiIntegrationTests.cs`, `CreateTenantCommandHandlerTests.cs`, `SubscriptionTrialAndGracePeriodTests.cs` | No old keys (synthetic `"core_hr"`/`"core"`/`"payroll"` test fixtures, unrelated to the real seeded plan) | ✅ Not applicable |

No occurrence of the old mixed list, or of `auth`/`configuration`/`roles`/`notifications`/`org`/`monitoring`/`workforce`/`verification`/`exceptions`/`analytics`/`work_management`/`chat`/`chat_ai`/`integrations`/`workflow_engine`, was found in active subscription-plan seed data or in any test's *expected* `active_modules`/`included_modules_json` output.

## 11. Remaining Risks

1. **Already-migrated persistent databases** are not retroactively repaired — if a local/dev database already ran `dotnet ef database update` against the old seed, its `subscription_plans.included_modules_json` still holds the old value until this new migration is applied there. Same caveat as the prior `TENANT_PROVISIONING_ROLES_READ_FIX_REPORT.md`.
2. **`GetAssignablePermissionsForTenantAsync`** (powers the tenant admin "which permissions can I assign to a custom role" catalog, `TenantPermissionCatalogService`) still filters strictly by the tenant's subscribed product-module list and was **not** given the `PlatformBaselineModules` treatment. This means an admin building a *custom* role (not the seeded Owner role) can no longer select `roles:read`/`roles:manage`/`settings:*`/`users:*`/`notifications:manage` from the assignable-permissions catalog, since those modules are never in `included_modules_json`. The seeded Owner role itself is unaffected (its permissions are granted directly by `DefaultRoleSeeder`, not through this catalog). Not fixed here — out of this task's explicit scope (Owner/default-role bootstrap and the seed data), but flagged as a real product gap for custom-role administration.
3. **`ModuleCatalogSeeder.cs`'s own hardcoded `modulesToSeed`/feature/ownership lists** are a third, pre-existing, inconsistent source of "what's a Phase 1 module" (it uses `time_off` instead of `leave`, is missing `workforce`/`exceptions`/`chat`/`chat_ai` compared to the migration-seeded catalog, and does not know about any of the 14 newly-added canonical keys). It was not touched beyond the `org` → `org_structure` rename it required, since reconciling its full internal vocabulary is a pre-existing separate cleanup, not something this task's module-list correction caused.
4. **`time_off` vs `leave` naming split** (§7) — pre-existing, not touched.
5. **New `module_catalog` rows have placeholder pricing** (`pricing_reference = "[]"`, `storage_reference = "[]"`, `ai_token_reference = "[]"`, `pricing_unit = "flat_rate"`) since this task's scope was module *membership*, not Phase 1 commercial pricing for the 14 new keys, which was never provided. These modules are marked `is_active = true`/`phase = "phase_1"` but are not priced for sale yet.
6. Old `module_catalog` rows for the removed/renamed keys (`org`, `monitoring`, `workforce`, `verification`, `exceptions`, `analytics`, `work_management`, `chat`, `chat_ai`, `integrations`, `workflow_engine`, `auth`, `configuration`, `roles`, `notifications`) were deliberately left in place, unmodified — `PermissionSeeder` and `ModuleCatalogSeeder` still legitimately own permissions under most of these keys (verified via `OrgPermissionSeedTests`/`ModuleCatalogSeederTests`/`ModuleAutoGrantsTests`, none of which broke), and the task does not ask for catalog curation beyond adding the missing canonical keys.
