# Dev Smoke Employee Seed Reconciliation Report

## Scope

`DevSmokeTestTenantSeeder` (Development/Test-only) previously created tenants, users, roles,
legal entities, and a subscription for four smoke-test users, but left `public.employees` empty.
This is now fixed: the seeder creates exactly one `Employee` row per seeded smoke user, tied to
the correct tenant and legal entity, without duplicating users or employees on repeated runs.

Work was confined to `HRMS-Backend-v1`. No changes were made to auth/login logic, Department,
Position, or LegalEntity schema, payment/system-config/OAuth code, the frontend, Postman, or
unrelated migrations. No new migration was required.

## Why users and employees are separate

`User` (`public.users`) is an authentication/identity row: login credentials, tenant membership,
email verification state. `Employee` (`public.employees`) is a Core HR profile row: employee
number, hire date, legal entity, employment type/status/work mode, and (later) department/position
assignment. The two are deliberately decoupled - a `User` can exist without ever becoming an
`Employee` (e.g. a pure platform/API integration account), and `employees.user_id` carries a
unique index (`ix_employees_user_id`), so a `User` can have **at most one** `Employee` row. This
change wires the two together for the four dev-smoke users without merging the concepts.

## Confirmation `employees` is Phase 1

- `employees` already has production-grade constraints in place from earlier migrations:
  `ix_employees_user_id` (unique) and `ix_employees_tenant_id_employee_number` (unique), both
  confirmed present in `ApplicationDbContextModelSnapshot.cs`.
- `employees` is already RLS-protected alongside `legal_entities` and `tenant_subscriptions`
  (`20260515022320_AddRlsPolicies.cs:17`), meaning it was already treated as a first-class,
  tenant-owned Phase 1 table before this change - this change only adds seed data for it, it does
  not newly promote it to Phase 1.

## Exact rows seeded

| Tenant | Email | Employee Number | First/Last Name | Legal Entity |
|---|---|---|---|---|
| acme | siyasiyamala932@gmail.com | ACME-0001 | Acme Owner | Acme Technologies |
| acme | paramanathanmuthaiya@gmail.com | ACME-0002 | Acme HR Manager | Acme Technologies |
| acme | mrt15473@gmail.com | ACME-0003 | Acme Work Manager | Acme Solutions |
| dapi | dapiyshanth1908@gmail.com | DAPI-0001 | Dapi Owner | Dapi Technologies |

Each employee gets a single row, matched/kept idempotent by `UserId`. `FirstName`/`LastName`/
`Email` are copied directly from the already-seeded `User` row (not duplicated as separate string
literals), so they can never drift out of sync with the user record.

`EmploymentTypeId`, `EmploymentStatusId`, and `WorkModeId` are all set to `1` for every seeded
employee (`full_time` / `active` / `on_site`). These IDs are guaranteed to exist before
`DevSmokeTestTenantSeeder` runs because `LookupDataSeeder` is registered earlier in the
hosted-service startup order (`DependencyInjection.cs:313` vs. `:316`), and its `LookupDataSeeder.cs`
hardcodes `Id = 1` for exactly those three codes. There is no FK constraint from
`employees.employment_type_id` / `employment_status_id` / `work_mode_id` to the lookup tables
(confirmed absent from both `20260519061316_AddLookupTables.cs` and `EmployeeConfiguration.cs`), so
a defensive existence check (`EnsureSmokeEmployeeReferenceDataAsync`) was added that queries the
three lookup tables for `Id = 1` before writing any Employee row, and throws a clear
`InvalidOperationException` if the guarantee is ever broken (e.g. a future reordering of
`AddHostedService<...>()` calls), rather than silently writing dangling lookup ids.

`HireDate` is a fixed constant (`2025-01-01`) rather than `DateTimeOffset.UtcNow`, so re-running
the seeder at different times never perturbs previously-seeded employee data.

## Exact legal entity mapping

- Acme Owner -> **Acme Technologies** (Acme's primary legal entity)
- Acme HR Manager -> **Acme Technologies**
- Acme Work Manager -> **Acme Solutions** (a real, already-seeded non-primary Acme legal entity -
  used per the task's instruction to prefer it over Acme Technologies where it exists)
- Dapi Owner -> **Dapi Technologies** (Dapi's only/primary legal entity)

Each user gets **exactly one** `Employee` row tied to a single `LegalEntityId`. No user is ever
seeded into multiple legal entities as multiple Employee rows - multi-legal-entity authority is
explicitly deferred to `position_assignments` (not built here).

## RLS/tenant-context approach

Employee seeding is inserted inside the existing per-tenant loop in `SeedAsync`, in the exact spot
where `SeedTenantUserAsync`/`SeedTenantRoleAsync` already run - i.e. **after**
`ResolveSmokeTenantContext(tenantContext, tenant)` has already switched the tenant context out of
admin mode into the target tenant's resolved context for that iteration of the loop. No new admin
mode / tenant context transitions were introduced; the new employee write rides the same
already-established RLS context as the sibling user/role writes in the same loop iteration.
`Employee.TenantId` is set explicitly to the tenant being seeded (there is no interceptor that
auto-populates `TenantId`, matching the existing pattern used for `User`, `Role`, and
`LegalEntity` in this same file), and `Employee.LegalEntityId` is always one of that same tenant's
already-seeded legal entity IDs - never a request-supplied or cross-tenant value.

The architecture test suite (`DevSmokeTestTenantSeederArchitectureTests.cs`) - which asserts admin
mode is entered before `SeedAsync`, tenant context is resolved before any per-tenant writes, and
that the seeder never disables RLS, uses `BYPASSRLS`, or connects with a privileged
`NpgsqlConnection` - passed unmodified against the new code (525/525 architecture tests green).

## Idempotency behavior

`SeedTenantEmployeeAsync` matches on `UserId` first (the seeder's existing convention for `User`
and `Role`, since fixed GUIDs anchor identity across dev-database rebuilds):

1. **Employee-number collision check (runs first, every time):** if an `Employee` row already
   exists in the same tenant with the target `EmployeeNumber` but a **different** `UserId`, the
   seeder throws `InvalidOperationException` immediately - this is the "dirty dev database" guard
   requested in the task (verified by a dedicated test that hand-plants a conflicting row and
   re-runs the seeder).
2. **No existing row (`UserId` not found):** a new `Employee` is created.
3. **Existing row found:** smoke-controlled fields (`EmployeeNumber`, `FirstName`, `LastName`,
   `Email`, `LegalEntityId`, `EmploymentTypeId`, `EmploymentStatusId`, `WorkModeId`) are refreshed
   in place - no new row, no duplicate.

Re-running the full seeder 2-3 times in a row (exercised by
`SeedAsync_IsIdempotentAcrossRepeatedRunsForEmployees`) always leaves exactly 4 `Employee` rows.

## What was intentionally not built

Per the task's scope, none of the following were added:
- Employee API/controllers
- Employee invite flow
- Department assignment UI/API
- Position assignment
- Employee hierarchy closure
- Multi-legal-entity authority (a user can only ever get one `Employee` row from this seeder)
- New Employee/Department/Position/LegalEntity tables or schema changes
- New auth/login behavior
- Any production bootstrap path - `DevSmokeTestTenantSeeder.StartAsync` still returns immediately
  unless `IsDevelopment()` or `IsEnvironment("Test")`, and that guard was not touched.

## Verification commands and results

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 1371, Skipped: 0, Total: 1371

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal
  -> Passed! Failed: 0, Passed: 525, Skipped: 0, Total: 525

git diff --check
  -> exit 0, no whitespace/conflict-marker errors (only benign LF/CRLF line-ending notices)

ASCII scan (PowerShell Select-String '[^\x00-\x7F]') on both touched files
  -> no output on either file (clean)
```

Docker was available in this environment, but `tests/ONEVO.Tests.Integration` has no dedicated
`DevSmokeTestTenantSeeder`/Employee-focused integration test (confirmed by search - the only
existing integration-adjacent coverage for this seeder is the architecture suite above and the
SQLite-backed unit suite). Per the instruction to avoid running the full multi-hour integration
suite unless needed, it was not run; there was no focused subset to target.

**Manual DB verification (Task H):** local Postgres (`OnevoDb`) was already migrated and up to
date (`setup-local-db.ps1 -RunMigrations` reported "No migrations were applied. The database is
already up to date."). The API was started once in `Development` (`ASPNETCORE_ENVIRONMENT=Development`),
confirmed via log output `"Development smoke-test tenants seeded: acme, dapi"`, then stopped. The
verification query returned exactly the expected 4 rows:

```
 slug |             email              | employee_number | first_name |  last_name   | legal_entity_name
------+--------------------------------+------------------+------------+--------------+-------------------
 acme | siyasiyamala932@gmail.com      | ACME-0001        | Acme       | Owner        | Acme Technologies
 acme | paramanathanmuthaiya@gmail.com | ACME-0002        | Acme       | HR Manager   | Acme Technologies
 acme | mrt15473@gmail.com             | ACME-0003        | Acme       | Work Manager | Acme Solutions
 dapi | dapiyshanth1908@gmail.com      | DAPI-0001        | Dapi       | Owner        | Dapi Technologies
(4 rows)
```

## Files changed

- `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` - added
  `SmokeEmployeeDefinition`, extended `SmokeTenantDefinition` with an `Employees` list, added
  `EnsureSmokeEmployeeReferenceDataAsync` and `SeedTenantEmployeeAsync`, wired employee seeding
  into the existing per-tenant/per-user loop.
- `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs` - added a
  `SeedLookupDataAsync` test helper (mirrors production hosted-service ordering) and 10 new tests
  covering: one employee per seeded user, per-user legal entity/employee-number assertions,
  wrong-tenant isolation, idempotency across repeated runs, uniqueness of employee numbers,
  row-reuse on rerun, the employee-number-collision failure path, and an EF metadata assertion that
  `employees.user_id` carries a unique index. The old
  `SeedAsync_DoesNotCreateAnyEmployeeRowForWorkManager` test (which asserted the now-intentionally-
  reversed "no employee row" behavior) was replaced.

No migration file was added - the required unique indexes already existed.

## Remaining gaps

- `position_assignments` (for department/position/manager assignment and multi-legal-entity
  authority) is not built. This is out of scope per the task and explicitly deferred.
- By design, a seeded smoke user can only ever have one `Employee` row, tied to one legal entity.
  If a future feature needs one user to hold roles/authority across multiple legal entities
  simultaneously, that must be modeled through `position_assignments`, not by adding more
  `Employee` rows for the same `UserId` (which the unique `user_id` index would reject anyway).
- This seeder remains Development/Test-only; no production bootstrap path was created or touched.
