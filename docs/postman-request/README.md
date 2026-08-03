# postman-request/ — Human-readable API docs

Plain-Markdown companion to the Postman collection under `postman/collections/` (which is git-ignored and only readable inside the Postman app). One `.md` file per finished API endpoint, grouped by module folder, so anyone can read the request/response shape on GitHub without opening Postman.

Maintenance rule: `docs/superpowers/rules/PROCESS_RULES.md` rule 7.

## Modules

- `Work Management/` — `Create Project.md` (`POST /api/v1/work/projects`)
- `Tenant Authentication/` — login, workspace selection, Google login, session exchange, session bootstrap (`/me`), logout, MFA (enable/confirm/verify), forgot/reset/force-change password, invitation preview + accept (password/Google), and legal acceptance (mid-login gate + post-login) — 17 endpoints total, backfilled 2026-08-03 from `docs/superpowers/workflow/authentication.md` + direct controller/DTO reads. `_Shared - Session Result Response.md` documents the common response shape most of these endpoints return.
