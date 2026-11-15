# Permission system review, 2026-11-15

Question: how good is the permission checking system, and how easy is it to add a new permission or change an existing one?

Answer: the checks themselves are sound. Changing the set of permissions is risky, because a permission is defined in many separate places as plain text and nothing ties them together. The fix tasks are 32, 33 and 34 in `docs/Improvements`, plus frontend task 25.

## How it works today

1. **Catalogue.** `PermissionSeeder` holds the list of permission codes (83 codes, each with a module and a description) and writes them to the `permissions` table at start-up.
2. **Roles.** A tenant role gets codes through `role_permissions`. Role templates and default roles are seeded by `RoleTemplateSeeder` and `ModuleCatalogSeeder`.
3. **Per-user overrides.** `user_permission_overrides` can grant or revoke one code for one user.
4. **Resolution.** `PermissionResolver.ResolveAsync` builds the effective set on every request:
   - a user with `*` gets everything;
   - codes granted automatically for each active module (`ModuleAutoGrants`, 7 codes such as `leave:read-own`);
   - role codes, kept only when the code's module is active for the tenant;
   - override grants, then override revokes (a revoke cannot remove an auto-grant);
   - two derived codes, `inbox:read` and `notifications:read`, added when the user holds any code in the `DerivedPermissions` trigger lists.
5. **Enforcement.** Controllers use `[RequirePermission("code")]` or `[RequireAnyPermission(...)]` (242 uses). Handlers and services also call `currentUser.HasPermission("code")` directly (105 calls, mostly Core HR, Attendance and Leave).
6. **Administration.** Tenant admins assign codes to roles through `AssignRolePermissionsCommand`; platform admins have a separate catalogue (`PlatformPermissionCatalog`) and `[RequirePlatformPermission]`.

## What is good

- Deny by default inside the resolver: a code only counts if its module is active.
- Role changes take effect on the next request, because the session ticket is rebuilt from the database each time.
- Platform and tenant permissions are fully separate.
- `RolePermissionAssignability` only lets a tenant admin hand out codes that belong to modules the tenant has.
- Retired codes are removed from old databases by the seeder, not only from new ones.

## Problems found

| # | Problem | Evidence |
|---|---|---|
| 1 | Codes are plain text everywhere. There is no constants class. A typo in an attribute compiles and silently locks everyone out except `*` | 242 attribute uses and 105 `HasPermission` calls, all string literals |
| 2 | One permission is described in up to six places that must be edited together | `PermissionSeeder`, `ModuleAutoGrants`, `DerivedPermissions`, `RoleTemplateSeeder`, `ModuleCatalogSeeder`, frontend literals |
| 3 | Nothing tests that the places agree | No test compares codes used by endpoints with the seeded catalogue |
| 4 | Auto-granted codes are not in the catalogue | `leave:read-own` and six others are checked by endpoints but have no row in `permissions`, so they never appear in the role screen and cannot be described or audited |
| 5 | 38 of 83 seeded codes are not referenced anywhere outside the seeders | `analytics:*`, `chat:*`, `documents:*`, `expense:*`, `grievance:*`, `payroll:read/write`, `performance:read*`, `reports:*`, `skills:*`, `time_off:*`, `verification:configure/view`, `workforce:*`, `settings:device*`, `employees:delete`. An admin can tick them in the role screen and nothing changes |
| 6 | `time_off:*` and `leave:*` both exist. Only `leave:*` is enforced | seeder list versus attribute list |
| 7 | Renaming a code is not possible safely. The only tool is "retire the old code and add a new one", and retiring deletes every role grant and user override of that code by cascade | `RetiredPermissionCodes` handling in `PermissionSeeder` |
| 8 | A new code is granted to nobody. Existing tenants keep their old role rows, so a new feature is invisible until each tenant admin edits their roles by hand | seeding only adds the catalogue row |
| 9 | The same access rule is written twice in different forms | attribute on the controller and an inline `HasPermission` in the handler; Inbox providers call commands without the controller attribute (noted in `CLAUDE.md`) |
| 10 | `DerivedPermissions` lists trigger codes for modules that are not enforced (`payroll:approve`, `expense:approve`, `workflows:execute`) and `workflows:execute` is not in the catalogue at all | `DerivedPermissions.cs` versus the seeder |
| 11 | `PermissionVersionService` keeps versions in a static in-memory dictionary. It resets on restart and is not shared between instances | its own comment says "Replace with Redis in production" |
| 12 | The 403 body names the missing code ("Permission 'x' required") | `RequirePermissionAttribute`. Useful for developers, but it tells a caller exactly what to ask for |
| 13 | The permission seeder runs at every start-up of every instance and deletes rows | hosted service in `DependencyInjection.cs` |

## What adding a permission costs today

To add `reports:export` a developer must:

1. add a line to `PermissionSeeder`;
2. type the same text in each controller attribute;
3. type it again in any handler that checks it;
4. decide whether it belongs in `ModuleAutoGrants` or `DerivedPermissions`;
5. add it to the right role templates in `RoleTemplateSeeder` and to module ownership in `ModuleCatalogSeeder`;
6. check the module the code is seeded under is one the tenant has, or tenant admins cannot grant it;
7. type it again in the frontend for menus, guards and buttons;
8. tell every existing tenant to add it to their roles, because nothing does that for them.

Nothing fails if a step is missed. The feature just does not appear for some users.

## What changing a permission costs today

- **Change the description or module:** safe. The seeder updates the row.
- **Rename the code:** not safe. Add the new code, retire the old one, and every tenant loses the grant. There is no old-to-new map.
- **Split one code into two, or merge two into one:** same problem, plus every literal in both repositories must be found by text search.
- **Remove a code:** the seeder deletes it and its grants. Any endpoint still checking the old text becomes unreachable, and no test notices.

## How to change it

The fixes are split over four tasks in `docs/Improvements`.

| Problems | How it is now | How to change it | Task |
|---|---|---|---|
| 1, 2, 3, 4, 10 | Codes are string literals in up to six places, and nothing checks they agree | One catalogue class in code; attributes, seeders and the frontend file read from it; a test compares endpoints with the catalogue | [Task 32](<../../Improvements/person-1-security-and-auth/task-32 One permission catalogue in code.md>) and frontend task 25 |
| 5, 6, 7, 8, 13 | Rename deletes grants, a new code reaches nobody, 38 codes do nothing, seeding runs at every start | Rename map, default grants for new codes, refuse to retire a granted code, a decision per unused code, seeding in the deploy step | [Task 33](<../../Improvements/person-1-security-and-auth/task-33 Safe add, rename and retire of permissions.md>) |
| 9 | The same rule is written in the controller and again in the handler | One place for each access rule | [Task 34](<../../Improvements/person-3-api-and-pagination/task-34 One place for each access rule.md>) |
| 11 | Permission versions live in process memory | Move to shared state | [Task 21](<../../Improvements/person-4-deployment-and-architecture/task-21 Shared state for running more than one instance.md>) |
| 12 | The 403 body names the missing code | Not assigned yet. Return a generic message outside Development and log the code on the server; add it to task 32 or 34 |
