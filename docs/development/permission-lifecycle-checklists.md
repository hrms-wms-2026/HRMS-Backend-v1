# Adding, renaming and retiring a tenant permission

Every tenant permission code lives in one file: `src/ONEVO.Application/Common/Authorization/PermissionCatalog.cs`.
`PermissionCatalogSynchronizer` applies it to the database at start-up (hosted `PermissionSeeder`, switch
`PermissionCatalog:SyncOnStartup`). The run is idempotent and runs in one transaction, so a refused step changes nothing.

Every change also gets a line in [docs/api/permission-changelog.md](../api/permission-changelog.md).

## How to add a permission

Files to touch: the catalogue and the endpoint (plus the changelog line).

1. In `PermissionCatalog.cs`, add a constant to the module's nested class, for example
   `public const string Export = "reports:export";` in `Reports`.
2. Add its definition to `Definitions`: `Assignable(Reports.Export, "reports", "Export reports.")`. The module key must be
   a module the tenant can have, or tenant admins cannot grant it.
3. Should existing tenants get it without an admin editing roles? Add an entry to `PermissionCatalog.DefaultGrants`:
   ```csharp
   new PermissionDefaultGrant(Reports.Export, [DefaultGrantTarget.Owner], new DateOnly(2026, 12, 1), "New export screen"),
   ```
   Targets: `DefaultGrantTarget.Owner` (each tenant's built-in Owner role), `FromTemplate(templateId)` (roles made from a
   role template), `HoldersOfCode(Reports.Read)` (roles that already hold another code, for a code split out of it).
   It runs once per database. If an admin later removes the code from a role, it is not added back.
4. Use the constant on the endpoint: `[RequirePermission(PermissionCatalog.Reports.Export)]`.
5. Run the unit and architecture tests. `PermissionLifecycleValidatorTests` checks the declarations, and the
   task 32 architecture tests check that the endpoint uses a live catalogue constant.

## How to rename a permission

Files to touch: the catalogue and the endpoints that check the code (plus the changelog line).

1. In `PermissionCatalog.cs`, change the constant's value to the new code (or add a new constant and delete the old one),
   and update its definition.
2. Add an entry to `PermissionCatalog.Renames`:
   ```csharp
   new PermissionRename("reports:view", "reports:read", new DateOnly(2026, 12, 1), "Align with other modules"),
   ```
   The sync changes the code on the existing `permissions` row. Role grants and user overrides point at the row id, so
   they follow it; nothing is deleted. If the new code was already seeded, grants are moved onto it without duplicates
   (an override already on the new code wins), then the old row is removed. Codes inside `role_templates` are renamed too.
3. Point every endpoint at the new constant. The old literal no longer exists, so a missed one fails the build.
4. Keep the rename entry for as long as any database may still have the old code. Leaving it in is harmless.
5. Tell the frontend (task 25) so menus and guards use the new code.

## How to retire a permission

Files to touch: the catalogue and the endpoints that checked the code (plus the changelog line).

1. Stop every endpoint from checking the code first.
2. Release 1, soft retire: change its definition to
   `Definition(code, module, "Why it is retired.", PermissionKind.SoftRetired)` and move the constant to the
   `Retired` class. The row stays with `retired_at` set. The code stops working everywhere and leaves the role screen,
   but grants stay in the table, so changing the kind back restores them.
3. Release 2 or later, remove: change the kind to `PermissionKind.Retired`. The sync deletes the row and, by cascade,
   its grants.
4. In both stages the sync first counts the role grants and user overrides that still hold the code, logs them per
   tenant, and refuses to run if any exist:
   `Refusing to remove permission 'x': N role grant(s) and M user override(s) in T tenant(s) would be lost.`
   Startup stops and nothing is changed. To go ahead, either declare a rename (to keep the access under another code)
   or set `DropGrants: true` on the definition if losing that access is intended.
5. Do not delete a code from the catalogue without a retirement. The sync would leave its row behind, still grantable.
