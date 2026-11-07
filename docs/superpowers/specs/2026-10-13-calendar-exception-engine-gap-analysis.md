# Calendar & Meetings → Investigation Inbox: Notifications, Exceptions and Handoff

> Brief for plugging the **Calendar & Meetings** module into the centralized **Investigation** inbox described in `INVESTIGATION-INBOX-HANDOFF.md` (2026-10-13). The inbox is one sidebar where an approver sees and resolves every alert and request for the employees they cover.
> Compiled 2026-10-13 from the code as it is now: backend `HRMS-Backend-v1` (checked-out branch `development`) and frontend `Hrms--Web-application---front-end---v1`. The Investigation handoff was written against `feature/task-subtasks`. The pieces it names and this report reuses were confirmed to exist on `development` too: `Exception` entity, `ExceptionAlertRouter`, `ExceptionDetectionJob`, `ITeamActionSource`, `EmployeeSignalCatalogue`, `EfExceptionRepository`.
>
> **How to read this.** Sections 1–4 describe **what exists today**. They come from static code reading: nothing was executed. Items marked **Confirmed** were traced end to end; items marked **Needs verification** need a failing test first. Sections 5–8 cover **what has to be built** and the **open decisions**. None of 5–8 exists yet.
>
> **Scope:** `Features/Calendar` (backend), `ONEVO.Infrastructure/Services/Calendar`, `ExternalServices/Calendar`, frontend `modules/calendar`. The Work Management project calendar and the Leave calendar are out of scope.

---

## 1. The goal in one paragraph

The calendar module already creates events, invites employees and guests, generates Zoom/Teams links and pulls attendance reports. It raises **no exceptions**, and several of its notifications are missing or silently dropped. The goal has two parts. First, make calendar notifications reliable (**Phase 0**). Second, have the calendar detect **meeting attendance, RSVP and meeting-load exceptions** and raise them as **cases in the existing exception model**, so they appear in the Investigation inbox for the employee's reporting manager. When the manager acts on a case, every count that included it must drop together. This is the same "21 → 20" rule the handoff sets.

### 1.1 Decisions captured in brainstorming

| # | Decision | Answer |
|---|----------|--------|
| D1 | Where calendar exceptions surface | The unified **Investigation** inbox, as cards (one per case). Visible to the employee's **reporting manager**. |
| D2 | Categories that become manager cards | **Attendance** (no-show / late / left early), **RSVP behaviour**, **Meeting load / conflicts**. **Integration health is not a manager card**; it goes to the employee as a notification only. |
| D3 | Attendance data source | **Zoom/Teams attendance report first, monitoring-agent signal as fallback.** No online signal (in-person, pasted link without agent data) means no attendance exception. |
| D4 | When a card is created | **Pattern + severity.** Single incidents are recorded. A card is created when a threshold is crossed in a rolling window, or immediately for high-impact cases. |
| D5 | Thresholds | **Fixed system defaults** (no tenant config). Values in §6.3. |
| D6 | Employee visibility | The employee is **notified** when a case is raised about them and **can add a justification**, which shows on the manager's card. |
| D7 | Manager actions | **Acknowledge / Dismiss (with reason) / Escalate.** |
| D8 | Escalation and routing | A case goes to the employee's **reporting manager**. On escalation (manual, or after the SLA) it goes **one level up, to that manager's reporting manager**. If there is no one to send it to, the case is **only logged**: it stays visible in the inbox and reports, but **no HR fallback** notification is sent. *This is the author's reading of the answer "goes to the reporting manager if there is one, otherwise just a log". Confirm it (§8 Q1), because it differs from today's monitoring router, which falls back to HR (§4.2).* |
| D9 | Retention | **Standard.** Calendar cases follow the same retention as the other monitoring exception cases; no calendar-specific policy (§6.6). |
| D10 | Organizer behaviour (e.g. frequent late cancellations) | **Not counted.** Only participant behaviour raises exceptions. |
| D11 | Extra weight when the manager organises | **Only for no-show** (recommended, see §6.2). |
| D12 | Existing notification gaps | Fixed first as **Phase 0**, by priority (§5). |

---

## 2. Notifications today (current state)

All calendar notifications go through `Features/Calendar/Services/CalendarNotificationSender.cs` → `IOutboxWriter`. There are two outbox types:

- `WorkNotification`: in-app notification, rendered from a `calendar_event_*` template in `NotificationTemplateSeeder.cs`.
- `CalendarEventInviteEmail`: email through `CalendarEventInviteEmailOutboxHandler` → `TransactionalEmailService`, template `calendar_event_invite`. It is a plain HTML email with **no `.ics`**.

| Trigger (handler) | In-app | Email | Recipients | Status |
|---|---|---|---|---|
| Create event + participants (`CreateCalendarEventCommandHandler`) | ✅ `calendar_event_participant_added` | ✅ invite (held back until the link exists if Zoom/Teams is pending) | Participants | Working |
| Create event + guests | ❌ (no account) | ✅ (held back if Zoom/Teams is pending) | Guests | Working |
| Add participants later (`AddCalendarEventParticipantsCommandHandler`) | ✅ | ✅ includes existing link | New participants | Working |
| Add guests later (`AddCalendarEventGuestsCommandHandler`) | ❌ | ✅ | New guests | Working |
| Zoom/Teams link generated (`CreateEventMeetingCommandHandler`) | ❌ | ✅ invite with join link | Participants + guests | Working |
| Update event (`UpdateCalendarEventCommandHandler`) | ⚠️ **never persisted** | ❌ | — | 🐞 **Bug, Confirmed** |
| Recurring: edit all (`EditRecurringOccurrenceCommandHandler.EditAllEventsAsync`) | ⚠️ **never persisted** | ❌ | — | 🐞 **Bug, Confirmed** |
| Recurring: cancel occurrence (`CancelRecurringOccurrenceCommandHandler`) | ⚠️ **never persisted** | ❌ | — | 🐞 **Bug, Confirmed** |
| Recurring: edit this only / this & following | ❌ | ❌ | — | Gap |
| Delete event (`DeleteCalendarEventCommandHandler`) | ✅ `calendar_event_cancelled` | ❌ | Participants; **guests get nothing** | Gap |
| Remove participant / guest | ❌ | ❌ | — | Gap (handler comment says deliberate) |
| Remove Zoom/Teams meeting (`RemoveEventMeetingCommandHandler`) | ❌ | ❌ | — | Gap: the link disappears with no notice |
| RSVP Accepted / Rejected (`RespondToCalendarEventCommandHandler`) | ❌ | ❌ | — | Gap: the organizer never learns |
| RSVP Resolution requested | ✅ | ❌ | Organizer | Working |
| RSVP Replacement nominated | ✅ | ❌ | Organizer only; **the nominee is not told** | Gap |
| Reminder before start | ❌ | ❌ | — | Not built |
| Token refresh failed → `ReauthRequired` (`CalendarConnectionTokenProvider`) | ❌ | ❌ | — | Silent, logs only |
| Sync failed 3× → `Failed` (`CalendarSyncService`) | ❌ | ❌ | — | Silent, logs only |
| Sync conflict (both sides changed, external wins) | ❌ | ❌ | — | Silent, stored on the link row only |

### 2.1 🐞 The three dropped notifications (Confirmed)

`OutboxWriter.EnqueueAsync` only calls `DbSet.AddAsync`. It never saves. `RespondToCalendarEventCommandHandler` documents the rule: enqueue **before** `SaveChangesAsync`, or the row is lost.

The Update, Edit-all and Cancel-occurrence handlers do the opposite: they call `SaveChangesAsync` first, then enqueue, and never save again. `UnitOfWork.ExecuteInTransactionAsync` only commits the DB transaction. It does not call `SaveChanges`, and no MediatR behavior saves on the handler's behalf. `CalendarNotificationSenderTests` mocks the writer, so nothing catches the bug.

### 2.2 Other confirmed weaknesses

- **Meeting link creation fails → invites are never sent.** With a provider pending, the create handler suppresses the participant invite email and all guest emails. The frontend (`calendar-page.component.ts:236`) calls `createMeeting` as a second request. If that request fails, nobody is ever emailed.
- **Zoom/Teams meetings are created without attendees** (`ZoomMeetingClient`, `MicrosoftGraphMeetingClient`). The provider sends no invites, and its attendee data cannot be tied back to invitations.

---

## 3. Data available for exception detection (current state)

| Data | Source | Used for | Limitation |
|---|---|---|---|
| Event, participants, RSVP status + reason | `calendar_events`, `calendar_event_participants` | RSVP, conflicts, load | Current status only. **No response timestamp, no history** |
| Online meeting | `calendar_event_meetings` (provider, external id, `LastAttendanceSyncedAt`) | Which events have a provider report | Auto-generated meetings only |
| Attendance | `calendar_event_meeting_attendances` (name, email, joined, left, duration) | No-show / late / left early | Keyed by **external email/name, not employee id**. Raw rows, never compared with participants |
| Agent meeting signal | `Monitoring/Meetings` `MeetingSignal` + `MeetingApplicationComplianceCalculator` (≥ 50% of duration in a meeting app) | Attendance fallback (D3) | Needs the agent. Cannot tell *which* meeting. 60 s samples |
| Overlaps | `CheckCalendarConflictsQueryHandler` | Double-booking | Create-time and informational only; not persisted |
| Leave | Leave requests (`EfLeaveRequestConflictProvider`) | Meeting on approved leave | Today it runs from the leave side only |
| Working hours | `WorkCalendarResolver`, `ExpectedWorkdayCalendar` | Off-hours meetings | Must be reused across features |
| Manager | `IEmployeeAuthorityResolver` (used by `ExceptionAlertRouter` and every attendance request) | Routing | Use the **same resolver** for "in my inbox" and "can act" (handoff §7.1) |

### 3.1 🐞 Attendance gaps that would produce false exceptions

1. **An empty attendance report is never retried (Confirmed).** `TeamsAttendanceSyncJob` and `ZoomAttendanceSyncJob` set `LastAttendanceSyncedAt` even when the provider returns zero rows, and `GetDueForAttendanceSyncAsync` only picks null rows. The data is lost for good, and every participant would look like a no-show.
2. **Identity matching.** Attendance rows must be matched to `employee_id` by work email plus connected-account email (`external_calendar_connections`). Unmatched rows are never used as evidence against anyone.
3. **"This & following" edit** creates `newMaster` without copying participants (`EditThisAndFollowingAsync`). **Needs verification.** If true, future occurrences have no participants, and RSVP and attendance attribution break.

---

## 4. The existing exception model the calendar should reuse (current state)

### 4.1 `Exception` entity, i.e. the "case" (`Domain/Features/Monitoring/Exceptions/Entities/Exception.cs`)

- `ExceptionType` is **stored by name (a string column)**. Calendar types can be appended **without a migration**. Today's values: `SustainedLowActivity`, `AttendanceIrregularity`, `UnusualActivityPattern`, `IdentityAnomaly`.
- `ExceptionStatus`: `Open → Acknowledged → Resolved | Escalated`.
- Fields: `Title`, `Description`, `MetadataJson` (evidence / measures), `DetectedAt`, `AcknowledgedAt/ById`, `ResolvedAt/ById`, `EscalatedAt/ById` (null = auto-escalated by the sweep), `ResolutionNote`.
- **Not present:** an employee-side justification field (needed for D6), a resolution outcome (excused vs confirmed, see handoff §6.1), and a dedup key.

### 4.2 Routing and escalation today

- `ExceptionAlertRouter.NotifyDetectedAsync`: notifies the **reporting manager** through `IEmployeeAuthorityResolver` (purpose `ExceptionAlertReview`). If there is **no manager, it falls back to HR** (`ExceptionPermissions.HrReview` holders). If neither exists, it only writes a log warning.
- `ExceptionAlertRouter.NotifyEscalatedAsync`: always notifies **HR**.
- `ExceptionDetectionJob` (nightly, 23:30 UTC) auto-escalates cases left **Open for 3 days** (`EscalationThreshold`), then notifies HR.
- The router **never notifies the employee** ("Never the employee themselves").
- ⚠️ **Conflict with D8.** Calendar cases need different routing: escalation goes to the manager's manager, with **no HR fallback** and a log only when nobody is above. That needs a per-type routing policy in the router, or a calendar-specific router (§6.5).

### 4.3 Where cases already show up

- **Attendance → Alerts** (`/attendance/alerts`, `MonitoringExceptionsController`, scope from `IExceptionScopeResolver`). This is the screen the Investigation inbox replaces.
- **My Team widget** via `MonitoringExceptionTeamActionSource` (`ITeamActionSource`).
- **Employee detail → "Needs attention"**, signal `monitoring_exceptions` (`EmployeeSignalCatalogue`).
  - 🐞 Known bug (handoff §3.1): `EfExceptionRepository.CountDetectedInRangeAsync` **does not filter by status**, so resolved cases still count.
  - Calendar cases would inherit this bug, and they would also be lumped under "monitoring" (see §6.4).
- **Frontend type handling:** `attendance/models/exception-alert.model.ts` (type labels) and `exception-evidence-panel` (per-type evidence). `GetExceptionEvidenceQueryHandler` switches on type (e.g. `IdentityAnomaly`). New calendar types need labels and a **meeting evidence** branch.

---

## 5. Phase 0: prerequisite fixes (before raising any calendar exception)

| Priority | Item | Type | Why it matters for exceptions |
|---|---|---|---|
| **P0-1** | Persist notifications in Update / Edit-all / Cancel-occurrence: enqueue before the save, or save again. Add a test with a real `OutboxWriter` + DbContext. | 🐞 Confirmed | People not told about a change cannot be blamed for missing it |
| **P0-2** | Attendance sync: do not mark synced on an empty result. Retry until 24 h after the meeting ends, then mark `attendance_unavailable`. | 🐞 Confirmed | Otherwise everyone looks like a no-show |
| **P0-3** | Verify, then fix, participants not being copied on "this & following" | 🐞 Needs verification | RSVP and attendance attribution |
| P1-1 | Store RSVP `responded_at` and status history | Data gap | Needed by `RSVP-NORESPONSE`, `RSVP-LATEDECLINE`, `RSVP-DECLINERATE` |
| P1-2 | Notify the organizer on Accepted / Rejected | Gap | Completeness |
| P1-3 | Email participants **and guests** on update / cancel / delete | Gap | Fairness |
| P1-4 | Notify the nominee on replacement nomination and removed participants/guests; notify participants when the Zoom/Teams link is removed | Gap | Fairness |
| P1-5 | If meeting-link creation fails: send the held-back invite without a link and alert the organizer | Gap, Confirmed | Otherwise invitees are never invited |
| P1-6 | Integration health → **employee** notification (`ReauthRequired`, `Failed`, sync conflict) | Gap | D2: not a card, but silent today |
| P2-1 | `.ics` (REQUEST / CANCEL) on invite, update and cancel emails | Enhancement | Lands in the recipient's own calendar |
| P2-2 | Reminder about 15 min before start | Enhancement | Prevents the no-shows the engine would flag |
| P2-3 | Pass attendees to Zoom/Teams when creating the meeting | Enhancement | Better attendance matching |

---

## 6. What has to be built (proposal, not existing)

### 6.1 Incidents → cases

- **Incident:** one occurrence, for example "employee X missed meeting Y on date D". Persist incidents in a small calendar-owned table, for example `calendar_meeting_incidents`:

  | Column | Notes |
  |---|---|
  | TenantId, EmployeeId | |
  | RuleCode | e.g. `CAL-ATT-NOSHOW` (§6.2) |
  | EventId, OccurrenceStart | which meeting (and which occurrence for recurring series) |
  | DetectedAt | |
  | Confidence | `provider` (Zoom/Teams report) or `agent` (fallback, labelled "estimated" on the card) |
  | EvidenceJson | joined/left, attended ratio, RSVP status, organizer. **No app names or activity samples** |
  | ExceptionId (nullable) | the case this incident is attached to |
  | Unique (TenantId, EmployeeId, RuleCode, EventId, OccurrenceStart) | the same incident cannot be recorded twice |

- **Case:** when a rule's threshold is crossed (§6.3), create an `Exception` row with a **new `ExceptionType` value per rule family** (e.g. `MeetingNoShow`, `MeetingLateJoin`, `MeetingEarlyLeave`, `MeetingRsvp`, `MeetingConflict`, `MeetingOverload`). Put the rule code, window and incident ids in `MetadataJson`.
- **Dedup:** while a case for (employee, type) is `Open` or `Acknowledged`, new incidents are **attached to it** rather than opening another case.
- **Exclusions (all rules):**
  - approved leave or a public holiday during the meeting
  - the event was cancelled, or the employee was removed before the start
  - RSVP was **Rejected**, or the nominated replacement attended (attendance rules only)
  - all-day events
  - events where the employee is the only participant
  - the employee is the organizer (D10: organizer behaviour is not counted)

### 6.2 Rules

| Code | Category | Incident | Case trigger | Severity | Source |
|---|---|---|---|---|---|
| `CAL-ATT-NOSHOW` | Attendance | RSVP Accepted or Pending, meeting ended, no join (provider) or below 50% app usage in the window (agent) | **3 / 30 days**. **Immediate** if the organizer is the employee's reporting manager, or the meeting is a 1:1 | Medium (immediate: High) | Provider → agent |
| `CAL-ATT-LATE` | Attendance | First join more than 10 min after start | **5 / 30 days** | Low | Provider only |
| `CAL-ATT-EARLY` | Attendance | Left more than 15 min early, **or** attended below 50% of duration | **5 / 30 days** | Low | Provider (agent for the ratio) |
| `CAL-RSVP-NORESPONSE` | RSVP | Still Pending at the start | **5 / 30 days** | Low | Participants (+P1-1) |
| `CAL-RSVP-DECLINERATE` | RSVP | Per window | **≥ 50% declined, minimum 6 invites / 30 days** | Low | Participants (+P1-1) |
| `CAL-RSVP-LATEDECLINE` | RSVP | Accepted → Rejected less than 2 h before the start | **3 / 30 days** | Low | Needs history (P1-1) |
| `CAL-RSVP-UNRESOLVED` | RSVP | The employee's Resolution request / Replacement nomination is unanswered by the organizer at the start | **Immediate**, case on the **requesting employee** (FYI to their manager: the conflict was never sorted out) | Medium | Participants |
| `CAL-LOAD-DOUBLEBOOK` | Load | 2 or more overlapping meetings (≥ 15 min) that are Accepted or Pending | **3 / 7 days** | Low | Events + recurrence expander |
| `CAL-LOAD-ONLEAVE` | Load | A meeting (not Rejected) overlaps approved leave | **Immediate**; auto-resolves on decline or removal | Medium | Events + leave |
| `CAL-LOAD-OVERLOAD` | Load | More than 5 h of Accepted meetings in one working day | **3 days / 7 days** | Low (wellbeing) | Events |
| `CAL-LOAD-OFFHOURS` | Load | An Accepted meeting falls outside working hours | **3 / 7 days** | Low | Events + `WorkCalendarResolver` |

**Why only no-show gets extra weight for manager-organised meetings (D11):**
- Missing a meeting your own manager called, or a 1:1, is the case a manager most wants to know about straight away, and it is rare enough not to flood the inbox.
- Extra weight on late or early-leave would turn small delays into cards and add noise.
- Weighting RSVP rules this way would penalise people for declining their manager, which is often legitimate.

### 6.3 Fixed system defaults (D5)

All values live in one constants class (e.g. `CalendarExceptionDefaults`), so tenant configuration can be added later without touching the rules:

`LateJoinMinutes = 10`, `EarlyLeaveMinutes = 15`, `MinAttendanceRatio = 0.50`, `NoShowCount = 3 / 30d`, `LateCount = 5 / 30d`, `EarlyLeaveCount = 5 / 30d`, `NoResponseCount = 5 / 30d`, `DeclineRate = 0.50 (min 6) / 30d`, `LateDeclineHours = 2`, `LateDeclineCount = 3 / 30d`, `DoubleBookOverlapMinutes = 15`, `DoubleBookCount = 3 / 7d`, `DailyMeetingHours = 5`, `OverloadDays = 3 / 7d`, `OffHoursCount = 3 / 7d`, `AttendanceReportCutoffHours = 24`, `ManagerSlaDays = 3` (same as the existing `ExceptionDetectionJob.EscalationThreshold`).

### 6.4 Counts must drop together (the 21 → 20 rule)

- **Count only `Open` and `Acknowledged` cases** everywhere calendar cases are counted. This also fixes the existing `CountDetectedInRangeAsync` bug from handoff §3.1.
- **Add a separate employee signal**, `meeting_exceptions` (category `meetings`, severity warning), to `EmployeeSignalCatalogue`. Exclude calendar types from `monitoring_exceptions`. Otherwise meeting cases inflate the "monitoring" number and send managers to the wrong drill-down.
- **Signal items:** a new item kind `meeting_incident` lists the incidents behind each open case.
- **My Team widget:** `MonitoringExceptionTeamActionSource` picks the new types up automatically, because it reads the same table. Decide whether calendar cases should get their own `ITeamActionSource` (`calendar_meeting_exceptions`, domain `people`) so the widget and the inbox can filter them separately. Recommended: yes.
- **Investigation inbox kind:** `meeting_exception`. It reuses the `exception_case` actions (acknowledge, resolve, escalate) and adds the meeting evidence view.
- **Resolution outcome** (aligns with handoff §6.1 "resolved ≠ excused"):
  - **Dismiss** = `Resolved` with outcome `excused`: the incidents stop counting toward future thresholds.
  - **Acknowledge, then Resolve** with outcome `confirmed`: the incidents keep counting, marked as reviewed.

### 6.5 Routing, escalation and employee side

- **Recipient:** the reporting manager from `IEmployeeAuthorityResolver` (purpose `ExceptionAlertReview`), the same resolver the inbox scope uses. Nobody should see a card they cannot act on.
- **Escalation (D8):**
  - Manual "Escalate", or **3 days Open** with no action → the case goes to the **next manager up**, i.e. the reporting manager of the person who held it.
  - Nobody above → status `Escalated`, **logged only**, no HR notification.
  - Implement this as a routing policy per `ExceptionType` in `ExceptionAlertRouter`, plus the stale sweep in `ExceptionDetectionJob`. Do **not** change the routing for the existing monitoring types.
- **No manager at all:** the case is created and **logged only** (D8). It stays visible to anyone with `exceptions:*` scope.
- **Employee (D6):**
  - New in-app template `calendar_exception_raised_for_you`, sent to the employee when a case opens.
  - New fields `EmployeeNote`, `EmployeeNoteAt` on `Exception`, or a small `exception_comments` table if other modules want this too (recommended, since the inbox is shared).
  - Endpoint `POST api/v1/monitoring/exceptions/{id}/employee-note`, allowed only for the subject employee.
  - The note shows on the manager's card and in the evidence panel.

### 6.6 Retention (D9, "standard")

Calendar cases live in the shared `exceptions` table and follow whatever retention applies to the other monitoring exception cases. Neither the code nor this report adds a calendar-specific purge. Raw incidents (`calendar_meeting_incidents`) are only evidence. Keep them for the same period as the cases they attach to; unattached incidents can be purged after **90 days**, which is three times the longest 30-day window. No exception-specific retention job was found in the code, so confirm the platform standard with the lead (§8 Q3).

### 6.7 Detection triggers

| Trigger | When | Rules |
|---|---|---|
| Post-meeting evaluator | After attendance sync succeeds, or at the 24 h cutoff when there is no report | ATT-*, RSVP-NORESPONSE, RSVP-UNRESOLVED |
| RSVP change | Inside `RespondToCalendarEventCommandHandler` | RSVP-LATEDECLINE; auto-resolve LOAD-ONLEAVE / DOUBLEBOOK |
| Leave approved; event created or changed | Leave approval and event handlers | LOAD-ONLEAVE, LOAD-DOUBLEBOOK |
| Nightly | Runs inside `ExceptionDetectionJob`'s tenant loop, or in a sibling job with the same shape | RSVP-DECLINERATE, LOAD-OVERLOAD, LOAD-OFFHOURS, threshold roll-ups, stale-case sweep |

### 6.8 Frontend

- `modules/calendar`: an "attendance" tab on the event detail (organizer view) that shows matched attendance and the incidents raised.
- `attendance/models/exception-alert.model.ts`: labels for the new types.
- `exception-evidence-panel`: meeting evidence (meeting title and time, RSVP, joined/left, confidence badge, employee note).
- Investigation inbox: a `meeting_exception` filter chip. Deep link `?alertId=` works unchanged, because these are ordinary exception cases.

---

## 7. Suggestions

1. **Fix Phase 0 P0 first.** P0-1 and P0-2 would make attendance cases unfair or plainly wrong.
2. **Kill the "save then enqueue" bug class.** Add an integration test that runs each calendar command against a real `OutboxWriter` and asserts the outbox rows exist.
3. **Ship a small rule set first.** Start with `CAL-ATT-NOSHOW`, `CAL-LOAD-ONLEAVE` and `CAL-RSVP-UNRESOLVED`: high value, low noise. Add the Low-severity rules once real inbox volume is visible.
4. **Show confidence.** Mark agent-fallback attendance as "estimated" on the card.
5. **Privacy.** Card evidence shows meeting facts only (title, time, joined/left), never app usage or activity samples. Follow Monitoring pillar visibility rules.
6. **Build a shared employee-note feature.** Build "employee justification" once for all exception cases, not only calendar ones. The inbox is shared, and the same need will come from attendance and monitoring.

---

## 8. Open decisions (need product / lead input)

1. **Confirm D8 escalation.** Is it correct that a calendar case is escalated to the **manager's manager**, and that when nobody is above, it is **only logged**, with no HR at any point? Today's monitoring cases fall back to HR and escalate to HR.
2. **Approver resolver.** Use `IEmployeeAuthorityResolver` (same as `ExceptionAlertRouter` and attendance requests), or "reporting line only" through `IEmployeeHierarchyClosureRepository.GetDirectManagerEmployeeIdAsync`? Whichever is chosen must drive both inbox visibility and "can act" (handoff §7.1).
3. **Retention "standard".** What is the platform-standard retention for exception cases? No retention job for `exceptions` exists in code today.
4. **Separate My Team source** for calendar cases, or keep them inside `MonitoringExceptionTeamActionSource`?
5. **Employee note.** Store it on `Exception` (calendar-only, quick) or in a shared `exception_comments` table (all modules)?
6. **Permission gate.** Are calendar cases covered by the existing `exceptions:view` / `ManagerReview` permissions, or should they get a new `meetings` permission? This ties into handoff §7.2, the inbox union gate.

---

## 9. Quick code map

```
Backend (HRMS-Backend-v1/src)
  ONEVO.Application/Features/Calendar/Services/CalendarNotificationSender.cs        all calendar notifications
  ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/                  🐞 dropped notification
  ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/              🐞 dropped (EditAll) + participants on split (verify)
  ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/            🐞 dropped notification
  ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/               RSVP; correct enqueue-before-save pattern
  ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/                   Zoom/Teams link + invite email
  ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/                overlap logic to reuse
  ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarEventInviteEmailOutboxHandler.cs
  ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxWriter.cs                enqueue only, no save
  ONEVO.Infrastructure/Persistence/UnitOfWork.cs                                     transaction commit only
  ONEVO.Infrastructure/Services/Calendar/{Teams,Zoom}AttendanceSyncJob.cs           🐞 empty report never retried
  ONEVO.Infrastructure/Services/Calendar/CalendarConnectionTokenProvider.cs          ReauthRequired (silent)
  ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs                      Failed / Conflict (silent)
  ONEVO.Infrastructure/ExternalServices/Calendar/{Zoom,MicrosoftGraph}MeetingClient.cs   no attendees sent
  ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs             calendar_event_* templates
  ONEVO.Application/Features/Monitoring/Meetings/Services/MeetingApplicationComplianceCalculator.cs  agent fallback
  ONEVO.Domain/Features/Monitoring/Exceptions/Entities/Exception.cs                  case entity (types by name)
  ONEVO.Application/Features/Monitoring/Exceptions/Services/ExceptionAlertRouter.cs  manager → HR routing (needs per-type policy)
  ONEVO.Application/Features/Monitoring/Exceptions/Services/ExceptionScopeResolver.cs  inbox scope
  ONEVO.Application/Features/Monitoring/Exceptions/Queries/GetExceptionEvidence/     add meeting evidence branch
  ONEVO.Infrastructure/Services/Monitoring/Exceptions/ExceptionDetectionJob.cs       nightly detection + 3-day sweep
  ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Exceptions/EfExceptionRepository.cs  🐞 count ignores status
  ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeSignalCatalogue.cs      add meeting_exceptions signal
  ONEVO.Application/Features/Dashboard/Team/Abstractions/ITeamActionSource.cs        inbox / My Team source pattern
  ONEVO.Application/Features/Dashboard/Team/Sources/MonitoringExceptionTeamActionSource.cs

Frontend (Hrms--Web-application---front-end---v1/src/app)
  modules/calendar/feature/calendar-page/calendar-page.component.ts                  create → createMeeting sequence
  modules/calendar/ui/calendar-event-form-modal/                                     participants, guests, provider
  modules/attendance/models/exception-alert.model.ts                                 type labels
  modules/attendance/feature/exception-alerts/exception-evidence-panel/              evidence per type
  layouts/main-layout/nav/nav-items.config.ts                                        "Investigation" sidebar (handoff)
```

Related: `INVESTIGATION-INBOX-HANDOFF.md` (inbox scope, resolution model, open decisions).
