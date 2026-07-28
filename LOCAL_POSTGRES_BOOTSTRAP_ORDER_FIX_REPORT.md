# Local PostgreSQL Setup Ordering Fix

## Revision 2

The first pass fixed the `users`/`tenants`-does-not-exist failure but missed
a second, later failure on a fresh database:

```
Applying migration '20260724174557_AddAuthLookupBaseLoginCandidatesFunction'.
...
CREATE SCHEMA IF NOT EXISTS auth_internal;
42501: permission denied for database OnevoDb
```

`GRANT CREATE, USAGE ON SCHEMA public TO onevo_migrator` only allows creating
objects *inside* `public`; creating a new schema (`auth_internal`) requires
`CREATE` on the database itself. Fix, scoped to `onevo_migrator` only:

- `ops/postgres/local-bootstrap-roles.sql` - added a new `db_name` psql
  variable and `GRANT format('GRANT CREATE ON DATABASE %I TO %I', :'db_name', :'migrator_user')`,
  placed alongside the other migrator schema grant. `onevo_app` is not
  touched by this grant.
- `ops/postgres/setup-local-db.ps1` - `$bootstrapArguments` now also passes
  `--set "db_name=$databaseName"` to `local-bootstrap-roles.sql`.
- `ops/postgres/local-post-migration-grants.sql` - tightened
  `REVOKE SELECT ON users, tenants` to `REVOKE SELECT ON public.users, public.tenants`
  for consistency with the fully-qualified GRANTs in the same file.
- `ops/postgres/README.md` - documents why `onevo_migrator` needs
  database-level `CREATE` and that `onevo_app` never receives it.

Re-verified after this change: `dotnet build` (0 errors), `dotnet test`
architecture suite (221/221 passing), PowerShell parser validation (clean),
`rg` broad-grant check (only the expected `NormalizedEmailArchitectureTests.cs`
assertion match, no SQL file matches), `git diff --check` (exit 0).

## Revision 3

After Revision 2, the fresh-DB flow got past `CREATE SCHEMA IF NOT EXISTS
auth_internal` (confirming the database-level `CREATE` grant works) but then
failed on the next statement in the same migration:

```
ALTER FUNCTION auth_internal.auth_lookup_base_login_candidates(varchar)
    OWNER TO onevo_auth_base_login_fn_owner;
42501: permission denied for schema auth_internal
```

`ALTER FUNCTION ... OWNER TO` requires the *new* owner to hold `CREATE` on the
function's schema at the moment ownership transfers - `USAGE` alone (which the
migration already granted) is not enough. This is a defect in the migration
itself, not in `ops/postgres/*`, so no post-migration-grants change could
have fixed it - the failure happens before that file ever runs.

Fix, in
`src/ONEVO.Infrastructure/Migrations/20260724174557_AddAuthLookupBaseLoginCandidatesFunction.cs`,
`Up()`: grant `onevo_auth_base_login_fn_owner` temporary `CREATE` on
`auth_internal` immediately before the `ALTER FUNCTION ... OWNER TO`
statement, then revoke `CREATE` and re-grant `USAGE`-only right after, so the
role ends the migration with exactly the same least-privilege shape it had
before (`USAGE` only, no `CREATE`):

```csharp
migrationBuilder.Sql($"GRANT USAGE, CREATE ON SCHEMA auth_internal TO {FunctionOwnerRole};");
migrationBuilder.Sql($"ALTER FUNCTION auth_internal.auth_lookup_base_login_candidates(varchar) OWNER TO {FunctionOwnerRole};");
migrationBuilder.Sql($"REVOKE CREATE ON SCHEMA auth_internal FROM {FunctionOwnerRole};");
migrationBuilder.Sql($"GRANT USAGE ON SCHEMA auth_internal TO {FunctionOwnerRole};");
```

`Down()` was not changed - it only drops the function and never transfers
ownership, so it never needed `CREATE`.

Verified this does not weaken any architecture guard: `Migrations_NeverCreateRoles`,
`AssertAllowlistedBypassRlsMigrationIsExactlyApproved`'s BYPASSRLS/role-creation
checks, and its exactly-one `ALTER FUNCTION`/`REVOKE ALL`/`GRANT EXECUTE`
counts (`TenantIsolationArchitectureTests.cs`) all still pass - the added
statements only touch schema-level `CREATE`/`USAGE`, not role creation,
BYPASSRLS, or the function's own owner/execute grants.

Re-verified after this change: `dotnet build` (0 errors), `dotnet test`
architecture suite (221/221 passing), `git diff --check` (exit 0).

## Root cause

`ops/postgres/setup-local-db.ps1` ran `local-bootstrap-roles.sql` as the only
SQL step, before any EF migration. That file ended with column-level grants
naming `users` and `tenants` directly (`REVOKE SELECT ON users, tenants ...`,
`GRANT SELECT (...) ON public.users ...`, `GRANT SELECT (...) ON
public.tenants ...`). On a fresh database those tables do not exist until EF
migrations create them, so PostgreSQL raised `relation "users" does not
exist` and setup failed before migrations ever ran.

## Files changed

- `ops/postgres/local-bootstrap-roles.sql` - removed the `users`/`tenants`
  REVOKE/GRANT block. Now contains only role creation, password/attribute
  repair, schema-level grants, default privileges, and the
  `onevo_auth_base_login_fn_owner` role setup/membership grant - all valid
  before any table exists.
- `ops/postgres/local-post-migration-grants.sql` (new) - holds the
  `users`/`tenants` REVOKE + column-level GRANTs that require migrated
  objects to exist. Idempotent (REVOKE-then-GRANT, `CREATE ... WHERE NOT
  EXISTS` pattern not needed since GRANT/REVOKE are already idempotent).
- `ops/postgres/setup-local-db.ps1` - added `$postMigrationGrantsPath`; when
  `-RunMigrations` is passed, runs `local-post-migration-grants.sql` as the
  admin role (its own `PGPASSWORD` try/finally) immediately after the EF
  migration command block succeeds. When `-RunMigrations` is not passed,
  prints a message that this step was skipped instead of failing.
- `ops/postgres/README.md` - documents the three-step order (pre-migration
  role bootstrap -> EF migrations -> post-migration object grants) and why
  step 3 cannot run before step 2.
- `tests/ONEVO.Tests.Architecture/BaseLoginArchitectureTests.cs` -
  `FunctionOwnerRoleGrants_AreColumnLevelOnly_NotBroadTableSelect` now asserts
  `local-bootstrap-roles.sql` contains neither `public.users` nor
  `public.tenants`, and that the two exact column-level grants live in
  `local-post-migration-grants.sql` instead.
- `tests/ONEVO.Tests.Architecture/LocalDatabaseRuntimeArchitectureTests.cs` -
  added `LocalBootstrapRolesSql_DoesNotReferenceUsersOrTenantsTables` and
  `LocalSetupScript_RunsPostMigrationGrantsOnlyAfterEfMigrationCommand`
  (asserts the post-migration grants argument block appears after the `ef
  database update` invocation in the script text).

## Before/after execution order

**Before:**
1. Create database if missing.
2. Run `local-bootstrap-roles.sql` (included `users`/`tenants` grants) -
   **fails on a fresh database**.
3. (never reached) `dotnet ef database update`.

**After:**
1. Create database if missing.
2. Run `local-bootstrap-roles.sql` (roles + schema-level grants only) -
   succeeds on a fresh database.
3. If `-RunMigrations`: run `dotnet ef database update`.
4. If `-RunMigrations`: run `local-post-migration-grants.sql` as admin.
5. If not `-RunMigrations`: print a message that step 4 was skipped.

## Proof: pre-migration file no longer references users/tenants

```
tests/ONEVO.Tests.Architecture/LocalDatabaseRuntimeArchitectureTests.cs
  LocalBootstrapRolesSql_DoesNotReferenceUsersOrTenantsTables -> PASS
tests/ONEVO.Tests.Architecture/BaseLoginArchitectureTests.cs
  FunctionOwnerRoleGrants_AreColumnLevelOnly_NotBroadTableSelect -> PASS
rg -n "GRANT SELECT ON users|GRANT SELECT ON public\.users|REVOKE SELECT ON users, tenants" ops/postgres src tests
  -> only match is ops/postgres/local-post-migration-grants.sql (the
     intentional post-migration REVOKE); local-bootstrap-roles.sql has no
     match.
```

## Proof: post-migration grants contain the exact column-level grants

```
rg -n "GRANT SELECT \(tenant_id, id, normalized_email, is_active, is_deleted, password_hash\) ON public\.users|GRANT SELECT \(id, slug, name, status\) ON public\.tenants" ops/postgres tests
  -> ops/postgres/local-post-migration-grants.sql (both grants)
  -> tests/ONEVO.Tests.Architecture/BaseLoginArchitectureTests.cs (assertion constants)
```

## Verification results

- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
  -> Build succeeded, 0 warnings, 0 errors.
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
  -> Passed: 221, Failed: 0, Skipped: 0.
- PowerShell parser validation
  (`[System.Management.Automation.Language.Parser]::ParseFile(...)`) on
  `setup-local-db.ps1` -> no parse errors.
- `rg` checks above -> both pass as described.
- `git diff --check` -> exit 0, no whitespace errors introduced.

## EXECUTE grant on `auth_lookup_base_login_candidates`

Not duplicated in `local-post-migration-grants.sql`. The
`20260724174557_AddAuthLookupBaseLoginCandidatesFunction` migration already
does `REVOKE ALL ... FROM PUBLIC` followed by `GRANT EXECUTE ... TO
onevo_app` as part of the same `Up()` that creates the function, so the
condition in the task ("if the migration does not already do it") is not
met. Adding a redundant grant in a second file would only create a second
source of truth for the same permission.

## Manual local-database testing

Not performed - no local PostgreSQL instance was available in this
environment to run `.\ops\postgres\setup-local-db.ps1 -RunMigrations`
end-to-end against a real fresh database. All verification above is static
(build, architecture tests, `rg`, PowerShell parser, `git diff --check`). The
change was scoped so the pre-migration file's SQL is a strict subset of what
it was before (same statements minus the three users/tenants ones), and the
post-migration file's SQL is a verbatim copy of what was removed, run through
the same `Invoke-PsqlChecked` helper already proven to work for the bootstrap
step - both are read the same way `psql` already reads
`local-bootstrap-roles.sql` (`--set` variables + `--file`), so this is a
straightforward split rather than a new mechanism, but real end-to-end
confirmation with a live database is recommended before relying on this in a
fresh environment.
