# Meetings API v1 (`/api/v1/meetings`)

The frontend contract for meetings-as-tasks Phase 2. Source of truth: `src/ONEVO.Application/Features/WorkManagement/Meetings/Contracts/`. Spec: `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` §5.1–§5.5, §7.

- Auth: tenant JWT (`TenantPolicy`). JSON is camelCase. Times are ISO-8601 with an offset; the server returns UTC.
- An occurrence is addressed as `{seriesId}` + `{originalStart}` (its UTC start before any edit, e.g. `2026-11-02T09:00:00Z`). This works whether or not the occurrence is materialized yet. `GET /occurrences/{taskId}` also exists for board links.
- Plan C adds the Teams/Zoom link (`…/link`) and per-occurrence attendance (`…/attendance`) as their own endpoints, and
  task meetings on `GET /api/v1/calendar` and the conflict check (see **Calendar** below). Occurrence and series
  responses are unchanged.

## Errors

Every error is ProblemDetails with a stable `code` extension:

```json
{ "status": 409, "title": "Conflict", "detail": "Someone else changed this meeting. Reload and try again.", "code": "meeting_changed" }
```

| Status | Meaning |
|---|---|
| 400 | Validation (`meeting_organizer_cannot_be_removed`, `meeting_response_invalid`, `meeting_title_invalid`, `meeting_location_invalid` (a location over 500 characters), `meeting_platform_invalid`, `meeting_guest_email_invalid`, `meeting_invalid_time`, `meeting_start_in_past`, `meeting_invalid_timezone`, `meeting_recurrence_invalid`, `meeting_module_unavailable`, `meeting_scope_invalid`, `meeting_same_module`) |
| 403 | You can see the meeting but may not do this (`meeting_organizer_required`, `meeting_module_member_required`, `meeting_calendar_write_required` for an Office meeting; an attendee trying an organizer action) |
| 404 | `meeting_not_found`: the meeting does not exist **or you may not see it**. The body is identical either way. |
| 409 | State or concurrency (`meeting_changed`, `meeting_occurrence_started`, `meeting_recurrence_empty`, `meeting_busy`, `meeting_nothing_to_move`, `meeting_office_not_ready`, `meeting_provider_not_connected`, `meetings_read_only`, `meeting_cancelled`, `meeting_link_exists`, `meeting_link_in_progress`, `meeting_link_status_unknown`, `meeting_link_unresolved`, `meeting_link_not_retryable`, `meeting_link_not_unresolved`, `attendance_recheck_expired`, `attendance_check_in_progress`, `attendance_not_applicable`, `approval_already_decided`, `meeting_approval_not_editable`, `bulk_invite_unavailable`) |
| 503 | `meetings_disabled`: the feature flag is off |

## Endpoints

| Method + route | Body → response | Who | Error codes |
|---|---|---|---|
| `POST /` | `CreateMeetingRequest` → 201 `MeetingCreatedResponse` | Project: module owner or ancestor owner creates; other module members get `requested`; non-members 403. Office (`projectId: null`): `calendar:write` | 400 create codes, 403, 409 `meeting_office_not_ready` / `meeting_provider_not_connected` / `meetings_read_only` / `meeting_busy`, 503 |
| `GET /?from=&to=&projectId=` | → `MeetingListItem[]` | Anyone; only meetings you organize or attend | 400 |
| `GET /occurrences/{taskId}` | → `MeetingOccurrenceResponse` | Attendee | 404 |
| `GET /series/{seriesId}/occurrences/{originalStart}` | → `MeetingOccurrenceResponse` (a not-yet-materialized occurrence has its future task id) | Attendee | 404 |
| `GET /series/{seriesId}` | → `MeetingSeriesResponse` | Attendee | 404 |
| `PATCH /series/{seriesId}/occurrences/{originalStart}` | `EditMeetingRequest` → `MeetingEditResponse` | Organizer | 400, 403, 404, 409 `meeting_changed` / `meeting_occurrence_started` / `meeting_recurrence_empty` / `meeting_busy` |
| `POST /series/{seriesId}/occurrences/{originalStart}/cancel` | `CancelMeetingRequest` → 204 | Organizer | 403, 404, 409 `meeting_occurrence_started` |
| `POST /series/{seriesId}/occurrences/{originalStart}/move` | `MoveMeetingRequest` → `MeetingMoveResponse` | Organizer + the destination module's create rule | 400 `meeting_same_module`, 403, 404, 409 `meeting_nothing_to_move` |
| `POST /series/{seriesId}/attendees` | `MeetingAttendeesRequest` → `MeetingAttendeesResponse` | Organizer | 403, 404 |
| `DELETE /series/{seriesId}/attendees/employees/{employeeId}` | → 204 | Organizer | 400 (organizer), 403, 404 |
| `DELETE /series/{seriesId}/attendees/guests/{email}` | → 204 | Organizer | 403, 404 |
| `POST /series/{seriesId}/occurrences/{originalStart}/attendees` | `MeetingAttendeesRequest` → `MeetingAttendeesResponse` | Organizer | 403, 404, 409 `meeting_occurrence_started` |
| `DELETE /series/{seriesId}/occurrences/{originalStart}/attendees/employees/{employeeId}` · `…/guests/{email}` | → 204 | Organizer | 403, 404 |
| `POST /series/{seriesId}/respond` | `RespondToMeetingRequest` → 204 | Self (an attendee) | 400, 404 |
| `GET /series/{seriesId}/link` | → `MeetingSeriesLinkResponse` (attendees: `joinUrl` only; organizer: also `link`) | Attendee | 404 |
| `POST /series/{seriesId}/link` | `MeetingLinkRequest` → `MeetingLinkResponse` (`provisioning`) | Organizer | 400 `meeting_platform_invalid`, 403, 404, 409 `meeting_link_exists` (send `regenerate: true` to replace it) / `meeting_link_in_progress` / `meeting_link_status_unknown` / `meeting_link_unresolved` / `meeting_provider_not_connected` / `meeting_cancelled` / `meeting_busy` / `meetings_read_only` |
| `POST /series/{seriesId}/link/retry` | → `MeetingLinkResponse` | Organizer; only from `provisioning_unknown` or `failed`, same generation | 403, 404, 409 `meeting_link_not_retryable` / `meeting_link_in_progress` / `meeting_link_unresolved` / `meeting_provider_not_connected` / `meeting_cancelled` / `meetings_read_only` |
| `DELETE /series/{seriesId}/link` | → 200 `MeetingLinkResponse`, or 204 when there was no link | Organizer | 403, 404, 409 `meeting_link_in_progress` / `meeting_link_status_unknown` / `meeting_link_unresolved` / `meeting_busy` / `meetings_read_only` |
| `GET /series/{seriesId}/occurrences/{originalStart}/attendance` | → `MeetingAttendanceResponse` (organizer: every attendee + `unmatched`; attendee: own row only, `unmatched: null`) | Attendee | 404 |
| `POST /series/{seriesId}/occurrences/{originalStart}/attendance/recheck` | → `MeetingAttendanceStatusResponse` | Organizer; up to 7 days after the occurrence ends | 403, 404, 409 `attendance_recheck_expired` / `attendance_check_in_progress` / `attendance_not_applicable` / `meetings_read_only` |
| `POST /series/{seriesId}/link/cleanup-confirmed` | → 204 (`unresolved` → `cancelled`) | Organizer | 403, 404, 409 `meeting_link_not_unresolved` / `meetings_read_only` |
| `GET /occurrences/{taskId}/action-items` | → `MeetingActionItemView[]` (the linked tasks the caller may see under the ordinary task rules) | Attendee | 404 `meeting_not_found` |
| `DELETE /occurrences/{taskId}/action-items/{actionTaskId}` | → 204 (the link is removed; the task itself stays) | The link's creator or the organizer | 403 `meeting_organizer_required`, 404 `meeting_not_found`, 409 `meetings_read_only` |
| `POST /bulk-preview` | `MeetingBulkPreviewRequest` → `MeetingBulkPreviewResponse` | The meeting's own gate: a module member for a new meeting, the organizer for an existing series | 400 `meeting_platform_invalid` / `meeting_bulk_selection_too_large` (a list carries more than 1000 ids), 403 `meeting_module_member_required` / `meeting_organizer_required` / `meeting_calendar_write_required`, 404 `meeting_not_found`, 409 `bulk_invite_unavailable` (the `Meetings:BulkInvite` flag is off) |
| `GET /api/v1/employees/{employeeId}/meeting-attendance?from=&to=` | → `EmployeeMeetingAttendanceResponse` | The employee themself, or a manager in their **current** reporting line. Nobody else. | 400 `meeting_invalid_time` (a window longer than 92 days), 404 `meeting_not_found` |

**Reporting-line attendance view.** The employee's own meeting attendance, shown on the employee profile
(spec 5.1, D16, D17). It lives on its own route, outside the meetings prefix:

```
GET /api/v1/employees/{employeeId}/meeting-attendance?from=&to=
```

- The window defaults to the last 30 days and may not be longer than 92 days; a longer window is
  400 `meeting_invalid_time`.
- Who may see it: the employee themself, or a manager in their **current** reporting line (any level up).
  Nobody else - not HR, not the project owner, not a manager the employee has left. Anybody else gets
  404 `meeting_not_found`, the same body as an employee who does not exist.
- Rows are the employee's own materialized, non-cancelled occurrences where they are an effective attendee,
  that have ended, inside the window, newest first. Legacy calendar events are not included.
- A private occurrence is masked for a manager caller: its title becomes `"Private meeting"` and the organizer
  is not named. The employee themself sees the real title.
- `outcome` is `joined` / `no_join_recorded` / `unknown` - the API never says "absent". `attendedSeconds` is
  null unless the employee joined.

**Action items.** An action item is an ordinary task raised from a meeting occurrence (spec 5.4). It is created with the ordinary task route:

```
POST /api/v1/work/objectives/{objectiveId}/tasks
{ "title": "...", "categoryId": "...", "priority": "medium", "sourceMeetingOccurrenceId": "<occurrence task id>" }
```

`sourceMeetingOccurrenceId` is optional and null for every ordinary task. When it is set, the occurrence must be one the caller can see and that still exists - otherwise the create is 404 `meeting_not_found` and nothing is written. The creator picks a module they are a member of (the default is the meeting's module); the module's existing create rules apply. The task and its link are written in one transaction; when the create goes through approval instead, the link is written when the approval is applied.

**Bulk invite preview.** The organizer picks whole departments, managers' teams and individual people, and the
preview expands them into one list of people before anything is invited (spec 5.2, D8). It is a preview only -
it writes nothing.

- **The preview is a snapshot.** It shows the people as they are at the moment of the preview. The form then
  submits the **explicit employee ids the preview showed** (`employeeIds` on the create request), never the
  groups: a group would be re-expanded later and could resolve to a different set of people.
- **The create re-validates the selected employees.** Anyone who stopped being active between the preview and
  the save is **skipped**, not invited: the create answers `skipped: [ { "employeeId": "…", "reason": "inactive" } ]`
  and invites everybody else. The preview is a snapshot, not a reservation.
- A department expands to its **own** employees; its sub-departments are not expanded.
- A manager expands to their **direct reports** only.
- "Active" is the same as everywhere else in Meetings: working, or on leave. Anyone else - and anyone with no
  employee record - is counted in `inactiveExcluded`.
- `alreadyInvited` counts the people already on the meeting (the organizer, and the series' participants when
  `seriesId` is set). They are not returned in `people`, so the frontend never adds them twice.
- `selected` is every id the organizer picked, before dedup; `duplicatesRemoved` is the repeats.
- Above the provider's capacity the response carries a **warning only** - there is no cap, and the organizer may
  go ahead. The limits are configured under `Meetings:ProviderCapacityWarning`, keyed by provider:
  `MicrosoftTeams` (default **1000**) and `Zoom` (default **300**). The warning is raised when the meeting's
  size - the previewed people plus the people already invited - is above the limit for the requested `platform`.

```jsonc
// MeetingBulkPreviewRequest
{ "projectId": "…|null", "objectiveId": "…|null", "departmentIds": ["…"], "managerEmployeeIds": ["…"],
  "employeeIds": ["…"], "seriesId": "…|null", "platform": "zoom" }
// → MeetingBulkPreviewResponse
{ "people": [ { "employeeId": "…", "name": "Arjun M.", "email": "arjun@example.com", "departmentName": "Engineering" } ],
  "selected": 320, "duplicatesRemoved": 4, "inactiveExcluded": 12, "alreadyInvited": 1,
  "capacityWarning": "This meeting will have 304 people; Zoom meetings support up to 300." }
```

**Approvals.** A `requested` meeting appears in the existing approval inbox as action `meeting.create` (or `meeting.move`):
- It is decided with the existing approval endpoints.
- The payload cannot be edited (`canEditPayload: false`), and a revert returns 409.
- The approver sees only the snapshot below, as the inbox item detail's `extraJson`: `{"snapshot":{…}}` (also
  `extraJson` on `GET /api/v1/work/approvals/{id}`), for `meeting.create` and `meeting.move`. The requester gets
  `extraJson: null`; nobody gets the payload (`requestedPayloadJson`/`appliedPayloadJson` are null for meeting
  actions, and the approvals list / approve / reject / cancel rows carry `payloadJson: "{}"`). A `meeting.move` submitted before snapshots existed has `extraJson: null`.

**Monitoring compliance.** The Work & Activity "Application usage" card of an employee profile
(`GET /api/v1/employees/{id}/work-activity/app-usage`) carries a `meetingCompliance` array: every meeting of
**that employee** in the period - the legacy calendar events and the task-meeting occurrences - with the share of
the meeting the meeting application was used for (spec D17).

- `eventId` is the legacy calendar event's id, or the occurrence's task id for a task meeting.
- `title` is the meeting's title, or **`"Private meeting"`** when it is private. A private meeting is listed,
  never named: its title, organizer, agenda, participants and link are hidden.
- `isPrivate` is `true` for a private meeting. The usage numbers of a private meeting are **unchanged** - only
  the title is masked.
- `startsAt` / `endsAt` are the meeting's bounds, `requiredSeconds` the seconds a compliant meeting needs
  (half the meeting), `meetingApplicationSeconds` the seconds the meeting application was actually used for,
  `usagePercentage` the share in percent (0..100) and `isCompliant` whether the used seconds reach the required
  ones.
- A meeting is only measured when it has a provider link (`calendar_event_meetings.status = 'active'`); a meeting
  without one has no meeting application to measure, so it is not listed.
- A **declined** or **removed** attendee has no row: a declined occurrence had no meeting application to use, so
  measuring it would report a false 0%.
- The view is the employee's own: it never shows another employee's meetings, and it is behind the employee
  visibility rule (403 for an employee the caller may not view).

## Bodies

```jsonc
// CreateMeetingRequest
{ "projectId": "…|null", "objectiveId": "…|null", "title": "Weekly delivery sync", "description": "Agenda…",
  "location": null, "isPrivate": false, "start": "2026-11-02T09:00:00+05:30", "durationMinutes": 30,
  "isAllDay": false, "timezone": "Asia/Colombo", "recurrenceRule": "FREQ=WEEKLY;BYDAY=MO",
  "participantEmployeeIds": ["…"], "guestEmails": ["client@example.com"], "platform": "zoom" }

// MeetingCreatedResponse
{ "outcome": "created", "seriesId": "…", "firstOccurrenceTaskId": "…", "approvalRequestId": null,
  "skipped": [ { "employeeId": "…", "reason": "inactive" } ] }

// MeetingOccurrenceResponse
{ "taskId": "…", "shortId": "ACME-M12", "seriesId": "…", "originalStart": "2026-11-02T03:30:00Z",
  "startAt": "2026-11-02T03:30:00Z", "endAt": "2026-11-02T04:00:00Z", "isAllDay": false, "timezone": "Asia/Colombo",
  "title": "Weekly delivery sync", "description": "Agenda…", "location": null, "isPrivate": false,
  "status": "scheduled", "isDetached": false, "revision": 0, "seriesRevision": 0, "recurrenceRule": "FREQ=WEEKLY;BYDAY=MO",
  "placement": { "projectId": "…", "projectName": "Acme Portal", "objectiveId": "…", "moduleTitle": "Delivery", "isOffice": false },
  "organizer": { "employeeId": "…", "name": "Priya K." }, "myRole": "attendee", "myResponse": "pending",
  "attendees": [ { "kind": "employee", "employeeId": "…", "name": "Arjun M.", "email": null, "response": "accepted",
                   "responseReason": null, "source": "series" },
                 { "kind": "guest", "employeeId": null, "name": null, "email": "client@example.com", "response": null,
                   "responseReason": null, "source": "series" } ],
  "canEdit": false }

// MeetingListItem: one per visible occurrence (board, Work Tree, My Work "Today's meetings")
{ "taskId": "…", "shortId": "ACME-M12", "seriesId": "…", "originalStart": "…", "startAt": "…", "endAt": "…",
  "isAllDay": false, "title": "Weekly delivery sync", "status": "in_progress", "isPrivate": false,
  "projectId": "…", "objectiveId": "…", "myRole": "organizer", "myResponse": "accepted",
  "organizerEmployeeId": "…" }

// MeetingSeriesResponse = series template + placement + organizer + myRole + attendees + upcomingOccurrences (MeetingListItem[])

// EditMeetingRequest: only the fields you change; revision = occurrence revision for "this", series revision otherwise
{ "scope": "this|following|all", "revision": 3, "title": "…", "description": "…", "location": "…",
  "isPrivate": false, "start": "…", "durationMinutes": 45, "isAllDay": false, "timezone": "…", "recurrenceRule": "…" }
// → MeetingEditResponse { "seriesId": "…", "occurrenceTaskId": "…", "split": true }   (split: a new series was created from here on)

// CancelMeetingRequest { "scope": "this|following|all" }
// MoveMeetingRequest   { "scope": "this|following", "projectId": "…", "objectiveId": "…" } → { "outcome": "moved|requested", "seriesId": "…", "approvalRequestId": null }
// MeetingAttendeesRequest { "employeeIds": ["…"], "guestEmails": ["…"] } → { "added": 2, "skipped": [] }
// RespondToMeetingRequest { "status": "accepted|rejected", "reason": "…", "originalStart": null }

// MeetingLinkRequest { "platform": "microsoft_teams|zoom", "regenerate": false }
// MeetingLinkResponse (organizer only)
{ "linkId": "…", "provider": "microsoft_teams", "status": "provisioning|active|provisioning_unknown|failed|unresolved|cancelled|not_connected",
  "joinUrl": "https://…|null", "organizerJoinUrl": "https://…|null", "failureReason": null, "generation": 1, "releasePending": false }
// MeetingAttendanceResponse
{ "status": "synced", "display": "Unknown|Recorded|Not applicable",
  "rows": [ { "kind": "employee|guest", "employeeId": "…|null", "email": "…|null", "displayName": "Arjun M.",
              "attendedSeconds": 900, "outcome": "joined|no_join_recorded|unknown" } ],
  "unmatched": [ { "start": "…", "end": "…|null" } ] }   // organizer only; null for attendees
// "no_join_recorded" only once synced; the API never says "absent".
// MeetingAttendanceStatusResponse { "status": "pending|provisional|synced|attendance_unavailable|sync_failed|not_applicable",
//   "nextCheckAt": "…|null", "finalizedAt": "…|null", "lastError": "provider_error|missing_scope|not_connected|reauth_required|null" }
// MeetingSeriesLinkResponse: attendee { "joinUrl": "https://…|null" }; organizer { "joinUrl": "…", "link": MeetingLinkResponse|null }

// EmployeeMeetingAttendanceResponse (GET /api/v1/employees/{employeeId}/meeting-attendance)
{ "items": [ { "occurrenceTaskId": "…", "startAt": "2026-11-02T03:30:00Z", "endAt": "2026-11-02T04:00:00Z",
               "title": "Weekly delivery sync", "organizerName": "Priya K.|null",
               "status": "pending|provisional|synced|attendance_unavailable|sync_failed|not_applicable",
               "display": "Unknown|Recorded|Not applicable",
               "outcome": "joined|no_join_recorded|unknown", "attendedSeconds": 900 } ] }
// "title": "Private meeting" and "organizerName": null for a private occurrence seen by a manager.

// MeetingApplicationComplianceItem (GET /api/v1/employees/{id}/work-activity/app-usage → meetingCompliance)
{ "eventId": "…", "title": "Weekly delivery sync|Private meeting", "startsAt": "2026-11-02T03:30:00Z",
  "endsAt": "2026-11-02T04:00:00Z", "requiredSeconds": 900, "meetingApplicationSeconds": 900,
  "usagePercentage": 100.0, "isCompliant": true, "isPrivate": false }
// "title" is "Private meeting" and "isPrivate" is true for a private meeting; the numbers never change.
```

## Calendar (`GET /api/v1/calendar`, `POST /api/v1/calendar/check-conflicts`)

- Task meetings appear next to calendar events as `CalendarEventItem`s with `itemKind: "meeting"`, `meetingSeriesId`,
  `meetingTaskId` (null for an occurrence that is not materialized yet) and `meetingStatus`. `id` is the occurrence's task
  id - for a virtual occurrence, the id it will get when it is materialized - and `originalStart` its key. Cancelled
  occurrences, cancelled series and moved-away slots are not listed. Open a meeting item with
  `GET /meetings/series/{meetingSeriesId}/occurrences/{originalStart}`.
- Only meetings you organize or attend are listed; `hasConflict` counts them.
- The conflict check includes the participants' task meetings. A meeting you cannot see comes back as
  `{ "conflictingEventId": "00000000-…", "conflictingEventTitle": "Busy", "isBusyOnly": true }` with its time only.

## Hours (2026-10-26, Part A)

- When an occurrence has ended, the hourly meeting job (`Meetings:Background`) stamps its task once with
  **person-hours = duration × (organizer + every internal attendee who did not decline)**, each for the full duration.
  Guests never count; a cancelled occurrence is stamped with 0. An all-day occurrence counts 8 h per person per day.
- The hours are the task's `estimatedHours` and `completedHours`, and roll up to its module, ancestor modules and project
  (`completedHours`). The module hour summary (Team Progress) counts them in `completedHours` only; meetings stay out of
  task counts.
- Later RSVP or attendee changes do not change a stamped occurrence.

## Rules the UI relies on

- **Status** is derived: `scheduled` → `in_progress` (from `startAt`) → `completed` (from `endAt`), or `cancelled`. Meeting cards are not status columns and cannot be dragged.
- **`canEdit`** is true only for the organizer before the occurrence ends. After it ends, the organizer may still edit `description` (minutes, D19) with `scope: "this"`; any other field returns `meeting_occurrence_started`.
- **Declining** keeps access (D18). Only removal revokes it.
- **Board, Work Tree and My Work** list `MeetingListItem`s for past, current and the next 7 days. Further-out occurrences exist only on the calendar (Plan C merges them into `GET /calendar`).
- **Links** (spec 5.7) belong to a series. After a split both series share one link. Removing or regenerating a link, or
  cancelling a series, affects only that series: the provider meeting is cancelled only when no series uses the link any
  more and no attendance check still needs it. Until then a removal returns `releasePending: true`. Create returns the link
  as `provisioning`; poll `GET …/link` until it is `active` (the link email to attendees is sent then). `provisioning_unknown`
  → offer Retry; `unresolved` → ask the organizer to delete the meeting in Teams/Zoom, then `cleanup-confirmed`.
- **Approval snapshot** (D5, `meeting.create` / `meeting.move` payload `snapshot`, frozen at submit):
  `{ title, description, start, durationMinutes, timezone, recurrenceSummary, organizerName, modulePath, participantNames[], guestEmails[], platform }`.
