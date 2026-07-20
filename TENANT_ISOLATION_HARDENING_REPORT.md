# Tenant Isolation Hardening — Report

## Docs read

- `C:\onevoNew\HRMS-Backend-v1\docs\superpowers\plans\2026-07-19-tenant-isolation-hardening.md` — the implementation plan this report documents the execution of.
- `C:\onevoNew\OneVo-HR\database\phase1-table-inventory.md` — exact column definitions for the tenant-owned tables touched (`users`, `roles`, `mfa_challenges`, `tenant_storage_stats`, `file_records`, `file_upload_reservations`, `positions`, `user_integration_connections`).
- `C:\onevoNew\OneVo-HR\database\schemas\shared-platform.md` — cross-checked `tenant_integration_credentials` and `user_integration_connections` schema/ownership.
- `C:\onevoNew\ONEVO_Backend_Architecture_Document.md` — multi-tenancy and RLS architecture expectations.
- `C:\onevoNew\HRMS-Backend-v1\FILE_STORAGE_FOUNDATION_STEP_REPORT.md` — the step report that originally surfaced both defects fixed here ("Tenant isolation — a real gap found and closed" section).

## Exact defects confirmed

1. **EF Core query-filter model-caching closure bug.** `ApplicationDbContext.OnModelCreating` built its tenant `HasQueryFilter` by embedding `Expression.Constant(_tenantContext)` — a direct reference to the specific `ITenantContext` (`TenantContextAccessor`) instance injected into whichever `ApplicationDbContext` happened to trigger EF's compiled-model build first. EF Core caches the compiled `IModel` once per process/`DbContextOptions` fingerprint, so every later `ApplicationDbContext` instance — even with a completely different tenant resolved on its own `ITenantContext` — silently reused the first instance's frozen tenant/mode inside the query filter. Confirmed reproducible with a unit test (`ApplicationDbContextTenantFilterTests`) that failed against the pre-fix code: a context resolved to tenant A returned both tenant A's and tenant B's rows, because the model had been built from a `System`-mode seed context whose filter always bypassed tenant scoping.
2. **Runtime PostgreSQL connection role was not structurally guaranteed to be non-superuser.** `appsettings.Development.json` connected as the literal PostgreSQL superuser `postgres`. `appsettings.json` (the deployable template) connected as `onevo` with no separate migration role and no documented restriction, so nothing prevented the same role from being granted superuser or `BYPASSRLS` in any real environment. PostgreSQL superusers and `BYPASSRLS` roles always skip Row-Level Security regardless of `FORCE ROW LEVEL SECURITY`, so every `tenant_isolation` RLS policy in the schema was unenforced for the actual runtime connection.

## Additional gap found and closed (beyond the two originally-named tables)

While auditing every `ITenantOwnedEntity` implementer against the RLS migration table lists (`AddRlsPolicies`, `UpdateRlsTenantContextMode`, `AddFileStorageRlsPolicies`), five tables were found with **no RLS policy at all**:

- `tenant_storage_stats` and `mfa_challenges` — the two tables `FILE_STORAGE_FOUNDATION_STEP_REPORT.md` already flagged as a known gap.
- `positions`, `user_integration_connections`, `tenant_integration_credentials` — found during this audit; not previously documented as a gap anywhere.

All five are closed by the new `AddMissingRlsPolicies` migration. This went beyond the two tables explicitly named in the task because the Part 3 architecture guard required by this task ("New tenant-owned entities are added without RLS policy coverage") checks *every* `ITenantOwnedEntity` in the model, not a fixed list — leaving the other three uncovered would have made that guard fail on day one for reasons unrelated to the two named defects, and would have left three tenant-owned tables genuinely unprotected by RLS.

## Exact files changed

- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — added `IsTenantFilterActive`/`CurrentTenantId` instance properties; rewrote `OnModelCreating`'s tenant filter to reference `this` instead of the injected `ITenantContext`.
- `src/ONEVO.Infrastructure/Migrations/20260719180411_AddMissingRlsPolicies.cs` (+ `.Designer.cs`) — new additive migration adding the `tenant_isolation` RLS policy to `tenant_storage_stats`, `mfa_challenges`, `positions`, `user_integration_connections`, `tenant_integration_credentials`.
- `src/ONEVO.Api/appsettings.json`, `src/ONEVO.Api/appsettings.Development.json` — `DefaultConnection` renamed to a restricted role (`onevo_app`); new `MigrationConnection` naming an elevated role (`onevo_migrator`).
- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContextFactory.cs` — prefers `MigrationConnection`, falls back to `DefaultConnection`.
- `scripts/db/bootstrap-roles.sql`, `scripts/db/README.md` (new) — dev/test bootstrap creating `onevo_migrator`/`onevo_app` and documenting the workflow.
- `src/ONEVO.Infrastructure/Configuration/ConfigurationStartupValidator.cs` — added a non-fatal boot warning if `DefaultConnection`'s username looks like a superuser role.
- `tests/ONEVO.Tests.Unit/Features/Infrastructure/ApplicationDbContextTenantFilterTests.cs` (new).
- `tests/ONEVO.Tests.Unit/Features/Infrastructure/ConfigurationStartupValidatorTests.cs` (new).
- `tests/ONEVO.Tests.Integration/Security/RestrictedRoleRlsEnforcementTests.cs` (new).
- `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs` (new); `tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj` (added `Microsoft.EntityFrameworkCore.InMemory` package reference for model-inspection contexts).
- `TENANT_ISOLATION_HARDENING_REPORT.md` (this file).

## EF model-caching issue and the chosen fix

EF Core caches the compiled `IModel` (including every entity's `HasQueryFilter` expression tree) once per process, keyed internally by the `DbContextOptions` fingerprint — not per `DbContext` instance. `ApplicationDbContext.OnModelCreating` built its tenant predicate with `Expression.Constant(_tenantContext)`, which bakes the *specific object reference* of whichever `ITenantContext` happened to be injected into the first `ApplicationDbContext` ever constructed under a given options fingerprint directly into that cached, shared expression tree. Every subsequent `ApplicationDbContext` built from options with the same fingerprint (the normal case in production, since `AddInfrastructure` always configures the same connection string, provider, and interceptor types) reuses the cached model, and therefore reuses that first frozen `ITenantContext` object — regardless of which tenant the current request actually resolved.

The fix adds two instance properties to `ApplicationDbContext` — `IsTenantFilterActive` and `CurrentTenantId` — that read through to the context's own `_tenantContext` field, and rewrites the filter to reference `this` (`Expression.Constant(this, typeof(ApplicationDbContext))` plus property access) instead of the injected service. EF Core specially resolves references to the declaring `DbContext` instance inside a query filter per execution, substituting the actual context instance running the query rather than baking in a fixed value — this is the same mechanism the official EF Core documentation describes for context-instance-scoped query filters. The result: the compiled model is still cached once per process (no performance regression), but the filter's *value* is now always read fresh from whichever `ApplicationDbContext` instance is actually executing the query.

Verified with `ApplicationDbContextTenantFilterTests`: two `ApplicationDbContext` instances built from the literal same `DbContextOptions` object (guaranteeing they share the cached model, exactly mirroring production's `AddDbContext` reuse) but constructed with different resolved tenants each see only their own tenant's rows. Confirmed this test fails against the pre-fix code (both contexts returned both tenants' rows, because the model was built from an unresolved `System`-mode seed context whose filter always bypassed tenant scoping).

## PostgreSQL RLS superuser/BYPASSRLS behavior and the chosen fix

PostgreSQL superusers unconditionally bypass Row-Level Security, and any role with the `BYPASSRLS` attribute does too — in both cases regardless of whether the table has `FORCE ROW LEVEL SECURITY` set. This means an application connecting as `postgres` (or any other superuser/`BYPASSRLS` role) never actually has its queries filtered by the `tenant_isolation` policies, even though the policies are present and correctly defined.

The fix introduces two distinct PostgreSQL roles per environment:
- `onevo_migrator` — owns the schema objects it creates (via `CREATE` privilege on the `public` schema), which lets it run `ALTER TABLE ... FORCE ROW LEVEL SECURITY` / `CREATE POLICY` as table owner without needing superuser. Used only by `ApplicationDbContextFactory` (i.e. `dotnet ef` tooling) via the new `ConnectionStrings:MigrationConnection`.
- `onevo_app` — `NOSUPERUSER NOBYPASSRLS`, granted only `SELECT`/`INSERT`/`UPDATE`/`DELETE` (no DDL). Used by the running API via the existing `ConnectionStrings:DefaultConnection` (unchanged call site in `DependencyInjection.AddInfrastructure`; only the connection string *value* changed).

Because both new tables' RLS policies use `FORCE ROW LEVEL SECURITY`, even `onevo_migrator` (the table owner) is subject to RLS on subsequent DML — it is not used for runtime app queries, only for schema migrations.

## Runtime role name used in dev/test

- `onevo_app` — dev/test `ConnectionStrings:DefaultConnection` role (`appsettings.Development.json`, `scripts/db/bootstrap-roles.sql`).
- `rls_enforcement_test_role` — Testcontainers-only, created inline per test run by `RestrictedRoleRlsEnforcementTests`.
- `file_storage_test_role` — pre-existing Testcontainers-only role from `FileStorageIntegrationTests` (unchanged by this task).

## Migration/admin role separation

- `onevo_migrator` — dev/test `ConnectionStrings:MigrationConnection` role, consumed only by `ApplicationDbContextFactory` (design-time `dotnet ef` tooling). Owns every table it creates going forward; `ALTER DEFAULT PRIVILEGES` in `scripts/db/bootstrap-roles.sql` automatically grants `onevo_app` DML on every table `onevo_migrator` creates from now on, so future migrations need no manual grant step.

## Test evidence

| Test file | What it proves |
|---|---|
| `tests/ONEVO.Tests.Unit/Features/Infrastructure/ApplicationDbContextTenantFilterTests.cs` | Two `ApplicationDbContext` instances sharing the same cached compiled model, resolved to different tenants, each see only their own tenant's rows (fails against the pre-fix closure-capture pattern). |
| `tests/ONEVO.Tests.Unit/Features/Infrastructure/ConfigurationStartupValidatorTests.cs` | Boot-time warning fires only when `DefaultConnection`'s username looks like a superuser role. |
| `tests/ONEVO.Tests.Integration/Security/RestrictedRoleRlsEnforcementTests.cs` | Through a real, dedicated non-superuser/non-BYPASSRLS PostgreSQL role: `users`, `roles`, `mfa_challenges`, `tenant_storage_stats`, `file_records`, and `file_upload_reservations` are all tenant-isolated by RLS; the role itself is confirmed non-superuser/non-BYPASSRLS via `pg_roles`; a connection with no tenant setting resolved sees zero rows rather than falling back to cross-tenant visibility. |
| `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs` | No tenant query filter captures an `ITenantContext` instance as a constant; every `ITenantOwnedEntity` has a query filter; `DefaultConnection` never names a superuser-looking role; no non-migration source disables RLS; no migration disables RLS or grants BYPASSRLS in its `Up()` direction; `IgnoreQueryFilters` has zero unaudited call sites; every tenant-owned table has RLS policy coverage across the migration history. |

Command-level results are recorded in the "Verification run" section below (populated after the full suite run).

### Verification run

_Populated by the final full-suite verification pass._

## Remaining risks

- **Production secrets are placeholders.** `appsettings.json`'s `onevo_app`/`onevo_migrator` passwords are `CHANGE_ME` placeholders, matching the file's existing convention (e.g. `Jwt:Secret`, `Encryption:MasterKey`). An operator must run an equivalent of `scripts/db/bootstrap-roles.sql` with real, unique secrets against the actual production database and confirm the deployed connection string matches — this task cannot verify or configure a live production database's actual role grants.
- **Pre-existing, adjacent query-filter overwrite behavior (not introduced or fixed by this task).** `UserRoleConfiguration` and `RolePermissionConfiguration` each call `HasQueryFilter(x => !x.Role.IsDeleted)` via `IEntityTypeConfiguration`. `ApplicationDbContext.OnModelCreating` calls `ApplyConfigurationsFromAssembly` first, then unconditionally calls `HasQueryFilter` again for every `ITenantOwnedEntity` (including `UserRole`/`RolePermission`, both of which have a `TenantId`). EF Core's `HasQueryFilter` **replaces** rather than combines filters, so the generic tenant-filter loop silently drops the `!Role.IsDeleted` predicate for these two entities — both before and after this task's fix (the fix changed *what* the tenant filter references, not *whether* it overwrites). This is unrelated to tenant isolation (it concerns soft-deleted-role visibility, not cross-tenant leakage) and is explicitly out of scope for this task's two named defects; flagged here because it was discovered during the audit.
- **The rest of the integration suite still runs through the Testcontainers default superuser role.** `AdminTestFactory` and similar `WebApplicationFactory`-based tests continue to exercise business logic through the Testcontainers superuser connection, matching prior practice — only the new `RestrictedRoleRlsEnforcementTests` suite specifically exercises a restricted role. Retrofitting the entire integration suite to a restricted role was judged out of scope (large blast radius across unrelated business-logic tests, no business-logic reason to require it).
- **`positions`, `user_integration_connections`, `tenant_integration_credentials` RLS coverage is proven only by migration + architecture guard, not a dedicated restricted-role DML integration test.** The task's explicit restricted-role test list was `users`, `roles`, `file_records`, `file_upload_reservations`, `tenant_storage_stats`, `mfa_challenges`; these three additional tables (discovered during this audit) have their RLS policy proven present by `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` and confirmed to exist in a live database by the Task 2 Docker verification (`pg_policies` inspection), but do not have a dedicated per-row isolation integration test.

## Confirmation no business tables were created

Only RLS policies were added, to five *existing* tables (`tenant_storage_stats`, `mfa_challenges`, `positions`, `user_integration_connections`, `tenant_integration_credentials`). `AddMissingRlsPolicies`'s `Up()` method contains no `CreateTable` call — verified by inspection and by the migration applying cleanly against a fresh throwaway PostgreSQL container where every referenced table already existed from prior migrations.

## Confirmation Onevo_Backend was not touched

No file under `C:\onevoNew\Onevo_Backend` was read or modified during this task. All work was confined to `C:\onevoNew\HRMS-Backend-v1`.
