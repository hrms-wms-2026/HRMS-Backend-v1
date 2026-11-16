# Incident Management — Tenant App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let tenants see incident banners meant for them, set an incident contact email, and let the internal tenant's engineers see which tasks come from incidents.

**Architecture:** Small changes to existing tenant app pieces: the main-layout announcement banner (already reads `api/v1/announcements/active`), the Settings > Support page (incident contact field), and Work Management task card, task form header and board/backlog filters (incident chip and filter).

**Tech Stack:** Angular 21, Tailwind, Vitest via `ng test`.

**Spec:** `HRMS-Backend-v1` `docs/superpowers/specs/next/2026-11-16-incident-management-design.md` (approved, DEC-030). **API:** `docs/superpowers/plans/next/2026-11-16-incident-management-backend.md` (Tasks 6, 7, 9).

**Repo:** `Hrms--Web-application---front-end---v1`, branch `feat/incident-management-tenant-ui` from `Development`, after the backend branch is pushed.

## Global Constraints

- No hard-coded colours (`npm run lint` runs `check-hardcoded-colors.mjs`); use theme tokens.
- Incident banner severity styling follows the existing banner's critical / warning / info styles.
- The task chip shows the incident number and severity only, never tenant names (the API does not send them).
- Incident contact email is editable only by users with `support:manage`.

## Review Focus

- **Banner for another tenant's incident:** the API never returns it; the banner component must not cache banners across a workspace switch (cleared on tenant change). Test in Task 1.
- **Invalid or cleared contact email:** an invalid address shows the API's validation message; saving an empty field sends `null` and shows "Updates will go to your support admins". Test in Task 2.
- **Task from an older incident snapshot** (incident severity changed after escalation): the chip shows the snapshot severity; no extra request is made. Test in Task 3.

---

### Task 1: Incident banners in the main layout

**Files:**
- Modify: `src/app/layouts/main-layout/announcement-banner/announcement-banner.component.ts` (+ spec)

**Interfaces:** consumes `api/v1/announcements/active`, which now also returns `selected_tenants` items for the caller's tenant. No API change on the client.

- [ ] **Step 1:** Spec: a `selected_tenants` item with severity `critical` renders with the critical style; banners reload (not reused) after the active tenant changes.
- [ ] **Step 2:** `npx ng test --watch=false --include "src/app/layouts/main-layout/announcement-banner/**/*.spec.ts"` → FAIL.
- [ ] **Step 3:** Implement the tenant-change reload if missing; no other change if the spec already passes for rendering.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(support): incident banners reload per tenant`.

### Task 2: Incident contact email in Settings > Support

**Files:**
- Modify: `src/app/modules/settings/data-access/support-tickets-api.service.ts` (`getIncidentContact()`, `saveIncidentContact(email: string | null)`)
- Modify: `src/app/modules/settings/feature/support-tickets/support-tickets.component.ts` (+ template, + spec)

**Interfaces:** `GET`/`PUT api/v1/settings/incident-contact`, body `{ email: string | null }`. A small card at the top of the Support page: "Incident contact email" with helper text "Incident updates are emailed here. If empty, they go to users who can manage support."

- [ ] **Step 1:** Spec: loads and shows the saved email; save sends trimmed value; empty sends `null` and shows the fallback text; API 400 message shown (Review Focus).
- [ ] **Step 2:** `npx ng test --watch=false --include "src/app/modules/settings/feature/support-tickets/**/*.spec.ts"` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(settings): incident contact email`.

### Task 3: Incident chip and filter on tasks

**Files:**
- Modify: `src/app/modules/work/models/dto/task.dto.ts` (`sourceIncident?: { id: string; number: string; severity: 'critical' | 'major' | 'minor'; linkKind: 'engineering_bug' | 'postmortem_action' }`) and its mapper
- Modify: `src/app/modules/work/ui/task-card/task-card.component.ts` (+ spec), `src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts` (chip beside the breadcrumb id at line ~160) (+ spec)
- Modify: `src/app/modules/work/feature/task-board/task-board.component.ts`, `src/app/modules/work/feature/task-backlog/task-backlog.component.ts` (filter toggle "Incident-linked" → `incident_linked=true`) (+ specs)

**Interfaces:** chip text "INC-12 · Critical" for engineering bugs and "Postmortem · INC-12" for postmortem actions; severity tone critical/major/minor from theme tokens.

- [ ] **Step 1:** Specs: chip renders from the DTO snapshot without extra requests (Review Focus); no chip when `sourceIncident` is absent; filter toggle adds `incident_linked=true`.
- [ ] **Step 2:** `npx ng test --watch=false --include "src/app/modules/work/ui/task-card/**/*.spec.ts" --include "src/app/modules/work/feature/task-board/**/*.spec.ts" --include "src/app/modules/work/feature/task-backlog/**/*.spec.ts"` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(work): incident chip and filter on tasks`.

### Task 4: Whole-branch verification

- [ ] `npx ng test --watch=false` — all pass (pre-existing failures on `Development` must not grow; list any).
- [ ] `npm run lint` — clean.
- [ ] `npm run build` — clean (pre-push hook).
- [ ] Push `feat/incident-management-tenant-ui`. No PR unless Dabi asks.
