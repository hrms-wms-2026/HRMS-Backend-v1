# Design: Documentation Process Setup + Architecture/Tables Audit (Phase 1)

**Date:** 2026-08-03
**Status:** Approved
**Owner request source:** conversation in IDE, HRMS-Backend-v1 project

## 1. Problem

`docs/superpowers/` has grown organically: `project_ core/` holds the two long-form architecture documents (backend, frontend) and the 252-table entity inventory (`phase1-table-inventory.md`); `plans/` holds dated implementation plans; `workflow/` holds code-verified reports written after specific investigations (e.g. `authentication.md`, which explicitly states "Source of truth: actual code" and lists concrete gaps).

Two problems exist today:

1. **No standing process.** There's no written rule that (a) work should get a plan before it starts, (b) the architecture/tables documents should be updated whenever a decision changes what they describe, or (c) each folder should carry a summary so a new AI session/dev can orient quickly without reading every file.
2. **Architecture doc drift.** `ONEVO_Backend_Architecture_Document.md` describes authentication only at a generic, principle level ("tenant users use secure cookies... optional MFA challenge..."). It predates and doesn't reference the concrete, code-verified flow already documented in `workflow/authentication.md` — the workspace-selection challenge, the base-domain exchange-code hand-off, the legal-acceptance gate, and the known gaps (dead `IJwtTokenService.GenerateDeviceToken`, the `force-change-password` continue-URL bug, no multi-session management). The doc isn't wrong so much as generic/aspirational where a more precise, verified description already exists elsewhere.

The rest of the architecture document (tenant isolation, caching, file handling, performance, testing, deployment) has the same generic-vs-verified gap, but auditing all of it against the codebase in one pass is a large effort. The user asked to phase this: do the process setup + the authentication reconciliation now, queue the rest.

## 2. Constraint

Only files inside `docs/superpowers/project_ core/` may be edited (the two architecture docs, the tables file). Every other file this work touches is a **new** file — nothing in `docs/superpowers/plans/` or `docs/superpowers/workflow/` that already exists gets modified.

## 3. Solution

### 3.1 New folders/files (additions only)

| Path | Purpose |
|---|---|
| `docs/superpowers/rules/PROCESS_RULES.md` | Standing rules (see §3.2) |
| `docs/superpowers/rules/SUMMARY.md` | Summary of the rules folder itself |
| `docs/superpowers/project_ core/SUMMARY.md` | Purpose + file list + dev-context summary of `phase1-table-inventory.md` (252 tables, 3 pillars + shared foundation + dev platform) + known-drift notes |
| `docs/superpowers/plans/SUMMARY.md` | Index of existing dated plans, one-liner each |
| `docs/superpowers/workflow/SUMMARY.md` | Index of existing workflow reports, one-liner each, flagging which ones are code-verified sources of truth |
| `docs/superpowers/specs/2026-08-03-doc-audit-and-process-setup-design.md` | This document |
| `docs/superpowers/plans/2026-08-03-doc-audit-and-process-setup.md` | The phased implementation plan (written via writing-plans skill next) |

### 3.2 Standing process rules (content of `rules/PROCESS_RULES.md`)

1. **Plan before non-trivial work.** Write an implementation plan to `docs/superpowers/plans/YYYY-MM-DD-<topic>.md` before starting code or document changes, for anything beyond a one-line fix.
2. **Keep `project_ core/` documents current.** Whenever a decision, code change, or investigation changes what the Architecture document(s) or `phase1-table-inventory.md` describe, update that document as part of finishing the task — not as a deferred follow-up.
3. **Every `docs/superpowers/` subfolder maintains its own `SUMMARY.md`.** Update it in the same change that adds/removes/significantly edits a file in that folder, so a fresh session can load folder context without reading every file.
4. **Verified reports outrank generic architecture claims.** When a `workflow/` report explicitly states it's code-verified (like `authentication.md`) and conflicts with the more generic `project_ core/` architecture text, treat the workflow report as correct until the architecture doc is updated to match it.
5. **Edit boundary.** Only `project_ core/` documents get content edits during doc-sync work; other folders only receive new files (new plans, new workflow reports, new summaries) unless the user explicitly asks to change an existing one.

### 3.3 Architecture/tables audit — phasing

**Phase 1 (this plan, done now):**
- Reconcile the Authentication section of `ONEVO_Backend_Architecture_Document.md` against `workflow/authentication.md`: replace generic-only language with the verified concrete flow (workspace-selection challenge, exchange-code hand-off, legal-acceptance gate), and add a documented-gaps subsection referencing the known issues.
- Summarize `phase1-table-inventory.md` into `project_ core/SUMMARY.md` as dev context (no content changes to the tables file itself yet).

**Phase 2 (queued, not started):** Verify remaining Architecture doc sections — tenant isolation implementation, caching, file/document handling, performance/reliability, logging/audit, testing, deployment — against actual code in `src/` and `tests/`, updating the doc section by section.

**Phase 3 (queued, not started):** Structural check of `phase1-table-inventory.md` against actual EF Core configurations/migrations — confirm each documented table exists (or flag drift), not an exhaustive column-by-column diff.

Phases 2 and 3 are written into the implementation plan as explicit future steps; they run when the user asks to continue.

### 3.4 Frontend architecture doc

`ONEVO_HRMS_Frontend_Architecture (1).md` cannot be code-verified from this repo (no frontend source present here). This limitation is recorded in `project_ core/SUMMARY.md` rather than silently skipped.

## 4. Out of scope for this phase

- Any column-level tables-file audit.
- Any edits to existing `plans/` or `workflow/` files.
- Full architecture doc sections beyond Authentication.
- Frontend code verification (no frontend repo present here).

## 5. Self-review notes

- No placeholders/TBDs remain in this design.
- Scope is explicit and phased; Phase 2/3 are named but not expanded into detailed steps here — that happens in the implementation plan.
- Edit boundary (project_ core/ only) is stated once and applied consistently across every proposed change.
