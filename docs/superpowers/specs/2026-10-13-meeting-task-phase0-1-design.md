# Meetings as Tasks — Phase 0 (calendar reliability) and Phase 1 (foundations)

> **Status:** design approved in brainstorming on 2026-10-13. This spec covers **Phase 0 and Phase 1 only**. Phases 2 and 3 are summarised in §5 so the decisions aren't lost; each gets its own spec before any code.
>
> **Repos and branches:** backend `HRMS-Backend-v1` and tenant frontend `Hrms--Web-application---front-end---v1`, both on `feature/meeting-task-phase0-1`, cut from `development` / `Development` on 2026-10-13.
>
> **Related:** `2026-10-13-calendar-exception-engine-gap-analysis.md` (written the same day in a separate session). Its §5 "Phase 0" overlaps ours. IDs like **P0-1** and **P1-3** below are that document's IDs, reused so the two specs cross-reference.

---

## 1. Goal

The CEO wants meetings to live inside the task (to-do) model. Everyone should see their meetings and tasks together, meetings should be trackable, and meeting follow-ups should become tasks. Eventually the Calendar becomes a daily planner.

Before meetings can move, two things have to be true:

1. **The calendar's notifications and attendance data are reliable.** Phase 2 turns meeting attendance into tracked data, and today notifications are silently dropped and attendance can be lost.
2. **Every project has a protected "Meeting" category, and every tenant has an Office project** to hold meetings that belong to no project.

Phase 0 delivers (1); Phase 1 delivers (2). Neither changes how meetings are stored.

## 2. Decisions captured in brainstorming

| # | Decision | Answer |
|---|---|---|
| B1 | What the CEO expects | All of: visibility (one list/planner), tracking (meetings as work), follow-up (action items → tasks). |
| B2 | Where non-project meetings live | The tenant's **Office** system project. Project meetings live in their own project. |
| B3 | Office project per tenant | Each tenant has **its own** Office project (not shared across tenants). It must exist for every tenant. |
| B4 | Office project lead | The **tenant owner** (the user holding the system `Owner` role, through their Employee record). Existing Office projects are switched to the tenant owner too (option a). |
| B5 | Who can see a meeting | **Organizer + participants only.** No project-lead bypass, no Office-lead bypass, no admin (`*`) bypass. *(Applies from Phase 2. Recorded here because it drives B4's safety.)* |
| B6 | Meeting category | A **"Meeting" category in every project**: cannot be deleted or renamed; can be reordered. |
| B7 | Meeting lifecycle | **Derived, never manual**: stored Scheduled/Cancelled; In progress and Completed computed from start/end time. Excluded from overdue, progress and monitor alerts. *(Phase 2.)* |
| B8 | Participants | Pick individually or **in bulk** (department / team / company / everyone). Bulk is expanded into individual rows at save time (snapshot). External guests by email. Participants are **not** added as Office project members. *(Phase 2.)* |
| B9 | Group of companies | The Office project follows the same legal-entity handling as any other project. Visibility is participant-based, so nothing leaks across companies. |
| B10 | Recurring meetings | The **series is one item**. An occurrence gets its own row only when something attaches to it (attendance, notes, an action item). *(Phase 2.)* |
| B11 | Google/Outlook-synced events | **Busy-time overlay only.** They never become meeting items. *(Phase 2/3.)* |
| B12 | Daily planner | **Time-blocking.** Planned blocks link a task to a time slot without changing its due date; plan vs actual is compared against clocked hours. *(Phase 3.)* |
| B13 | Action items | **Manual** quick-add on the meeting. Transcript-based AI suggestions are a later sub-project. *(Phase 2.)* |
| B14 | Where an action-item task goes | Defaults to the meeting's project (or the Office project), and can be changed when it's created. *(Phase 2.)* |
| B15 | Storage model | **Approach 1**: a meeting is a `tasks` row with `TaskKind = meeting`, plus a 1:1 `meeting_details` table. Existing meeting IDs are reused as task IDs during migration. *(Phase 2.)* |

---

## 3. Phase 0 — Calendar reliability fixes (backend + small frontend)

All findings below were verified against `development` on 2026-10-13. Line numbers refer to that commit.

### 3.1 How notifications are saved (the root cause of P0-1)

`CalendarNotificationSender` only *stages* outbox rows. `OutboxWriter.EnqueueAsync` does `AddAsync` and never saves. `UnitOfWork.ExecuteInTransactionAsync` begins and commits a transaction but does not flush pending changes. **Anything enqueued after a handler's last `SaveChangesAsync` is silently dropped.** The handler tests mock `ICalendarNotificationSender`, so they cannot see this.

**Rule for every Phase 0 handler:** stage all outbox rows (notifications and emails) *before* the single `SaveChangesAsync` that commits the business change. That way the change and its notifications commit together or not at all.

### 3.2 Items

| ID | Problem (verified) | Fix |
|---|---|---|
| **P0-1a** | `UpdateCalendarEventCommandHandler`: notify (`:60`) runs after the save (`:52`), so the notification is lost. | Move the notify block before the save. |
| **P0-1b** | `EditRecurringOccurrenceCommandHandler` "edit all": notify (`:65`) runs after the save (`:57`). The "this only" (`:71-95`) and "this and following" (`:97-127`) scopes notify no one. | Move "edit all"'s notify before the save. Add update notifications to the other two scopes, sent to the participants of the affected occurrence(s). |
| **P0-1c** | `CancelRecurringOccurrenceCommandHandler`: notify (`:62`) runs after the save (`:54`). | Move the notify block before the save. |
| **P0-2** | Teams: `MicrosoftGraphMeetingClient` returns `[]` when no report exists yet. `TeamsAttendanceSyncJob` then sets `LastAttendanceSyncedAt` with zero rows, and the meeting is never retried. The due filter (`EfCalendarEventMeetingRepository:35-36`) has no grace period. Zoom has the same path if it returns 200 with empty `participants`. **Neither client follows pagination** (Graph `@odata.nextLink`; Zoom `next_page_token`). | (1) Due filter: `EndDate < now − 30 min`. (2) An empty result does **not** mark the meeting synced; it is retried on the next run. (3) Once `EndDate < now − 24 h` and the result is still empty, finalise with status `attendance_unavailable` (new nullable `AttendanceSyncStatus` column: `synced` / `attendance_unavailable`). (4) Follow pagination in both clients until the last page. |
| **P0-3** | "This and following" may not copy participants onto the new series. Reported, **not yet verified**. | Write a failing test first. Fix only if it fails. |
| **P1-2** | `RespondToCalendarEventCommandHandler:69` tells the organizer only about ResolutionRequested and ReplacementNominated. Accepted and Rejected are silent. | Add in-app organizer notifications `calendar_event_rsvp_accepted` and `calendar_event_rsvp_rejected`, staged before the save (`:82`). |
| **P1-3** | Update, cancel-occurrence and delete reach participants in-app only. Guests get nothing (`NotifyEventUpdatedAsync` / `NotifyEventCancelledAsync` take employee IDs only). Delete (`DeleteCalendarEventCommandHandler:60-67`) never loads guests. | Send an email to participants **and guests** on update, cancel-occurrence and delete, using the existing `calendar_event_invite` email pipeline with an "updated" / "cancelled" subject variant. Guests are loaded before the event is removed. |
| **P1-4** | Removing a participant (`RemoveCalendarEventParticipantCommandHandler`) or guest (`RemoveCalendarEventGuestCommandHandler`) is silent; the participant handler says so in a comment. Removing the Zoom/Teams meeting (`RemoveEventMeetingCommandHandler:65-70`) is silent, so people keep a dead join link. The replacement nominee is never told. | Removed participant → in-app + email. Removed guest → email. Meeting link removed → in-app to participants and email to participants + guests. Nominee → in-app. The deliberate-silence comment goes. |
| **P1-5** | When a Zoom/Teams meeting is pending, `CreateCalendarEventCommandHandler` holds back invites (`:75`, `:88`). If `CreateEventMeetingCommandHandler` then fails (409 at `:49-58`, provider throw at `:64/:69`), no invites are ever sent. The frontend ignores the result (`calendar-page.component.ts:236-237`). | Backend: on meeting-creation failure, send the held-back invites **without a link** and notify the organizer in-app (`calendar_event_meeting_link_failed`). Frontend: show an error toast and keep the event, with the option to add a meeting later. |
| **X-1** | Every successful `CreateEventMeeting` re-emails everyone (`:111-123`). Remove-then-re-add, or a retried request, sends duplicate invites. | Stamp invite emails with an idempotency key `(eventId, meetingId, recipient)`. The outbox handler skips a key it has already sent. |
| **X-2** | `DeleteCalendarEventCommandHandler` cancels the remote Teams/Zoom meeting before `SaveChangesAsync` (the comment at `:38-40` is wrong about rollback). A failed save leaves the event alive with a dead meeting. | Delete locally first. The remote cancel becomes an outbox message, so it runs only after the delete commits and is retried on failure. |

### 3.3 Out of scope for Phase 0 (owned by the exception-engine work)

P1-1 (RSVP history), P1-6 (integration-health notices), P2-1 (`.ics`), P2-2 (reminders) and P2-3 (attendees passed to Zoom/Teams).

### 3.4 Testing

- Unit tests for each handler change. They assert the outbox rows staged, not just "sender was called".
- **New integration tests** (Testcontainers Postgres, real `OutboxWriter` + `ApplicationDbContext`). For P0-1a/b/c, P1-2, P1-3 and P1-4, they assert the outbox row **exists in the database after the command returns**. This closes the gap that let P0-1 ship.
- P0-2: unit tests for the empty / grace / cut-off / pagination paths of both sync jobs, with fake clients.

---

## 4. Phase 1 — Foundations

### 4.1 System task categories

**Data:** a new nullable column `task_categories.system_key` (`varchar(32)`). A unique index on `(tenant_id, project_id, system_key)` where `system_key is not null` stops a project getting two Meeting categories. Constant `TaskCategorySystemKeys.Meeting = "meeting"`.

**Rules (backend, enforced in handlers):**

| Action | System category (`system_key` set) | Normal category |
|---|---|---|
| Delete (`DeleteTaskCategoryCommandHandler`) | ✗ 409 "System categories can't be deleted." | unchanged |
| Rename (`EditTaskCategoryCommandHandler`) | ✗ 409 if the name changes; a display-order-only edit is allowed | unchanged |
| Reorder (`ReorderTaskCategoriesCommandHandler`) | ✓ | unchanged |
| Assign tasks to it | ✓ (Phase 2 will put meetings here) | unchanged |

**API:** `TaskCategoryResponse` gains `systemKey` (`string | null`) and `isSystem` (`bool`).

**Seeding:**

- `DefaultTaskCategoryTemplate.BuildRows` adds `meeting` (`DisplayOrder = 4`, `SystemKey = meeting`), so every new project gets it. It keeps the template's lowercase style.
- `OfficeProjectProvisioner` adds `Meeting` (`SystemKey = meeting`) after Onboarding/Offboarding, in its existing capitalised style.
- Demo seeders that build categories by hand are updated to match.

**Backfill (EF migration, data step), per project:**

- If the project already has a category named `meeting` (case-insensitive) → set its `system_key = 'meeting'`. No duplicate; existing tasks stay where they are.
- Otherwise → insert a `meeting` category with `DisplayOrder = max + 1`.

### 4.2 Office project for every tenant, led by the tenant owner

**Tenant owner:** the user holding the tenant's system role named `Owner` (assigned by `TenantOwnerInvitationService`). Expired assignments (`UserRole.ExpiresAt` in the past) don't count; if more than one user still holds it, the earliest `UserRole.AssignedAt` wins. The owner's **Employee** record in that tenant is the lead. If the owner has several Employee records (one per legal entity), it's the one in the primary legal entity, falling back to the earliest created.

**`OfficeProjectReconciler`** is a new idempotent service. For one tenant it:

1. Creates the Office project if it's missing, using `OfficeProjectProvisioner`. Lead = the tenant owner's employee; owning legal entity = that employee's legal entity.
2. If the project exists but its lead isn't the tenant owner's employee, sets `LeadId` to the owner's employee and adds the owner as a system `ProjectMember` of the Office module if they aren't one. **The previous lead's membership is kept**, so they still see the checklist tasks they're assigned to or created.
3. Does nothing if the tenant has no owner employee yet. It retries the next time it runs.

**When it runs:**

- **Startup backfill:** a hosted service runs it once for every active tenant. It runs under admin mode for the cross-tenant tenant list, then switches to each tenant before writing, the same pattern as `ActivityDailySummaryJob`. It's safe to run on every start.
- **After an employee is created:** the two paths that create Employee rows (`OnboardingDraftWriteService` finalize and `ApproveAccessGrantRequestCommandHandler`) call it for that tenant after their save. A new tenant gets its Office project as soon as the owner's employee exists.
- **Existing call sites:** `EnsureAsync` stays where it is (the checklist provisioner) as a safety net, and gains the Meeting category.

**Concurrency:** `OfficeProjectProvisioner` already handles the unique-violation race. The lead update is a single-row update guarded by the project's current `LeadId`, so concurrent runs converge.

### 4.3 Frontend (tenant app)

- `TaskCategoryDto` / `TaskCategoryColumn`: add `systemKey?: string | null` and `isSystem?: boolean`. Update the mapper.
- `category-list-editor.component.ts`: for system rows, hide the delete button and make the name read-only. Show a lock icon with the tooltip "System category: can't be renamed or deleted." Drag-reorder still works.
- The task form's "Work item type" picker shows Meeting like any other category. Phase 2 decides whether creating a meeting through the task form is allowed.

### 4.4 Testing

- Unit tests: the delete/rename guards, reorder still allowed, template includes Meeting, and the reconciler cases (create when missing; switch lead; keep old membership; no-op without an owner employee; earliest-owner tie-break).
- Integration test (Testcontainers): the backfill migration on a database seeded with projects that do and don't already have a `meeting` category. Exactly one system Meeting category per project afterwards, and no task moves.
- Frontend: component tests for the editor (no delete or rename on system rows; reorder emits).

---

## 5. Later phases (decided, specified separately)

- **Phase 2 — Meeting as task** (B5, B7, B8, B10, B13, B14, B15). `TaskKind = meeting` + `meeting_details` (start/end, timezone, all-day, recurrence, location, privacy, lifecycle). Participant, guest, online-meeting and attendance tables re-pointed to the task, reusing existing event IDs. `meeting_occurrences` materialised on touch. Participants-only visibility. Derived lifecycle and metric exclusion. Bulk participant expansion. Manual action items. The `/calendar` API contract is kept stable so the frontend calendar changes little. **Coordinate with the exception-engine work**, which reads attendance by event ID; reusing IDs keeps those keys stable.
- **Phase 3 — Daily planner** (B11, B12). One timeline: meetings, planned task blocks (new table), a synced-event busy overlay, and due/overdue tasks. Plan vs actual against clocked hours.
- **Later:** transcript-based action-item suggestions; SCL-01 locking for any new background job.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Switching existing Office leads drops lead-bypass visibility of checklist tasks for the old lead | Accepted (B4 option a). The old lead keeps membership and keeps seeing tasks they're assigned to or created. Called out in release notes. |
| Backfill on a large tenant | Set-based SQL in the migration, not per-row EF calls. |
| A project's existing user category named "meeting" becomes protected | Intended. It's the same concept, and its tasks are untouched. |
| Phase 0 changes email volume (guests now get update/cancel mail) | Intended. The idempotency key (X-1) prevents duplicates. |
