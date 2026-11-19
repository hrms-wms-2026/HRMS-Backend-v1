# Task 33: Safe add, rename and retire of permissions

| | |
|---|---|
| Priority | 33 of 34 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security, DB, Deployment |
| Depends on | 32 |

## What we found

Full review: `docs/reports/security/2026-11-15-permission-system-review.md`.

- **Rename is destructive.** The only way to change a code is to add the new one and put the old one in `RetiredPermissionCodes`. The seeder then deletes the old row, and every role grant and user override of it goes with it by cascade. Every tenant loses that access with no warning.
- **A new permission reaches nobody.** Seeding adds the catalogue row only. Existing tenants keep their old `role_permissions`, so a new feature stays hidden until each tenant admin edits their roles by hand.
- **38 of the 83 seeded codes are not referenced anywhere outside the seeders** (`analytics:*`, `chat:*`, `documents:*`, `expense:*`, `grievance:*`, `payroll:read`, `payroll:write`, `performance:read*`, `reports:*`, `skills:*`, `time_off:*`, `workforce:*` and others). An admin can tick them and nothing changes.
- `time_off:*` and `leave:*` both exist. Only `leave:*` is enforced.
- The seeder runs, and deletes rows, at every start of every API instance.
- There is no record of when a code was added, renamed or removed.

## What to do

1. Add a rename map to the catalogue from task 32: old code to new code. The seeder (or a migration) updates the `permissions` row in place, so role grants and overrides follow the row. No delete, no cascade.
2. Add default grants for new codes: each new catalogue entry may name the role templates that should receive it. A one-time step adds it to the matching roles of existing tenants. Record which tenants and roles were changed.
3. Before retiring a code, log how many roles and overrides still hold it, per tenant. Refuse to retire when the count is above zero unless a rename target or an explicit "drop grants" flag is given.
4. Review the 38 unreferenced codes with the product owner. For each: keep because the module is coming (mark it "planned" and hide it from the role screen), or retire it. Decide `time_off:*` versus `leave:*` here.
5. Move permission seeding out of API start-up into the deploy step (task 09), so it runs once per release and not once per instance.
6. Keep a short changelog file `docs/api/permission-changelog.md`: date, code, what changed, why.
7. Write the checklist "how to add a permission" and "how to rename a permission" in `docs/development/`, each with the exact files to touch after task 32 (it should be two: the catalogue and the endpoint).

## Done when

- A test renames a code and shows role grants and user overrides still exist under the new code.
- A test adds a code with a default template and shows an existing tenant's matching role receives it.
- Retiring a code that is still granted fails with a message that says how many grants would be lost.
- The 38 codes each have a written decision.

## Design (2026-11-19)

### Where the loss comes from today

`permissions` is the parent of `role_permissions` and `user_permission_overrides`, both with
`ON DELETE CASCADE` (`PermissionConfiguration`). `PermissionSeeder` deletes every row whose code is in
`RetiredPermissionCodes`, at every start of every instance. A rename can only be written as "add the
new code, retire the old one", so the delete takes every grant and override with it.

### The fix: change the row, never the children

A grant points at `permissions.id`, not at the code text. So a rename is an `UPDATE permissions SET
code = new WHERE code = old`: the id stays, and every grant and override follows it. Nothing is
deleted and the cascade never fires.

### Declarations (code, reviewed in a PR)

`PermissionLifecycle` (Application, `Features/Auth/Permission/Lifecycle`) holds three lists next to
the catalogue:

| List | Entry | Meaning |
|---|---|---|
| `Renames` | `Rename(from, to, date, reason)` | Old code becomes new code. Chains (`a→b`, `b→c`) resolve to the last code |
| `DefaultGrants` | `DefaultGrant(code, targets, date, reason)` | One-time backfill of a new code to matching roles of existing tenants. Targets: the `Owner` system role, roles made from a role template, or roles that already hold another code |
| `Retirements` | `Retire(code, stage, reason, date, dropGrants)` | `SoftRetired`: row stays, `retired_at` is set, the code stops working and disappears from the role screen, grants stay so a revert brings them back. `Removed` (a later release): row and its grants are deleted |

`PermissionLifecycleValidator` checks the lists against the catalogue (rename target exists, source
is not a live code, no cycles, a code is not both renamed and retired, a default grant names a live
code). A unit test runs it on the real lists, so a bad declaration fails CI, not a deploy.

### Sync run (`PermissionCatalogSynchronizer`)

One transaction, in admin tenant mode (`role_permissions` and overrides are under RLS), guarded by
`sp_getapplock` so two API instances starting together run it one after the other:

1. **Renames.** Old row present, new row absent: update the code in place. Both present (the new code
   was seeded by an earlier build): copy grants and overrides to the new row (skipping ones already
   there), then delete the empty old row. Codes inside `role_templates.permission_codes_json` are
   renamed too.
2. **Catalogue rows.** Add missing codes, update description and module (as before). A code that is
   back in the catalogue loses its `retired_at`.
3. **Default grants.** Runs once per code: the ledger key `default-grant:<code>` stops a second run,
   so an admin who later removes the code from a role is not overruled. Module gating stays in
   `PermissionResolver` (a role code only counts when its module is active), so no entitlement check
   is needed here.
4. **Retirements.** Count role grants and overrides per tenant and log them. If the count is above
   zero and `dropGrants` is false, the run fails with "Refusing to retire 'x': N role grants and M user
   overrides in T tenants would be lost ...". The transaction rolls back, so nothing is half done.
   `SoftRetired` sets `retired_at`; `Removed` deletes the row.
5. **Ledger.** Each applied change writes one row to `permission_changes` (global table: change key,
   kind, code, previous code, JSON detail with the tenants and roles touched, time). The key is
   unique, so a change is recorded once.

A global query filter `retired_at IS NULL` on `Permission` hides soft-retired codes from the
resolver, the role screen, entitlement and assignability with no change to those queries. Only the
synchronizer reads past it.

### Start-up versus deploy step

Task 09 (deploy step) is not built yet. The sync stays in `PermissionSeeder` (a hosted service) but
is now idempotent, serialized by the app lock, and only deletes what is declared `Removed`. Setting
`PermissionCatalog:SyncOnStartup=false` turns it off for an instance; when task 09 adds the migration
job, it calls `PermissionCatalogSynchronizer` once per release and the API instances run with the
switch off.

### Migration

`retired_at` column on `permissions` and the `permission_changes` table. Created last, on the latest
base, after tasks 32 and 39 are merged.
