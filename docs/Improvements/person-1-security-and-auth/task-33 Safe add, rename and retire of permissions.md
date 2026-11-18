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
