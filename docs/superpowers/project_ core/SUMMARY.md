# project_ core/ — Summary

**Purpose:** The living architecture and data-model documents for ONEVO. Unlike `workflow/` (dated point-in-time reports), these describe current/target design and must be kept in sync per [[PROCESS_RULES]] rule 2.

**Last updated:** 2026-08-03

## Files

- `ONEVO_Backend_Architecture_Document.md` — backend architecture: layering (Api/Application/Domain/Infrastructure), tenant isolation, authentication/authorization, database, caching, file handling, performance, logging, testing, deployment. Reconciled against `workflow/authentication.md` for the Authentication section on 2026-08-03 (Phase 1); remaining sections are queued for Phase 2 (see `plans/2026-08-03-doc-audit-and-process-setup.md`).
- `ONEVO_HRMS_Frontend_Architecture (1).md` — frontend (Angular) architecture. **Cannot be code-verified from this repo** — no frontend source is present in `HRMS-Backend-v1`. Treat as documentation-only until checked against the actual frontend repo.
- `SKILL.md` — the full-stack engineering agent skill/rulebook (backend + frontend engineering rules, review checklist, forbidden patterns). This is prescriptive convention, not a description of current implementation state.
- `phase1-table-inventory.md` — full column-level definitions for all **252 Phase 1 tables** (245 core + 7 Developer Platform Extensions). See breakdown below. Structural verification against actual EF configurations/migrations is queued as Phase 3.

## Dev context: `phase1-table-inventory.md` table breakdown

| Group | Modules (table count) | Total |
|:---|:---|---:|
| Pillar 1 — HR Management | Infrastructure (13), Auth & Security (20), Org Structure (8), Core HR (14), Time Off (7), Calendar (5), Configuration (11) | 78 |
| Pillar 2 — Monitoring | Activity Monitoring (8), Discrepancy Engine (3), Time & Attendance (18), Identity Verification (8), Productivity Analytics (5) | 42 |
| Pillar 3 — Work Management | Foundation + Projects + Objectives (17), Task Management + Worklogs (15), Sprint Planning (5), Collaboration (5), GitHub Repository Integration (6) | 48 |
| Shared Foundation | Shared Platform (54), Agent Gateway (6), Reporting Engine (3) | 63 |
| Developer Platform | Platform users/credentials/sessions/RBAC/auth events (9), System Config provider catalog/service keys (2), OAuth app registration/secret rotation (2), Platform alerts (1) | 14 |
| Developer Platform Extensions | Demo Profile/Request approval flow (4), Subscription plan modules/add-ons/pricing (3) | 7 |

**Explicitly excluded as Phase 2 (not in the 252):** Exception Engine, Workflow/Automation Engine, Microsoft Teams integration, `integration_connections`, `project_workspaces`, Chat + Chat AI, Payroll (incl. Compensation Setup), Skills & Learning (incl. Qualification Tracking), Grievance, Expense, IDE Extension, Customize Dashboard, agent release/ring tables, `platform_api_keys`, `overtime_records`, `ai_provider_configs` + `tenant_ai_provider_overrides`.

## Known drift / open items

- Authentication section of the backend architecture doc was generic/principle-level only until 2026-08-03; now reconciled against the verified flow in `workflow/authentication.md` (see that file's own "Current Gaps and Risks" section for unresolved issues: dead `IJwtTokenService.GenerateDeviceToken`, narrowly-used legacy `RefreshToken` table, the `force-change-password` continue-URL bug on base-domain-triggered forced password changes, no multi-session/device management).
- Remaining backend architecture sections (tenant isolation implementation, caching, file/document handling, performance/reliability, logging/audit, testing, deployment) have not yet been verified against `src/`/`tests/` — queued as Phase 2 in `plans/2026-08-03-doc-audit-and-process-setup.md`.
- `phase1-table-inventory.md` has not yet had a structural check against actual EF Core configurations/migrations (table existence, not full column diff) — queued as Phase 3 in the same plan.
- Frontend architecture doc is unverifiable from this repo (see Files section above).
