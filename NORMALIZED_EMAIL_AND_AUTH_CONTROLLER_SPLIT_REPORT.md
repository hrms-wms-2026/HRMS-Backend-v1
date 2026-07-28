# Normalized Email and Auth Controller Split Report

## CI integration startup fix

### Symptom

Integration tests failed in CI (never locally) at `WebApplicationFactory.CreateClient()`,
most visibly in `BaseDomainLoginIntegrationTests.InitializeAsync` (line 51), before any test
body ran.

### Root cause

`Program.cs` runs two startup validators as top-level statements, before
`WebApplicationFactory.ConfigureWebHost` is ever applied:

- `ConfigurationStartupValidator.ValidateRequiredLocalConfiguration(...)`
- `DatabaseConnectionStartupValidator.ValidateAndOpenAsync(...)`

Both read `builder.Configuration` at that point in `Program.cs`, which - for a fresh process -
only contains appsettings and whatever is already in the process's environment variables.
`ConfigureWebHost`'s `config.AddInMemoryCollection(...)` calls run later, so
`ConnectionStrings:DefaultConnection`, `ConnectionStrings:MigrationConnection`,
`Encryption:MasterKey`, and the `Jwt:*` keys supplied there arrive too late for these validators.

Locally this was masked: the repo-root `.env` file loads these same values as process
environment variables (via `DotEnvLoader.LoadIfPresent()`) before `Program.cs`'s validator calls
run, so `dotnet test` passed. CI has no `.env` file, so `builder.Configuration` was missing
`Encryption:MasterKey` (failing `ConfigurationStartupValidator`) and/or had no valid
`onevo_app`/`onevo_migrator` connection strings to open (failing
`DatabaseConnectionStartupValidator`).

A second, compounding issue: `DatabaseConnectionStartupValidator.ValidateAndOpenAsync` actually
opens a real PostgreSQL connection as `onevo_app`. `PrivilegedRoleTestBootstrap` (used by every
Testcontainers-backed integration test) created `onevo_app` as `NOLOGIN`, so even with the right
connection string in place, authentication would still fail.

### Fix

1. **`PrivilegedRoleTestBootstrap`** - in Testcontainers only, `onevo_app` and `onevo_migrator`
   are now created `LOGIN` with deterministic test passwords
   (`PrivilegedRoleTestBootstrap.AppRolePassword` / `MigratorRolePassword`), so the pre-`Build()`
   validator can actually authenticate. `onevo_app` remains `NOBYPASSRLS` - only its LOGIN-ability
   changes from production. `onevo_auth_base_login_fn_owner` is unchanged (`NOLOGIN BYPASSRLS`).
   This LOGIN/password behavior is test-only and must never be copied into
   `ops/postgres/local-bootstrap-roles.sql` or any production/deploy role bootstrap.

2. **`IntegrationDatabaseBootstrap`** (new) - given the Testcontainers admin/superuser connection
   string, creates the privileged roles and runs EF migrations, before any
   `WebApplicationFactory<Program>` is touched. Consolidates the `MigrateDatabaseAsync` logic that
   was previously duplicated across `AdminTestFactory`, `ApiBootTests`, and
   `BaseDomainLoginIntegrationTests`.

3. **`IntegrationTestEnvironmentScope`** (new) - builds `onevo_app`/`onevo_migrator` connection
   strings against the same ephemeral Testcontainers database and sets the process-level
   environment variables `Program.cs`'s validators need
   (`ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__DefaultConnection`,
   `ConnectionStrings__MigrationConnection`, `Encryption__MasterKey`, `Jwt__Secret`,
   `Jwt__TenantIssuer`, `Jwt__TenantAudience`, `DevAdmin__Email`, `DevAdmin__Password`,
   `PlatformBootstrap__SuperAdminEmail`, `PlatformBootstrap__SuperAdminFullName`,
   `Tenancy__RootDomain`) - the same variables the repo-root `.env` supplies locally. It saves
   every previous value on construction and restores it (`Dispose`/`DisposeAsync`), so it never
   leaks state across tests.

4. **`WebApplicationFactoryCollection`** (new xUnit collection) - process environment variables
   are global, and xUnit runs distinct test classes in parallel by default. Every test class that
   constructs a `WebApplicationFactory<Program>` via `IntegrationTestEnvironmentScope` is now tagged
   `[Collection(WebApplicationFactoryCollection.Name)]`, serializing the window between "set the
   env vars" and "the factory's first host build" across all of them, so two tests can never race
   and boot a host against the wrong ephemeral database. Tests that build `ApplicationDbContext`
   directly instead of going through `WebApplicationFactory` (e.g.
   `RestrictedRoleRlsEnforcementTests`, `StorageQuotaIntegrationTests`) do not touch process
   environment variables and keep running in parallel with everything else. This collection was
   not explicitly requested but is required for the fix to be reliable, rather than merely
   working by accident of scheduling.

5. Every `WebApplicationFactory<Program>`-based integration test class
   (`BaseDomainLoginIntegrationTests`, `ApiBootTests`, `TenantsAdminApiIntegrationTests`,
   `PlatformAdminAuthIntegrationTests`, `ConfigurationTemplateManagerIntegrationTests`,
   `UserIntegrationConnectionPersistenceTests`, `TenantProvisioningE2ETests`) now creates an
   `IntegrationTestEnvironmentScope` and runs `IntegrationDatabaseBootstrap`/
   `AdminTestFactory.MigrateDatabaseAsync` before constructing its factory or calling
   `CreateClient()`, and disposes the scope in `DisposeAsync`. The existing `ConfigureWebHost`
   overrides (test doubles for `ITotpService`, `IGoogleIdTokenValidator`,
   `IPlatformOAuthAppResolver`, and the `ApplicationDbContext` registration) are unchanged - they
   still matter for post-`Build()` config consumers (e.g. the `/health/ready` check) and for
   wiring test doubles, but they are no longer the only source of the config
   `Program.cs`'s pre-`Build()` validators need.

### What did not change

- No test assertions were rewritten.
- No tests were skipped.
- Production role bootstrap (`ops/postgres/local-bootstrap-roles.sql`) and production migration
  behavior were not touched or weakened - `onevo_app`/`onevo_migrator` LOGIN-with-deterministic-
  password only exists inside `PrivilegedRoleTestBootstrap`, which only ever runs against
  Testcontainers-created databases.
- `onevo_app` is still `NOBYPASSRLS` everywhere, test and production alike.
