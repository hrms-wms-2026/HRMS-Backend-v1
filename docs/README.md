# Backend docs

Start here. Every document about this repo lives under `docs/`, except the three that must sit elsewhere (listed at the end).

## Where things are

| Folder | What's in it |
|---|---|
| [superpowers/project_ core/](superpowers/project_%20core/) | **Living architecture.** [ONEVO_Backend_Architecture_Document.md](superpowers/project_%20core/ONEVO_Backend_Architecture_Document.md) (layers, request pipeline, tenant isolation, auth, security, CI), [MODULE_MAP.md](superpowers/project_%20core/MODULE_MAP.md) (every feature area, what it does, where its docs are), `phase1-table-inventory.md`, and `SUMMARY.md` with the reconciliation log. |
| [superpowers/workflow/](superpowers/workflow/) | End-to-end flow write-ups verified against code, e.g. [authentication.md](superpowers/workflow/authentication.md) (login, sessions, CSRF, cookies). |
| [superpowers/specs/](superpowers/specs/) | Designs. `next/` = approved, not built yet; `finished/<date>/` = built. Index: [specs/SUMMARY.md](superpowers/specs/SUMMARY.md). |
| [superpowers/plans/](superpowers/plans/) | Implementation plans. Same `next/` / `finished/<date>/` split. Index: [plans/SUMMARY.md](superpowers/plans/SUMMARY.md). |
| [superpowers/rules/](superpowers/rules/) | How docs are created and kept current: [PROCESS_RULES.md](superpowers/rules/PROCESS_RULES.md), [FILE_CREATION_RULES.md](superpowers/rules/FILE_CREATION_RULES.md). |
| [reports/](reports/) | Point-in-time reports (implementation, verification, fixes, audits), one folder per area. |
| [postman-request/](postman-request/) | API request docs by area, plus the `ONEVO-HRMS.postman_collection.json` export. |
| [notifications/](notifications/) | Notification event inventory, decisions and rollout. |
| [development/](development/) | Local dev setup — [ports.md](development/ports.md) (also mirrored in the platform-admin front-end repo). |
| [deployment/](deployment/) | Deployment and secret-handling notes. |

### Report areas

`attendance`, `build-and-ci`, `dashboard`, `database`, `employee`, `legal-entity`, `monitoring`, `notifications`, `onboarding`, `org-structure`, `security`, `time-tracking`, `work-area`, `work-management`.

## Quality, testing and security

- **Run the gates:** `../dev-env/` (outside this repo) holds `verify.ps1` (build + unit + architecture, `-Full` adds Testcontainers integration tests), `security-scan.ps1` (gitleaks, Semgrep, DevSkim, NuGet audit, OSV, Trivy, actionlint, zizmor) and `find-unauthorized-endpoints.ps1`. See `../dev-env/README.md` and the commands in [`../CLAUDE.md`](../CLAUDE.md).
- **Latest hardening pass:** [reports/security/2026-10-27-uat-hardening-report.md](reports/security/2026-10-27-uat-hardening-report.md) — what was fixed (stored XSS, analyzer issues), what was verified safe, what is still open.
- **Integration test status:** [reports/build-and-ci/2026-10-27-integration-csrf-label-investigation.md](reports/build-and-ci/2026-10-27-integration-csrf-label-investigation.md) — why the integration jobs are non-blocking and why the old "CSRF issue" label was wrong.
- **Security rules for new code:** architecture document §4.2 (explicit endpoint authorization, rich-text active-content check) and §3.3.1 (RLS `TenantTables` convention).
- **Known gaps found in code vs docs:** [reports/documentation/2026-10-28-docs-and-code-gap-audit.md](reports/documentation/2026-10-28-docs-and-code-gap-audit.md).

## Documents that live outside `docs/`

- [`../README.md`](../README.md) and [`../CLAUDE.md`](../CLAUDE.md) at the repo root (entry point and agent instructions).
- [`../schema-snapshots/`](../schema-snapshots/README.md): generated per-domain schema snapshots. They stay at the repo root because the generator resolves `.env` relative to that folder.
- [`../ops/postgres/README.md`](../ops/postgres/README.md): Postgres operations notes, next to the scripts they describe.

## Conventions

- File names: lowercase kebab-case. Dated docs start with `YYYY-MM-DD-`.
- New reports go in `reports/<area>/`. New plans and designs go in `superpowers/plans|specs/next/` and move to `finished/<date>/` once built; nothing stays loose in `plans/`, `specs/` or `finished/`.
- Move files with `git mv` so history follows them, and update the folder's `SUMMARY.md` in the same change.
- When code changes what the architecture document says, update it in the same change.
