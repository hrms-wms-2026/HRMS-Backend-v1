# Incident Management — Admin Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Incidents area to the Platform Admin console: list, declare, detail with lifecycle actions, timeline, affected tenants, customer-update composer, escalation with retry, postmortem, settings, and incident actions on support tickets.

**Architecture:** New feature module `src/app/modules/incidents` following the `support` module layout (`data/` models and services mapping the snake_case API to camelCase, `feature/` standalone components with `.html` + `.ts` + `.spec.ts`). Routes in `app.routes.ts` with breadcrumb and `permission` data; sidebar entry under Support. All calls go to the backend API from the backend plan.

**Tech Stack:** Angular 21 (standalone components, signals), Tailwind, Jest, existing shared UI (`button`, `modal`, `confirmation-dialog`, `status-badge`, `table`, `pagination`, `searchable-select`, `empty-state`, `error-banner`, skeletons).

**Spec:** `HRMS-Backend-v1` `docs/superpowers/specs/next/2026-11-16-incident-management-design.md` (approved, DEC-030). **API:** `docs/superpowers/plans/next/2026-11-16-incident-management-backend.md` (routes and DTO fields there are the contract).

**Repo:** `platform-administration`, branch `feat/incident-management-admin-ui` from `development`, after the backend branch is pushed (develop against a local backend running it).

## Global Constraints

- Endpoint paths only in `src/app/core/config/api-endpoints.ts`; base URL from `environment.apiUrl` (repo CLAUDE.md).
- Route and sidebar `permission` data: list/detail `platform.incidents.read`; every write button hidden unless `PermissionStore.hasPermission('platform.incidents.manage')`; settings save needs `platform.incidents.configure`.
- Status labels exactly Investigating, Identified, Monitoring, Resolved, Closed; severity labels Critical, Major, Minor. Severity badge tones: critical = red, major = amber, minor = slate.
- Nothing reaches customers without the composer's **Send** button. Changing status only opens the composer.
- No light-only colours: every new element needs `dark:` variants (repo dark-mode pattern).
- Product name in UI copy is "Crownus"; the visible-product-name guard test must stay green.
- Copy strings fixed by the spec: "Customer update pending", "Fix ready", "Does this need an engineering task?", "You have no employee record in the internal tenant with this email. Ask to be added first." (shown as returned by the API).

## Review Focus

- **Double click on Send:** the button disables while sending and the composer keeps one `send_key` per opening, so a retry after a network error reuses it. Test in Task 4.
- **Session expiry during a long composer edit:** the draft text survives a 401 → login → return (kept in the component's state via `sessionStorage` key `incident-update-draft:{id}:{status}`), cleared after a successful Send. Test in Task 4.
- **Incident with zero affected tenants:** Send is disabled with the hint "Add an affected tenant first". Test in Task 4.
- **Escalation partially fails** (one of three bugs `failed`): the dialog closes, the links list shows two created and one failed with Retry, and "Fix ready" stays off. Test in Task 5.
- **User without manage permission opens an incident:** read-only view, no action buttons, no composer. Test in Task 3.

---

### Task 1: Data layer

**Files:**
- Create: `src/app/modules/incidents/data/incident.model.ts`, `incidents.service.ts`, `incident-settings.service.ts`
- Modify: `src/app/core/config/api-endpoints.ts` (`incidents` block)
- Test: `src/app/modules/incidents/data/incidents.service.spec.ts`

**Interfaces:**
- Produces `IncidentsService` methods (Observables): `list(filters)`, `get(id)`, `timeline(id)`, `declare(req)`, `updateDetails(id, req)`, `changeStatus(id, status)`, `changeSeverity(id, severity)`, `acknowledge(id)`, `addTenant(id, tenantId)`, `removeTenant(id, tenantId)`, `linkTicket(id, ticketId)`, `addNote(id, note)`, `customerUpdateDraft(id, forStatus?)`, `sendCustomerUpdate(id, req)`, `markNoCustomerUpdate(id)`, `escalate(id, req)`, `retryWorkLink(id, linkId)`, `internalEmployees(search)`, `savePostmortem(id, req)`, `completePostmortem(id, req)`; `IncidentSettingsService.get()`, `save(req)`.
- Models in camelCase mirroring the backend DTOs (`Incident`, `IncidentDetail`, `IncidentTimelineEvent`, `CustomerUpdateDraft`, `IncidentWorkLink`, `IncidentSettings`, `Severity`, `IncidentStatus` union types).

- [ ] **Step 1:** Write `incidents.service.spec.ts`: `list({ open: true, severity: 'critical' })` sends `open=true&severity=critical`; `get` maps `customer_update_pending` → `customerUpdatePending`, `fix_ready` → `fixReady`, `affected_tenant_count` → `affectedTenantCount`, work link `task_short_id` → `taskShortId`; `sendCustomerUpdate` posts `send_key`, `for_status`, `banner`, `email`.
- [ ] **Step 2:** `npx jest src/app/modules/incidents/data` → FAIL.
- [ ] **Step 3:** Implement models, services, endpoints.
- [ ] **Step 4:** Same command → PASS.
- [ ] **Step 5:** Commit `feat(incidents): admin data layer`.

### Task 2: Routes, sidebar and list page with Declare

**Files:**
- Create: `src/app/modules/incidents/feature/incidents-list/` (`incidents-list.ts/.html/.spec.ts`), `src/app/modules/incidents/ui/declare-incident-dialog/` (`.ts/.html/.spec.ts`), `ui/severity-badge/`
- Modify: `src/app/app.routes.ts` (`incidents`, `incidents/:id`, `incidents/settings`), `src/app/layouts/main-layout/sidebar/sidebar.ts` + `.html` (item "Incidents", route `/incidents`, icon `incidents`, permission `platform.incidents.read`, first item under Support), `sidebar.spec.ts`

**Interfaces:**
- `DeclareIncidentDialog` inputs: `fromTicket?: { id: string; tenantId: string; tenantName: string; subject: string }`; output `declared: Incident`. Used again in Task 8.
- List columns: Number, Title, Severity badge, Status, Owner, Affected tenants (count), flags "Customer update pending" and "Fix ready", Detected. Filters: Open/All, status, severity, owner. Sidebar shows a red count badge of open Critical incidents (one `list({ open: true, severity: 'critical', pageSize: 1 })` call, using `total`).

- [ ] **Step 1:** Specs: list renders flags and badge tones; filters call `list` with the right params; Declare requires title and severity and calls `declare` then navigates to `/incidents/{id}` (with `?compose=1` for Critical, which makes the detail page open the composer at once, spec 5.2); sidebar contains `/incidents` and hides it without the read permission.
- [ ] **Step 2:** `npx jest src/app/modules/incidents/feature/incidents-list src/app/modules/incidents/ui src/app/layouts/main-layout/sidebar` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(incidents): incidents list, declare and navigation`.

### Task 3: Incident detail — header, lifecycle, tenants, links, timeline

**Files:**
- Create: `src/app/modules/incidents/feature/incident-detail/` (`incident-detail.ts/.html/.spec.ts`), `ui/incident-timeline/`, `ui/affected-tenants-panel/`
- Test: specs alongside

**Interfaces:**
- Opening `/incidents/{id}?compose=1` opens the composer for the current status immediately (used after declaring a Critical incident).
- Header: number, title, severity (changeable), status (changeable via a select limited to allowed transitions), owner, timestamps, **Acknowledge** button while `acknowledgedAt` is null. Status change calls `changeStatus` and then opens the composer (Task 4) for the new status. Moving to Identified with no `engineering_bug` link shows the prompt "Does this need an engineering task?" with Escalate / Not now.
- Affected tenants panel: searchable tenant picker (existing tenants list endpoint), add, remove with confirmation "Remove {tenant}? Its banner disappears now; emails already sent stay sent."
- Links: support tickets (link to `/support/tickets/{id}`), work links (Task 6 renders them).
- Timeline: oldest first, each event with time, actor and a readable sentence per event type; notes form.

- [ ] **Step 1:** Specs: allowed-transition list for `closed` is only Investigating; acknowledge button hidden after acknowledgement; remove tenant asks for confirmation and calls `removeTenant`; Identified prompt appears only without engineering links; manage-less user sees no action controls (Review Focus).
- [ ] **Step 2:** `npx jest src/app/modules/incidents/feature/incident-detail src/app/modules/incidents/ui/incident-timeline src/app/modules/incidents/ui/affected-tenants-panel` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(incidents): incident detail, lifecycle and timeline`.

### Task 4: Customer update composer

**Files:**
- Create: `src/app/modules/incidents/ui/customer-update-composer/` (`.ts/.html/.spec.ts`)
- Modify: `incident-detail.ts/.html` (open on status change and from "Post update"; history list of sent updates)

**Interfaces:**
- Inputs `incident: IncidentDetail`, `forStatus: IncidentStatus`; outputs `sent`, `closed`. On open: calls `customerUpdateDraft`, creates `sendKey = crypto.randomUUID()`, restores a saved draft from `sessionStorage` if present. Shows targets (names and count), a notice listing `tenantsNotYetTold`, Banner and Email toggles (both on). **Send** disabled while sending, when there are no targets, or when title/body are empty. For Minor incidents a secondary **No customer update required** button calls `markNoCustomerUpdate`.

- [ ] **Step 1:** Specs: draft prefill; one `sendKey` reused across a failed then retried send; Send disabled while in flight and with zero targets (Review Focus); draft restored after re-creation and removed after success (Review Focus); no-update button only for minor.
- [ ] **Step 2:** `npx jest src/app/modules/incidents/ui/customer-update-composer` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(incidents): customer update composer`.

### Task 5: Escalation and work links

**Files:**
- Create: `src/app/modules/incidents/ui/escalate-dialog/`, `ui/work-links-panel/` (each `.ts/.html/.spec.ts`)
- Modify: `incident-detail.html` (Escalate button, panel, "Fix ready" banner with "Move to Monitoring and post an update?" action)

**Interfaces:**
- Escalate dialog: one or more bug rows (title required, description optional, assignee from `internalEmployees(search)`); a `submissionId = crypto.randomUUID()` per dialog opening; calls `escalate`; on response shows per-row result and closes when at least one row was submitted.
- Work links panel: kind label (Engineering bug / Postmortem action), short id, status chip (Pending / Created / Failed), error text, **Retry** for failed (and pending older than 2 minutes).
- Fix ready banner action calls `changeStatus(id, 'monitoring')` then opens the composer.

- [ ] **Step 1:** Specs: multiple rows submitted with one submission id; partial failure shows Retry only on the failed link and Fix ready stays hidden (Review Focus); Retry calls `retryWorkLink`; Fix ready banner action changes status then opens the composer.
- [ ] **Step 2:** `npx jest src/app/modules/incidents/ui/escalate-dialog src/app/modules/incidents/ui/work-links-panel` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(incidents): escalation and work links`.

### Task 6: Postmortem

**Files:**
- Create: `src/app/modules/incidents/ui/postmortem-panel/` (`.ts/.html/.spec.ts`)
- Modify: `incident-detail.html` (panel visible from Resolved; Closed option disabled for Critical until completed, with the hint "Complete the postmortem before closing a Critical incident")

**Interfaces:** fields summary, root cause, customer impact, what went well, what went badly; Save draft → `savePostmortem`; Complete (summary and root cause required) with action rows reusing the escalate row component → `completePostmortem` with its own `submissionId`.

- [ ] **Step 1:** Specs: hidden before Resolved; Complete disabled without summary/root cause; Closed disabled for critical until `completedAt`.
- [ ] **Step 2:** `npx jest src/app/modules/incidents/ui/postmortem-panel` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(incidents): postmortem`.

### Task 7: Incident settings page

**Files:**
- Create: `src/app/modules/incidents/feature/incident-settings/` (`.ts/.html/.spec.ts`)

**Interfaces:** shows internal tenant, project, objective, bug category and the validity state (`isValid`, `problem`); editable only with `platform.incidents.configure`; tenant picker lists only tenants the API accepts (errors from the API shown inline).

- [ ] **Step 1:** Specs: read-only without configure; invalid state shows the problem text; save calls `save`.
- [ ] **Step 2:** `npx jest src/app/modules/incidents/feature/incident-settings` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(incidents): incident settings`.

### Task 8: Incident actions on support tickets

**Files:**
- Modify: `src/app/modules/support/feature/support-ticket-detail/support-ticket-detail.ts/.html/.spec.ts`, `src/app/modules/support/data/support-ticket.model.ts` + service (map `incident_id`, `incident_number`)

**Interfaces:** when the ticket has no incident: **Declare incident** (opens `DeclareIncidentDialog` with `fromTicket`) and **Link to incident** (picker of open incidents → `linkTicket`); when it has one: a chip "INC-12" linking to `/incidents/{id}`. Buttons need `platform.incidents.manage`.

- [ ] **Step 1:** Specs for both states and the permission rule.
- [ ] **Step 2:** `npx jest src/app/modules/support/feature/support-ticket-detail` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5:** Commit `feat(support): declare or link incidents from tickets`.

### Task 9: Whole-branch verification

- [ ] `npx jest` — all suites pass.
- [ ] `npm run lint` — clean.
- [ ] `npm run build` — no warnings or errors (pre-push hook rule).
- [ ] Manual check against the local backend: declare from a ticket, send an update, escalate one bug, see "Fix ready" after the bug is done in the internal tenant.
- [ ] Push `feat/incident-management-admin-ui`. No PR unless Dabi asks.
