# Documentation Process Setup + Architecture/Tables Audit (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Stand up a durable documentation process for `docs/superpowers/` (a rules folder, a per-folder `SUMMARY.md`), and reconcile the Authentication section of `ONEVO_Backend_Architecture_Document.md` against the already code-verified `workflow/authentication.md`, while queuing the remaining architecture-doc sections and the tables-file structural check as explicit future phases.

**Architecture:** This is a documentation-only change — no application code, no tests in the pytest/dotnet-test sense. "Verification" for each task means grep/read-back checks that the written file contains the required sections and that no existing file was modified outside the allowed boundary.

**Tech Stack:** Markdown files under `docs/superpowers/`.

## Global Constraints

- Only files under `docs/superpowers/project_ core/` may receive content edits in this plan. Every other file this plan touches is a **new** file.
- Do not modify any existing file in `docs/superpowers/plans/` or `docs/superpowers/workflow/`.
- Do not touch the unrelated pending git changes already in the working tree (the `*_REPORT.md` root-to-`workflow/` move). Do not `git add -A`; stage only the files this plan creates/modifies if a commit is requested.
- Spec reference: `docs/superpowers/specs/2026-08-03-doc-audit-and-process-setup-design.md`.

---

### Task 1: Create the rules folder

**Files:**
- Create: `docs/superpowers/rules/PROCESS_RULES.md`
- Create: `docs/superpowers/rules/SUMMARY.md`

**Interfaces:**
- Produces: the 5 standing rules referenced by every later task and by all future work in this project.

- [x] **Step 1: Write `docs/superpowers/rules/PROCESS_RULES.md`**

```markdown
# Documentation & Process Rules

These rules govern how `docs/superpowers/` is maintained. They apply to every future task in this project unless the user explicitly overrides one.

## 1. Plan before non-trivial work

Write an implementation plan to `docs/superpowers/plans/YYYY-MM-DD-<topic>.md` before starting code or document changes, for anything beyond a one-line fix.

## 2. Keep `project_ core/` documents current

Whenever a decision, code change, or investigation changes what the Architecture document(s) or `phase1-table-inventory.md` describe, update that document as part of finishing the task — not as a deferred follow-up.

## 3. Every `docs/superpowers/` subfolder maintains its own `SUMMARY.md`

Update the folder's `SUMMARY.md` in the same change that adds, removes, or significantly edits a file in that folder, so a fresh session can load folder context without reading every file in it.

## 4. Verified reports outrank generic architecture claims

When a `workflow/` report explicitly states it is code-verified (for example `authentication.md`, which opens with "Source of truth: actual code") and it conflicts with more generic `project_ core/` architecture text, treat the workflow report as correct until the architecture doc is updated to match it.

## 5. Edit boundary

Only `project_ core/` documents (the architecture docs, the tables file) get content edits during doc-sync work. Other folders (`plans/`, `workflow/`) only receive **new** files (new plans, new workflow reports) — an existing file there is only changed if the user explicitly asks for that specific file to change.

**Origin:** established 2026-08-03, see `docs/superpowers/specs/2026-08-03-doc-audit-and-process-setup-design.md`.
```

- [x] **Step 2: Write `docs/superpowers/rules/SUMMARY.md`**

```markdown
# rules/ — Summary

**Purpose:** Standing process rules for how `docs/superpowers/` is maintained across sessions.

**Last updated:** 2026-08-03

## Files

- `PROCESS_RULES.md` — the 5 standing rules: plan-before-work, keep `project_ core/` docs in sync with decisions, per-folder `SUMMARY.md` maintenance, workflow reports outrank generic architecture claims when they conflict, and the edit boundary (only `project_ core/` gets content edits; other folders are new-file-only).

## Open items

None.
```

- [x] **Step 3: Verify**

Run: `test -f "docs/superpowers/rules/PROCESS_RULES.md" && test -f "docs/superpowers/rules/SUMMARY.md" && echo OK`
Expected: `OK`

- [x] **Step 4: Commit** (only if the user has asked for a commit — see Global Constraints)

```bash
git add docs/superpowers/rules/PROCESS_RULES.md docs/superpowers/rules/SUMMARY.md
git commit -m "docs: add standing process rules for docs/superpowers"
```

---

### Task 2: Index the existing workflow reports (new file only)

**Files:**
- Create: `docs/superpowers/workflow/SUMMARY.md`

**Interfaces:**
- Consumes: existing file titles/scope lines already read from each report in `docs/superpowers/workflow/*.md`.
- Produces: a one-stop index so a fresh session doesn't need to open all 12 files to know what's there.

- [x] **Step 1: Write `docs/superpowers/workflow/SUMMARY.md`**

```markdown
# workflow/ — Summary

**Purpose:** Point-in-time, code-verified investigation/implementation reports. Unlike `project_ core/` (living architecture docs), these are dated snapshots of what was found or built for a specific task.

**Last updated:** 2026-08-03

## Files

- `authentication.md` — **Code-verified source of truth** for the full login/session/CSRF/authorization flow (base-domain login, workspace-selection challenge, exchange-code hand-off, MFA, legal-acceptance gate, logout), with an explicit "Current Gaps and Risks" section. Per [[PROCESS_RULES]] rule 4, this outranks the generic Authentication section in `ONEVO_Backend_Architecture_Document.md` wherever the two differ.
- `TENANT_SESSION_EXCHANGE_LOGIN_FLOW_REPORT.md` — implementation/verification report for the base-domain-to-tenant-host session-exchange mechanism (the one-time code hand-off).
- `LOGIN_WORKSPACE_RESPONSE_FIX_REPORT.md` — fix report for tenant/workspace fields missing from login response DTOs.
- `DEV_SMOKE_MULTI_TENANT_SEED_EXPANSION_REPORT.md` — report for expanding the Development/Test-only `DevSmokeTestTenantSeeder` to two tenants with multiple seeded users/roles/legal entities.
- `BACKEND_MKCERT_TENANT_SUBDOMAIN_HTTPS_REPORT.md` — fix report for local HTTPS/mkcert trust between the Angular dev server and the tenant-subdomain backend.
- `LEGAL_DOCUMENT_RICH_CONTENT_MANAGEMENT_REPORT.md` — implementation report for storing Terms/Privacy rich content in `legal_document_versions` plus admin CRUD/publish/archive and public read endpoints.
- `LEGAL_ENTITY_GENERAL_SETTINGS_BACKEND_AUDIT_PLAN.md` — Part 1 audit + plan (no code changed) for the Legal Entity / Company General Settings backend work.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2A_SCHEMA_REPOSITORY_REPORT.md` — Part 2A: schema/entity/repository layer report for Legal Entity General Settings.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2B_APPLICATION_CONTRACTS_REPORT.md` — Part 2B: Application-layer commands/queries/DTOs report for the same feature.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` — Part 2C: controller endpoint wiring + HTTP/integration test report for the same feature.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2D_POSTMAN_AND_HTTP_VALIDATION_REPORT.md` — Part 2D: Swagger/Postman re-verification report for the same feature.
- `LEGAL_ENTITY_POSTMAN_STALE_FOLDER_CLEANUP_REPORT.md` — Postman-collection-only cleanup report (no backend/doc changes).

## Open items

- This index was built from file titles/scope lines only; if a report is later superseded or found stale, mark it here rather than deleting it silently.
```

- [x] **Step 2: Verify**

Run: `test -f "docs/superpowers/workflow/SUMMARY.md" && diff <(ls docs/superpowers/workflow/*.md | xargs -n1 basename) <(grep -oE '^\- `[A-Za-z_.]+\.md`' docs/superpowers/workflow/SUMMARY.md | sed -E 's/^- `//; s/`$//' | sort) 2>/dev/null; echo "checked"`
Expected: every existing `workflow/*.md` file (except the new `SUMMARY.md` itself) is listed in the summary — visually confirm the file count matches (12 existing files + `authentication.md` = the list above has 12 entries total including `authentication.md`).

- [x] **Step 3: Commit** (only if requested)

```bash
git add docs/superpowers/workflow/SUMMARY.md
git commit -m "docs: add workflow/ summary index"
```

---

### Task 3: Index the existing plans (new file only)

**Files:**
- Create: `docs/superpowers/plans/SUMMARY.md`

**Interfaces:**
- Consumes: `**Goal:**` lines already extracted from each existing plan file.
- Produces: a chronological index of plans so a fresh session can see what's been planned without opening every file.

- [x] **Step 1: Write `docs/superpowers/plans/SUMMARY.md`**

```markdown
# plans/ — Summary

**Purpose:** Dated implementation plans, one per feature/fix, following the header format in the `writing-plans` skill (Goal / Architecture / Tech Stack / Global Constraints / numbered tasks with checkboxes).

**Last updated:** 2026-08-03

## Files (chronological)

- `2026-07-27-forgot-password-restricted-role-http-rls-proof.md` — proves `POST /api/v1/auth/forgot-password` enforces PostgreSQL RLS end-to-end over real HTTP under the restricted `onevo_app` runtime role, closing a gap where existing HTTP tests ran against a Testcontainers superuser connection that never surfaced RLS violations.
- `2026-07-28-legal-document-rich-content.md` — stores Terms/Privacy legal document content directly in `legal_document_versions`, adds Developer Platform admin CRUD+publish/archive endpoints and public read endpoints, and wires content into the pending-legal-acceptance flow.
- `2026-07-28-tenant-host-password-login-retirement.md` — removes the dead tenant-host email/password login path (`LoginCommand`/`LoginCommandHandler`/`LoginCommandValidator`), leaving base-domain credential-first login as the only password login entry point.
- `2026-08-02-dev-smoke-multi-tenant-seed-expansion.md` — expands `DevSmokeTestTenantSeeder` (Development/Test only) to two tenants, multiple users/roles, and multiple legal entities, all idempotent.
- `2026-08-03-doc-audit-and-process-setup.md` — **this plan.** Sets up `rules/` + per-folder `SUMMARY.md` files, reconciles the Architecture doc's Authentication section against `workflow/authentication.md`, and queues the remaining architecture/tables audit as Phase 2/3.

## Open items

- Phase 2 (remaining `ONEVO_Backend_Architecture_Document.md` sections: tenant isolation, caching, file handling, performance, testing, deployment) and Phase 3 (structural table-existence check of `phase1-table-inventory.md`) are queued in `2026-08-03-doc-audit-and-process-setup.md` Tasks 6-7 but not yet executed.
```

- [x] **Step 2: Verify**

Run: `test -f "docs/superpowers/plans/SUMMARY.md" && echo OK`
Expected: `OK`. Manually confirm all 5 files under `docs/superpowers/plans/` (4 pre-existing + this one) appear in the list.

- [x] **Step 3: Commit** (only if requested)

```bash
git add docs/superpowers/plans/SUMMARY.md
git commit -m "docs: add plans/ summary index"
```

---

### Task 4: Dev-context summary for `project_ core/` (new file only)

**Files:**
- Create: `docs/superpowers/project_ core/SUMMARY.md`

**Interfaces:**
- Consumes: the table/pillar breakdown table from the top of `phase1-table-inventory.md` (lines 1-18) and the file list of `project_ core/`.
- Produces: the "development context" the user asked for — a fast-loading summary of what the tables doc contains, without reading all ~4,890 lines of it.

- [x] **Step 1: Write `docs/superpowers/project_ core/SUMMARY.md`**

```markdown
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
```

- [x] **Step 2: Verify**

Run: `test -f "docs/superpowers/project_ core/SUMMARY.md" && echo OK`
Expected: `OK`. Manually confirm the table counts (78 + 42 + 48 + 63 + 14 + 7 = 252) match the source total stated in `phase1-table-inventory.md` line 5.

- [x] **Step 3: Commit** (only if requested)

```bash
git add "docs/superpowers/project_ core/SUMMARY.md"
git commit -m "docs: add project_ core/ dev-context summary"
```

---

### Task 5: Reconcile the Authentication section of the backend Architecture doc

**Files:**
- Modify: `docs/superpowers/project_ core/ONEVO_Backend_Architecture_Document.md:528-649` (the `### 3.4 Authentication and Authorization` section, ending just before `### 3.5 Database Architecture` at line 650)

**Interfaces:**
- Consumes: the verified flow described in `docs/superpowers/workflow/authentication.md` (§1 Overview, §3 Login Workflow, §4 API-by-API behaviour, §6 Session Lifecycle, §12 Current Gaps and Risks).
- Produces: an Architecture doc section that keeps its existing valid general rules but adds the concrete verified flow and a linked gaps subsection, so future readers don't have to cross-reference two documents to know what's actually implemented.

This task edits an existing `project_ core/` file, which is inside the allowed edit boundary.

- [x] **Step 1: Insert the verified-flow subsection**

Insert immediately after the existing "Implementation mechanism: browser authentication uses ASP.NET Core Cookie Authentication..." paragraph (currently line 545) and before the existing "Rules:" line (currently line 547):

```markdown

**Verified end-to-end flow (code-verified, see `docs/superpowers/workflow/authentication.md`):**

Tenant browser login is base-domain credential-first, not tenant-host password login:

```text
1. Browser submits email/password to the base/system host: POST /api/v1/auth/login
2. BaseLoginCommandHandler fetches all tenant/user candidates for that email across
   every tenant and verifies the password with a fixed-work-factor timing-safe check
   (always exactly 8 BCrypt comparisons, padded with a dummy hash)
3. Zero/overflow matches -> generic 401 (enumeration-safe)
   Multiple matches (2-8) -> workspace-selection challenge (5-minute, single-use)
   Exactly one match -> LoginContinuationService.ContinueAsync
4. Continuation order: must_change_password -> MFA challenge (if verified TOTP exists)
   -> legal-acceptance gate -> finalize
5. Finalization is explicit, not host-inferred:
     BaseDomainExchange  -> issues a 2-minute opaque one-time exchange code, no cookie set yet
     TenantHostDirect    -> sets the real onevo_session/onevo_csrf cookies immediately
6. Browser follows continue_url to the tenant subdomain's /auth/continue?code=...
   -> POST /api/v1/auth/session-exchange consumes the code and finally sets
      onevo_session + onevo_csrf on the correct tenant host
```

Session lifecycle values (tenant `sessions` table and admin `platform_user_sessions`, same policy for both): sliding window 30 minutes, renewal threshold 15 minutes, absolute lifetime 8 hours hard cap regardless of activity, revocation is DB-flag based (`IsRevoked=true`) and immediate on logout.
```

- [x] **Step 2: Append a documented-gaps subsection**

Insert immediately before `### 3.5 Database Architecture` (currently line 650), after the existing "Frontend rule" code block:

```markdown

#### Documented Gaps (verified against code, see `docs/superpowers/workflow/authentication.md` §12)

These are known, unresolved items — not proposed changes:

- `IJwtTokenService.GenerateDeviceToken` is registered in DI but has zero call sites in `src/` — unused scaffolding for an unbuilt device/agent auth surface.
- The legacy `RefreshToken`/`IRefreshTokenRepository` table is only touched by `ResetPasswordCommandHandler` (revocation on password reset) — no login path ever issues one.
- `ForcePasswordChangeCommandHandler` requires tenant-host context, but the continue-URL is built from the host where login started — a base-domain-triggered forced password change can produce an unreachable `continue_url`.
- No multi-session/device management exists on either tenant or admin side (no listing or selective revocation of concurrent sessions).
- No permission-gating UI consumer exists yet; `permissions[]`/`activeModules[]` are fetched and stored by the frontend but not read for conditional rendering.

Per [[PROCESS_RULES]] rule 4, this list is sourced from the code-verified report and must be kept in sync with it — update both together if either changes.
```

- [x] **Step 3: Verify the edit**

Run: `grep -n "Verified end-to-end flow" "docs/superpowers/project_ core/ONEVO_Backend_Architecture_Document.md"` and `grep -n "Documented Gaps" "docs/superpowers/project_ core/ONEVO_Backend_Architecture_Document.md"`
Expected: both greps return one match each, both located between the existing `### 3.4 Authentication and Authorization` heading and the existing `### 3.5 Database Architecture` heading (confirm with `grep -n "^### 3\."` on the file that section numbering/order is undisturbed).

- [x] **Step 4: Commit** (only if requested)

```bash
git add "docs/superpowers/project_ core/ONEVO_Backend_Architecture_Document.md"
git commit -m "docs: reconcile architecture doc auth section with verified auth workflow"
```

---

### Task 6: Record Phase 2 as queued future work (no execution)

**Files:**
- Modify: `docs/superpowers/plans/2026-08-03-doc-audit-and-process-setup.md` (this file — add the queued-phase record below as part of writing this plan; no separate edit needed once this plan file itself contains this task)

**Interfaces:**
- Produces: an explicit, actionable follow-up description so Phase 2 can be started on request without re-deriving scope.

- [x] **Step 1: Confirm this section exists in the plan (it does, below in "Queued Follow-Up Phases")** — no action needed beyond what's written in that section; this task exists so Phase 2 has its own checkbox and isn't silently skipped.

---

### Task 7: Record Phase 3 as queued future work (no execution)

**Files:**
- Same as Task 6 — recorded in "Queued Follow-Up Phases" below.

- [x] **Step 1: Confirm this section exists in the plan (it does, below in "Queued Follow-Up Phases")** — no action needed beyond what's written in that section; this task exists so Phase 3 has its own checkbox and isn't silently skipped.

---

## Queued Follow-Up Phases (not executed by this plan)

**Phase 2 — remaining Architecture doc sections vs. code.** For each of: tenant isolation implementation (`HostTenantResolutionMiddleware`, `TenantEnforcementMiddleware`, `TenantRlsInterceptor`, RLS policies in `ops/postgres`), caching (`src/ONEVO.Infrastructure/Caching`), file/document handling (`src/ONEVO.Infrastructure/Services` R2 integration, `file_records`/`file_upload_reservations` usage), performance/reliability, logging/audit (Serilog usage, `audit_logs` writes), testing (actual `tests/ONEVO.Tests.*` coverage vs. the doc's stated targets), and deployment — read the actual implementation, compare it against the corresponding Architecture doc section, and update that section in place following the same pattern as Task 5 (verified-flow-style addition, gaps subsection where applicable). Write a new dated plan (`docs/superpowers/plans/YYYY-MM-DD-architecture-phase2-<section>.md`) per section or per small group of sections when this phase is started, per rule 1 in `PROCESS_RULES.md`.

**Phase 3 — tables file structural check.** Enumerate actual EF Core entity configurations under `src/ONEVO.Infrastructure/Persistence/Configurations/` and applied migrations under `src/ONEVO.Infrastructure/Migrations/`, then confirm each of the 252 tables documented in `phase1-table-inventory.md` has a corresponding configuration/migration (flag missing or extra tables). This is existence/structure checking, not a column-by-column diff. Write a dated plan before starting, per rule 1.

Both phases start only when explicitly requested — they are not implied by completing Tasks 1-5.

---

## Self-Review

**Spec coverage:** Design §3.1 (new folders/files) → Tasks 1-4. §3.2 (standing rules) → Task 1 Step 1. §3.3 Phase 1 → Task 5. §3.3 Phase 2/3 → Queued Follow-Up Phases section + Tasks 6-7. §3.4 (frontend doc limitation) → Task 4 Step 1 Files section. §4 (out of scope) → respected: no column-level tables audit, no edits to existing `plans/`/`workflow/` files, no sections beyond Authentication touched, no frontend code verification attempted.

**Placeholder scan:** No TBD/TODO markers; every file has full content written out; every verify step has a concrete command and expected output.

**Type/name consistency:** `[[PROCESS_RULES]]` link target matches the file created in Task 1 (`rules/PROCESS_RULES.md`). Table counts in Task 4 (78+42+48+63+14+7=252) match `phase1-table-inventory.md`'s stated "Phase 1 total: 252 tables". Section line numbers referenced in Task 5 (528-649, insertion points at 545 and 650) were confirmed by reading the live file before writing this plan.
