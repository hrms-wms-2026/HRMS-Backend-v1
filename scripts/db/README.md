# Database role bootstrap (dev/test)

ONEVO uses two PostgreSQL roles per environment:

| Role | Used by | Privileges |
|---|---|---|
| `onevo_migrator` | `dotnet ef database update` / `dotnet ef migrations add`, via `ConnectionStrings:MigrationConnection` | Owns the schema objects it creates; can run DDL, `FORCE ROW LEVEL SECURITY`, `CREATE POLICY`. **Not** superuser, **not** BYPASSRLS. |
| `onevo_app` | The running API, via `ConnectionStrings:DefaultConnection` | Normal `SELECT`/`INSERT`/`UPDATE`/`DELETE` only. No DDL. **Not** superuser, **not** BYPASSRLS — this is what makes PostgreSQL Row-Level Security actually apply to every request the app makes. |

Neither role is ever `postgres` or another superuser. A superuser (or any role
with `BYPASSRLS`) silently ignores `FORCE ROW LEVEL SECURITY`, which would
make the `tenant_isolation` policies on every tenant-owned table a no-op.

## First-time setup (local dev)

1. Create the target database as an existing superuser, e.g.:
   ```
   createdb -U postgres OnevoDb
   ```
2. Run the bootstrap script **before** the first migration:
   ```
   psql -h localhost -U postgres -d OnevoDb -f scripts/db/bootstrap-roles.sql
   ```
3. Run migrations (uses `ConnectionStrings:MigrationConnection` from
   `appsettings.Development.json`, i.e. `onevo_migrator`):
   ```
   dotnet ef database update --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.Api/ONEVO.Api.csproj
   ```
4. Run the API normally (uses `ConnectionStrings:DefaultConnection`, i.e.
   `onevo_app`).

## Adding a migration later

`dotnet ef migrations add <Name>` and `dotnet ef database update` both go
through `ApplicationDbContextFactory`, which prefers
`ConnectionStrings:MigrationConnection`. No extra grants are needed for new
tables — `ALTER DEFAULT PRIVILEGES` in the bootstrap script already covers
every table `onevo_migrator` creates from now on.

## CI / Testcontainers

Integration tests that specifically prove RLS enforcement
(`tests/ONEVO.Tests.Integration/Security/RestrictedRoleRlsEnforcementTests.cs`)
create an equivalent restricted role inline against their own throwaway
Testcontainers Postgres instance — they do not depend on this script. The
rest of the integration suite still runs migrations and app logic through the
Testcontainers default superuser role, matching prior practice; only the
RLS-enforcement-focused tests need a non-superuser connection to be
meaningful.
