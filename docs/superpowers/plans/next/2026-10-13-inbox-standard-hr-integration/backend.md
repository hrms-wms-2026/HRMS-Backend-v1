# Backend parts — Inbox Standard + HR integration

Read `README.md` first. Every part ends with:
- the gate: build + full unit + architecture suites;
- one commit (`feat(inbox): …`, `fix(calendar): …`, …) that lists the non-Inbox files touched and any deleted tests.

Every test list below is a **minimum**.

---

## Platform

### S1 — Kind registry, decision journal, revert standard

1. `Features/Inbox/Contracts/InboxKindDescriptor.cs`: `InboxKindDescriptor` and `InboxCapabilities` (README §2). Add `IInboxKindRegistry`, filled from DI (`services.AddInboxKind(...)` extension). Add `GET api/v1/inbox/kinds` → descriptors, which the FE caches.
2. Domain `Features/Inbox/Entities/InboxDecisionJournalEntry`: EF config, migration `AddInboxDecisionJournal`, and the RLS coverage migration.
3. `IInboxDecisionJournal`:
   - `RecordAsync(entry)`;
   - `LatestAsync(ref)`;
   - `ListForItemAsync(ref)`;
   - `ListByActorAsync(actor, from, to, page)`;
   - `MarkRevertedAsync(original, revertEntry)`.
4. `InboxRevertWindow` (`Minutes = 30`, `Compute(entry, viewer, now)`). Make WM `ApprovalRevertWindow.Minutes` read it. WM behaviour is unchanged; its existing tests stay green.
5. Revert machinery:
   - `IInboxDecisionReverter` with `(Source, Kind)`, `CanRevertAsync`, `RevertAsync`;
   - `InboxDecisionReverterBase`, which enforces README §3.4 rule 1 and calls an abstract `CheckDependenciesAsync` + `ApplyRevertAsync`;
   - the router action `revert` is generic: when the descriptor has `Revert`, the router resolves the reverter instead of the provider. `AvailableActions` gets `revert` (with `revertableUntil` in a new `InboxAction.Meta` dictionary) from the journal + window, added by the router, not by each provider.
6. `IInboxStepsBuilder`: merges the provider's module steps + journal entries + conversation system events (participant added/removed) into one ordered `Steps` list. Reverted journal rows show struck-through (`State = "reverted"`; add this state).
7. History tab API: `GET api/v1/inbox/history?from&to&source&kind&page` → items the caller decided (journal by actor), each mapped through its provider's `GetDetailAsync(...).Item`. Items the caller can no longer see show a minimal row from the journal: title from `after_json.title`.
8. Bulk API: `POST api/v1/inbox/bulk` `{ action, items: ItemRef[], comment?, outcome? }`.
   - Allowed only when every item's descriptor has `Bulk`.
   - Each item runs as its own action (its own transaction), and failures are collected: the response is `{ succeeded[], failed[{ref, message}] }`.
   - Leave items with the same action are grouped into one call to the **existing** `BulkApproveLeaveRequestsCommand` / `BulkRejectLeaveRequestsCommand`.

**Tests:**
- registry duplicates → startup error;
- journal ordering;
- revert window: not the actor / 31 min / not the latest / already reverted;
- steps merge order;
- history paging;
- bulk partial failure;
- bulk rejected for a non-bulk kind.

### S2 — Escalation standard

1. `Features/Inbox/Escalation/IInboxEscalationPolicy` + registry, `EscalationContext` (`ref`, current holder ids, subject employee, legal entity, project), and `EscalationTarget` (`Employees(ids)` | `LogOnly(reason)`).
2. Policies:
   - `ReportingLineThenHrPolicy` (logic **moved** from `ExceptionAlertRouter.NotifyEscalatedAsync`/HR fallback);
   - `ReportingLineThenLogPolicy`;
   - the `WmHierarchyPolicy` stub (implemented in W1).
3. `InboxEscalationSweepJob` (BackgroundService, hourly, per active tenant, with the same tenant-loop pattern as `ExceptionDetectionJob`). It moves the 3-day stale sweep out of `ExceptionDetectionJob`. **Characterisation test first:** for the existing exception types, the old sweep and the new sweep produce the same status, `EscalatedById = null` and the same HR notifications.
4. The generic `escalate` action (with a note) on any kind with `Escalate`. Each escalation:
   - writes a journal row;
   - adds the target(s) as conversation `viewer`s;
   - posts a system message "Escalated to X" (or "Escalated – no one above, logged");
   - sends `InboxChanged` to the old and new holders.

**Tests:** each policy chain; root/no-manager → LogOnly with no notification; the sweep only touches overdue open items; the characterisation test.

### S3 — Conversations v2

Schema (migration `ConversationsV2` + RLS coverage):
- `conversation_participants`: add `RemovedAt?`, `RemovedByEmployeeId?`, `LeftAt?`. Add `JoinReason` value `viewer`.
- `conversation_messages`: add `EditedAt?`, `DeletedAt?`, `DeletedByEmployeeId?`, `Tag?` (`info_request|info_response|justification|null`).
- New `conversation_message_revisions` (`message_id, body_html, revised_at, revised_by, kind: edit|delete`). This is the audit and is never exposed to the UI except via the investigation export (S5).
- New `conversation_message_attachments` (`message_id, file_record_id, file_name, content_type, size_bytes`).

Resolver contract: add `Task<bool> CanViewAsync(tenantId, ItemRef, callerEmployeeId, ct)` to `IConversationSubjectResolver` (implemented by the WM resolver now: true when the caller can see the item in the WM provider). `ConversationAccess`: base ∪ active added ∪ `CanViewAsync` (→ upsert as `viewer` on open).

Commands:
- `RemoveConversationParticipant(conversationId, employeeId)`:
  - allowed for base participants, or for the `AddedBy` of that row;
  - base and viewer rows can't be removed (422);
  - system message, realtime `participantRemoved`, `InboxChanged` to the removed person.
- `LeaveConversation(conversationId)`: only `added` rows; base → 422 "You're part of this request".
- `EditConversationMessage(id, bodyHtml, mentions)`: author only, text messages only, not deleted. It writes a revision, sets `EditedAt`, notifies **newly** mentioned people, and sends realtime `messageUpdated`.
- `DeleteConversationMessage(id)`: author only. It writes a revision (kind `delete`, with the full body), sets `DeletedAt`, and blanks `BodyHtml` + attachments in API responses (shown as "message deleted"). Realtime `messageDeleted`.
- `PostConversationMessage`: accepts `attachmentFileIds[]` (max 5, each ≤ 25 MB, the existing storage allow-list) and `tag?`. Only the subject employee may use `tag=justification`.
- Upload: reuse the existing storage upload + `FileRecord` with a new purpose `conversation_attachment`. Download: a new `GET api/v1/conversations/{id}/attachments/{attachmentId}` that checks `ConversationAccess` and then streams through the existing storage service. **Never** return a public URL.
- A removed or left person loses access immediately. Their old messages stay.

The hub sends the new events `messageUpdated`, `messageDeleted`, `participantRemoved`. `JoinConversation` re-checks access, so a removed person's group membership is dropped on their next join attempt. The server also removes the connection's group membership on removal (it tracks connectionIds per employee in the existing user group).

**Tests:**
- every permission branch (remove by base / by adder / by stranger / remove a base row);
- leave as base vs added;
- edit/delete by a non-author;
- revision rows written;
- a deleted body is hidden;
- an attachment download is denied to a removed member;
- viewer upsert via `CanViewAsync`;
- the justification tag is subject-only.

### S4 — Inbox gate, counts per kind, notifications, redirects contract

1. Counts: `GET /inbox/counts` adds `bySource` and `byKind` maps (for the FE tab badges). `PendingReceived` remains the rail badge.
2. Bell: `RelatedEntityType = "inbox:{source}:{kind}"` for **new** notifications from all providers. Add a `NotificationNavigation` contract test listing every template code → inbox ref. The existing WM mappings stay.
3. The inbox works for any authenticated tenant employee: remove any WM-module gate on `InboxController` / `ConversationsController` if present. Providers decide visibility.
4. `InboxChanged` everywhere: add `IInboxChangeNotifier.Enqueue(tenantId, employeeIds, ref)` (outbox, existing `InboxChangedOutboxHandler`). It is called by the providers after actions, **and** by module event paths that create items outside the inbox (leave submitted, correction requested, exception detected, alert materialised, calendar case raised). Those calls go next to the existing notification enqueue in each module, before `SaveChanges` (respect the enqueue-before-save rule; see calendar doc §2.1).

**Tests:** the counts maps; the notification type mapping; one enqueue-before-save test per module path, using the real `OutboxWriter` + DbContext.

### S5 — Investigation export

`GET api/v1/inbox/{source}/{kind}/{id}/export`. Allowed for deciders and HR scope. It returns a PDF/HTML bundle of:
- the item detail;
- the steps;
- the journal;
- the full chat **including revisions and deleted messages**, marked;
- the attachment list.

This is the reason P7 keeps the audit. Reuse the existing PDF/HTML generation service if one exists (grep `IPdf`/`Html` render); otherwise return HTML.

**Tests:** access, revisions included, no access for an added member.

---

## Sources

### L1 — Leave provider (`Features/Leave/Inbox`)

- `LeaveInboxSourceProvider` (`Source="leave"`, kind `request`).
  - **Received:** requests where the caller has an **actionable** approver row. Reuse the logic behind `GET /requests/pending-approvals` (incl. delegation and `in_order` gating), extracted into a query service shared with `LeaveApprovalTeamActionSource`.
  - **Sent:** the caller's own requests (`/requests/my`), plus on-behalf requests the caller submitted.
  - **Decided by me:** from the journal + the existing approval-history query (no 100 cap in the inbox; page it instead).
  - **Context:** `legal_entity`. **Facets:** `leave_type`, `employee`, `department`.
  - **Subtitle:** `"{type} · {start}–{end} · {hours} h"`.
- `GetDetailAsync`:
  - change set: type, from/to (`datetime`, **editable** for the actionable approver), total/paid/unpaid hours, reason, balance before → after, warnings, conflict snapshot, documents (download links through the existing document endpoint);
  - steps: submitted → each approver row (approved/rejected/forwarded/info/skipped, with delegate shown) → final → cancelled/partial cancel.
- Actions → `LeaveApprovalDecisionService`:
  - `approve` (optional `editedPayloadJson` `{startAt,endAt}`);
  - `reject` (reason required);
  - `request_info` (question) → also posts the conversation message `info_request` (D-d);
  - `respond_info` (requester) → conversation `info_response`;
  - `forward` (note; label "Send to {manager}" from the detail's `ForwardTo`);
  - `change_decision` (the existing rule: only the final decider, only before start). This stays a separate action from `revert`: revert = 30-min undo; change decision = the module's own rule;
  - `cancel` (requester: full/partial via `CancelLeaveRequestCommand`; HR with `leave:manage`).
- **Edited approve:** a new `ApproveEditedLeaveRequestCommand` in Leave. It is the only new Leave command, and it runs in one transaction:
  1. re-runs `LeaveRequestSubmissionEvaluator` for the new range with `excludeRequestId`, so the request doesn't overlap itself;
  2. on errors, returns 422 with the evaluator messages;
  3. updates StartAt/EndAt/hours/allocations/pending hours;
  4. records the change in a balance audit `adjustment`;
  5. approves through the existing decision service;
  6. writes the journal with before/after.
  The employee gets the `leave_request_approved` notification. Its body says "approved with changes".
- `LeaveDecisionReverter` (README §3.4 rows) + `LeaveConversationSubjectResolver`:
  - base = requester + on-behalf submitter + every approver row's employee (and the delegate-from approver);
  - `CanView` = HR with `leave:read`.
- Bulk: the S1 bulk path groups leave items into the existing bulk commands.
- Delegates: no change to the API. The FE moves the UI (D-f).

**Tests:**
- received respects `in_order` and delegation;
- sent includes on-behalf;
- edited approve with an evaluator error → 422 and no change;
- edited approve → hours/allocations/audit;
- revert approve → balance restored;
- revert blocked after start;
- revert reject with no balance → 409;
- request_info posts a conversation message;
- forward → the new approver sees it, the old one doesn't;
- a member added to the chat cannot approve.

### A1 — Attendance request kinds (`Features/TimeAttendance/Inbox`)

- `AttendanceInboxSourceProvider` (`Source="attendance"`) with kinds `correction`, `work_mode_change`, `location_change`, `device_change`.
  - **Received:** `ResolveApprovalInboxScopeAsync(attendance:approve)` per LE → pending requests for those employees. Reuse the queries behind each `GET approvals` endpoint, extracted and shared with the 4 `Attendance*TeamActionSource`s.
  - **Sent:** `GET my` equivalents. Device change has no `my` endpoint: add a repository query, not an endpoint.
  - **Facets:** `employee`, `department`, `kind`, `correction_type`.
- Detail change sets:
  - **correction:** original vs requested clock-in/out/break. Editable `time`/`datetime` + break list (`kind="breaks"`, JSON).
  - **work-mode:** current → requested mode, editable `select` with options from the active work modes.
  - **location:** a map link (lat/lng/accuracy) + distance from the current work location.
  - **device:** current → new device name/OS/fingerprint (shortened).
- Actions map to the existing `Approve*/Reject*/Cancel*` commands. Location also has `TrayRespond…`: the inbox only uses the web commands.
- **Edited approve:**
  - Correction: `ApproveAttendanceCorrectionCommand` gets an optional `EditedValues` (requested clock-in/out/break). The workflow validates them with the **same** preview rules (`PreviewAttendanceCorrectionCommand` logic), stores the edited values on the correction (the requested values are kept, in new `Approved*` columns, or in `ReviewComment` JSON if columns are refused — prefer columns, migration `AddCorrectionApprovedValues`), then applies them.
  - Work mode: `ApproveWorkAreaChangeRequestCommand` gets an optional `ApprovedWorkModeId`, validated as active and allowed for the employee.
- **Before-snapshots for revert:**
  - `AttendanceCorrectionWorkflow.ApplyCorrectionAsync` returns the record's before-state (times, breaks, worked/late minutes, or "no record" for `full_day` that created one). The provider stores it in the journal.
  - Work mode stores the previous mode.
  - Location stores the previous work location.
  - Device stores the old/new registration ids + statuses.
- Reverters per README §3.4.
- Subject resolver: base = requester + resolved approver; `CanView` = HR scope (`attendance:read`).
- Legacy deep links: map `?requestId` + `type` to the inbox ref (used by the FE redirects).

**Tests:**
- scope equals action authorisation (a non-approver neither sees nor acts);
- device scoping via the employee's LE (D-c);
- edited correction validated with the preview rules;
- edited work mode must be active;
- revert correction restores the record (incl. deleting a record created by `full_day`);
- revert blocked by a newer correction;
- device revert blocked after new-device activity;
- bulk approve across kinds.

### A2 — (merged into A1 if small) Tray/location `applied` state

The location change `applied` state shows as a step. Revert after `applied` restores the previous location only when there was no newer change. Test it.

### X1 — Monitoring exception cases (`Features/Monitoring/Exceptions/Inbox`)

- **Count bug fix first:** `EfExceptionRepository.CountDetectedInRangeAsync` counts only `Open` + `Acknowledged`. Add a regression test. This fixes the `monitoring_exceptions` signal.
- `ExceptionInboxSourceProvider` (`Source="monitoring"`, kind `exception_case`):
  - list = cases in `IExceptionScopeResolver` scope (manager, approvers, HR, `exceptions:*`). Items routed to the caller are `received`; scope-only items are `info`. The subject employee sees their own open cases as `info`, with evidence limited to facts (P10);
  - facets `exception_type`, `employee`, `severity`; status `open|acknowledged|resolved|escalated`.
- Detail: evidence from `GetExceptionEvidenceQuery` (reuse), mapped to `ExplanationDetails` + a `detailRenderer="exception-evidence"` payload (`EvidenceJson` in a new optional `InboxItemDetail.Extra` field).
- Actions:
  - `acknowledge`;
  - `resolve` with an outcome `excused|confirmed` + note. Store the outcome: add an `Outcome` column to `Exception` (migration). Existing resolve calls default it to `confirmed`;
  - `escalate` via the S2 policy for the type.
  - `ExceptionAlertRouter` now asks the policy registry: monitoring types → `ReportingLineThenHrPolicy` (unchanged behaviour).
- **D-b:** raise an `IdentityAnomaly` case when the clock-in completes without the required camera verification. Hook it where `camera_verification_skipped` is decided today. Use the same per-day dedup as face mismatch.
- Reverter: restores the previous status/timestamps/outcome from the journal.
- Subject resolver: base = subject employee + routed reviewer(s); `CanView` = scope resolver.

**Tests:** the count bug; scope = act; the subject sees limited evidence; outcome stored; the escalate policy per type; revert; the camera-skipped case dedup.

### E1 — Employee attendance alerts: materialise + resolve (`Features/TimeAttendance/Alerts`)

1. Schema (migration `AddAttendanceAlerts` + RLS coverage). Table `attendance_alerts`:
   - `id, tenant_id, employee_id, legal_entity_id, work_date (date), signal_key, source_id?, severity, status (open|resolved), outcome (excused|confirmed|converted_to_leave)?, note, resolved_by, resolved_at, converted_leave_request_id?, detected_at, details_json`;
   - unique `(tenant, employee, work_date, signal_key, source_id)`.

   Signal keys: `absent_days`, `late_clock_ins`, `early_clock_outs`, `short_hours_days`, `off_schedule_work`, `location_violations`, `idle_activity_alerts`, `missing_clock_outs`. Missing clock-out stays resolvable via correction too: an approved `clock_out` correction **auto-resolves** it with outcome `excused` + note "corrected".
2. `AttendanceAlertMaterializer`:
   - runs hourly for yesterday and earlier in the last 35 days, and on demand per employee after attendance writes / approved corrections / approved leave;
   - computes per-day outcomes with `AttendancePeriodCalculator` (absent/late/early/short/off-schedule) and the notifications table (location/idle, `source_id = notification id`);
   - upserts open rows;
   - a day that **no longer qualifies** (e.g. leave approved later) gets its open row **deleted** if never resolved. Resolved rows are kept.
3. **Counts drop together (21 → 20).** `AttendancePeriodCalculator` gets an `IAttendanceAlertResolutionLookup`. Days with a resolution outcome `excused` or `converted_to_leave` for that signal are excluded from that signal's count; `confirmed` stays counted.
   - One change updates the overview, KPI strip, signals, signal items, compare-month and My Team.
   - Notification-based counts (`GetEmployeeAttendanceDisciplineQueryHandler`, the signals monitoring count, `EfEmployeeRepository` 6-h lookbacks) subtract resolved `source_id`s.
   - **Characterisation tests before changing the calculator:** with no resolutions, all outputs are identical.
4. The employee list gets an `openAlertCount` on `EmployeeListItemResponse.attendanceSummary` (D-a), from one grouped query, not N+1.

**Tests:**
- materialiser idempotent;
- a no-longer-qualifying day is removed;
- resolved rows kept;
- calculator exclusion per outcome;
- the 21→20 test across overview + signals + list count + inbox count;
- correction auto-resolve.

### E2 — Alerts in the Inbox

- Added to `AttendanceInboxSourceProvider` (kind `alert`, `Category=alert`):
  - received = open alerts for employees in the caller's `attendance:approve` scope;
  - the subject sees their own as `info`;
  - facets `signal_key`, `employee`, `department`, `work_date`.
- Actions: `resolve` with `outcome` (`excused|confirmed|convert_to_leave`) + note.
  - **Convert to leave** (absence only) runs a new Leave command, `CreateApproverBackdatedLeaveCommand(employeeId, leaveTypeId, workDate)`:
    - approver-only;
    - it bypasses only `AllowBackdatedRequests`;
    - it runs every other evaluator rule (balance, policy, type gender, etc.);
    - it creates the request **already approved** by the caller (approver rows + balance deduction + audit);
    - it links `converted_leave_request_id`.

    The FE asks for the leave type (options = the employee's eligible types).
  - **Bulk resolve** groups by outcome.
- Reverter: deletes the resolution (row back to open). For `converted_to_leave` it cancels the created leave through `CancelLeaveRequestCommand` (HR path, reason "Reverted from Inbox"). Blocked if that leave changed since.
- Subject resolver: base = subject + approver(s) from scope; `CanView` = HR `attendance:read`.

**Tests:** scope; each outcome; convert-to-leave balance + approved state + bypass limited to backdating; revert of convert; bulk.

### C0 — Calendar Phase 0 (P0 bugs) — must land before C2

- **P0-1** Update / Edit-all / Cancel-occurrence enqueue **before** `SaveChangesAsync` (or save again). Add an integration test per command with the real `OutboxWriter` + DbContext asserting the outbox rows exist. Then grep all calendar commands for the save-then-enqueue pattern and fix them all.
- **P0-2** `TeamsAttendanceSyncJob` / `ZoomAttendanceSyncJob` don't mark synced on an empty result. They retry until `AttendanceReportCutoffHours=24` after the end, then set `attendance_unavailable` (new nullable column/flag on `calendar_event_meetings`).
- **P0-3** "This & following" split: first write a failing test proving participants are not copied. If it fails, copy the participants (with their RSVP reset to pending for the future series).

### C1 — Calendar Phase 0 (P1 + P2)

- **P1-1** RSVP `responded_at` + new `calendar_event_participant_rsvp_history` (participant, from, to, at, reason).
- **P1-2** Organizer notified on Accepted/Rejected (new templates `calendar_event_rsvp_accepted|rejected`).
- **P1-3** Email participants **and guests** on update / cancel / delete (reuse the invite email outbox type with a new `calendar_event_updated|cancelled` email template).
- **P1-4** The nominee is notified on replacement nomination; removed participants/guests are notified; participants are told when the meeting link is removed.
- **P1-5** Meeting-link creation fails → send the held-back invite without a link + an in-app alert to the organizer.
- **P1-6** Integration health → **employee** in-app notification (`ReauthRequired`, `Failed`, sync conflict). Not an Inbox card (calendar D2).
- **P2-1** `.ics` attachment (REQUEST/CANCEL with a stable UID + SEQUENCE) on invite/update/cancel emails.
- **P2-2** A reminder 15 min before start (job, at most once per occurrence per participant).
- **P2-3** Pass attendees to Zoom/Teams when creating the meeting.

Each item needs a test.

### C2 — Calendar meeting-exception engine

- Schema: `calendar_meeting_incidents` (calendar doc §6.1 columns + unique key) + RLS. New `ExceptionType` values (stored by name, no enum migration): `MeetingNoShow`, `MeetingLateJoin`, `MeetingEarlyLeave`, `MeetingRsvp`, `MeetingConflict`, `MeetingOverload`.
- `CalendarExceptionDefaults`: the constants from calendar doc §6.3.
- Rules (calendar doc §6.2, all 11), each a class implementing `ICalendarIncidentRule` with the exclusions from §6.1. Attendance identity matching: work email + connected-account email → employee_id; unmatched rows are never evidence. Confidence `provider|agent` (agent fallback through `MeetingApplicationComplianceCalculator`).
- `CalendarCaseService`:
  - threshold roll-up → create or attach to an open/acknowledged `Exception` of that type for that employee (dedup);
  - an immediate rule → create the case directly;
  - the subject employee is notified (`calendar_exception_raised_for_you`);
  - routed via `ReportingLineThenLogPolicy` (P6).
- Triggers (calendar doc §6.7):
  - the post-meeting evaluator after sync/cutoff;
  - the RSVP-change hook in `RespondToCalendarEventCommandHandler` (late decline; auto-resolve ONLEAVE/DOUBLEBOOK on decline);
  - the leave-approved + event-change hooks;
  - the nightly roll-ups.
- Signals: a new `meeting_exceptions` signal in `EmployeeSignalCatalogue` (category `meetings`). Calendar types are **excluded** from `monitoring_exceptions`. New signal item kind `meeting_incident`. New `CalendarMeetingExceptionTeamActionSource`, and calendar types are excluded from `MonitoringExceptionTeamActionSource`.
- Resolution: Dismiss = resolved/`excused` (incidents stop counting toward thresholds); Resolve = `confirmed` (they keep counting).

**Tests:**
- every rule incl. its exclusions;
- dedup attach;
- immediate rules;
- thresholds at the boundary (2 vs 3 in 30 days);
- unmatched attendance ignored;
- the empty-report retry interaction;
- escalation is log-only at the top;
- the signal split.

### C3 — Calendar in the Inbox

- `CalendarInboxSourceProvider` (`Source="calendar"`, kind `meeting_exception`). It wraps the `Exception` rows of the calendar types, reusing the X1 action code through a shared `ExceptionCaseActions` helper. Detail = meeting evidence: title, time, RSVP history, joined/left, confidence badge "estimated" for agent, incidents list. **Never** app names or samples.
- Subject resolver: base = subject + the reporting manager; escalation targets join as viewers.

**Tests:** scope; evidence privacy (no monitoring fields serialised); actions shared with X1.

### W1 — Work Management aligned to the standard

- Register WM descriptors.
- The WM provider writes the journal on decide/cancel/revert, and the WM revert delegates to the standard window; the WM revert logic is unchanged.
- `WmHierarchyPolicy` for `monitor_alert` (escalate to the next ancestor Module owner; root → log). Monitor alerts gain the manual `escalate` action + an SLA of 3 days.
- WM conversation resolver implements `CanViewAsync` (done in S3), and alert subjects are added (P10: the `SubjectEmployeeId` of deadline-overload alerts).

**Tests:** the existing WM suites stay green; journal rows; alert escalation chain.

### BZ — Backend coverage audit + gate

Per-handler checklist across S1–W1: every new command, query, provider method, reverter, policy, rule, job and hub method has at least one happy-path and one denial/edge test. Write the missing ones. Run the full unit + architecture suites.

Report:
- the counts;
- the migration list in apply order;
- every non-Inbox file touched, grouped by module, for the teammate.

---

## Appendix — Calendar rules (copied from the user's CALENDAR-INVESTIGATION-INBOX.md, 2026-10-13)

The source documents were pasted in chat and are not on disk. This appendix is the authoritative copy for C2.

### Incident table `calendar_meeting_incidents`

| Column | Notes |
|---|---|
| TenantId, EmployeeId | |
| RuleCode | e.g. `CAL-ATT-NOSHOW` |
| EventId, OccurrenceStart | which meeting, and which occurrence of a recurring series |
| DetectedAt | |
| Confidence | `provider` (Zoom/Teams report) or `agent` (fallback, labelled "estimated") |
| EvidenceJson | joined/left, attended ratio, RSVP status, organizer. NO app names / activity samples |
| ExceptionId (nullable) | the case this incident is attached to |

Unique key: `(TenantId, EmployeeId, RuleCode, EventId, OccurrenceStart)`.

### Exclusions (all rules)

- approved leave or a public holiday during the meeting;
- the event was cancelled, or the employee was removed before the start;
- RSVP Rejected, or the nominated replacement attended (attendance rules only);
- all-day events;
- events where the employee is the only participant;
- the employee is the organizer (organizer behaviour is never counted).

### Rules

| Code | Category | Incident | Case trigger | Severity | Source |
|---|---|---|---|---|---|
| `CAL-ATT-NOSHOW` | Attendance | RSVP Accepted/Pending, meeting ended, no join (provider) or < 50% meeting-app usage in the window (agent) | 3 / 30 days. **Immediate** if the organizer is the employee's reporting manager, or the meeting is a 1:1 | Medium (immediate: High) | Provider → agent |
| `CAL-ATT-LATE` | Attendance | First join > 10 min after start | 5 / 30 days | Low | Provider only |
| `CAL-ATT-EARLY` | Attendance | Left > 15 min early, or attended < 50% of duration | 5 / 30 days | Low | Provider (agent for the ratio) |
| `CAL-RSVP-NORESPONSE` | RSVP | Still Pending at the start | 5 / 30 days | Low | Participants + RSVP history |
| `CAL-RSVP-DECLINERATE` | RSVP | Per window | ≥ 50% declined, min 6 invites / 30 days | Low | Participants + history |
| `CAL-RSVP-LATEDECLINE` | RSVP | Accepted → Rejected < 2 h before the start | 3 / 30 days | Low | RSVP history (C1 P1-1) |
| `CAL-RSVP-UNRESOLVED` | RSVP | The employee's Resolution request / Replacement nomination is unanswered by the organizer at the start | **Immediate**, case on the requesting employee (FYI to the manager) | Medium | Participants |
| `CAL-LOAD-DOUBLEBOOK` | Load | ≥ 2 overlapping (≥ 15 min) Accepted/Pending meetings | 3 / 7 days | Low | Events + recurrence expander |
| `CAL-LOAD-ONLEAVE` | Load | A meeting (not Rejected) overlaps approved leave | **Immediate**; auto-resolves on decline/removal | Medium | Events + leave |
| `CAL-LOAD-OVERLOAD` | Load | > 5 h of Accepted meetings in one working day | 3 days / 7 days | Low | Events |
| `CAL-LOAD-OFFHOURS` | Load | An Accepted meeting falls outside working hours | 3 / 7 days | Low | Events + `WorkCalendarResolver` |

Only no-show gets the "manager organised" weighting.

### `CalendarExceptionDefaults`

`LateJoinMinutes=10`, `EarlyLeaveMinutes=15`, `MinAttendanceRatio=0.50`, `NoShowCount=3/30d`, `LateCount=5/30d`, `EarlyLeaveCount=5/30d`, `NoResponseCount=5/30d`, `DeclineRate=0.50 (min 6)/30d`, `LateDeclineHours=2`, `LateDeclineCount=3/30d`, `DoubleBookOverlapMinutes=15`, `DoubleBookCount=3/7d`, `DailyMeetingHours=5`, `OverloadDays=3/7d`, `OffHoursCount=3/7d`, `AttendanceReportCutoffHours=24`, `ManagerSlaDays=3`.

### Triggers

| Trigger | When | Rules |
|---|---|---|
| Post-meeting evaluator | After attendance sync succeeds, or at the 24 h cutoff with no report | ATT-*, RSVP-NORESPONSE, RSVP-UNRESOLVED |
| RSVP change | Inside `RespondToCalendarEventCommandHandler` | RSVP-LATEDECLINE; auto-resolve LOAD-ONLEAVE / DOUBLEBOOK |
| Leave approved; event created/changed | Leave approval + event handlers | LOAD-ONLEAVE, LOAD-DOUBLEBOOK |
| Nightly | `ExceptionDetectionJob` tenant loop (or a sibling job) | RSVP-DECLINERATE, LOAD-OVERLOAD, LOAD-OFFHOURS, threshold roll-ups |

The stale-case sweep runs in `InboxEscalationSweepJob` (S2), not in the nightly job.

### Retention

Unattached incidents are purged after 90 days. Cases follow the standard `exceptions` retention.
