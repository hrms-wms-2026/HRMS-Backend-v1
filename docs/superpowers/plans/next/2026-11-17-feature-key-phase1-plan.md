# Feature-Key Entitlement Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One fail-closed backend service decides whether a tenant may use a feature key now, and every active customer-facing HTTP and non-HTTP surface asks it.

**Architecture:** First reconcile module identities to one canonical key per module. Then add feature lifecycle and an RLS-protected `tenant_feature_entitlements` table. Then move module entitlement to `SubscriptionStatusRules.HasActiveAccess` behind a shared subscription-access reader and build the feature evaluator, with a per-instance cache, on top of it. HTTP surfaces use `[RequireFeature]`. Jobs, outbox handlers, tray/ingest handlers and dashboard providers use `IFeatureGate`. An architecture test proves coverage against a manifest.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server, RLS via `SESSION_CONTEXT`), xUnit 2.9, FluentAssertions 8, Moq/NSubstitute, EF InMemory (unit), Testcontainers.MsSql (SQL Server tests, CI only), `IMemoryCache`.

**Spec:** `docs/superpowers/specs/next/2026-11-16-feature-key-management-design.md` (DEC-038). **Inventory:** `docs/reports/security/2026-11-16-feature-key-phase1-inventory.md`.

## Global Constraints

- Feature key format `^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$`, max length 100, prefix equals owning `module_key` + `.`.
- `FeatureKey` and owning `ModuleKey` are immutable; keys are never deleted or reused.
- Lifecycle values `draft`, `active`, `deprecated`, `retired`. `deprecated` stays evaluable; `draft` and `retired` deny.
- Decision values `enabled`, `disabled`. Source values `migration`, `subscription`, `platform_operator`, `system`.
- Reason codes: `enabled_default`, `enabled_override`, `disabled_default`, `invalid_key`, `unknown_feature`, `feature_inactive`, `module_inactive`, `module_not_entitled`, `subscription_inactive`, `disabled_override`, `globally_disabled`, `configuration_error`.
- Baseline modules (`PlatformBaselineModules.Keys`): `auth`, `configuration`, `roles`, `notifications` — module-entitled without appearing in `selected_modules`, but still need subscription access.
- Subscription rule: latest `tenant_subscriptions` row by `created_at` desc, `id` desc; entitled iff `SubscriptionStatusRules.HasActiveAccess(status, access_ends_at, now)`. Module and feature evaluation use the same reader.
- `EffectiveUntil` is exclusive. Ordinary changes propagate within 60 seconds. Cache TTL ≤ 60 s and never past `EffectiveUntil`. Cache or database failure denies.
- `tenant_feature_entitlements` is `RlsPredicate.Tenant`. Background work enumerates tenants, then calls `ITenantContextSwitcher.SwitchToTenantAsync` before evaluating.
- Never auto-enable an `IsDefaultIncluded=false` feature. Phase 1 writes no explicit tenant decisions and exposes no operator CRUD. Do not use or extend `FeatureAccessGrant`.
- Additive release: keep `module_features.is_active` and every legacy column.
- Never apply migrations to a shared database. SQL Server (Testcontainers) suites run in CI only — local disk is too tight for Docker.
- Tenant-facing errors: stable problem type `feature_disabled` plus correlation id; never reason codes or configuration.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; push branches, open PRs only when Dabi asks.

## Decisions applied

DEC-040 canonical keys = feature prefixes · DEC-041 six WorkSync keys → `work_management` · DEC-042 `leave` → `time_off` · DEC-043 the 14 unimplemented keys start `draft` · DEC-044 add `work_management.meetings`, `integrations.zoom` · DEC-046 trial end via `access_ends_at` · DEC-047 always-on exclusions + fail-secure MFA · DEC-048 feature-less modules merged · DEC-049 `time_attendance.attendance_corrections` replaces `monitoring.attendance_corrections` · DEC-050 both GitHub keys gated and `draft`.

Canonical alias map (used by Tasks 1–3):

| Legacy | Canonical |
|---|---|
| `org_structure` | `org` |
| `leave` | `time_off` |
| `activity_monitoring`, `desktop_agent_gateway`, `discrepancy_engine`, `exception_engine` | `monitoring` |
| `identity_verification` | `verification` |
| `productivity_analytics` | `analytics` |
| `worksync_foundation`, `projects`, `objectives_milestones`, `tasks`, `boards`, `planning_sprints` | `work_management` |
| `attendance` (auto-grant only) | `time_attendance` |
| `employees` (auto-grant only) | `core_hr` |

Permission module moves (codes unchanged): `org:*`, `legal_entity:*` → `org`; `leave:*` → `time_off`; `monitoring:*`, `activity:read:self` → `monitoring`; `attendance:read|write|approve` → `time_attendance`; `projects:create` → `work_management`.

## Review Focus

1. **Messy `selected_modules`** (legacy + canonical mixed, duplicates, whitespace, unknown keys, non-array or corrupt JSON): canonical, deduplicated keys; unknown keys ignored with a warning; corrupt JSON denies and logs, never throws. Tests in Tasks 1 and 8.
2. **Evaluation under the wrong tenant context** (a job that forgot to switch; a tray handler that evaluates before `SwitchToTenantAsync`): RLS would hide an explicit `disabled` row and the default would wrongly enable. The evaluator must deny with `configuration_error` unless the connection is in tenant mode for the same tenant, or in admin mode. Tests in Tasks 10 and 18.
3. **Exact boundary instants** (`now == access_ends_at`, `now == effective_until`, a cached positive crossing either): deny at the instant. Tests in Tasks 8, 10 and 11.
4. **Infrastructure failure** (`DbException`, timeout, `OperationCanceledException`): deny, never cache a positive; cancellation propagates instead of being cached as a deny. Test in Task 11.
5. **Disabled feature in a multi-tenant job or outbox run**: that tenant's work is skipped with a logged reason; the message completes (no infinite retry); other tenants in the same run still process; tenant context is correct after an exception. Tests in Task 14.

---

## PR batches and estimates

| PR | Tasks | Contents | Estimate (dev-days) |
|---|---|---|---|
| PR-A | 1–4 | Module identity reconciliation (code + migration M1) | 2.5–3.5 |
| PR-B | 5–7 | Lifecycle, `tenant_feature_entitlements`, RLS, backfill (M2, M3) | 2–3 |
| PR-C | 8–11 | Subscription-access reader, `ModuleEntitlementService` on `HasActiveAccess`, trial end (M4), evaluator, cache | 3.5–5 |
| PR-D | 12–14 | `FeatureKeys`, `[RequireFeature]`, `IFeatureGate`, summary endpoint, non-HTTP patterns, coverage architecture tests | 2.5–3.5 |
| PR-E1…E6 | 15–20 | Coverage per module group (one PR each) | 6–8 |
| PR-F | 21 | SQL Server/UAT evidence, release notes | 1.5–2 |
| FE PR (tenant repo) | 22 | Canonical keys, summary-driven nav, Playwright UAT | 1.5–2 |
| **Total** | | | **19.5–27** |

Order: A → B → C → D → E1–E6 → F, with the FE PR after D. PR-C carries both the module-rule move and the evaluator so they cannot ship apart. UAT starts only after all of them are on `development`, because DEC-033 needs every active key gated. Task 23 (remove the alias map) is a later release.

## File structure

| Path | Responsibility |
|---|---|
| `src/ONEVO.Application/Features/SharedPlatform/ModuleCatalog/Helpers/ModuleKeyAliases.cs` | Temporary legacy → canonical map |
| `src/ONEVO.Application/Features/SharedPlatform/FeatureEntitlement/FeatureKeys.cs` | Constants for every feature key used by gates |
| `src/ONEVO.Application/Features/SharedPlatform/FeatureEntitlement/FeatureLifecycle.cs`, `FeatureReasonCodes.cs`, `FeatureDecision.cs` | Value sets and the decision record |
| `src/ONEVO.Application/Common/ServiceInterfaces/IFeatureEntitlementService.cs`, `IFeatureGate.cs`, `IFeatureEntitlementInvalidator.cs`, `IGlobalFeatureOverrideProvider.cs`, `ITenantSubscriptionAccessReader.cs` | Contracts |
| `src/ONEVO.Domain/Features/SharedPlatform/ModuleCatalog/Entities/TenantFeatureEntitlement.cs` | Tenant decision entity |
| `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogDefinitions.cs` | Static module/feature/ownership definitions (moved out of the seeder so tests can read them) |
| `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/ModuleCatalog/TenantFeatureEntitlementConfiguration.cs` | EF mapping |
| `src/ONEVO.Infrastructure/Services/SharedPlatform/FeatureEntitlement/` | `TenantSubscriptionAccessReader`, `FeatureEntitlementService`, `CachingFeatureEntitlementService`, `FeatureEntitlementVersionStore`, `FeatureEntitlementChangeInterceptor`, `FeatureGate`, `NoGlobalFeatureOverrides`, `FeatureEntitlementOptions` |
| `src/ONEVO.Api/Filters/RequireFeatureAttribute.cs` | HTTP gate |
| `src/ONEVO.Api/Controllers/Tenant/SharedPlatform/FeaturesController.cs` | `GET /api/v1/features/effective` |
| `src/ONEVO.Infrastructure/Migrations/` | M1 `ReconcileModuleIdentities`, M2 `AddFeatureLifecycleAndTenantFeatureEntitlements`, M3 `BackfillFeatureLifecycle`, M4 `SetTrialAccessEnd` |
| `tests/ONEVO.Tests.Architecture/FeatureGates/` | `FeatureGateManifest`, `FeatureGateExclusions`, coverage tests |
| `tests/ONEVO.Tests.Integration/SqlServerMigration/FeatureEntitlementSqlTests.cs` | RLS, constraints, triggers, migrations |

Test commands used below:

- Unit: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~<Name>"`
- Architecture: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~<Name>"`
- SQL Server: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~SqlServerMigration.<Name>"` — **CI only**; locally run `dotnet build` and leave the run to CI.

---

## PR-A — Module identity reconciliation

### Task 1: Legacy module key alias map

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/ModuleCatalog/Helpers/ModuleKeyAliases.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/ModuleKeyAliasesTests.cs`

**Interfaces:**
- Produces: `static class ModuleKeyAliases` with `IReadOnlyDictionary<string,string> LegacyToCanonical`, `string Canonicalize(string key)`, `IReadOnlyList<string> CanonicalizeAll(IEnumerable<string?> keys)`, `IReadOnlyList<string> ExpandWithLegacyAliases(IEnumerable<string> canonicalKeys)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("org_structure", "org")] [InlineData("leave", "time_off")]
[InlineData("activity_monitoring", "monitoring")] [InlineData("exception_engine", "monitoring")]
[InlineData("identity_verification", "verification")] [InlineData("productivity_analytics", "analytics")]
[InlineData("planning_sprints", "work_management")] [InlineData("core_hr", "core_hr")]
public void Canonicalize_maps_legacy_and_keeps_canonical(string input, string expected)
    => ModuleKeyAliases.Canonicalize(input).Should().Be(expected);

[Fact]
public void CanonicalizeAll_trims_dedupes_keeps_first_order_and_drops_blanks()
    => ModuleKeyAliases.CanonicalizeAll([" tasks", "projects", null, "", "core_hr", "work_management", "leave"])
        .Should().Equal("work_management", "core_hr", "time_off");

[Fact]
public void ExpandWithLegacyAliases_adds_every_legacy_key_for_each_canonical_key()
    => ModuleKeyAliases.ExpandWithLegacyAliases(["org"]).Should().BeEquivalentTo(["org", "org_structure"]);
```

- [ ] **Step 2: Run, expect FAIL** (`ModuleKeyAliases` missing): `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ModuleKeyAliasesTests"`
- [ ] **Step 3: Implement** with the map from "Decisions applied", ordinal comparison, and a summary comment: temporary migration map, removed in Task 23.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): add temporary legacy module key alias map`

### Task 2: Canonical module keys in code

**Files:**
- Modify: `ModuleEntitlementService.cs` (canonicalize keys read from `selected_modules`), `PermissionSeeder.cs` (module values), `ModuleCatalogSeeder.cs` (ownership `org_structure`→`org`, `projects`→`work_management`; `org.structure_management` module `org`), `ModuleAutoGrants.cs` (keys `time_off`, `time_attendance`, `monitoring`; drop `employees`, already covered by `core_hr`), `SubscriptionPlanConfiguration.cs` (HasData list canonical, deduped), `TrayPresenceRequirementEvaluator.cs` (`MonitoringModuleKey = "monitoring"`), the 20 `[RequireAnyModule(...)]` sites → `[RequireAnyModule("work_management")]`, the six WorkSync arrays (`EmployeeOverviewWorkModules`, `GetMyTeamCapabilitiesQueryHandler`, four `Work*TeamActionSource`) → one constant `WorkManagementModule.Key = "work_management"`, `GetCurrentSessionQueryHandler` and `LoginSessionMaterialFactory` (`active_modules` = `ModuleKeyAliases.ExpandWithLegacyAliases(canonical)` until the FE PR ships), `DevSmokeTestTenantSeeder`/`Dapi*` seeders that write module lists.
- Test: update `ModuleAutoGrantsTests`, `ModuleEntitlementServiceTests`, `PermissionResolver*Tests`, `GetCurrentSessionQueryHandlerTests`, `TrayPresenceRequirementEvaluatorTests`, `DevSmokeTestTenantSeederTests`; create `tests/ONEVO.Tests.Architecture/ModuleKeyCanonicalArchitectureTests.cs`.

**Interfaces:**
- Consumes: `ModuleKeyAliases` (Task 1).
- Produces: `GetActiveModuleKeysForTenantAsync` returns canonical keys only; `WorkManagementModule.Key`.

- [ ] **Step 1: Write failing tests**
  - `ModuleEntitlementServiceTests.Legacy_selected_modules_are_returned_canonical`: subscription `["org_structure","tasks","projects","leave"]` → `["org","work_management","time_off"]`.
  - `PermissionResolverModuleAutoGrantTests.Legacy_plan_still_grants_monitoring_permissions`: plan `["activity_monitoring"]`, role holds `monitoring:read` (module `monitoring`) → effective.
  - `PermissionResolverTests.Identity_verification_plan_grants_verification_permissions` (fixes the bug in inventory §2.2).
  - `GetCurrentSessionQueryHandlerTests.Active_modules_include_legacy_aliases_during_transition`.
  - Architecture `No_legacy_module_key_literals_outside_alias_map`: scan `src/**/*.cs` except `ModuleKeyAliases.cs` and `Migrations/` for `"org_structure"`, `"activity_monitoring"`, `"identity_verification"`, `"productivity_analytics"`, `"worksync_foundation"`, `"objectives_milestones"`, `"planning_sprints"`, `"desktop_agent_gateway"`, `"discrepancy_engine"`, `"exception_engine"` → none. (`"leave"`, `"tasks"`, `"projects"`, `"boards"`, `"attendance"` are also domain words, so the test checks them only inside `RequireAnyModule(`, `ModuleAutoGrants`, `Perm(` and `Module =` contexts.)
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** the modifications listed above.
- [ ] **Step 4: Run** the unit and architecture filters above plus `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Auth|FullyQualifiedName~Dashboard|FullyQualifiedName~Tenancy"`; expect PASS.
- [ ] **Step 5: Commit** `refactor(entitlement): use canonical module keys across code and seeders`

### Task 3: Migration M1 `ReconcileModuleIdentities`

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/20261118000000_ReconcileModuleIdentities.cs` (hand-written; timestamp after the latest migration at execution time)
- Test: `tests/ONEVO.Tests.Integration/SqlServerMigration/ModuleIdentityMigrationTests.cs`

**Interfaces:**
- Produces: persisted data with canonical keys; legacy `module_catalog` rows `is_active = 0` (never deleted).

- [ ] **Step 1: Write failing SQL Server tests** (seed rows, run the migration's SQL, assert):
  - `Json_arrays_are_rewritten_deduped_and_ordered`: `selected_modules` `["tasks","projects","core_hr","leave"]` → `["work_management","core_hr","time_off"]`; the same for `subscription_plans.included_modules_json`, `role_templates.module_keys_json`, `configuration_templates.module_keys_json`.
  - `Corrupt_json_rows_are_left_untouched` (`ISJSON = 0`).
  - `Columns_are_rewritten`: `permissions.module`, `module_permission_ownership.module_key`, `module_features.module_key` (`org.structure_management` → `org`), `module_integration_links.module_key`.
  - `Legacy_catalog_rows_are_deactivated_not_deleted` (13 rows, `is_active = 0`, count unchanged).
  - `Migration_is_idempotent` (run twice, same result).
  - `Untouched_tables`: `feature_access_grants`, `module_catalog_price_history` unchanged.
- [ ] **Step 2: Run in CI, expect FAIL.**
- [ ] **Step 3: Implement** `Up`: `EXEC sp_set_session_context N'app.tenant_context_mode', N'admin'`; table variable `@alias(legacy, canonical)` with the map; JSON rewrite per table with `OPENJSON` + `STRING_AGG(... ) WITHIN GROUP (ORDER BY first_ordinal)` over `GROUP BY ISNULL(canonical, value)`, only where `ISJSON(col) = 1` and an alias occurs; column `UPDATE ... FROM @alias`; deactivate legacy catalog rows; reset session context to `NULL` in a `TRY/CATCH` finally pattern as in `20261113120000_GrantChecklistsManageToEmployeeWriters`. `Down`: no-op with a comment (data reconciliation is forward-only).
- [ ] **Step 4: CI run PASS;** locally `dotnet build`.
- [ ] **Step 5: Commit** `feat(entitlement): migrate persisted module keys to canonical identities`

### Task 4: Plan module key validation

**Files:**
- Modify: `CreateSubscriptionPlanCommandHandler.cs`, `UpdateSubscriptionPlanCommandHandler.cs`
- Test: `CreateSubscriptionPlanCommandHandlerTests.cs`, `UpdateSubscriptionPlanCommandHandlerTests.cs`

- [ ] **Step 1: Write failing tests:** `Rejects_inactive_or_unknown_module_key` (`"tasks"` → failure, code `module_key_invalid`), `Rejects_baseline_module_key` (`"auth"` → failure), `Stores_canonical_deduped_keys`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** using the injected `IModuleCatalogService`: every key must be an active catalog module and not in `PlatformBaselineModules.Keys`; status 400, error code `module_key_invalid`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `fix(subscription): validate plan module keys against the active catalog`

---

## PR-B — Lifecycle and tenant decisions

### Task 5: Lifecycle in the catalog definitions and seeder

**Files:**
- Create: `ModuleCatalogDefinitions.cs` (modules, features with `Lifecycle`, `ReplacementFeatureKey`, ownership), `FeatureLifecycle.cs`
- Modify: `ModuleFeature.cs` (add `string LifecycleStatus = FeatureLifecycle.Active`, `string? ReplacementFeatureKey`, `byte[] RowVersion`), `ModuleCatalogSeeder.cs` (reads definitions)
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/ModuleCatalogDefinitionsTests.cs`, `ModuleCatalogSeederTests.cs`

**Interfaces:**
- Produces: `FeatureLifecycle.Draft|Active|Deprecated|Retired`, `FeatureLifecycle.IsEvaluable(string)` (true for `active`, `deprecated`), `ModuleCatalogDefinitions.Features` (`IReadOnlyList<FeatureDefinition>` with `Key, Module, Name, IsDefaultIncluded, Lifecycle, ReplacementKey`).

- [ ] **Step 1: Write failing tests**
  - `Every_feature_key_matches_format_and_module_prefix` (regex from Global Constraints).
  - `Feature_count_is_67` and `New_keys_exist`: `work_management.meetings`, `integrations.zoom`, `time_attendance.attendance_corrections` (all `active`, default-included).
  - `Draft_keys_are_exactly`: the 14 gap keys (inventory §3.3) plus `integrations.github`, `work_management.github_integration`.
  - `Attendance_corrections_is_deprecated_with_replacement`.
  - Seeder: `Existing_rows_keep_key_module_and_lifecycle` (seeded row changed to `retired` stays `retired`); `New_rows_get_definition_lifecycle`; `IsActive_mirrors_IsEvaluable_for_new_rows`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** The seeder inserts missing rows and updates `Name`/`IsDefaultIncluded` only; it never writes `FeatureKey`, `ModuleKey`, `LifecycleStatus` or `ReplacementFeatureKey` on existing rows.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): add feature lifecycle to catalog definitions`

### Task 6: Migration M2 — lifecycle schema, decision table, RLS

**Files:**
- Create: `TenantFeatureEntitlement.cs` (Domain; `BaseEntity` subclass: `FeatureKey`, `Decision`, `Source`, `Reason`, `EffectiveFrom`, `EffectiveUntil`, `UpdatedById`, `RowVersion`), `TenantFeatureEntitlementConfiguration.cs`, migration `…_AddFeatureLifecycleAndTenantFeatureEntitlements.cs`
- Modify: `ModuleFeatureConfiguration.cs` (lifecycle columns; FK to module `DeleteBehavior.Restrict`), `ApplicationDbContext.cs` (DbSet), `RlsTableRegistry.cs` (`["tenant_feature_entitlements"] = RlsPredicate.Tenant`)
- Test: `tests/ONEVO.Tests.Integration/SqlServerMigration/FeatureEntitlementSqlTests.cs`

**Schema** (`module_features`): `lifecycle_status nvarchar(16) NOT NULL DEFAULT N'active'` with `CK_module_features_lifecycle`; `replacement_feature_key nvarchar(100) NULL` self-FK, `CK` not equal to own key; `row_version rowversion`; `CK_module_features_key_format`: `LEFT(feature_key, LEN(module_key) + 1) = module_key + N'.'`, `feature_key NOT LIKE N'%[^a-z0-9_.]%' COLLATE Latin1_General_BIN2`, exactly one `.`, and the first character after the `.` is a letter; trigger `trg_module_features_immutable` rejecting any `DELETE` and any `UPDATE` that changes `feature_key` or `module_key` (`THROW 51010`).

**Schema** (`tenant_feature_entitlements`): `id uniqueidentifier PK`, `tenant_id` FK `tenants` (no cascade), `feature_key nvarchar(100)` FK `module_features` (no cascade), `decision nvarchar(16)` `CK IN (N'enabled', N'disabled')`, `source nvarchar(24)` `CK IN (N'migration', N'subscription', N'platform_operator', N'system')`, `reason nvarchar(500) NULL`, `effective_from datetimeoffset NOT NULL`, `effective_until datetimeoffset NULL` with `CK effective_until IS NULL OR effective_until > effective_from`, `BaseEntity` audit columns, `updated_by_id uniqueidentifier NULL`, `row_version rowversion`; unique index `UX_tenant_feature_entitlements_tenant_feature (tenant_id, feature_key)`; `migrationBuilder.Sql(RlsSql.CreatePolicy("tenant_feature_entitlements", RlsPredicate.Tenant))`, with `Down` dropping the policy first.

- [ ] **Step 1: Write failing SQL Server tests:** `Tenant_A_cannot_read_or_write_tenant_B_decisions` (EF and raw SQL); `Missing_session_context_returns_no_rows_and_blocks_insert`; `Stale_context_is_cleared_on_pooled_connection` (reuse `TenantContextConnectionTests` helpers); `Check_constraints_reject_bad_decision_source_and_period`; `Unique_tenant_feature_rejects_second_row`; `Unknown_feature_key_fk_rejects`; `Catalog_feature_cannot_be_deleted_or_rekeyed` (runtime identity, ad-hoc SQL → error 51010); `Key_format_check_rejects_bad_keys` (`"Org.x"`, `"org.x.y"`, `"core_hr.x"` under module `org`). The registry-driven `SqlServerRlsCrossTenantTests` theory picks up the table automatically — confirm it is listed.
- [ ] **Step 2: Run in CI, expect FAIL.**
- [ ] **Step 3: Implement** (generate with `dotnet ef migrations add AddFeatureLifecycleAndTenantFeatureEntitlements --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`, then add the hand-written SQL for the trigger, checks and RLS).
- [ ] **Step 4: CI PASS;** locally `dotnet build` and `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~SqlServerModel|FullyQualifiedName~TenantIsolation"`.
- [ ] **Step 5: Commit** `feat(entitlement): add feature lifecycle schema and tenant_feature_entitlements with RLS`

### Task 7: Migration M3 `BackfillFeatureLifecycle`

**Files:** migration `…_BackfillFeatureLifecycle.cs`; test in `FeatureEntitlementSqlTests.cs`

- [ ] **Step 1: Write failing tests:** `Inactive_rows_become_retired`; `Gap_and_github_keys_become_draft` (16 keys, `is_active = 0`); `New_feature_rows_inserted_once` (the three new keys, idempotent); `Attendance_corrections_deprecated_with_replacement`; `No_tenant_decision_rows_created`; `Default_included_flags_unchanged`.
- [ ] **Step 2: Run in CI, expect FAIL.**
- [ ] **Step 3: Implement** with `IF NOT EXISTS` inserts for the three new keys, then the lifecycle updates; keep `is_active` = (`lifecycle_status IN ('active','deprecated')`).
- [ ] **Step 4: CI PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): backfill feature lifecycle (DEC-043, DEC-049, DEC-050)`

---

## PR-C — Subscription rule, evaluator, cache (ship together)

### Task 8: Shared subscription-access reader; `ModuleEntitlementService` on `HasActiveAccess`

**Files:**
- Create: `ITenantSubscriptionAccessReader.cs`, `TenantSubscriptionAccessReader.cs`
- Modify: `ModuleEntitlementService.cs` (uses the reader; returns canonical keys that are active catalog modules), DI registration
- Create: `docs/reports/security/2026-11-1x-feature-key-affected-tenants.md` (per-environment results table, filled by the operator from inventory §5.1)
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantSubscriptionAccessReaderTests.cs`, `ModuleEntitlementServiceTests.cs`

**Interfaces:**
- Produces: `Task<TenantSubscriptionAccess> GetAsync(Guid tenantId, CancellationToken ct)`; `sealed record TenantSubscriptionAccess(bool HasAccess, string? Status, DateTimeOffset? AccessEndsAt, IReadOnlyList<string> ModuleKeys, bool SelectedModulesCorrupt)`. `ModuleKeys` are canonical and limited to active catalog modules; unknown keys are logged as warnings with tenant id and key (no personal data).

- [ ] **Step 1: Write failing tests**
  - Status matrix: `active|trialing|maintenance_included|subscription_included` with null, future and past `access_ends_at`; `past_due`/`cancelled` at `access_ends_at` − 1 s (access), `== now` (no access), + 1 s (no access), null (no access); unknown status (no access).
  - `Latest_row_wins_even_if_inactive` (newest `cancelled` past end, older `active` → no access; inventory §4.2 case c).
  - `Corrupt_json_denies_and_flags`; `Unknown_keys_dropped`; `Inactive_catalog_module_dropped`.
  - `ModuleEntitlementService` regressions: grace-window `past_due` returns modules; `active` past `access_ends_at` returns none; `GetAssignablePermissionsForTenantAsync` follows the same rule; `IsModuleEnabledAsync("tasks")` is false (legacy keys are not callable) and `IsModuleEnabledAsync("work_management")` is true.
  - Use a fake `IDateTimeProvider` for every instant.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Query: `TenantSubscriptions.AsNoTracking().Where(TenantId == id).OrderByDescending(CreatedAt).ThenByDescending(Id).FirstOrDefaultAsync()`, then `SubscriptionStatusRules.HasActiveAccess`. Leave `EfSubscriptionRepository` and `TenantModuleStatusReader` unchanged (inventory §4.1 follow-up).
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~TenantSubscriptionAccessReader|FullyQualifiedName~ModuleEntitlementService|FullyQualifiedName~PermissionResolver|FullyQualifiedName~DefaultRoleSeeder"`; expect PASS.
- [ ] **Step 5:** Add the affected-tenant report file with one row per environment (`environment`, `run at`, `gains`, `loses`, `different row`, `ended trials`, `operator`); the operator fills it before release. **Commit** `feat(entitlement): module entitlement uses HasActiveAccess via a shared subscription-access reader`

### Task 9: Trial end sets `access_ends_at` (DEC-046)

**Files:**
- Modify: `CreateTenantCommandHandler.cs` (`AccessEndsAt = trialEndDate` when trialing), `ProcessStripeEventCommandHandler.cs` (subscription-updated mapped to `active` also clears `AccessEndsAt`, matching `invoice.paid`), `DevSmokeTestTenantSeeder.cs` if it creates trialing rows
- Create: migration `…_SetTrialAccessEnd.cs`
- Test: `CreateTenantCommandHandlerTests`, `ProcessStripeEventCommandHandlerTests`, `FeatureEntitlementSqlTests.Trial_backfill_sets_access_end`

- [ ] **Step 1: Write failing tests:** `Trialing_tenant_gets_access_end_at_trial_end`; `Active_tenant_has_null_access_end`; `Stripe_subscription_updated_to_active_clears_access_end`; reader test `Ended_trial_denies_through_access_ends_at`; SQL `Backfill_only_touches_trialing_rows_with_null_access_end` (admin session context, idempotent).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Migration: `UPDATE dbo.tenant_subscriptions SET access_ends_at = trial_end_date WHERE status = N'trialing' AND access_ends_at IS NULL AND trial_end_date IS NOT NULL;` inside the admin session-context pattern.
- [ ] **Step 4: Run, expect PASS** (SQL test in CI).
- [ ] **Step 5: Commit** `fix(subscription): end trial access through access_ends_at (DEC-046)`

### Task 10: Feature evaluator

**Files:**
- Create: `IFeatureEntitlementService.cs`, `FeatureDecision.cs`, `FeatureReasonCodes.cs`, `IGlobalFeatureOverrideProvider.cs`, `NoGlobalFeatureOverrides.cs`, `FeatureEntitlementService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/FeatureEntitlementServiceTests.cs`

**Interfaces:**
- Consumes: `ITenantSubscriptionAccessReader` (Task 8), `FeatureLifecycle` (Task 5), `ITenantContext`, `IDateTimeProvider`.
- Produces:
  - `Task<FeatureDecision> EvaluateAsync(Guid tenantId, string featureKey, CancellationToken ct = default)`
  - `Task<IReadOnlyList<FeatureDecision>> EvaluateAllAsync(Guid tenantId, CancellationToken ct = default)` — evaluable keys only
  - `sealed record FeatureDecision(bool IsEnabled, string ReasonCode, string FeatureKey, string? ModuleKey, string? DecisionSource, long ConfigurationVersion, DateTimeOffset? EffectiveUntil)`
  - `IGlobalFeatureOverrideProvider.IsForceDisabledAsync(string featureKey, CancellationToken ct)` → Phase 1 `NoGlobalFeatureOverrides` returns `false`.

Order (spec §8): context guard → key format (`invalid_key`) → feature row (`unknown_feature`; not evaluable lifecycle → `feature_inactive`) → module active (`module_inactive`) → subscription access (`subscription_inactive`) → module selected or baseline (`module_not_entitled`) → global override (`globally_disabled`) → current explicit decision `effective_from <= now < effective_until` (`disabled_override` / `enabled_override`) → `IsDefaultIncluded` (`enabled_default` / `disabled_default`). Context guard: deny `configuration_error` unless `ITenantContext.ContextMode == Tenant && TenantId == tenantId`, or `ContextMode == Admin`. Any exception → log, return `configuration_error` (cancellation rethrows).

- [ ] **Step 1: Write failing tests** — one per reason code, plus: `Explicit_enable_cannot_bypass_module` (module not selected + `enabled` row → `module_not_entitled`); `Explicit_enable_cannot_bypass_draft`; `Baseline_feature_entitled_without_selection` (`auth.mfa_enforcement`, `selected_modules` `["core_hr"]`); `Baseline_feature_still_needs_subscription`; `Deprecated_feature_still_evaluable`; `Effective_until_is_exclusive` (`now == effective_until` → row ignored, default applies); `Future_effective_from_ignored`; `Wrong_tenant_context_denies` and `System_mode_denies` (Review Focus 2); `Mixed_case_or_whitespace_key_is_invalid`; `Alias_migrated_tenant_keeps_access` (selected `["tasks"]` → `work_management.tasks` enabled); `Module_and_feature_agree_for_same_instant` (property test over the status matrix of Task 8: `IsModuleEnabledAsync(m)` false ⇒ every feature of `m` denied).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Tenant decisions are read only here (`_db.TenantFeatureEntitlements`).
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): add fail-closed feature entitlement evaluator`

### Task 11: Cache and invalidation

**Files:**
- Create: `CachingFeatureEntitlementService.cs` (decorator), `FeatureEntitlementVersionStore.cs` (singleton; `IFeatureEntitlementInvalidator`), `FeatureEntitlementChangeInterceptor.cs` (`SaveChangesInterceptor` + `IDbTransactionInterceptor`), `FeatureEntitlementOptions.cs`, metrics in the decorator (`Meter "Crownus.FeatureEntitlement"`: `feature_decisions_total{reason}`, `feature_decision_cache_total{result=hit|miss}`, `feature_decision_duration_ms`)
- Modify: DI (`IFeatureEntitlementService` = decorator over the evaluator; interceptor registered on `ApplicationDbContext`), `appsettings.json` (`FeatureEntitlement:DecisionTtlSeconds: 60`)
- Test: `CachingFeatureEntitlementServiceTests.cs`, `FeatureEntitlementChangeInterceptorTests.cs`

**Interfaces:**
- Produces: `IFeatureEntitlementInvalidator.InvalidateTenant(Guid tenantId)`, `InvalidateAll()`; cache key `fe:{globalVersion}:{tenantVersion}:{tenantId:N}:{featureKey}`; `FeatureEntitlementOptions.DecisionTtlSeconds` validated 1–60 on start.

- [ ] **Step 1: Write failing tests:** `Second_call_hits_cache`; `Expiration_is_min_of_ttl_and_effective_until` (positive cached at `effective_until − 5 s` is gone at `effective_until`); `Negative_decisions_cached`; `Configuration_error_never_cached`; `Evaluator_exception_denies_and_is_not_cached`; `Cancellation_propagates_and_is_not_cached` (Review Focus 4); `InvalidateTenant_only_affects_that_tenant`; `InvalidateAll_affects_everyone`; interceptor: `TenantSubscription_change_invalidates_tenant_after_save`, `TenantFeatureEntitlement_change_invalidates_tenant`, `ModuleFeature_or_ModuleCatalogItem_change_invalidates_all`, `Inside_transaction_publishes_on_commit_only`, `Rollback_publishes_nothing`; options `Ttl_above_60_fails_validation`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.** Raw-SQL changes (migrations) are covered by the 60-second TTL. Document the per-instance topology in `docs/reports/security/2026-11-1x-feature-key-affected-tenants.md` § "Cache topology": each API instance caches for ≤ 60 s; Phase 2 force-disable needs distributed invalidation first (spec §10).
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): cache feature decisions with post-commit invalidation`

---

## PR-D — Gates and coverage tests

### Task 12: `FeatureKeys`, `IFeatureGate`, `[RequireFeature]`, summary endpoint

**Files:**
- Create: `FeatureKeys.cs` (one `public const string` per catalog key, e.g. `TimeOffRequests = "time_off.requests"`), `IFeatureGate.cs`, `FeatureGate.cs`, `RequireFeatureAttribute.cs`, `FeaturesController.cs`
- Modify: `ONEVO.Api/Errors/ProblemTypes` (add `feature_disabled`)
- Test: `tests/ONEVO.Tests.Unit/Api/Filters/RequireFeatureAttributeTests.cs`, `FeatureGateTests.cs`, `FeaturesControllerTests.cs`, `tests/ONEVO.Tests.Architecture/FeatureGates/FeatureKeysArchitectureTests.cs`

**Interfaces:**
- Produces:
  - `IFeatureGate.IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken ct) : Task<bool>`
  - `IFeatureGate.EnsureEnabledAsync(Guid tenantId, string featureKey, CancellationToken ct) : Task<Result>` → `Result.Failure("This feature is not available for your organization.", 403, "feature_disabled")`
  - `RequireFeatureAttribute(string featureKey)` with `public string FeatureKey { get; }`
  - `GET /api/v1/features/effective` → `[{ featureKey, moduleKey, isEnabled }]` (no reason codes)

- [ ] **Step 1: Write failing tests:** attribute → 401 unauthenticated; 403 `feature_disabled` with `correlationId = HttpContext.TraceIdentifier` and no reason code when disabled; 403 when `TenantId == Guid.Empty`, when the gate is not registered, or when the gate throws; passes when enabled. Gate logs `tenant_id`, `feature_key`, `reason_code` at Information on deny. Summary endpoint returns only evaluable keys and requires authentication. Architecture: `Every_FeatureKeys_constant_exists_in_catalog_definitions`, `Every_catalog_key_has_a_FeatureKeys_constant`, `RequireFeature_arguments_are_FeatureKeys_constants`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): add RequireFeature gate, IFeatureGate and effective-feature summary`

### Task 13: Non-HTTP gate patterns

**Files:**
- Modify: `EndpointGate.cs` (adds `FeatureKeySets` read from `RequireFeatureAttribute` on the mirrored endpoint; `AllowsAsync` takes `IFeatureGate`), `EndpointMirrorWidgetDataProvider.cs`, `WidgetDataProviders.cs`
- Test: `EndpointGateTests.cs`, `WidgetProviderTestKit.cs`

Pattern every later task follows (write it once here; tasks refer to it):
- **Job:** enumerate tenants in admin mode → `SwitchToTenantAsync(tenant)` → `if (!await gate.IsEnabledAsync(tenant.Id, key, ct)) { log skip; continue; }` → work. The existing per-tenant `try/catch` stays; evaluation happens after the switch.
- **Outbox handler:** resolve the tenant from the payload, switch, `IsEnabledAsync`; when disabled log `feature_disabled` and return normally (message completes; no retry).
- **In-handler (tray/ingest/login):** after `SwitchToTenantAsync`, `EnsureEnabledAsync`; return its failure.

- [ ] **Step 1: Write failing tests:** `Widget_hidden_when_mirrored_endpoint_feature_disabled`; `Widget_shown_when_enabled`; `EndpointGate_requires_all_feature_sets`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(entitlement): feature gates for dashboard widgets`

### Task 14: Coverage manifest and architecture tests

**Files:**
- Create: `tests/ONEVO.Tests.Architecture/FeatureGates/FeatureGateManifest.cs` (`IReadOnlyDictionary<string, Type[]>` feature key → controllers/jobs/handlers from inventory §3.2), `FeatureGateExclusions.cs` (`record Exclusion(string Key, string? Surface, string Reason, string ReviewRef)`), `FeatureGateCoverageTests.cs`, `FeatureDecisionAccessArchitectureTests.cs`
- Test: those files plus a job/outbox behaviour test kit `tests/ONEVO.Tests.Unit/Features/SharedPlatform/FeatureGateJobKit.cs`

Initial exclusions (each needs Dabi's sign-off in the PR-D review):
- `roles.permission_management`, `configuration.tenant_settings` — always-on core entitlement, DEC-047.
- Surfaces `PasswordResetEmailOutboxHandler`, `AdminPasswordResetEmailOutboxHandler`, `AdminPasswordChangedEmailOutboxHandler`, `EmployeeOnboardingInviteEmailOutboxHandler`, `TenantOwnerInviteEmailOutboxHandler`, `PlatformManagerInviteEmailOutboxHandler`, `InvoiceEmailOutboxHandler` — transactional security mail, DEC-047.
- `work_management.boards` — presentation of `work_management.tasks` data with no separate data or action; protected by the tasks gate.
- Surface `MonitoringRetentionPurgeJob` — retention purge must keep deleting data when monitoring is off.

- [ ] **Step 1: Write failing tests:** `Every_evaluable_catalog_key_is_in_manifest_or_excluded`; `Every_manifest_controller_has_RequireFeature_for_its_key_on_class_or_every_action`; `Every_manifest_non_http_type_depends_on_IFeatureGate`; `Draft_keys_may_have_gates` (allowed) and `Active_key_without_gate_fails`; `Every_exclusion_has_reason_and_review_ref` and `No_wildcard_exclusions`; `TenantFeatureEntitlements_used_only_in_FeatureEntitlementService_and_configuration`; `No_RequireAnyModule_with_legacy_keys`. Behaviour kit tests (Review Focus 5): `Job_skips_disabled_tenant_and_processes_next`; `Job_exception_on_one_tenant_does_not_leak_context_to_next` (asserts the switcher was called for each tenant before evaluation); `Outbox_disabled_completes_without_retry`.
- [ ] **Step 2: Run, expect FAIL** (manifest lists surfaces not yet gated — mark those keys `pending` in the manifest so the suite passes for PR-D, and the PR-E tasks remove `pending` key by key; the final PR-E task asserts `pending` is empty).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~FeatureGate|FullyQualifiedName~FeatureDecisionAccess"`; expect PASS.
- [ ] **Step 5: Commit** `test(entitlement): coverage manifest and architecture tests for feature gates`

---

## PR-E — Coverage (one PR per task)

Each task: add `[RequireFeature(FeatureKeys.X)]` to the listed controllers (class level unless actions differ), add `IFeatureGate` per the Task 13 pattern to the listed non-HTTP types, remove `pending` for those keys in the manifest, and write one behaviour test per non-HTTP type (`<Type>_skips_when_<key>_disabled`) plus one per controller group (`<Controller>_returns_403_when_<key>_disabled`) using the existing controller test style. Surfaces per key are in inventory §3.2. Run `dotnet test tests/ONEVO.Tests.Unit` and `tests/ONEVO.Tests.Architecture` before each commit.

### Task 15: Core HR and Org (PR-E1, 1–1.5 d)
Keys `core_hr.*` (5 implemented) and `org.structure_management`. Note: `Employees` mixes keys per action, so gate at action level. Commit `feat(entitlement): gate core HR and org structure features`.

### Task 16: Time Off and Calendar (PR-E2, 1–1.5 d)
Keys `time_off.*` (6), `calendar.*` (4). `LeaveCreditJob` (job pattern); `LeaveApprovalEmailOutboxHandler`, `Calendar*OutboxHandler` (outbox pattern); `calendar.time_off_visibility` inside `CheckCalendarConflicts` (leave overlay omitted, not an error, when disabled). Commit `feat(entitlement): gate time off and calendar features`.

### Task 17: Time & Attendance (PR-E3, 0.5–1 d)
Keys `time_attendance.work_schedules`, `time_attendance.attendance_corrections` (DEC-049: `AttendanceCorrections` controller uses the new key; the deprecated `monitoring.attendance_corrections` gets no new gate and is listed in the manifest as `deprecated → replacement`). `AttendanceAlertMaterializerJob`, `TeamAttendance`/`TeamSchedule` widgets. Commit `feat(entitlement): gate time and attendance features`.

### Task 18: Monitoring, Verification, Analytics (PR-E4, 1.5–2 d)
Keys `monitoring.*` (10 implemented; the deprecated `monitoring.attendance_corrections` is handled in Task 17), `verification.identity_checks`, `verification.face_match`, `analytics.daily_reports`, `analytics.monitoring_snapshots`, `analytics.productivity_dashboard`. Tray and ingest handlers use the in-handler pattern **after** `SwitchToTenantAsync`; add `Tray_handler_evaluates_after_switch` for `TrayClockInCommandHandler` and `IngestActivitySnapshotsCommandHandler` (Review Focus 2). `TrayPresenceRequirementEvaluator` keeps its module check and adds `monitoring.presence_sessions`. Commit `feat(entitlement): gate monitoring, verification and analytics features`.

### Task 19: Work Management (PR-E5, 1–1.5 d)
Keys `work_management.projects|tasks|sprints|okrs|resource_planning|work_analytics|meetings`. Replace the 20 `[RequireAnyModule("work_management")]` with `[RequireFeature(...)]` (the evaluator already enforces the module); delete `RequireAnyModuleAttribute` and switch `EndpointGate`'s module sets to feature sets once unused. Meetings: `Meetings`, `EmployeeMeetingAttendance` controllers, `MeetingAttendanceJob`, `MeetingOccurrenceGeneratorJob`, `MeetingLinkProvisionOutboxHandler`. Team action sources and `EmployeeOverviewWorkModules` move from module checks to `IFeatureGate`. Commit `feat(entitlement): gate work management features`.

### Task 20: Integrations and baseline (PR-E6, 1–1.5 d)
- `integrations.github` on `GitHubIntegrationController`; `work_management.github_integration` in `GitHubUserIntegrationAvailability` (both `draft` → deny, DEC-050); test `Github_endpoints_403_while_draft`.
- `integrations.microsoft_teams` (`TeamsAttendanceSyncJob`, Teams branch of `MeetingLinkProvisionOutboxHandler`), `integrations.zoom` (`ZoomAttendanceSyncJob`, Zoom branch), `integrations.google_workspace` (Google branch of `CalendarSyncJob`, `CompleteCalendarConnectionCommandHandler`).
- `auth.optional_google_oauth`: refuse workspace selection after a Google base login when disabled (`SelectWorkspaceCommandHandler`, `LoginContinuationService`), error code `feature_disabled`.
- `auth.mfa_enforcement` (DEC-047 fail-secure): gate only the tenant MFA-policy change endpoints; test `Mfa_challenge_still_required_for_enrolled_user_when_feature_disabled`.
- `notifications.in_app_delivery`, `notifications.email_delivery` (tenant notification e-mail channel only).
- Final step: `Manifest_has_no_pending_keys` added to `FeatureGateCoverageTests`.
Commit `feat(entitlement): gate integration and baseline features`.

---

## PR-F — Evidence

### Task 21: SQL Server isolation and UAT evidence (1.5–2 d)

**Files:** `FeatureEntitlementSqlTests.cs` (extend), `docs/reports/security/2026-11-1x-feature-key-phase1-uat.md`

- [ ] **Step 1:** Add SQL tests: `Evaluator_under_pooled_connection_never_sees_other_tenant_decision` (two tenants, same pooled connection, alternating `SwitchToTenantAsync`); `Admin_mode_reads_all_tenants_for_enumeration_only`.
- [ ] **Step 2:** UAT checklist with evidence slots for spec §12 UAT cases: default-included works; disabled feature hidden and direct API 403; tenant without module denied despite explicit enable row (inserted by test fixture only); suspended subscription removes access; change visible within 60 s; `auth.mfa_enforcement` works without `auth` in `selected_modules`; alias-migrated tenant keeps access; ended trial denies.
- [ ] **Step 3:** CI run of the SQL suite PASS; record run links in the UAT report.
- [ ] **Step 4: Commit** `test(entitlement): SQL Server isolation and UAT evidence for feature keys`

### Task 22: Tenant frontend (separate repo PR, 1.5–2 d)

Repo `front end-org/Hrms--Web-application---front-end---v1`: replace legacy keys in `app.routes.ts`, `nav-items.config.ts`, `main-layout.component.ts`, `tenant-workspace-ready.ts` with canonical keys; load `GET /api/v1/features/effective` after session and hide nav items whose feature is disabled; show an "unavailable" state on 403 `feature_disabled`. Playwright: the spec §12 UAT cases that have UI, plus `Direct_api_call_to_disabled_feature_returns_403`. Commit and push on its own branch.

### Task 23 (later release): remove the alias map

After the FE PR is deployed everywhere: delete `ModuleKeyAliases`, remove legacy-alias expansion from `active_modules`, and make `No_legacy_module_key_literals_outside_alias_map` also forbid the alias file. Not part of Phase 1 acceptance.

---

## Spec coverage check

| Spec | Tasks |
|---|---|
| §6 key governance, immutability | 5, 6 |
| §7.1 lifecycle | 5, 6, 7 |
| §7.2 decision table, RLS `Tenant`, switcher | 6, 13, 21 |
| §7.3 `FeatureAccessGrant` untouched | Global Constraints, 3 |
| §8 algorithm and reason codes | 10 |
| §9 HTTP, background, frontend | 12, 13, 15–20, 22 |
| §10 caching | 11 |
| §11 steps 1–11 | 1–4 (1), 8–9 (2), inventory (3), 5–6 (4–5), 7 (6–8), 15–20 (9), 8 (10), Global Constraints (11) |
| §12 unit/contract | 1, 8, 9, 10, 11 |
| §12 architecture | 2, 12, 14, 20 |
| §12 SQL Server | 3, 6, 7, 9, 21 |
| §12 UAT | 21, 22 |
| §13 acceptance | all; affected-tenant report in 8 |
| §17 errors/observability | 11 (metrics), 12 (problem type, logging) |
