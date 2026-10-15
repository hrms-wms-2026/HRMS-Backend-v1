# Frontend parts — Inbox Standard + HR integration

Read `README.md` first. Every part ends with the gate (`ng build` + full vitest suite) and one commit.

**Rules for all parts:**
- `modules/inbox` must not import from other feature modules. Shared pieces move to `shared/ui` or `core`.
- Each part's report lists every non-inbox file it touched.

---

## Platform

### FS1 — Kind registry + capability-driven card

- `inbox-kinds.store`: loads `GET /inbox/kinds` once and exposes `descriptor(source, kind)`.
- The `inbox-item-card` action bar is built **only** from `AvailableActions` + descriptor capabilities. Remove every remaining `source === 'work'` branch.
- **Generic dialogs:** the comment dialog (exists), plus:
  - outcome picker (`resolve`: Excused / Confirmed / Convert to leave — the last one needs a leave type select, with options from the action's `Meta`);
  - escalate dialog (note + target preview from `Meta.escalateTo`, or "No one above – this will be logged");
  - forward dialog ("Send to {name}").
- **Generic edit-before-approve form** built from `ChangeSet` rows where `editable`. One field component per `kind`: `text`, `date`, `datetime`, `time`, `hours`, `select` (options), `employee`, `breaks` (list editor). Validation errors returned by the API (422 with messages) appear inline.
- **Revert banner:** when `revert` is available, show "Undo — available until 10:42" with a live countdown; it hides itself at expiry.
- **Detail renderer registry:** `InboxDetailRendererRegistry` maps a `detailRenderer` key to a standalone component that gets `detail.extra`. Default = the explanation + change-set view (exists). Leave/attendance/exception/calendar renderers are registered by FL/FA/FX/FC from `modules/inbox/renderers/*`; the renderers live in the inbox module.

Specs: actions per capability, each dialog, each form field kind, countdown expiry, renderer fallback.

### FS2 — List, tabs, bulk, history, counts, nav

- **Tabs:**
  - `All · Requests · Alerts · History`, with per-tab counts from `counts.byKind`/`bySource`;
  - a `Received · Sent · About me` segment (About me = `Direction=info` where the caller is the subject).
- **Filters:** Source (all 5), Kind, Status, Project, Legal entity, Department, Employee, Requested by, Date range, plus the provider-specific fields from `/inbox/filters` (leave type, correction type, exception type, signal key, severity). The filter state is saved in the URL query params, so it survives a reload and can be shared.
- **Bulk:**
  - row checkboxes appear when the selected kinds all have `Bulk`;
  - an `app-bulk-action-bar` (reuse the existing shared one) offers Approve / Reject / Resolve;
  - partial failures are listed in a result toast/dialog.
- **History tab:** `GET /inbox/history`, read-only rows with "Decided {action} · {time}" and a revert chip while it is inside the window.
- **Group by employee** toggle (alerts tab): collapsible groups with counts. This is how "21 absent" reads for a manager.
- **Toolbar menu:** "Delegate my approvals". It moves the Leave delegate modal here (D-f), shown with `leave:approve`; the component moves from `leave/` into `shared/ui` or `inbox/ui`.
- **Nav:** the `inbox` item loses its WM `requiredModules` gate (P8). The badge stays `pendingReceived + unreadMessages`. Update `nav-access.spec.ts`.

Specs: tabs/counts, URL-synced filters, bulk partial failure, history revert chip, group-by, nav gate.

### FS3 — Conversation v2 UI

- **Participants popover:**
  - each person shows a role badge (Requester / Approver / Subject / Viewer / Added);
  - a **Remove** button on added members, shown to base participants and to whoever added that member;
  - a **Leave conversation** item for added members, with a confirm.
- **Messages:**
  - author hover menu with Edit / Delete;
  - edit inline using the composer;
  - "(edited)" marker;
  - a deleted message becomes the placeholder "Message deleted";
  - realtime `messageUpdated`, `messageDeleted`, `participantRemoved` are handled (a removed self → the panel switches to "You were removed from this conversation" and stops sending).
- **Attachments:** composer attachments are enabled for chat. Files upload with purpose `conversation_attachment`, max 5, size/type errors show inline. Messages show attachment chips that download through the authenticated endpoint (blob → save; never a raw URL).
- **Tagged messages:**
  - `info_request` / `info_response` get a label;
  - `justification` (subject only) gets a highlighted card style;
  - the subject's composer has a "Mark as justification" toggle.
- **Export** button (deciders/HR) → `/export`.

Specs: every permission-driven button visibility, edit/delete flows, realtime handling, the attachment limits, justification toggle visibility.

---

## Sources

### FL — Leave in the Inbox

- Renderer `leave-request`:
  - balance impact (before → after hours, paid/unpaid split);
  - warnings (notice, max consecutive, team absence);
  - calendar conflicts;
  - documents;
  - approver trail with forwarded/delegated rows, inside the steps.
- Editable approval uses `datetime` fields. On a 422 the evaluator messages show inline above the form.
- Actions:
  - Request info → comment dialog;
  - Respond info (requester) → comment dialog;
  - Forward;
  - Change decision (only when offered);
  - Cancel (requester) with the existing full/partial dialog, moved to shared.
- **Redirect:** `/time-off/team-approvals` → `/inbox?source=leave&tab=requests`, and `?requestId=X` → `/inbox/leave/request/X`.
  - Remove `leave/feature/pending-approvals/**` and its nav child.
  - The approval-specific parts of the leave approval store/API that only it used are removed. Check first: the My Team widget still deep-links, so update `my-team-deep-link.service.ts` to the inbox refs.
- The employee screens (`my-balances`, the wizard, `requests-list` for HR) stay. Their "view" links to requests the caller must act on point to the inbox.

### FA — Attendance requests + alerts in the Inbox

- **Renderers:**
  - `attendance-correction` (before/after timeline bar for the day);
  - `work-mode-change`;
  - `location-change` (static map link + distance);
  - `device-change`;
  - `attendance-alert` (the day's timeline: scheduled vs actual, break, leave/holiday markers, the source notification for location/idle).
- Editable: the `breaks` and `time` fields, plus a work-mode `select`.
- **Resolve outcome dialog** for alerts. Convert-to-leave shows the leave type select + a balance hint.
- **Redirects:**
  - `/attendance/time-tracking?view=team&type=…` and the two legacy redirect routes → `/inbox?source=attendance&kind=…`;
  - `?requestId=` → the item.
  - Remove `correction-approvals`, `work-area-approvals`, `location-change-approvals`, `device-change-approvals` and the team-view host branch in `TimeTrackingComponent`. **Keep** the employee's own requests view.
- **People module:**
  - the employee list row shows the "N open alerts" chip (D-a) linking to `/inbox?tab=alerts&employee={id}`;
  - the "Needs attention" signal items link each resolvable item to its inbox ref.

### FX — Exception cases in the Inbox

- Move `attendance/feature/exception-alerts/exception-evidence-panel` → `shared/ui/exception-evidence-panel`. The renderer `exception-evidence` wraps it.
- The subject's own case view hides monitoring-only evidence sections (P10). The backend already strips them; the FE just must not crash on missing sections.
- **Redirect:** `/attendance/alerts` (incl. `?alertId=`) → `/inbox/monitoring/exception_case/{id}` or the filtered list. Remove the Alerts page + its nav child + its store/api if unused after the move. Update the type labels in one shared place.

### FC — Calendar

- Renderer `meeting-exception`: meeting title/time, RSVP history, joined/left bars per occurrence, confidence badge ("estimated"), incidents table.
- `modules/calendar` event detail (organizer view): a new **Attendance** tab showing matched attendance rows + the incidents raised, read-only.
- The integration health notifications (P1-6) navigate to Settings → Calendar Sync.

### FW — Work Management alignment

- WM items use the generic dialogs and the edit form.
- Monitor alerts get **Escalate**.
- Tree marks keep their "Open in Inbox" link.
- Remove any leftover WM-specific code in the inbox now covered by the standard.

### FZ — Frontend coverage audit + gate + browser pass

- **Coverage:** per component/store checklist across FS1–FW; write the missing specs. Then `ng build` + the full suite.
- **Browser pass**, with multiple users in separate browser sessions; screenshots as proof:
  1. An employee submits leave → the approver sees it live. Edit the dates → approve → balance correct → revert within 30 min → back to pending with the balance restored.
  2. Request info → the message appears in the chat → the employee replies in the chat → it returns to pending.
  3. An attendance correction is edited → approved → the record is changed → revert restores it.
  4. A device change is approved → revert is blocked after new-device activity (or simulated).
  5. An employee alert shows "21 absent" → resolve one as Excused → the employee list, the overview signal and the inbox all show 20. "Confirmed" stays at 20. Convert-to-leave creates an approved leave.
  6. An exception case: acknowledge → escalate → the HR target joins as a viewer and sees it.
  7. A calendar no-show case is raised at the threshold → the manager escalates → the top of the chain is log-only.
  8. Chat: add a member, remove them; an added member leaves; edit and delete a message (the export still shows the original); an attachment is uploaded and downloaded; a removed member cannot download.
  9. Bulk approve 3 leave + 2 corrections, with 1 failing → partial result shown.
  10. Old URLs (`/time-off/team-approvals?requestId=`, `/attendance/alerts?alertId=`, the team approvals view) land on the right inbox item.
  11. An employee with no approve permission sees Inbox with Sent and About me only.
