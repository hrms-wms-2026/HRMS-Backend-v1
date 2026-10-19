# Meetings API v1 (`/api/v1/meetings`)

The frontend contract for meetings-as-tasks Phase 2. Source of truth: `src/ONEVO.Application/Features/WorkManagement/Meetings/Contracts/`. Spec: `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` §5.1–§5.5, §7.

- Auth: tenant JWT (`TenantPolicy`). JSON is camelCase. Times are ISO-8601 with an offset; the server returns UTC.
- An occurrence is addressed as `{seriesId}` + `{originalStart}` (its UTC start before any edit, e.g. `2026-11-02T09:00:00Z`). This works whether or not the occurrence is materialized yet. `GET /occurrences/{taskId}` also exists for board links.
- `link` (Teams/Zoom) and `attendance` fields are **added by Plan C**. Plan B responses do not carry them yet.

## Errors

Every error is ProblemDetails with a stable `code` extension:

```json
{ "status": 409, "title": "Conflict", "detail": "Someone else changed this meeting. Reload and try again.", "code": "meeting_changed" }
```

| Status | Meaning |
|---|---|
| 400 | Validation (`meeting_invalid_time`, `meeting_start_in_past`, `meeting_invalid_timezone`, `meeting_recurrence_invalid`, `meeting_module_unavailable`, `meeting_scope_invalid`, `meeting_same_module`) |
| 403 | You can see the meeting but may not do this (`meeting_module_member_required`; an attendee trying an organizer action) |
| 404 | `meeting_not_found`: the meeting does not exist **or you may not see it**. The body is identical either way. |
| 409 | State or concurrency (`meeting_changed`, `meeting_occurrence_started`, `meeting_recurrence_empty`, `meeting_busy`, `meeting_nothing_to_move`, `meeting_office_not_ready`, `meeting_provider_not_connected`, `meetings_read_only`, `approval_already_decided`, `meeting_approval_not_editable`, `bulk_invite_unavailable`) |
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
```

## Rules the UI relies on

- **Status** is derived: `scheduled` → `in_progress` (from `startAt`) → `completed` (from `endAt`), or `cancelled`. Meeting cards are not status columns and cannot be dragged.
- **`canEdit`** is true only for the organizer before the occurrence ends. After it ends, the organizer may still edit `description` (minutes, D19) with `scope: "this"`; any other field returns `meeting_occurrence_started`.
- **Declining** keeps access (D18). Only removal revokes it.
- **Board, Work Tree and My Work** list `MeetingListItem`s for past, current and the next 7 days. Further-out occurrences exist only on the calendar (Plan C merges them into `GET /calendar`).
- **Approval snapshot** (D5, `meeting.create` payload `snapshot`):
  `{ title, description, start, durationMinutes, timezone, recurrenceSummary, organizerName, modulePath, participantNames[], guestEmails[], platform }`.
