# Task 32: One permission catalogue in code

| | |
|---|---|
| Priority | 32 of 34 (lower number = build first). Added 2026-11-15. Do this before adding any new permission |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security, Architecture |
| Depends on | nothing |

## What we found

Full review: `docs/reports/security/2026-11-15-permission-system-review.md`.

- Permission codes are plain text in every place they are used: 242 `[RequirePermission]` and `[RequireAnyPermission]` attributes and 105 inline `HasPermission("...")` calls. There is no constants class.
- A typo compiles. The endpoint then rejects everyone except a user with `*`, and no test notices.
- One permission is described in up to six separate places: `PermissionSeeder`, `ModuleAutoGrants`, `DerivedPermissions`, `RoleTemplateSeeder`, `ModuleCatalogSeeder` and the frontend.
- The seven auto-granted codes (for example `leave:read-own`, `attendance:read-own`, `calendar:read`) are enforced by endpoints but have no row in the `permissions` table.
- `DerivedPermissions` refers to `workflows:execute`, which is not in the catalogue at all.

## What to do

1. Create one static catalogue in `ONEVO.Application`, for example `Permissions`, grouped by module: `Permissions.Leave.Approve = "leave:approve"`. Each entry carries code, module, description and a kind: assignable, auto-granted or derived.
2. Make `PermissionSeeder` build its list from the catalogue instead of its own `Perm(...)` lines.
3. Make `ModuleAutoGrants` and `DerivedPermissions` refer to catalogue entries, not text. Put the auto-granted and derived codes in the catalogue too, marked so the role screen shows them as "given automatically" and does not let an admin tick them.
4. Replace the text in attributes and `HasPermission` calls with the constants. Do it one module per commit; the string values do not change, so behaviour does not change.
5. Add an architecture test: every permission argument of `[RequirePermission]`, `[RequireAnyPermission]` and `HasPermission` is a catalogue constant, never a literal.
6. Add a test: every code in `RoleTemplateSeeder`, `ModuleCatalogSeeder`, `ModuleAutoGrants` and `DerivedPermissions` exists in the catalogue.
7. Put module keys and feature keys in the same catalogue (`Modules.X`, `Features.X`). Feature keys are the chosen method for managing API changes (task 43), so they need the same compile-time safety as permission codes.
8. Expose the catalogue as a JSON file or endpoint for the frontend (frontend task 25).

## Done when

- A search for `RequirePermission("` in the API project finds nothing.
- Using a code that is not in the catalogue fails the build or the architecture suite.
- The seeded `permissions` table is identical before and after, apart from the newly listed auto-granted and derived codes.
- Full unit and architecture suites pass.
