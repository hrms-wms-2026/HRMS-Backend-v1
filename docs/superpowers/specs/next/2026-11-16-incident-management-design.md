# Incident Management (Platform Admin): Design

**Date:** 2026-11-16 · **Status:** design for review · **Owner:** Dabi
**Decisions behind this design:** DEC-005, DEC-009, DEC-014, DEC-015, DEC-017, DEC-018, DEC-019, DEC-020, DEC-021, DEC-022 in Dabi's decisions log (`onexso\todo\DECISIONS.md`).

## 1. Intent

When something breaks in Crownus after release, **the Crownus team must know first**, act in one place, and then **keep the affected customers informed**. Today there is no incident concept: a customer raises a support ticket, an email alert may arrive, and the fix is tracked somewhere else. This design adds incident management to Platform Admin, modelled on how Dynamics 365 Customer Service cases link to Azure DevOps work items: the incident lives on the platform side and talks to customers; the engineering work lives in Crownus's own Work Management, in an internal tenant the team will move to from Azure DevOps (DEC-015). The two are linked and kept in sync.

Order of importance (DEC-017, Dabi): 1) the team knows and coordinates, 2) affected customers get updates, 3) SLA reporting later (timestamps only for now).

## 2. Goals, non-goals, anti-goals

**Goals (Release 1)**
- A platform admin can declare an incident from scratch or from a support ticket, with a severity, an owner and the affected tenants.
- The team is notified according to severity.
- Each status change pre-fills a customer update from a template; a person edits it and sends it to the affected tenants only, as an in-app banner and an email.
- The incident can be escalated into one or more linked bugs in the internal tenant's Work Management; when the bugs are done the incident shows "fix ready".
- A timeline records everything that happened, with timestamps for detected, acknowledged, first customer update and resolved.
- A small postmortem after resolution; its actions become tasks in the internal tenant.

**Non-goals (Release 1)**
- SLA timers, breach warnings or automatic escalation (DEC-020).
- A public status page.
- Syncing comments between incidents and bugs; only links and status.
- GitHub links on bugs (DEC-016, after Release 1).
- On-call rotations or paging.

**Anti-goals (would make this a failure even if it works)**
- Anything reaching customers without a person pressing Send (DEC-018).
- An update about one customer's incident showing to unaffected tenants (DEC-017).
- Customer tenant names or customer data appearing inside the internal tenant's Work Management.
- The platform gaining a general way to write into customer tenants. The only cross-tenant write allowed is into the one configured internal tenant.
- Privileged actions that the platform audit log does not record.

## 3. Constraints

- **Platform users are not tenant employees.** Work Management tasks need a tenant, a project, an objective, a category and a creating user; assignees are employees. The internal tenant must exist with the team as employees (DEC-015).
- **RLS and admin mode.** Platform Admin runs in admin mode. Writing into the internal tenant must switch tenant context explicitly (`ITenantContextSwitcher`, which resets the connection so RLS sees the new tenant), and switch back.
- **Audit (F-07).** Every incident write command implements `IAuditedCommand`; the architecture tests that require this must keep passing. Audit projections never include update bodies, postmortem text or customer contact emails.
- **Permissions** are platform permission codes in `PlatformPermissionCatalog`, enforced with `[RequirePlatformPermission]` under `AdminPolicy` (MFA required).
- **Email and notifications** go through the outbox and notification templates. New templates must be added wherever reference-data parity tests expect them.
- **Builds on unmerged work.** Support tickets, announcements and `Support:AlertEmail` come from backend PR #227 (`feat/platform-support-module`); `IAuditedCommand` and its architecture tests come from `feat/sec-f07-platform-audit-log` (no PR yet). This feature starts after both are merged into `development`, in the order already agreed (DEC-001: #227, then F-07). It uses #227's flat announcement model (`all` / `tenants` / `platform_admins`); the older hierarchical model on `feature/platform-admin-notifications-v2` is abandoned (that branch is on the stale-branch list).
- **Existing pieces to reuse:** support tickets (one tenant each; priorities low/medium/high/urgent; `Support:AlertEmail` already emails the support inbox), announcements (title, body, severity info/warning/critical, start/end, audience `all` / `tenants` / `platform_admins`; tenant banner reads `api/v1/announcements/active`), the outbox processor, and the Work Management precedent of creating a task from another source (`SourceMeetingOccurrenceId`).

## 4. Approaches

| Approach | Summary | Verdict |
|---|---|---|
| **A. Separate platform-side Incident module** | New `DevPlatform/Incidents` feature owns incidents, timeline, affected tenants, customer updates and postmortem. Engineering work is Work Management bugs in the internal tenant, created by one guarded cross-tenant writer and synced back by outbox events. | **Chosen (DEC-022).** An incident spans many tenants, so it is platform data; permissions, MFA and audit stay in Platform Admin; Work Management only does engineering work. Matches the D365 case ↔ DevOps work item model. |
| B. Incident as a Work Management item type in the internal tenant | Reuse board, comments and assignment. | Rejected: platform admins are not employees there, affected tenants and customer updates are platform data, and customer names would end up inside an internal tenant's tasks. |
| C. Extend the support ticket | Add type "incident" and severity to `SupportTicket`. | Rejected: a ticket belongs to one tenant; an incident affects many. It would break the ticket model. |

## 5. Design

### 5.1 Concepts

| Concept | Meaning |
|---|---|
| **Incident** | A platform-level problem affecting one or more tenants. Readable number (e.g. `INC-12`), title, description (internal), severity, status, owner, responders, timestamps. |
| **Affected tenant** | A tenant the incident affects. Customer updates go only to these. Added by hand; a tenant whose ticket is linked is added automatically. |
| **Link** | A connection from the incident to a support ticket, an alert (phase 2) or an internal-tenant Work Management task. |
| **Customer update** | A message sent to the affected tenants for a status change: pre-filled from a template, edited and sent by a person. Becomes a targeted announcement and an email. |
| **Timeline event** | An append-only record of something that happened (declared, severity changed, status changed, update sent, tenant added/removed, bug created, bug done/reopened, postmortem completed, note added). |
| **Postmortem** | Cause, impact, what went well or badly, and prevention actions; each action becomes an internal-tenant task. |

### 5.2 Severity (DEC-020)

| Severity | Example | Team notified on declare / severity raise | First customer update |
|---|---|---|---|
| **Critical** | Login down for everyone; data not loading | Every platform user holding `platform.incidents.read` (email) | Immediately after declare (the composer opens straight away) |
| **Major** | A core feature down for some tenants (payroll, attendance, tray) | Owner and responders | Within 1 hour (shown as guidance, not enforced) |
| **Minor** | Small problem with a workaround | Owner only | Only if the team chooses |

No SLA timers. The incident stores `detected_at`, `acknowledged_at` (set only by an explicit **Acknowledge** action; opening or reading an incident never changes data), `first_update_sent_at`, `resolved_at`, `closed_at`, so SLA reporting can be computed later.

### 5.3 Lifecycle

```
Investigating → Identified → Monitoring → Resolved → Closed
```
- **Investigating:** declared; cause unknown.
- **Identified:** cause known. If no engineering bug is linked, the UI asks "Does this need an engineering task?" (DEC-019).
- **Monitoring:** fix applied; watching.
- **Resolved:** the team confirms the problem is fixed. Telling customers is a separate Send (below).
- **Closed:** postmortem completed (required for Critical, optional for others; DEC-021). Minor and Major can be closed straight from Resolved.
- Moving backwards (e.g. Monitoring → Investigating) is allowed and recorded in the timeline. A Closed incident can be reopened only to Investigating.

Each status change opens the customer-update composer with the template for the new status (5.5). **Changing status and sending a customer update are two separate commands, each audited.** Changing status never sends anything. Until an update has been sent for the current status, the incident shows **"Customer update pending"** (in the list and the header); the Critical badge stays red while it is pending.

### 5.4 How incidents start

1. **From scratch:** "Declare incident" on the Incidents list.
2. **From a support ticket:** "Declare incident" on a ticket (or "Link to incident" to attach it to an open one). The ticket is linked and its tenant becomes an affected tenant. Urgent tickets keep using the existing `Support:AlertEmail` notification so the team hears about them.
3. **From an alert (phase 2, after the observability foundation, teammate task-17):** Azure Monitor action groups post to a webhook. Alerts are stored and de-duplicated; a Critical alert shows a "Declare incident from this alert?" suggestion in Platform Admin. Nothing is declared automatically. During UAT, alerts only go to one email address (DEC-014).

### 5.5 Customer updates (DEC-017, DEC-018)

- **Templates** per status (Investigating, Identified, Monitoring, Resolved), with placeholders for the incident title and a short impact line. Stored as notification templates so wording can be edited in Platform Admin.
- **Composer:** on a status change, or "Post update" at any time, the composer shows the pre-filled title and body, the target tenants (the affected list, with a count), and the channels (banner and email, both on by default). The person edits and presses **Send**. Nothing sends without that press.
- **Banner:** sending creates or updates one announcement per incident with a new audience **`selected_tenants`** and a target list equal to the affected tenants at send time. Severity maps Critical → `critical`, Major → `warning`, Minor → `info`. The tenant endpoint `api/v1/announcements/active` returns a `selected_tenants` announcement only to its target tenants. On Resolved, the banner shows the resolved message for a set time (builder's call, e.g. 24 hours) and then ends.
- **Email:** via the outbox to each affected tenant's **incident contact** (a new optional email field in the tenant's settings); if empty, to the tenant's admin users. (Dabi's call, recorded in the conversation, default accepted.)
- Every sent update is a timeline event; the first one sets `first_update_sent_at`.
- Adding an affected tenant later does not resend old updates; the next update includes it, and the composer reminds the sender that newly added tenants have not been told yet.
- **Removing an affected tenant** removes it from the incident's active announcement target list in the same command, so its banner disappears at once. Emails already sent cannot be recalled; the timeline records the removal.
- The announcement's target list is always kept equal to the incident's current affected tenants (add does not show the old message to new tenants until the next Send; remove takes effect immediately).

### 5.6 Escalation to engineering (DEC-019)

- **Internal tenant configuration** (Platform Admin setting): internal tenant, project, objective, bug category. Escalation is disabled until it is set. *(Designer's call, for safety:)* a tenant counts as internal only if it carries a fixed `is_internal` flag, set by migration or seed and never from the UI; the setting can only point at a flagged tenant; changing the setting needs its own permission `platform.incidents.configure`, seeded for Platform Super Admin only, and is audited. The guarded writer checks both the flag and the setting. Setup of the tenant, project and team employees is part of DEC-015 and happens before this feature is used.
- **"Escalate to engineering"** on the incident creates one or more bugs: title, description (written by the person; prefilled with the incident number, severity, status and the count of affected tenants, never their names), optional assignee chosen from internal-tenant employees.
- **One guarded writer.** A single application service performs every write into the internal tenant. It refuses any tenant that is not both flagged internal and configured, switches context with `ITenantContextSwitcher`, creates the task, and returns to admin mode. `ITenantContextSwitcher` has no switch-back today; adding a scoped switch that always restores admin mode (including on failure) is part of this work. Every write is audited.
- **Direct creation, approvals bypassed on purpose (Dabi, 2026-11-16).** Work Management normally lets only an effective manager of the module create a task directly; others get a pending `task.create` approval request, and they may only assign themselves. Escalation is urgent and comes from an MFA-verified, permission-checked, audited platform admin, so the guarded writer creates the task directly through the task write service, skipping the approval engine and the module-manager check, and sets the chosen assignee in the same operation. Only this writer may do this. Postmortem action tasks use the same path.
- **Creator (Dabi, 2026-11-16):** the internal-tenant employee whose email matches the escalating platform admin. If there is none, escalation stops with a clear message ("You have no employee record in the internal tenant with this email. Ask to be added first."). No service user is built. The creator does not need to own the configured objective.
- **Incident work links (platform side).** One row per task the incident asks for: incident, kind (`engineering_bug` or `postmortem_action`), idempotency key, status (`pending`, `created`, `failed`), the created task id and short id, error text on failure, timestamps. Only `engineering_bug` links count for "Fix ready".
- **Idempotent, retry-safe creation.**
  1. The composer generates an idempotency key when the escalation form (or a postmortem action) is opened.
  2. The command first writes the link row as `pending` with that key (unique per incident), on the platform side.
  3. The guarded writer creates the task in the internal tenant, storing the same key on the task (unique per tenant). If a task with that key already exists, it returns that task instead of creating another.
  4. The link row is marked `created` with the task id; on error it is marked `failed` with the reason, and the UI shows **Retry**, which reuses the same key.
  5. A cleanup check finds `pending` links older than a few minutes and resolves them by looking up the task by key (created → mark `created`; none → mark `failed`).
  Creation stays synchronous so the person sees the bug at once; the outbox is used only for the sync back (5.7).
- **Work Management changes:** nullable `SourceIncidentId`, `SourceIncidentLinkKind` and `ExternalRequestKey` (unique per tenant when set) on the task, following the `SourceMeetingOccurrenceId` pattern; a task chip "From incident INC-12 · Critical" (or "Postmortem action · INC-12") linking to the incident in Platform Admin; a board filter for incident-linked tasks.

### 5.7 Sync back

- Every path that changes a task's status (move, drag, approval completion, bulk) emits an outbox message when the task has a `SourceIncidentId`, carrying the link kind. The handler is idempotent (outbox retries may deliver twice).
- The incident handler records a timeline event. When **all** `engineering_bug` links are `created` and their tasks are in a status whose category is done (`TaskStatus.Category = done`), the incident shows **"Fix ready"** and prompts "Move to Monitoring and post an update?". If a bug leaves done, "Fix ready" clears.
- `postmortem_action` links (5.8) never affect "Fix ready". A `pending` or `failed` engineering link keeps "Fix ready" off.

### 5.8 Postmortem (DEC-021)

- Available from Resolved. Fields: summary, root cause, customer impact, what went well, what went badly, prevention actions (each with title and optional assignee).
- Completing it creates each action as an internal-tenant task through the guarded writer, as a `postmortem_action` link with its own idempotency key, records a timeline event and allows Closed.
- Required before Closed for Critical; optional for Major and Minor.

### 5.9 Permissions and audit

| Permission | Allows |
|---|---|
| `platform.incidents.read` | See incidents, timeline, updates, postmortems. Receives Critical incident emails. |
| `platform.incidents.manage` | Declare, edit, change severity/status, manage affected tenants and links, send customer updates, escalate, write postmortems. |
| `platform.incidents.configure` | Change the internal tenant configuration. Super Admin only. |

All write commands are audited (F-07). Seed all three permissions for Platform Super Admin; `configure` goes to no other role.

### 5.10 Screens

**Platform Admin**
- **Incidents** (under Support): list with filters (status, severity, owner, open/closed), badge for open Critical.
- **Incident detail:** header (number, title, severity, status, owner, timestamps); tabs or sections for Overview, Timeline, Customer updates (history plus composer), Affected tenants, Links (tickets, bugs, alerts later), Postmortem.
- **Support ticket detail:** "Declare incident" / "Link to incident".
- **Settings:** internal tenant configuration for escalation; update templates.

**Tenant app**
- The existing announcement banner shows targeted incident announcements.
- Tenant settings: optional incident contact email.

**Internal tenant (Work Management)**
- Incident chip on tasks; incident-linked filter.

## 6. Phasing

| Phase | Contents | When |
|---|---|---|
| **1** | Incident module, severity, lifecycle, timeline, timestamps, team notifications, declare from scratch / from ticket, affected tenants, customer updates (templates, composer, targeted banner, email, incident contact), escalation and sync, postmortem, permissions and audit, screens | Release 1 |
| **2** | Alert webhook intake, de-duplication, "declare from alert" suggestion | After the observability foundation (task-17) exists |

## 7. Rough size

| Part | Developer-days |
|---|---|
| Backend incident module (entities, migration, commands, queries, timeline, notifications, permissions, audit) | 6–8 |
| Announcements targeting + tenant endpoint + incident contact + email templates | 2–3 |
| Escalation writer, Work Management changes, sync, postmortem actions | 5–7 |
| Platform Admin screens (list, detail, composer, postmortem, settings, ticket actions) | 6–8 |
| Tenant app (banner targeting check, incident contact setting, task chip, filter) | 2–3 |
| Tests (unit, architecture, integration incl. RLS of the cross-tenant writer) and UAT | 3–4 |
| **Phase 1 total** | **24–33** |
| Phase 2 alert intake | 4–6 |

## 8. Left to the builder

- Table and column names, number format and sequence for `INC-` ids.
- Exact template wording (a first draft is fine; Dabi can edit it in Platform Admin).
- How long the resolved banner stays (suggested 24 hours).
- Exactly how "tenant admin users" are found for the email fallback, using the existing role model.
- Page layout details, following the existing Platform Admin patterns.
