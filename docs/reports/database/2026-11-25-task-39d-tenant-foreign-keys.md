# Task 39d: foreign key from every tenant_id to tenants (2026-11-25)

Task file: `docs/Improvements/person-1-security-and-auth/task-39d Tenant foreign keys.md`. Follows task 39c (`2026-11-24-task-39c-audit-column-foreign-keys.md`).

## What changed

| Item | Before | After |
|---|---|---|
| `tenant` lines in `missing-foreign-keys.txt` | 163 | 0 |
| Allow-list lines in `missing-foreign-keys.txt` | 309 | 146 |
| `tenant_id` foreign keys to `tenants` | 13 (configured per table) | 176 |

- **Migration**: `20261124131426_AddTenantForeignKeys39d`, generated with `dotnet ef migrations add`. It adds 163 foreign keys to `tenants`, all `NO ACTION`, and the 19 indexes they need; the other 144 tables already had an index that starts with `tenant_id`. It changes no table or column. Its one hand-written part is a read-only check (see "Hand-written SQL").
- **One migration, not batches of about 30**: Execution asked for one migration (`AddTenantForeignKeys39d`). The keys add no data change, and the check runs first, so one transaction is enough.
- **Keys**: `ReferenceForeignKeys.ApplyTenantColumns` in `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`. It loops over every root table with a `TenantId` property (`Guid` or `Guid?`) and skips a column that already has its key (the Auth challenges, billing, support, configuration-template and tenancy tables configure theirs). It runs after `ApplyConfigurationsFromAssembly`, so it sees those keys. A table added later gets its key without a new line.
- The loop matches the property, not `ITenantOwnedEntity`: that interface's `Guid TenantId` leaves out the nullable `PlatformShared` tables. A nullable `tenant_id` (for example `outbox_messages`) stays nullable, and the key checks only values that are not null.
- The generated key set is exactly the 163 tables of the allow-list (checked by diffing the migration's tables against the removed lines).
- `tenant_provisioning_states` has `tenant_id` as its primary key. With the key, the snapshot no longer marks it as generated on add. Nothing writes that table through EF, so behaviour is unchanged.

## Delete behaviour and RLS

- A tenant is cancelled, never deleted: the application has no hard delete of a `tenants` row. All keys are `NO ACTION`, so there is no cascade and no multiple cascade path.
- RLS predicates do not filter a foreign key check, so the keys hold under every predicate, `TenantStrict` included. `ONEVO.Tests.Architecture` (the RLS registry and policy tests) passes unchanged.
- The hand-written check counts rows in the `admin` tenant context mode, because SQL Server RLS applies to every login (`sa` included), and resets the mode afterwards.

## Hand-written SQL

`AddTenantForeignKeys39d.CheckTenantOrphansSql` runs first in `Up`, before any index or key. It counts, per table, every `tenant_id` that is not a `tenants` row. If there is one, it stops with error 50039 and names each table (`table=rows`). It changes nothing: a tenant id cannot be mapped to another tenant, and rows are never deleted. Without it, the first failing key would stop the migration with a plain key error that names one table only.

`TenantOrphanCheckTests` (integration) covers both paths on `project_categories`: the check passes on clean data, and with the key switched off (NOCHECK) and an unknown tenant id written, it stops with 50039, names `project_categories=1`, and leaves the row as it is.

The two `payment_gateway` tables have no `tenant_id`, so `CredentialOwnershipCompletionArchitectureTests` is not affected.

## Bad data in the local database

The agent cannot connect to the database (the connection needs the `sa` password from `.env`, which the auto-mode classifier refused). The user ran the read-only check `C:\Users\User\Desktop\orphans39d.sql` on the local `CrownusDb` (localhost) with:

```powershell
& "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE" -S localhost,1433 -d CrownusDb -U sa -C -i C:\Users\User\Desktop\orphans39d.sql -o C:\Users\User\Desktop\orphans39d-result.txt
```

Result: 163 lines, one per column, all `unknown 0`; no `NULL`, no `Guid.Empty`, no missing table. No `tenant_id` blocks the keys. The UAT database was not touched, and no migration was applied by the agent.

The check prints, per table, the rows, `NULL`, `Guid.Empty`, `tenants` matches and unknown values with up to 3 samples. It runs in the `admin` tenant context mode and changes nothing (its only `INSERT` fills a table variable). Its table list is the 163 tables above.

## Test fixtures

The integration suite found fixtures that wrote made-up tenant ids. They were fixed; no test was weakened:

- `TestTenants` (new, `tests/ONEVO.Tests.Integration/Support`) adds the tenants row behind a fixture tenant id. `UserDashboardRepositoryTests` (6 tests) and `SessionRevocationTests` use it.
- `WorkAreaChangeRequestsIntegrationTests.MigratedSchema_HasExpectedColumnsRestrictiveForeignKeysAndIndexes` counted 5 NO ACTION keys on `work_area_change_requests`; it now expects 6 and names the new `fk_work_area_change_requests_tenants_tenant_id`.

All 8 failures of the first full run failed again when run alone, so they were real. After the fixes the four classes pass 22 of 22.

## Gate

To be filled in after the full run.
