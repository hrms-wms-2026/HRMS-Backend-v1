# Dev Smoke Multi-Tenant Seed Expansion Report

## Scope

Expanded `DevSmokeTestTenantSeeder` (Development/Test only) to seed two tenants (acme, dapi),
three Acme users with distinct roles/permissions, one Dapi owner, three Acme legal entities, and
one Dapi legal entity. No Department APIs, Position APIs, `position_assignments`, or legal-entity
membership modeling were added.

## Files changed

- `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` — rewritten to loop
  over tenant/user/legal-entity definitions instead of a single hardcoded Acme tenant.
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs` — new,
  13 focused tests against the expanded seeder (SQLite in-memory fixture).
- `docs/superpowers/plans/2026-08-02-dev-smoke-multi-tenant-seed-expansion.md` — implementation
  plan used for this task.
- `DEV_SMOKE_MULTI_TENANT_SEED_EXPANSION_REPORT.md` — this report.

No other files were touched. `git status` at the start of this task already showed unrelated,
pre-existing uncommitted work in this repo (a "Legal Entity General Settings" feature: Postman
collection edits, `LegalEntity.cs`, `LegalEntityConfiguration.cs`, a new migration, several new
Application/Api/test files, etc.). None of that was created, edited, or reviewed by this task —
it was already present in the working tree before this task started and is left exactly as
found.

## Final seeded tenants

| Slug | Name | Status | Tenant Id |
|---|---|---|---|
| acme | Acme Test | Active | `da810816-3fed-4e71-9a44-f93e9b509bc7` (existing, unchanged) |
| dapi | Dapi Test | Active | `6b0874ab-71db-401f-859f-bdd50c1317fb` (new) |

## Final seeded users

| Email | Tenant | Role | Password |
|---|---|---|---|
| siyasiyamala932@gmail.com | acme | Tenant Owner | Password123! (existing owner, unchanged) |
| paramanathanmuthaiya@gmail.com | acme | HR Manager | Password123! |
| mrt15473@gmail.com | acme | Work Manager | Password123! |
| dapiyshanth1908@gmail.com | dapi | Tenant Owner | Password123! |

All passwords are seeded only inside this Development/Test seeder via `IPasswordHasher.Hash`,
never hardcoded elsewhere.

## Final seeded legal entities

**Acme (3, exactly one primary):**

| Name | Company Code | Country | Currency | Timezone | Primary |
|---|---|---|---|---|---|
| Acme Technologies | ACME | LK | LKR | Asia/Colombo | Yes |
| Acme Solutions | ACMESOL | LK | LKR | Asia/Colombo | No |
| Acme Global Services | ACMEGS | LK | LKR | Asia/Colombo | No |

**Dapi (1, primary):**

| Name | Company Code | Country | Currency | Timezone | Primary |
|---|---|---|---|---|---|
| Dapi Technologies | DAPI | LK | LKR | Asia/Colombo | Yes |

## Final seeded roles and exact permissions

- **Tenant Owner** (acme and dapi, one role per tenant): every `Permission` row currently defined
  by `PermissionSeeder.GetAllPermissions()` **except** the `"*"` bypass code. This mirrors the
  exclusion `DefaultRoleSeeder.SeedDefaultRolesAsync` already applies to its own "Owner" role
  (`p.Code != "*"`), which is the codebase's existing definition of "forbidden/internal-only" for
  explicit tenant role grants. The set is resolved dynamically from the `Permissions` table at
  seed time, so it always tracks whatever `PermissionSeeder` currently defines (~112 codes as of
  this task).
- **HR Manager** (acme, `paramanathanmuthaiya@gmail.com` only): exactly
  `org:read`, `org:manage`, `employees:read`, `employees:write`, `roles:read`.
- **Work Manager** (acme, `mrt15473@gmail.com` only): exactly
  `org:read`, `employees:read`, `projects:read`, `tasks:read`, `tasks:write`
  (does **not** include `org:manage`).

If any of the eight explicitly-requested codes above did not exist in the `Permissions` table,
`ResolveRolePermissionsAsync` throws `InvalidOperationException` and startup fails loudly instead
of silently seeding a partial role. All eight codes exist today (verified via the passing unit
tests), and `PermissionSeeder` is registered as a hosted service before `DevSmokeTestTenantSeeder`
in `ONEVO.Infrastructure/DependencyInjection.cs` (lines 294 and 299), so permissions are always
present before the smoke-test seeder runs in production startup order.

## Idempotency proof

- All tenants, users, roles, legal entities, and subscriptions are matched by fixed GUIDs
  (`FirstOrDefaultAsync(x => x.Id == fixedId)`), never by `Add()`-on-miss without a prior lookup.
- `RolePermission`/`UserRole` rows are matched by their natural composite key
  (`TenantId`+`RoleId`+`PermissionId` / `TenantId`+`UserId`+`RoleId`) via `AnyAsync` before
  insert.
- `global_email_directory` rows are upserted with `ON CONFLICT (email, tenant_id) DO NOTHING` and
  cleaned up with a per-tenant, per-seeded-email-set scoped `DELETE ... NOT IN (...)` (fixed from
  the previous single-email version, which would have deleted two of the three Acme users' rows
  on every run once a second Acme user existed).
- `SeedAsync_AcmeHasExactlyThreeLegalEntitiesAfterRepeatedSeeding`,
  `SeedAsync_DapiHasExactlyOneLegalEntityAfterRepeatedSeeding`,
  `SeedAsync_IsIdempotentAcrossTenantsUsersAndRoles`, and
  `SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager` each run `SeedAsync` twice against the
  same database and assert row counts stay fixed (2 tenants, 4 users, 2 "Tenant Owner" roles, 1
  "HR Manager" role, 1 "Work Manager" role, exactly 3/1 legal entities with exactly one primary
  each, and zero Employee rows) — all four pass.
- `SeedAsync_ScopedCleanupRemovesOwnStaleRowButNeverTouchesOtherTenants` inserts a stale row under
  Acme's own tenant (an email no longer part of Acme's seeded set) and a row under a tenant id the
  seeder never visits at all, then reruns the seeder and asserts: the stale Acme row is gone (the
  per-tenant scoped `DELETE` did its job), Acme's three current emails are untouched, and the
  unrelated tenant's row is completely untouched — proving the cleanup is both effective and
  correctly scoped, not just effective.
- Role-permission grants are **additive by design and never pruned**: e.g. the pre-existing Acme
  "Tenant Owner" role previously held only `integrations:manage`; reruns now add every other
  non-`"*"` permission without removing anything. This satisfies "do not use broad destructive
  deletes" but means a role's permission set can only grow across seeder versions, never shrink,
  even if a permission code is later removed from an explicit list (HR/Work Manager). This is
  deliberate, not an oversight.

## Tests run and counts

- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
  → **343/343 passed** (includes the 5 pre-existing `DevSmokeTestTenantSeederArchitectureTests`
  source-ordering/RLS-safety assertions, unaffected by the refactor).
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "DevSmokeTestTenantSeeder|SmokeTenant|Seed" --no-restore --verbosity minimal`
  → **32/32 passed**. Verified per-class breakdown (via `--verbosity normal`): 13
  `DevSmokeTestTenantSeederTests` (new), 10 `PlatformAccessSeederTests`, 4
  `PlatformOAuthProviderMetadataSeederTests`, 2 `PlatformProviderCatalogTests`, 1
  `PermissionSeederTests`, 1 `ProviderOptionQueriesTests`, 1 `ModuleCatalogSeederTests` — all
  pre-existing, all still passing unchanged. (`DefaultRoleSeederTests.cs` matched the filter by
  name but is an empty file with no `[Fact]`s, so it contributes 0.)
- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` → **0 errors, 0
  warnings**.
- Docker-based integration checks: **skipped** — `docker info` failed in this environment
  ("docker NOT available"). No integration tests were run or added for this task; unit tests
  cover all 14 required assertions structurally (see next section for #14).

New unit test file coverage of the 14 required assertions:
1. Both tenants exist → `SeedAsync_CreatesBothAcmeAndDapiTenants`.
2. Dapi owner belongs only to dapi → `SeedAsync_DapiOwnerBelongsOnlyToDapi`.
3. Acme owner full permissions → `SeedAsync_AcmeOwnerBelongsToAcmeWithFullPermissionsExceptWildcard`.
4. HR Manager permissions → `SeedAsync_AcmeHrManagerBelongsToAcmeWithItsRequiredPermissions`.
5. Work Manager permissions → `SeedAsync_AcmeWorkManagerBelongsToAcmeWithItsRequiredPermissions`.
6. Three users have different permission sets → `SeedAsync_TheThreeAcmeUsersHaveDifferentPermissionSets`.
7. Acme exactly 3 legal entities after rerun → `SeedAsync_AcmeHasExactlyThreeLegalEntitiesAfterRepeatedSeeding`.
8. Dapi exactly 1 legal entity after rerun → `SeedAsync_DapiHasExactlyOneLegalEntityAfterRepeatedSeeding`.
9. Idempotent rerun → `SeedAsync_IsIdempotentAcrossTenantsUsersAndRoles`.
10. No duplicate Employee rows for mrt15473 → `SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager`
    (this seeder creates **zero** Employee rows for any user — see "Employee rows" section below
    — so the assertion is that zero rows exist after two seed runs, which is a stronger proof
    than "exactly one, not duplicated").
11. No multi-legal-entity membership for mrt15473 → satisfied structurally: no employee/membership
    row of any kind is created for this user (see below); covered by the same test as #10.
12. All `RolePermission`/`UserRole` rows have non-empty `TenantId` →
    `SeedAsync_AllSeededRolePermissionAndUserRoleRowsHaveNonEmptyTenantId`.
13. Every seeded user has a `global_email_directory` row for its tenant →
    `SeedAsync_EverySeededUserHasAGlobalEmailDirectoryRowForItsTenant`.
14. No test depends on tenant-host password login → satisfied structurally: every test in
    `DevSmokeTestTenantSeederTests` asserts directly against `ApplicationDbContext` rows and never
    calls a login endpoint or handler. No dedicated test was needed or added for this requirement.

`SeedAsync_ScopedCleanupRemovesOwnStaleRowButNeverTouchesOtherTenants` is an additional 13th test,
beyond the 14 listed requirements, added specifically to exercise the scoped `DELETE` path in
`SeedGlobalEmailDirectoryAsync` (see "Idempotency proof" above) — the other 12 tests only proved
inserts land, not that stale-row cleanup is both effective and correctly scoped.

### Why SQLite in-memory instead of EF InMemory for these tests

`SeedGlobalEmailDirectoryAsync` uses `ExecuteSqlInterpolatedAsync`/raw SQL (as the pre-existing
code already did), which the EF Core InMemory provider cannot execute at all. The tests instead
reuse the existing `SqliteTestApplicationDbContext` pattern from
`PostgresMfaChallengeStoreTests.cs` (SQLite shared-cache in-memory connection, `EnsureCreated()`
for the full EF model). `global_email_directory` itself has no EF entity mapping in production
(it exists only via a raw-SQL migration and is accessed only via raw SQL), so `EnsureCreated()`
does not create it; the test fixture recreates the same DDL manually, test-only — this does not
touch the real migration.

## Required search results

```
grep -rn "owner@acme.test" src tests
```
Matches only in pre-existing, unrelated test fixtures (`BaseDomainLoginIntegrationTests.cs`,
`BaseForgotPasswordCommandHandlerTests.cs`, `EnableMfaCommandHandlerTests.cs`,
`LoginWorkspaceResponseTests.cs`, etc.) that use `owner@acme.test` as a generic placeholder email
for unrelated auth-handler unit tests — none of these are the smoke-test seeder's owner email
(which is, and always was, `siyasiyamala932@gmail.com`), and none were touched by this task.

```
grep -n "Guid.Empty" src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs
```
No matches.

```
grep -n "LegalEntityId" src/ONEVO.Domain/Features/Auth/Roles/Entities/UserRole.cs
```
No matches.

```
grep -rln "legal_entity_membership|company_membership" src tests
```
No matches.

```
grep -rln "position_assignments" src tests
```
No matches.

## Confirmation: no production bootstrap/runtime behavior changed

- `DevSmokeTestTenantSeeder.StartAsync`'s Development/Test environment guard
  (`!_environment.IsDevelopment() && !_environment.IsEnvironment("Test")` → `return;`) is
  unchanged.
- No migrations were added or modified.
- No changes to Auth/login handlers, Department APIs, Position APIs, `position_assignments`, or
  any tenant provisioning/runtime tenant-creation logic.
- No Postman files were touched.
- No `git commit` or `git push` was performed — all changes remain unstaged/uncommitted in the
  working tree per instruction.
- All 343 `ONEVO.Tests.Architecture` tests pass, including the RLS/privileged-connection and
  Development/Test-only guards specific to this seeder.

## Confirmation: no forbidden modeling was added

- No Department API, Position API, or `position_assignments` changes.
- No new `legal_entity_membership`/`company_membership`/similar table.
- No `LegalEntityId` added to `UserRole`.
- No Employee, Department, or Position rows are created by this seeder at all (see below).

## Employee rows: intentionally not created

`Employee.LegalEntityId` (nullable) already models "one employee → one legal entity" today, and
`employees` has a unique index on `UserId` (`EmployeeConfiguration.cs`), making "one user, many
employees" structurally impossible even if a row existed. The task's own required seed result,
idempotency list, and legal-entity rules never mention Employee rows — only tenants, users, roles,
legal entities, `global_email_directory`, subscriptions, and `tenant_auth_policies`. Creating
Employee rows here would be inventing org-structure data the task explicitly says not to invent,
and would require guessing at `EmployeeNumber`, `HireDate`, and lookup-table IDs with no
authoritative source. This seeder therefore creates **zero** Employee rows for any of the four
seeded users, which is verified by
`SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager`.

Multi-legal-entity access for mrt15473@gmail.com is intentionally deferred until Department APIs,
Position APIs, and then the Phase 1 position_assignments / authority assignment model are
implemented in that order.
