# postman-request/ — Human-readable API docs

Plain-Markdown companion to the Postman collection under `postman/collections/` (which is git-ignored and only readable inside the Postman app). One `.md` file per finished API endpoint, grouped by module folder, so anyone can read the request/response shape on GitHub without opening Postman.

Maintenance rule: `docs/superpowers/rules/PROCESS_RULES.md` rule 7.

## Modules

- `Work Management/` — 23 endpoint docs (Projects: Create/Edit/Delete/Get/List/Achieve/Unachieve; Objectives: Create/Edit/Delete/Get/Get Tree/Get Subtree/My Project Milestones/Transfer Head/Add Member/Remove Member/Achieve/Unachieve/My History; Change Requests: Approve/Reject/List Mine). Last extended 2026-08-08 (My Project Milestones — see `docs/superpowers/plans/next/2026-08-08-work-management-my-project-milestones.md`); previously extended the same day for member management, Achieve/Unachieve, Get Objective, My Objective History (`docs/superpowers/plans/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve.md`). This list was stale (showed only `Create Project.md`) until 2026-08-08's rule-compliance check caught it — kept current going forward per `docs/superpowers/rules/PROCESS_RULES.md` rule 6.
- `Tenant Authentication/` — login, workspace selection, Google login, session exchange, session bootstrap (`/me`), logout, MFA (enable/confirm/verify), forgot/reset/force-change password, invitation preview + accept (password/Google), and legal acceptance (mid-login gate + post-login) — 17 endpoints total, backfilled 2026-08-03 from `docs/superpowers/workflow/authentication.md` + direct controller/DTO reads. `_Shared - Session Result Response.md` documents the common response shape most of these endpoints return.
