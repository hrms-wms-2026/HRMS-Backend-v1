# Local PostgreSQL setup

This directory contains local development and test database setup tooling. It
is not API runtime code and is not production or staging provisioning.

ONEVO uses two PostgreSQL roles:

| Role | Connection | Purpose |
|---|---|---|
| `onevo_app` | `DefaultConnection` | Restricted runtime API access. It is `NOSUPERUSER`, `NOBYPASSRLS`, and has no schema-changing privileges. |
| `onevo_migrator` | `MigrationConnection` | Explicit EF schema migration only. It is not a superuser and cannot bypass RLS. |

The API must never connect as `postgres`, another administrator, or a role
with `BYPASSRLS`. It never executes the bootstrap SQL or EF migrations during
startup.

## First-time local setup

Run from PowerShell:

```powershell
cd C:\onevoNew\HRMS-Backend-v1
Copy-Item .env.example .env
notepad .env
# Replace both local database password placeholders, then save the file.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass  # only if local scripts are blocked
.\ops\postgres\setup-local-db.ps1 -RunMigrations
dotnet run --project src\ONEVO.Api\ONEVO.Api.csproj
```

`.env` is intentionally absent from git and is ignored by `.gitignore`. It
stores each local database password once in `ONEVO_DB_APP_PASSWORD` and
`ONEVO_DB_MIGRATOR_PASSWORD`. Never commit a populated `.env`.

The API loads `.env` automatically before ASP.NET Core configuration is built.
It constructs `DefaultConnection` and `MigrationConnection` from the atomic
`ONEVO_DB_*` values. Full `ConnectionStrings__*` assignments in `.env` are
ignored so passwords are not duplicated. Explicit host/process connection
string environment variables still take precedence and are not overwritten.

## Normal later run

Once the database, roles, passwords, and migrations are prepared, setup is not
required on every run:

```powershell
cd C:\onevoNew\HRMS-Backend-v1
dotnet run --project src\ONEVO.Api\ONEVO.Api.csproj
```

If `.env` is missing, incomplete, or still contains password placeholders, the
API fails before seeders and background services start and explains how to
repair the local configuration.

## When to rerun setup

Rerun the helper when:

- the local database is missing or was dropped;
- either PostgreSQL role is missing;
- a role password changed or became stale;
- schema migrations need to be applied; or
- local grants or role restrictions need repair.

PostgreSQL roles are cluster-level objects. Dropping `OnevoDb` does not drop
`onevo_app` or `onevo_migrator`. The helper always applies `ALTER ROLE`, so it
repairs existing passwords and restrictions instead of only creating missing
roles.

The helper reads `.env`, creates the database when missing, provisions both
roles, grants current and future privileges, and prints only the database name
and usernames. It never prints passwords. With `-RunMigrations`, it runs:

```powershell
dotnet ef database update --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj
```

EF tooling requires `MigrationConnection` and cannot fall back to the runtime
`DefaultConnection`. The helper may also set both process connection strings
for convenience, but normal `dotnet run` does not depend on that side effect.

## Advanced troubleshooting

The SQL helper can be inspected or executed directly by a PostgreSQL
administrator, but it requires the same psql variables supplied by the
PowerShell helper. The supported workflow is to rerun `setup-local-db.ps1`.
Do not put password-bearing `psql -v` commands in shell history or docs.

Production and staging use the same two-role architecture but must provision
equivalent roles and secrets through the hosting/database/deployment process.
