# Meetings as Tasks — Phase 2 Design

- **Date:** 2026-10-18
- **Status:** Architecture and business decisions frozen (2026-10-18). One SQL detail is still open until its integration tests run (§5.6).
- **Base:** `feature/meeting-phase0-port`: development's calendar Phase 0 (C0, C1, C2 exception engine, C3 Inbox) plus our ported Phase 0/1 work (0C link reliability, attendance failure classification, the E6b/E6c fix, Meeting category, Office project).
- **Predecessor:** [2026-10-13-meeting-task-phase0-1-design.md](2026-10-13-meeting-task-phase0-1-design.md) §5. Where this spec differs from that one, this spec wins (§2.3).
- **Timing:** implementation starts after go-live. Nothing in this spec ships before then.

---

## 1. Goal

A meeting becomes a **work item** in the existing Work Tree (Project → Milestone/module → leaf item), next to tasks. It keeps every calendar capability the company already relies on: participants, guests, recurrence, Teams/Zoom links, invites, attendance, .ics, reminders and the meeting-exception engine. Its outcomes become normal tasks linked back to it.

Success means:
- The Work Tree, task behaviour, approvals, RLS and existing calendar events behave exactly as they do today.
- Only the organizer and participants can ever see a meeting.
- Meetings never distort task metrics.

## 2. Decisions

### 2.1 Business decisions (approved)

| # | Topic | Decision |
|---|---|---|
| D1 | Timing | Full Phase 2 after go-live. |
| D2 | Hierarchy | The existing Work Tree, unchanged. A meeting is a **leaf** work item under a module. There is no separate meeting hierarchy and no new parent–child rules. |
| D3 | Form | One shared meeting form everywhere: the Calendar and a "New meeting" button on the project board. The ordinary task form no longer offers the Meeting category. |
| D4 | Creation rights | The module owner or an owner of an ancestor module creates directly. Any other module member raises a **`meeting.create`** approval request through the existing engine; HR fallback is kept. A non-member gets 403. Nothing is created (no task, link or invite) until the request is approved. If the first occurrence has already started when the approver decides, the request becomes **Stale** and the organizer must reschedule. Project meetings need no `calendar:write`. Office meetings (created from the Calendar with no project) still need `calendar:write` and are created directly. |
| D5 | Approver view | The approver sees a snapshot frozen when the request is submitted: title, agenda, date/time/duration, a recurrence summary, organizer, module path, participant names, guest emails and the platform. The approver never sees the link, attendance or action items. After deciding, the approver keeps only the snapshot in approval history. |
| D6 | Visibility | **Organizer and participants only.** No bypass for admin `*`, tenant Owner, project lead, module owner, approver or HR. Meetings never appear in search, reports, dashboards or exports for anyone else. |
| D7 | Participants | Any active employee in the company, plus external guests by email. Inviting someone does **not** make them a module member. |
| D8 | Bulk invite | The organizer can pick a whole department, or a manager's team (that manager's direct reports). The group is expanded into a snapshot of individual people at that moment. The organizer can remove anyone, sees the final count, duplicates are removed and inactive employees are excluded. There is no new permission and no participant cap. There is no "everyone in the company" option. The preview reuses exactly the data the participant picker already shows. |
| D9 | Edit / cancel | The organizer edits or cancels directly; no approval is needed. Moving a meeting to another module follows D4 for the destination module. |
| D10 | Status | Derived from time, never set by hand: **Scheduled → In Progress → Completed**, or **Cancelled**. Meetings are not task-status columns on the board and cannot be dragged. |
| D11 | Metrics | Excluded from every task metric. There is no separate meeting-metrics feature. My Work shows "Today's meetings" in its own list. |
| D12 | Clock-in / tray | A meeting has no clock-in. The tray app is unchanged in Phase 2 (meetings come to the tray with the Phase 3 Day Planner). |
| D13 | Recurrence | **Each occurrence is its own work item.** One approval covers the whole series, and the series shares one link. The Work Tree shows a rolling **next 7 days** of upcoming occurrences; past occurrences stay. The Calendar still shows every future occurrence. |
| D14 | Action items | Action items are normal tasks. The creator places one in **any module they are a member of** (the default is the meeting's module), under that module's existing create rules, and it is linked to the occurrence. A viewer sees only the action items whose task they are allowed to see. |
| D15 | Existing meetings | No migration. Calendar events created before Phase 2 stay as calendar events (limitation documented in §6). |
| D16 | Attendance access | The organizer sees the full list. Each participant sees only their own row. Everyone above a participant in their **current** reporting line sees that participant's attendance summary, and only that, on the employee profile ("Meeting attendance"). Guests' attendance is visible to the organizer only. |
| D17 | Private meetings | In the manager attendance view and the monitoring/compliance view, a private meeting shows as **"Private meeting"**: the title, organizer, agenda, participant list and link are hidden. |
| D18 | Declined RSVP | Declining keeps access, and the participant can change the answer later. Only explicit removal revokes access. |
| D19 | Notes after the meeting | The organizer may edit a completed occurrence's `description` (minutes) afterwards. Time, attendees and placement cannot change. The edit is audited and sends no email. |
| D20 | Moving | Moves are **forward-only**. Moving a series moves only its occurrences that have not started yet; historical occurrences keep their original module. Moving a single occurrence leaves a permanent **suppression record** under the original series, so the generator never recreates it. |
| D21 | Attendance checkpoints | Checks at end + 30 min, + 2 h and + 24 h, all configurable. A provider failure is kept separate from "no data". Absence is never inferred. Late provider reports can be recovered. |

### 2.2 Architecture decisions (frozen)

- **Task-centric.** The canonical meeting is a `tasks` row with `task_kind = 'meeting'`, with a 1:1 `meeting_details` row. A new meeting has **no** `personal_calendar_events` row (the dual-row model was rejected).
- The Phase 0 calendar engine is reused through a **meeting-owner** abstraction: an owner is either a `CalendarEvent` (an existing event) or a `MeetingSeries` (Phase 2).
- **Fail-closed task queries.** An EF Core 10 named query filter, `ExcludeMeetings`, hides meetings from every existing task query.
- **Prerequisite P-1:** the OutboxProcessor concurrency fix (§9).

### 2.3 Changes from the Phase 0/1 spec §5

| Phase 0/1 §5 said | Now |
|---|---|
| Recurring series is one item (B10) | Each occurrence is its own work item (D13) |
| Meetings get their own meeting-time metrics (B7) | No meeting metrics (D11) |
| Bulk invite has a maximum recipient count (B8) | No cap (D8) |
| ID-reuse migration of existing meetings (B15) | No migration (D15) |
| Participant tables re-pointed to the task | Participant tables get an owner column; a series owns its participants (§4.2) |

---

## 3. What development already provides (and how Phase 2 joins it)

| Development piece | Phase 2 integration |
|---|---|
| **C2 meeting-exception engine.** `CalendarIncidentEngine.BuildInputAsync` builds each employee's occurrences from calendar events. Incidents are unique on `(tenant, employee, rule, event_id, occurrence_start)`. | A new `IEmployeeMeetingOccurrenceSource` merges legacy events with task-meeting occurrences. For a task meeting, `event_id` = the **occurrence task id** and `occurrence_start` = `original_start`, so the key survives splits and moves. Attendance comes from the occurrence's own rows (§5.8). Private titles are masked as development already does. |
| **C1 reminders** (`CalendarEventReminderJob`, `calendar_event_reminders_sent` unique on event, occurrence and employee) | The job also reads materialized task-meeting occurrences in its 15-minute window, keyed by the occurrence task id. Effective attendees who have not declined get the reminder. |
| **C1 RSVP history** (`calendar_event_participant_rsvp_history`) | Series participants' answers are recorded the same way (`event_id` = series id). Per-occurrence answers (`meeting_occurrence_attendees`) use `event_id` = occurrence task id. |
| **C1 .ics** (stable UID per event, SEQUENCE from time) | UID = series id; one occurrence adds RECURRENCE-ID = `original_start`. Every Phase 2 invite, update, cancel and link email carries an .ics. |
| **C1 organizer RSVP notices, removal notices, connection-problem notices** | Reused unchanged through the meeting owner. |
| **C3 Inbox** (calendar exception cases) | Unchanged. Cases built from task meetings flow through the same provider. |
| **Port: 0C link reliability** (claims, generations, `invites_pending`, keyed link emails, outbox meeting cancel) | Generalised to the series owner (§5.7). |
| **Port: attendance failure classification** (`attendance_unavailable_at`, `attendance_sync_failed_at`, E6b/E6c session matching) | Phase 2 runs the same classification per occurrence (§5.8). |

---

## 4. Data model

### 4.1 Tables

The composite foreign keys need these unique indexes first: `tasks (tenant_id, id)`, `objectives (tenant_id, project_id, id)` and `calendar_event_meetings (tenant_id, id)`.

**`tasks`** (no schema change)

A meeting occurrence is a task with `task_kind = 'meeting'`, the project's Meeting system category and a placeholder `status_id` (status is derived, D10). Its ShortId comes from a **separate per-project counter**: `{Identifier}-M{n}`.

**`meeting_series`** (new)

| Column | Notes |
|---|---|
| `id` PK, `tenant_id` | RLS |
| `project_id`, `objective_id` | FK (`tenant_id`, `project_id`, `objective_id`) → objectives. **The only placement authority.** |
| `organizer_employee_id` | FK → employees |
| `title`, `description`, `location`, `is_private` | Template for occurrences that are not materialized yet |
| `dtstart`, `duration_minutes`, `is_all_day`, `timezone`, `recurrence_rule` | A null rule means a one-off meeting |
| `cancelled_at` | |
| `creation_path` | `direct` / `approval` / `calendar_office` |
| `approval_request_id` | UNIQUE where not null |
| `split_from_series_id` | FK → meeting_series |
| `split_reason` | `edit_following` / `move_forward` / `move_single` |
| `split_at`, `revision`, concurrency token | |

**`meeting_details`** (new, 1:1 with a task)

| Column | Notes |
|---|---|
| `task_id` PK | FK (`tenant_id`, `task_id`) → tasks, ON DELETE RESTRICT |
| `series_id` | FK → meeting_series |
| `original_start` | **UNIQUE (`series_id`, `original_start`), NOT DEFERRABLE** (§5.6) |
| `start_at`, `end_at`, `is_all_day`, `location`, `is_private`, `is_detached`, `cancelled_at`, `revision` | |
| `calendar_event_meeting_id` | FK, nullable: the link this occurrence uses |
| `attendance_status`, `attendance_next_check_at`, `attendance_checkpoint`, `attendance_finalized_at`, `attendance_last_error` | |

**`meeting_occurrence_suppressions`** (new)

PK (`series_id`, `original_start`), `reason` (`moved_single`), `moved_to_task_id` FK → tasks, `created_by_employee_id`, `created_at`. **Rows are never deleted.**

**`meeting_occurrence_attendees`** (new)

Per-occurrence add/remove: `occurrence_task_id`, `attendee_kind`, `employee_id` / `email`, `change`, `response_status`, `response_reason`. A CHECK enforces the right identity column per `attendee_kind`. Partial unique indexes give one row per employee and per lower(email).

**`meeting_series_links`** (new)

`series_id`, `calendar_event_meeting_id`, `origin` (`created` / `inherited_on_split`), `attached_at`, `released_at`. UNIQUE (`series_id`) WHERE `released_at` IS NULL.

**`meeting_attendance_sessions`** (new)

UNIQUE (`calendar_event_meeting_id`, `provider_session_id`). `session_start`, `session_end`. `occurrence_task_id` is nullable and **not unique**: one occurrence may have several sessions.

**`job_leases`** (new, platform level, no RLS)

`job_name` PK, `owner_instance_id`, `lease_until`, `heartbeat_at`.

**Existing calendar tables (additive changes only):**

| Table | Change |
|---|---|
| `calendar_event_participants`, `calendar_event_guests` | + `meeting_series_id` FK. `event_id` becomes nullable. CHECK `num_nonnulls(event_id, meeting_series_id) = 1`. Series-scoped partial unique indexes. Existing indexes are unchanged (NULLs are distinct). |
| `calendar_event_meetings` | + `owner_kind` (`calendar_event` / `meeting_series`). `calendar_event_id` becomes nullable. Owner CHECK. The existing claim SQL is unchanged for the legacy owner. |
| `calendar_event_meeting_attendances` | + `occurrence_task_id`, `attendance_session_id`, `participant_key`. UNIQUE (`attendance_session_id`, `participant_key`, `joined_at`) where a session is set. |
| `external_calendar_event_links` | + `meeting_task_id`. CHECK exactly one owner. UNIQUE (`meeting_task_id`, `external_calendar_connection_id`). |
| `personal_calendar_events` | **No change.** |

**Deferred constraint triggers** (constraint triggers, not ON CONFLICT arbiters):
- A `meeting` task and its `meeting_details` row exist together, 1:1.
- Every meeting task's placement equals its series' placement.

**Plain trigger:** an occurrence's key never changes within its series (§5.6).

### 4.2 Source of truth

| Data | Source |
|---|---|
| Placement | `meeting_series` |
| Title and agenda of a materialized occurrence | `tasks.title` / `tasks.description` |
| Time, location, privacy and cancellation of a materialized occurrence | `meeting_details` |
| Occurrences not materialized yet, and defaults for new ones | `meeting_series` template |
| Series attendees | participants / guests rows with `meeting_series_id` |
| One occurrence's attendees | series attendees − `remove` + `add` |
| Link | `calendar_event_meetings` + `meeting_series_links`; the occurrence's link is `meeting_details.calendar_event_meeting_id` |
| Attendance | sessions + attendance rows tagged with the occurrence |
| Status | derived, never stored |

A series-wide edit updates the series and every future, not-started, non-detached occurrence in **one transaction**. Past occurrences never change, except their notes (D19).

---

## 5. Behaviour

### 5.1 Permissions and privacy

**Access tiers**

| Tier | Who | Can see |
|---|---|---|
| Organizer | `meeting_series.organizer_employee_id` | Everything |
| Series attendee | Series participant not removed for that occurrence | The meeting: agenda, attendees, link, visible action items, own attendance |
| Occurrence attendee | Per-occurrence `add` | That occurrence only |
| Approver | `ApprovalFeedParticipants.CanSee` | The request snapshot, through approval endpoints only |
| Reporting-line manager | `GetAncestorChainEmployeeIdsAsync` (current chain) | That employee's attendance summary only (D16, D17) |
| Everyone else | Including Owner, admin `*`, project lead, module owner, HR | Nothing: 404 |

**One enforcement point.** `IMeetingAccessResolver` decides the tier. `MeetingVisibility.ForCaller` is the single EF expression for every list (tree, board, My Work, calendar). "Does not exist" and "not allowed" return the same 404 body.

**Architecture tests:**
- Every `IgnoreQueryFilters("ExcludeMeetings")` is on an allow-list and is paired with `MeetingVisibility`.
- The unnamed `IgnoreQueryFilters()` is forbidden on `WorkTask`.
- Raw SQL that touches `tasks` must be on an allow-list.

**Endpoint matrix (new `/meetings`)**
- **Create:** project — module rules (D4); Office — `calendar:write`.
- **Bulk preview:** the same gate as create; returns picker fields only.
- **Get occurrence or series:** attendee.
- **Edit, cancel, move, attendees, link retry/remove/cleanup, attendance recheck:** organizer (move also needs the destination create rule).
- **Respond:** self.
- **Link:** attendees get the join URL; the organizer gets the full status.
- **Attendance:** organizer full list; attendee own row.
- **Action items:** meeting access + destination module create rule.
- **`GET /employees/{id}/meeting-attendance`:** self or reporting-line ancestor.

**Existing endpoints**
- `/calendar/*` is unchanged for legacy events.
- `GET /calendar` merges in the task meetings the caller may see.
- `check-conflicts` returns busy + time only for meetings the caller cannot see.
- Every `/tasks/*`, sprint and board command returns 404 for a meeting id.
- Approval detail sets `CanEditPayload = false` for meeting actions; revert returns 409.

**RLS.** Every new tenant table has `ENABLE` + `FORCE ROW LEVEL SECURITY` + a `tenant_isolation` policy, following the existing migrations' pattern and covered by `TenantIsolationArchitectureTests`. `job_leases` is platform-level and allow-listed. Composite tenant FKs block cross-tenant references. Participant-level privacy is enforced in the application layer (§5.1); RLS is tenant-level only.

**Leak coverage.** Each of these surfaces gets a test proving it shows no meeting to an outsider:
- task lists and metrics;
- activity feed and edit logs;
- search, exports and reports;
- ShortId numbering;
- calendar list and conflicts;
- approval feed and notifications;
- monitoring compliance (private titles masked);
- external push (organizer's own connection only);
- error bodies;
- logs (ids only).

### 5.2 Create and approve

**Validation** (all create paths):
- The first occurrence is in the future.
- End is after start.
- The timezone is valid.
- The rule expands within 500 occurrences.
- Inactive participants are skipped and reported.
- Guest emails are valid and de-duplicated.
- A chosen platform needs the organizer's connection; without one, the organizer is offered "create without a link" (P1-5).

**Direct create** (project owner path, or Office), all in one transaction with the series lock (§5.6):
1. Insert the series and its attendees.
2. Insert the first occurrences: one for a one-off meeting; the 7-day window for a recurring one (§5.6 SQL).
3. Queue keyed invites with their .ics.
4. After commit, provision the link (§5.7).

**Request create** (member path): `WorkApprovalEngine.SubmitAsync` with action `meeting.create` and target `meeting`. The payload is the full form plus the D5 snapshot. Nothing else is written.

**Approve:**
1. A conditional `pending → approved` update; 409 if it was already decided.
2. If the first occurrence has started, or the requester is no longer a module member, the request becomes **Stale** and the requester is notified with the form pre-filled.
3. Otherwise the direct-create transaction runs, with `approval_request_id` UNIQUE so a double apply creates nothing twice.

**Reject and cancel:** the existing flows. Payload edit and revert return 409.

**Bulk invite** (`Meetings:BulkInvite`, needs P-1):
1. The preview expands departments and managers' direct reports into active people, showing the counts: selected, duplicates removed, inactive excluded, already invited.
2. The form submits explicit employee ids.
3. Save re-validates who is still active.
4. Outbox batching respects `Email:MaxPerSecond`.
5. Above the configured provider capacity (`Meetings:ProviderCapacityWarning:*`) the organizer sees a warning only. There is no cap.

### 5.3 Edit, cancel, attendees

| Scope | Behaviour |
|---|---|
| This occurrence | Materialize if needed, edit `tasks` + `meeting_details`, set `is_detached`, keep the key. Email `update:{taskId}:{rev}:{row}` with RECURRENCE-ID. |
| All, non-key fields | Update the series and the future non-detached occurrences. Email keyed on series id + revision. |
| All, time or rule | An internal forward split at the first occurrence that has not started (§5.5). |
| This and following | A forward split at that occurrence (`edit_following`). |
| Started or past occurrence | 409, except `description` (D19). |

**Cancel**
- **One occurrence:** set `cancelled_at`; email `cancel:{seriesId}:{originalStart}:{row}`.
- **This and following:** `cancelled_at` from P onward, plus UNTIL on the series.
- **Series:** `meeting_series.cancelled_at`; every future occurrence is cancelled.
- The link reference is released (§5.7).
- Deleting from the calendar means cancelling.

**Attendees**
- **Series add:** clears any future `remove` override for that person.
- **Series remove:** clears future `add` overrides and revokes access at once.
- **Per-occurrence add/remove:** materializes the occurrence and writes an override row.
- **RSVP:** series attendees answer for the series (as today); per-occurrence adds answer for their occurrence. Declining keeps access (D18).
- RSVP history and organizer notices work as in development's C1.

### 5.4 Action items

The creator picks a module they are a member of (the default is the meeting's module). Creation goes through the existing `TaskWriteService` + `task.create` with one **optional** payload field, `sourceMeetingOccurrenceId` (null for every ordinary task).
- **Direct create:** the task and its `meeting_action_items` link are written in one transaction.
- **Via approval:** the applier links the task when it applies the request. Reject or stale means no link.
- A viewer sees only the linked tasks they can see under the task rules; there is no count leak.
- The link creator or the organizer can unlink.

### 5.5 Recurrence, splits and moves (forward-only)

- **Occurrence key:** (`series_id`, `original_start`).
- **Task id:**
  - At first materialization it is `DeterministicGuid(series | original_start)`, the same id the calendar shows for the virtual occurrence.
  - **After that it never changes.**
- **Split** (`edit_following`, `move_forward`, and the internal time/rule edit), at point P, in one transaction holding both series locks (smaller key first):
  1. The old series gets UNTIL = P − 1 s. Its past occurrences are untouched.
  2. A new series is created with `split_from_series_id` and `split_reason`.
     - It copies series attendees with their RSVP. Per development's split rule, RSVPs are reset when the time or length changes.
     - It gets an **inherited link reference**: no provider call, no email.
  3. Future materialized occurrences are remapped **in time order** to the new sequence, **keeping their task id**.
     - Surplus occurrences become Cancelled if they carry action items, attendees or attendance; otherwise they are soft-deleted.
     - Occurrences missing from the new sequence are inserted with the §5.6 upsert.
     - Suppressions after P are copied with the same time-order mapping.
- **Move a series** (`move_forward`):
  - The destination module's D4 rule applies.
  - P is recomputed **when the move is applied**.
  - The old placement keeps the historical occurrences.
  - Each moved task gets an `objective` row in `TaskEditLog`.
  - 409 if nothing upcoming remains.
- **Move one occurrence** (`move_single`, not started only):
  - A one-off series is created in the destination with the **same task id**.
  - A suppression record is written in the same transaction.
  - The generator, series edits and calendar expansion all respect suppressions.
- **Generator** (hourly, `job_leases`, `Meetings:Background`): per series, one transaction with the series lock; window [now, now + 7 days]; no emails.
- Rows beyond the window come only from a touch (edit, cancel or attendee override) or a one-off meeting.

### 5.6 Concurrency-safe insert (PostgreSQL), the open item

`UNIQUE (series_id, original_start)` is **NOT DEFERRABLE** so it can be an `ON CONFLICT` arbiter. No transient collision is possible because an occurrence's key never changes inside a series. Splits and moves only write into a **new, empty** series, with keys that are strictly increasing and distinct. A trigger rejects any same-series key change.

```sql
SELECT pg_advisory_xact_lock(hashtextextended($tenant_id::text || ':' || $series_id::text, 0));
WITH candidate AS (
  SELECT $task_id::uuid AS id
  WHERE NOT EXISTS (SELECT 1 FROM meeting_details d WHERE d.series_id = $series_id AND d.original_start = $original_start)
    AND NOT EXISTS (SELECT 1 FROM meeting_occurrence_suppressions s WHERE s.series_id = $series_id AND s.original_start = $original_start)
), t AS (
  INSERT INTO tasks (id, tenant_id, task_kind, ...) SELECT id, $tenant_id, 'meeting', ... FROM candidate
  ON CONFLICT (id) DO NOTHING RETURNING id
)
INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, ...)
SELECT id, $tenant_id, $series_id, $original_start, ... FROM t
ON CONFLICT ON CONSTRAINT uq_meeting_details_series_original_start DO NOTHING;
```

A meeting task left without details fails the deferred constraint trigger at commit, and the idempotent generator retries. Uniqueness therefore does not depend on the advisory lock.

**Verification (must pass before the SQL model is marked frozen):**
1. `condeferrable = false`.
2. The exact SQL is idempotent.
3. 16 parallel transactions without the lock produce one row and no orphan task.
4. With the lock, nothing aborts.
5. A split remap with a time shift keeps the task ids.
6. A same-series key change raises.
7. A time-changing "all events" edit forward-splits.
8. Suppression blocks the insert, including under concurrency.

### 5.7 Links (owner = series)

- **Claim:** under the series lock, the link row and its reference are inserted in a short transaction that commits before the provider call (the Phase 0 rule).
- **Correlation key:** `{linkId}:{gen}` for series links. Legacy links keep `{eventId}:{gen}`.
- **`invites_pending`** behaves as in the port: the link email doubles as the invite, and the fallback goes out once.
- **"Needed" rule.** A link is still needed while any referencing series has an upcoming non-cancelled occurrence, an unfinalized attendance check, or a 7-day recheck window still open.
- **Releasing:**
  - A series cancel or remove releases only that series' reference.
  - The provider cancel (`meeting-cancel:{linkId}:{gen}`) happens only when the **last** reference is released and the link is no longer needed, decided under the link row lock.
  - Regenerating a link affects only that series.

### 5.8 Attendance (per occurrence)

- **Configurable** (`Meetings:Attendance`): checkpoints `[30m, 2h, 24h]`, `MatchWindowBefore = 60m`, `LateRecheckWindow = 7d`, `FailedRetryAfter = 72h`, validated at startup.
- **Sessions:**
  - Teams attendance reports and Zoom past instances are upserted as sessions.
  - Every unassigned session whose start falls in [`start_at` − 60m, `end_at`] is assigned to the occurrence (nearest start wins; earlier on a tie). One occurrence may have several sessions.
  - A session that matches no occurrence is shown to the organizer only.
- **Records:**
  - Upserted on (`session`, `participant_key`, `joined_at`).
  - A person's time is the **union** of their intervals.
  - The employee is resolved from the work email or a connected-account email (the same identity rule as the C2 engine).
- **Statuses:**
  - `pending`, then `provisional`, then `synced`.
  - `attendance_unavailable`: the provider answered, but there are no sessions.
  - `sync_failed`: the provider erred; the reason is kept.
  - `not_applicable`: the meeting has no link.
- **Display:**
  - "Unknown" for unavailable, failed and pending.
  - "No join recorded" only when synced; **never "Absent"**.
- **Recovery:**
  - Automatic: one retry 72 h after a failure.
  - Manual: the organizer can recheck for up to 7 days.
  - A status only moves forward; it never goes back from synced.
- Legacy meetings keep the port's one-shot sync with the E6b/E6c fix. **E6a remains a documented limitation (§6).**

### 5.9 Reads

| Surface | Shows |
|---|---|
| Work Tree / board | Tasks plus visible meeting occurrences (past, current and the next 7 days), with a derived status pill; meetings cannot be dragged |
| My Work, "Today's meetings" | Visible Phase 2 occurrences plus legacy events marked "Calendar event" (the organizer or a participant, and the event has attendees or a link) |
| Calendar | Legacy events unchanged; materialized occurrences; virtual occurrences beyond the window (excluding materialized keys, suppressions and cancelled series); deterministic ids |
| Employee profile, "Meeting attendance" | Ancestor-chain check; private meetings masked; Phase 2 occurrences only |
| Approval detail | Snapshot only |

### 5.10 External calendar push

`CalendarSyncService.PushAsync` also pushes the organizer's materialized occurrences, matched through `external_calendar_event_links.meeting_task_id` so a pulled-back copy never duplicates. Attendees' calendars get the .ics by email, as today.

---

## 6. Existing meetings (documented limitations)

Calendar events created before Phase 2 stay in the calendar system with today's behaviour. They do not appear in the Work Tree, cannot have action items, and are not in the manager attendance view. They do appear in My Work, marked "Calendar event". **E6a:** a legacy recurring series syncs attendance for its first occurrence only. Moving these meetings to tasks later would be a separate, unapproved project.

---

## 7. Errors

**Conventions:**
- **404:** the meeting does not exist, or the caller may not see it (same body).
- **403:** the caller can see the meeting but may not do this.
- **400:** validation.
- **409:** state or concurrency.
- Provider failures never fail the request; they surface as link or attendance status.
- `/meetings/*` responses add a stable `code` extension to ProblemDetails.
- Logs carry ids only.

| Area | Main cases (code) |
|---|---|
| Create | `meeting_invalid_time`, `meeting_start_in_past`, `meeting_invalid_timezone`, `meeting_recurrence_invalid`, `meeting_module_member_required` (403), `meeting_module_unavailable`, `meeting_office_not_ready` (triggers reconcile), `meeting_provider_not_connected`, `meetings_disabled`, `meetings_read_only`, `bulk_invite_unavailable`; inactive people are skipped and returned in `skipped[]` (not an error) |
| Approval | `approval_already_decided`; Stale for a started meeting or a lost membership; a DB failure keeps the request pending (retry is safe); `meeting_approval_not_editable` |
| Edit | `meeting_changed` (revision), `meeting_occurrence_started` (except notes), `meeting_scope_invalid`, `meeting_recurrence_empty`, `meeting_busy` (lock timeout 5 s) |
| Cancel | Idempotent; a past occurrence is 409; the provider cancel is skipped while the link is still needed; a parked cancel shows in the link status |
| Move | `meeting_nothing_to_move`, same module 400, destination 403, Stale when nothing upcoming is left at apply time |
| Attendance | `attendance_recheck_expired`, `attendance_check_in_progress`; a missing Zoom scope gives `sync_failed` with reason `missing_scope` |
| Jobs | A lost lease finishes the current series and stops; per-series isolation; 3 failures in a row raise an alert; the integrity job alerts only |
| Outbox | At-least-once; a crash after send may duplicate unless the provider supports an idempotency key |

---

## 8. Flags, release and recovery

- **Flags:** `Meetings:Create`, `Meetings:BulkInvite` (refuses to start without P-1), `Meetings:Background`, `Meetings:ReadOnly`. No flag hides or deletes existing meetings.
- **Release order:** **P-1 → Release A (the safe floor):** schema, `ExcludeMeetings`, guards and `job_leases`, all flags off. Then **Release B:** the features.
  - Rolling back from B to A is safe.
  - **Never roll back below A once meeting rows exist.** A build without the filter would show meetings as ordinary tasks. The runbook must say this.
- **Recovery is roll-forward.** The data-deleting down migration is for dev and test only, and a guard blocks it in production.
- **Before deploying:** a dry run counts any existing rows that would violate the new constraints (expected 0).

## 9. Prerequisite P-1 — Outbox concurrency (separate platform task)

- Atomic claiming with `FOR UPDATE SKIP LOCKED`, a `processing` status and a lease.
- Owner-guarded completion and lease recovery.
- Provider idempotency keys where genuinely supported.
- **At-least-once** delivery is documented as such, never "exactly once".
- Multi-instance tests, including a crash after the send.
- Required before `Meetings:BulkInvite` is turned on. Recommended before Release B.

---

## 10. Test plan

**Unit (xUnit/Moq)**
- Access resolver: every tier × every endpoint (§5.1).
- Status derivation.
- Template vs detached propagation.
- Split mapping: time order, surplus rows, copied suppressions.
- Effective attendees: series ± overrides, last instruction wins.
- Bulk expansion: dedup, inactive, counts.
- Action-item linking on direct create and on approval.
- Attendance matcher: window, nearest start, ties, multiple sessions, interval union.
- Status machine: forward-only, checkpoint config validation.
- Error codes (§7).

**Integration (Testcontainers, real Postgres)**
- **§5.6 SQL suite** (all 8 cases).
- **Phase 0 parity:** the calendar integration classes run for both owner kinds (CalendarEvent and MeetingSeries); legacy expectations must not change.
- Approval apply idempotency and Stale.
- Split and move: ids stable, history kept, `TaskEditLog` rows, inherited link with no email.
- Suppression under a concurrent generator.
- Link lifecycle: cancel one of two series means no provider cancel; both released means a provider cancel; a single-series regenerate.
- Attendance: 3 weekly sessions on one link; 2 sessions in one occurrence; repeated checkpoints add no duplicates; late recovery; failure vs unavailable.
- RLS for every new table; composite FKs refuse cross-tenant ids.
- Migration on existing data; the down-migration guard.
- `job_leases` with two instances.

**Architecture**
- `ExcludeMeetings` allow-list.
- No unnamed `IgnoreQueryFilters` on `WorkTask`.
- Raw-SQL-on-tasks allow-list.
- Tenant isolation covers the new tables.
- `meeting_details` unique is not deferrable.

**Leak (one test per §5.1 surface)**
- An outsider — Owner, admin `*`, lead, module owner, HR, an approver after the decision, a peer — sees nothing and gets a 404.

**Engine integration**
- The C2 engine evaluates task-meeting occurrences with `event_id` = occurrence task id.
- A split does not duplicate incidents.
- Private titles are masked.
- Reminders fire once per occurrence and attendee.

**Frontend (Vitest)**
- Shared meeting form: project/Office, module picker, bulk preview with counts, recurrence, platform.
- Board card with the status pill and no drag.
- My Work "Today's meetings".
- Calendar merge, virtual ids and suppressions.
- Approval snapshot view.
- Attendance panels (organizer, self, manager; private masking; "Unknown" vs "No join recorded").
- Action-item creation with the module picker.

## 11. Risks

| Risk | Mitigation |
|---|---|
| A task query that bypasses `ExcludeMeetings` leaks meetings | Named filter + architecture tests + leak suite |
| A rollback below Release A exposes meetings as tasks | Two-step release, runbook ban, safe floor |
| Split and move logic | Lock ordering, key-immutability trigger, ids stable by construction, dedicated integration tests |
| Provider rate limits on big invites | P-1, outbox batching, capacity warnings |
| Zoom app scopes (past instances, list meetings) | Add them to the Marketplace app before release; users reconnect |
| The engine misreads task meetings | `IEmployeeMeetingOccurrenceSource` with parity tests against legacy events |

## 12. Out of scope

- Historical migration.
- The tray app.
- Meeting metrics.
- Transcript-based action items.
- An "everyone" bulk invite.
- Per-occurrence RSVP for series attendees.
