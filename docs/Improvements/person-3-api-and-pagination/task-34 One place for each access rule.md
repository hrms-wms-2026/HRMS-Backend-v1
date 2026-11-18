# Task 34: One place for each access rule

| | |
|---|---|
| Priority | 34 of 34 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day for the design and one module. Other modules are follow-up days |
| Area | Security, Architecture |
| Depends on | 32. Related to 15 and 21 |

## What we found

Full review: `docs/reports/security/2026-11-15-permission-system-review.md`.

- Access is decided in two layers that are written separately: 242 controller attributes and 105 inline `HasPermission` calls in handlers and services (Core HR 18 files, Attendance 11, Leave 11, Dashboard 6).
- Where both exist for one action they can drift apart. Where only the attribute exists, any code path that calls the command without the controller skips the check. `CLAUDE.md` names the Inbox providers as a known case.
- Rules that mix a permission with a relationship ("has `attendance:approve` and is this employee's manager") are written by hand in each handler.
- `PermissionVersionService` stores versions in a static in-memory dictionary. It is lost on restart and not shared between instances. Its own comment says to replace it.
- `RequirePermissionAttribute` returns the missing code in the 403 body.

## What to do

1. Decide the rule and write it in `docs/development/architecture-rules.md`: the permission check lives with the command or query, so every caller gets it. The controller attribute stays as a fast first gate and as documentation.
2. Keep one fixed order of checks for every request: module active, feature key on (task 43), permission, then the relationship rule. Document it next to the rule above.
3. Add a marker for requests, for example `IRequirePermission { string Permission }`, and a MediatR pipeline behaviour that checks it before the handler runs. Dashboard widget providers and Inbox providers then get the check for free.
4. For rules that need a relationship, create named policy classes per module (for example `LeaveAccessPolicy.CanApprove(caller, request)`), and call those from handlers instead of repeating the `HasPermission` plus relationship code.
5. Convert one module end to end as the reference. Leave is a good choice: it has both attribute and inline checks and Inbox actions.
6. Add an architecture test: a command or query handled by a controller with `[RequirePermission]` must carry the same permission on the request, or be on an explicit allow-list.
7. Move `PermissionVersionService` to the shared cache from task 21, or remove it if the cookie session path makes it unnecessary. Decide and write down which.
8. In the 403 body, return the missing code only in Development. In other environments return a generic message and log the code with the correlation id.

## Done when

- In the converted module, calling a command directly (as an Inbox provider does) without the permission is rejected by a test.
- The module has no `HasPermission("` text left outside its policy class.
- The plan and order for the remaining modules is written down.
