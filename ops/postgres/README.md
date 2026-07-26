# Local PostgreSQL setup

This directory contains local development and test database setup tooling. It
is not API runtime code and is not production or staging provisioning.

ONEVO uses three PostgreSQL roles:

| Role | Connection | Purpose |
|---|---|---|
| `onevo_app` | `DefaultConnection` | Restricted runtime API access. It is `NOSUPERUSER`, `NOBYPASSRLS`, and has no schema-changing privileges. |
| `onevo_migrator` | `MigrationConnection` | Explicit EF schema migration only. It is not a superuser, cannot bypass RLS, and is `NOCREATEROLE` - it cannot create either of the other two roles. |
| `onevo_auth_base_login_fn_owner` | Never connects (`NOLOGIN`) | Owns only the `auth_lookup_base_login_candidates` `SECURITY DEFINER` function, so that one function can run with `BYPASSRLS` regardless of the caller's RLS session, without any session ever being able to authenticate as this role directly. |

The API must never connect as `postgres`, another administrator, or a role
with `BYPASSRLS`. It never executes the bootstrap SQL or EF migrations during
startup.

## Required deploy order

Role provisioning is always a separate, explicit step - never something an EF
migration does silently. The `20260724174557_AddAuthLookupBaseLoginCandidatesFunction`
migration assumes `onevo_auth_base_login_fn_owner` and `onevo_app` already
exist; if either is missing, its `ALTER FUNCTION`/`GRANT` statements fail with
a clear PostgreSQL "role ... does not exist" error instead of the migration
silently creating a `BYPASSRLS`-capable role. The required order, in every
environment, is:

1. **Bootstrap privileged roles** (`ops/postgres/local-bootstrap-roles.sql`,
   run by `setup-local-db.ps1` locally, or the equivalent DB/deployment-owned
   bootstrap step in staging/production) - creates/repairs `onevo_app`,
   `onevo_migrator`, and `onevo_auth_base_login_fn_owner` with their fixed
   attributes and grants. This step runs against a database that may not have
   any schema yet, so it must only touch roles and schema-level privileges -
   never `public.users`, `public.tenants`, or any other migrated table/
   function by name. It also grants `onevo_migrator` (only) database-level
   `CREATE` on the target database, because migrations that create a new
   schema (for example `CREATE SCHEMA IF NOT EXISTS auth_internal` in
   `20260724174557_AddAuthLookupBaseLoginCandidatesFunction`) fail with
   `permission denied for database ...` without it - `CREATE` on the `public`
   schema alone is not enough to create a sibling schema. `onevo_app` never
   receives this grant.
2. **Run EF migrations** as `onevo_migrator` (`MigrationConnection`). This
   creates `public.users`, `public.tenants`, `auth_internal`, and
   `auth_internal.auth_lookup_base_login_candidates`.
3. **Apply post-migration object grants**
   (`ops/postgres/local-post-migration-grants.sql`, run by `setup-local-db.ps1`
   when `-RunMigrations` is passed) - grants `onevo_auth_base_login_fn_owner`
   column-level `SELECT` on exactly the `public.users`/`public.tenants`
   columns `auth_lookup_base_login_candidates` needs. These grants cannot run
   in step 1 because the tables do not exist until step 2 has run; running
   them before migrations fails with a PostgreSQL
   `relation "users" does not exist` error.
4. **Run the API** as `onevo_app` (`DefaultConnection`).

Running `setup-local-db.ps1` without `-RunMigrations` performs step 1 only and
prints a message that step 3 was skipped, since the tables it grants on may
not exist yet.

Testcontainers-backed integration tests replicate step 1 in-process
(`PrivilegedRoleTestBootstrap.EnsureRolesExistAsync`) against their own
ephemeral database before migrating, since there is no separate deploy
pipeline for a disposable test container.

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

Immediately after the EF migration command succeeds, `-RunMigrations` also
runs `ops/postgres/local-post-migration-grants.sql` as the admin role to apply
the post-migration object grants described above. Without `-RunMigrations`,
the helper prints a message that this step was skipped instead of running it,
since `public.users`/`public.tenants` may not exist yet.

## Advanced troubleshooting

The SQL helper can be inspected or executed directly by a PostgreSQL
administrator, but it requires the same psql variables supplied by the
PowerShell helper. The supported workflow is to rerun `setup-local-db.ps1`.
Do not put password-bearing `psql -v` commands in shell history or docs.

Production and staging use the same two-role architecture but must provision
equivalent roles and secrets through the hosting/database/deployment process.
