# Meeting availability warnings

**Status:** Draft 2026-10-28, builder check done, awaiting the user's approval.
**Builds on:** `2026-10-27-planner-calendar-merge-design.md` (working-hours windows), the meetings-as-tasks Phase 2 spec (`2026-10-18-meeting-task-phase2-design.md`), and the existing calendar conflict check (`POST /api/v1/calendar/check-conflicts`).
**Repos:** backend (this repo) and the Angular tenant app (`front end-org/Hrms--Web-application---front-end---v1`).
**Branch:** `feature/meeting-availability-warnings` in both repos, cut from `feature/planner-calendar-merge`.

## 1. Intent

When someone books a meeting, they should see, before they press Create, who cannot make it and why:

- the person already has a meeting or event at that time (this check exists, but only the "New event" form uses it);
- the person is on approved leave;
- it is a public holiday for that person;
- the time falls outside that person's working hours, or on their non-working day.

This came out of the question "do we need an availability engine?" (2026-10-27/28 conversation). The answer was no: extend the existing conflict check instead.

**Success:** an organiser picks a time in the meeting form, sees a short warning list ("Kavisna is on leave", "Thamsan: outside working hours 09:00–17:00"), changes the time if they want, and creates the meeting. Nobody is ever blocked.

## 2. Goals, non-goals, anti-goals

**Goals (now)**
1. Wire the conflict check into the **Meeting (work item)** form, in both create and edit.
2. Extend the check with three new warning kinds: leave, holiday, outside working hours.
3. Both forms (meeting form and New event form) show the new kinds, since they share the endpoint.

**Later (only if users ask)**
- "Suggest times": a slot finder built on the same data. Not in this change.

**Non-goals**
- Room or resource booking.
- Real-time free/busy from external Google or Outlook calendars. Synced events already in our database count, as they do today.
- Checking guests (email-only invitees). We have no data about them.
- Checking every occurrence of a recurring meeting.

**Anti-goals** (failures even if everything else works)
- A warning stops the user from saving. Every warning is advisory; Create/Save stays enabled.
- The check leaks private data: the leave type or reason, or the title of a private meeting the caller cannot see.
- The form becomes slow or jumpy while typing: the check is debounced and never blocks input.

## 3. Decisions taken in the conversation

| # | Decision | Source |
|---|---|---|
| D1 | No separate availability engine. Extend `check-conflicts`. | user (agreed in chat) |
| D2 | Wire the check into the meeting form. | user |
| D3 | Add leave, holiday and outside-working-hours checks. | user |
| D4 | Warnings only, never block (same rule as planner overlaps). | user |
| D5 | "Suggest times" is later; room booking and real-time external free/busy are out. | user |
| D6 | Build on a new branch off `feature/planner-calendar-merge`, spec first, then implement. | user |
| D7 | Leave warnings follow the existing leave visibility rules: outside the caller's scope, a leave shows as "Busy". | user (builder check) |

## 4. Behaviour

### 4.1 What gets checked
- **Who:** every selected participant employee, including people added through bulk invite, **and the organiser**. The **meeting form** adds the organiser's own employee id to the ids it sends; the server does not add the caller, so the New event form's behaviour stays as it is (C5). Guests are skipped.
- **When:** the meeting's start–end range. For an all-day meeting, the whole day in the meeting's timezone.
- **Recurring meetings:** only the first occurrence (the one the form's date and time describe). The form says "Checked for the first occurrence".

### 4.2 Warning kinds
Each warning is one row: person, kind, a short label, and the overlapping time range.

| Kind | Fires when | Label shown | Privacy |
|---|---|---|---|
| `event` | Existing behaviour: an event, recurring event or task meeting of that person overlaps. | The event title, or "Busy" when the caller cannot see it. | Unchanged from today. |
| `leave` | The person has an **approved** leave request whose **exact time range** (`StartAt`–`EndAt`) overlaps the meeting, so a morning half-day does not warn on an afternoon meeting (C7). Pending, rejected and cancelled leave do not count. A partial cancellation ends the leave the day before its effective date (the same as the leave calendar). | "On leave", or "Busy" when the caller may not see that person's leave (D7). | Leave type, reason and hours are never returned. The caller's leave visibility (`ILeaveVisibilityScopeProvider`, the same scope as the leave calendar) decides "On leave" vs "Busy". The caller always sees their own leave. |
| `holiday` | The meeting touches a date that is a public holiday for the person's legal entity (the same holiday provider the leave calendar uses). | The holiday name, for example "Public holiday: Deepavali". | Holidays are public. |
| `outside_hours` | Part of the meeting is outside the person's working window, or falls on their non-working day. | "Outside working hours (09:00–17:00)" or "Non-working day". | Working hours are not sensitive. |

- **Working hours source:** the person's legal entity schedule, read with the same rules as `PlannerWorkWindows.For`. The break does not count as outside hours.
- **Timezone:** the working window and holiday dates are read in **the participant's own effective timezone** (`ICalendarTimezoneResolver.ResolveForUserAsync` for the participant's user). So a 09:00–17:00 schedule means 09:00–17:00 where that person is. This matches planner decision C1.
- **No schedule configured** (or an overnight window): no `outside_hours` warning for that person.
- **All-day meetings** skip `outside_hours`. They still get `leave`, `holiday` and `event` warnings.
- **No legal entity** on the employee: no holiday and no outside-hours check for that person.
- A meeting can produce several warnings for one person. Duplicate holiday rows for the same date are collapsed.

### 4.3 The meeting form
- The check runs, debounced, whenever the date, start, minutes, all-day flag, timezone or participant list changes, and there is at least one person to check. This covers create, and edit when the time changes.
- **Edit:** the meeting being edited must not report itself as a conflict. The request carries the meeting's **series id**, and the server skips every occurrence of that series, created or not (C8). This also stops a moved daily meeting from clashing with its own next occurrence.
- While the check runs, a small "Checking availability…" line shows. A failed check shows nothing and logs a console warning. It must not block saving or show an error toast.
- The warnings show above the buttons, grouped by person, in this order: leave, holiday, outside hours, event. With no warnings, a quiet line reads "Everyone is available".
- **Create / Save stay enabled** whatever the warnings say.

### 4.4 The New event form
It already shows conflicts from the same endpoint. It must render the new kinds with their labels instead of treating every row as an event. No other change.

## 5. Backend changes

1. **Request:** `CheckCalendarConflictsRequest` gets one optional field, `ExcludeMeetingSeriesId` (the meeting being edited). Task meetings whose `MeetingSeriesId` matches are skipped. Old callers keep working.
2. **Response:** each `CalendarConflict` gets `Kind` (`event | leave | holiday | outside_hours`), defaulting to `event`. The existing fields keep their meaning:
   - `ConflictingEventId`: the event or meeting id for `event`, `Guid.Empty` for the new kinds.
   - `ConflictingEventTitle`: the label from 4.2.
   - `OverlapStart` / `OverlapEnd`: the part of the meeting that is affected.
3. **Data reads, batched per request, not per person:**
   - Approved leave: `ILeaveRequestReadRepository.ListApprovedCoveringAsync` for all participants at once.
   - Holidays: `ILeaveCalendarHolidayProvider.ListHolidaysAsync` once, for all participants' legal entities.
   - Legal entities: one read for all participants.
   - Timezones: resolved per participant (the existing resolver).
4. **Permissions:** unchanged (`calendar:read`). The response never includes leave details beyond "On leave". For people outside the caller's leave visibility scope, an approved leave shows as `leave` kind with the label "Busy" (the kind still tells the form to group it first; the label hides the reason). The handler resolves that scope once per request.
5. `docs/api` is updated with the new field and kinds.

## 6. Frontend changes

- Models: `CalendarConflict` gains `kind`, and the request gains `excludeMeetingSeriesId`.
- Meeting form: a debounced availability check using the calendar API, a warnings block, and the "Checked for the first occurrence" note for repeating meetings.
- Event form: label rows by kind.
- One small presentational component for the warnings list, shared by both forms if it fits cleanly; otherwise each form renders its own (builder's call).

## 7. Testing

- **Backend (TDD):** unit tests for each kind, including:
  - approved vs pending leave, and partial cancellation;
  - a holiday in the participant's timezone across a date boundary;
  - outside hours at the start, at the end, and on a non-working day; break time not flagged;
  - no schedule configured, and no legal entity;
  - all-day meetings;
  - the organiser included;
  - `ExcludeMeetingSeriesId` skipping created and not-yet-created occurrences of the edited series;
  - leave of someone outside the caller's leave scope shows as "Busy"; the caller's own leave shows "On leave";
  - a half-day leave that does not overlap the meeting does not warn;
  - private meetings still "Busy".
- An integration test proves the endpoint returns the kinds and never a leave type.
- **Frontend (vitest):**
  - the meeting form calls the check on time and participant changes, debounced;
  - warnings render grouped by person;
  - Create stays enabled with warnings;
  - a failed check does not block;
  - the event form labels the new kinds.

## 8. Calls made by Claude (please check)

| # | Question | Call |
|---|---|---|
| C1 | Should pending leave warn? | No, approved only. Pending leave may be rejected, and warning on it would be noisy. |
| C2 | Show the leave type? | No, only "On leave" (or "Busy" per D7), for privacy. |
| C3 | Whose timezone defines working hours? | The participant's own, as in the planner. |
| C4 | Recurring meetings | First occurrence only. |
| C5 | Is the organiser checked, and who adds them? | Yes. The meeting form adds the organiser's employee id; the endpoint does not add the caller, so the New event form is unchanged. |
| C6 | Guests | Skipped. |
| C7 | Leave overlap: exact time or whole day? | Exact `StartAt`–`EndAt` overlap. A partial cancellation ends the leave at the start of its effective date in the participant's timezone. |
| C8 | What does an edit exclude? | The whole series (`ExcludeMeetingSeriesId`), so created and not-yet-created occurrences are both skipped. Missing a clash with another occurrence of the same series is acceptable. |
| C9 | A participant with no user account (no timezone)? | Use the meeting's timezone for that person. |
