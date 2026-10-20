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
| 400 | Validation (`meeting_organizer_cannot_be_removed`, `meeting_response_invalid`, `meeting_title_invalid`, `meeting_platform_invalid`, `meeting_guest_email_invalid`, `meeting_invalid_time`, `meeting_start_in_past`, `meeting_invalid_timezone`, `meeting_recurrence_invalid`, `meeting_module_unavailable`, `meeting_scope_invalid`, `meeting_same_module`) |
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

**Action items.** An action item is an ordinary task raised from a meeting occurrence (spec 5.4). It is created with the ordinary task route:

```
POST /api/v1/work/objectives/{objectiveId}/tasks
{ "title": "...", "categoryId": "...", "priority": "medium", "sourceMeetingOccurrenceId": "<occurrence task id>" }
```

`sourceMeetingOccurrenceId` is optional and null for every ordinary task. When it is set, the occurrence must be one the caller can see and that still exists - otherwise the create is 404 `meeting_not_found` and nothing is written. The creator picks a module they are a member of (the default is the meeting's module); the module's existing create rules apply. The task and its link are written in one transaction; when the create goes through approval instead, the link is written when the approval is applied.

**Approvals.** A `requested` meeting appears in the existing approval inbox as action `meeting.create` (or `meeting.move`):
- It is decided with the existing approval endpoints.
- The payload cannot be edited (`canEditPayload: false`), and a revert returns 409.
- The approver sees only the snapshot below.

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
  "projectId": "…", "objectiveId": "…", "myRole": "organizer", "myResponse": "accepted" }

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
- **Approval snapshot** (D5, `meeting.create` payload `snapshot`):
  `{ title, description, start, durationMinutes, timezone, recurrenceSummary, organizerName, modulePath, participantNames[], guestEmails[], platform }`.
