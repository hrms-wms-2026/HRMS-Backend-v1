# Meetings as Tasks Phase 2 — Plan E: Frontend

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give organizers and attendees the Phase 2 meeting UI on top of the B–D APIs:
- create and edit meetings as work items;
- see them on the board, Work Tree, My Work and the calendar;
- manage the Teams/Zoom link, attendance and action items;
- decide meeting approvals;
- managers get the reporting-line attendance tab.

**Architecture:**
- One `meetings` data layer (models + `MeetingApiService`) mirrors `docs/api/meetings-v1.md`, which is the contract.
- UI pieces are standalone Angular components in a new `src/app/modules/work/meetings/` folder, used by the board, the tree, the dashboard, the calendar and the approvals page.
- An occurrence is always addressed by `{seriesId, originalStart}`. That works for materialized and virtual occurrences, so the calendar never needs a task id first.

**Tech Stack:** Angular 21 standalone components + signals, the existing `@ngrx/signals` stores pattern, HttpClient, Vitest (`ng test`), Tailwind as already used in `modules/work`.

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md` (backend repo), §5.1–§5.10, D5, D8, D14, D16–D19. **Contract:** `docs/api/meetings-v1.md` (backend repo, Plans B–D).

**Repo:** `C:\Users\User\OneDrive\Desktop\onexso\front end-org\Hrms--Web-application---front-end---v1`.
**Base:** `feature/meeting-task-phase0-1` (Phase 0 calendar-meeting UI, worktree `C:\Users\User\onexso-worktrees\fe-meeting-task`).
**New:** branch `feature/meeting-task-phase2-e`, worktree `C:\Users\User\onexso-worktrees\fe-phase2-e`.

## Global Constraints

- **Contract.** Types and routes come from `docs/api/meetings-v1.md` only. The JSON is camelCase. Times are sent with an offset and come back as UTC; display them in the user's zone.
- **Errors.** Every failure is ProblemDetails with `code`. Map codes to copy in one place (`meeting-error-messages.ts`). 404 `meeting_not_found` always reads "This meeting isn't available" — never "you don't have access".
- **Wording (exact):**
  - attendance shows "Unknown" (pending, unavailable, failed) and "No join recorded" (only when synced) — **never "Absent"**;
  - a private meeting shown to a manager or in a busy-only conflict reads "Private meeting" / "Busy";
  - the approver sees only the snapshot (D5).
- **Behaviour:**
  - Meeting cards are not status columns and cannot be dragged (§5.9).
  - The board, the Work Tree and My Work show past, current and the next 7 days. Further-out occurrences are calendar-only.
  - After an occurrence ends, the organizer may still edit only its notes (`description`, scope `this`) (D19).
  - Declining keeps access (D18).
- **Bulk invite (D8).**
  - The groups are a department, or a manager's direct reports. There is no "everyone" option.
  - The preview is a snapshot. The user can remove people, and the form submits explicit employee ids.
  - Capacity is a warning, never a block.
  - `bulk_invite_unavailable` hides the bulk panel.
- **Flags** are server-side. `meetings_disabled` (503) hides the "New meeting" entry points. `meetings_read_only` shows writes as disabled, with "Meetings are read-only right now".
- **No dummy data.** Every screen reads the real API. Specs use `HttpTestingController` or service stubs.

## Review Focus

1. **A virtual occurrence opened from the calendar** — no task id yet — must load by `{seriesId, originalStart}`, and an edit with scope `this` materializes it. Owned by Task 5: `opens a virtual occurrence by series and original start`.
2. **Polling a link that stays `provisioning`** must stop once a terminal state comes back, or after 2 minutes. It must offer Retry on `provisioning_unknown` and the cleanup confirmation on `unresolved`. Owned by Task 3: `stops polling on a terminal status and offers the next action`.
3. **A recurring edit with scope "all" that changes time** returns `split: true` with a new `seriesId`. The UI must follow it (refresh by the new series), not keep editing the old one. Owned by Task 3: `follows the new series after a splitting edit`.
4. **An attendee without organizer rights** must never see edit, cancel, link-management or recheck controls, even when the API would 403. Owned by Task 3: `hides organizer actions for attendees`.
5. **Attendance for a synced occurrence with a person with no rows** shows "No join recorded". The same person before sync shows "Unknown". Owned by Task 6: `uses No join recorded only when synced`.

---

## File Structure

| Path (under `src/app/modules/work/meetings/`) | Responsibility |
|---|---|
| `models/meeting.models.ts` | Contract types (requests, responses, statuses, codes) |
| `data-access/meeting-api.service.ts` | Every `/api/v1/meetings` and `/employees/{id}/meeting-attendance` call |
| `utils/meeting-error-messages.ts`, `utils/recurrence-presets.ts` | Error copy; RRULE presets ↔ labels |
| `ui/meeting-form-modal/` | Create / edit (shared), incl. the bulk invite panel |
| `ui/meeting-scope-dialog/` | "This / this and following / all" |
| `feature/meeting-occurrence-drawer/` | Details, attendees, RSVP, link, attendance, action items, notes |
| `ui/meeting-link-panel/`, `ui/meeting-attendance-table/`, `ui/meeting-action-items/` | Drawer sections |
| `ui/meeting-card/` | Board / tree / My Work card (status pill, no drag) |
| `ui/meeting-approval-snapshot/` | `meeting.create` / `meeting.move` approval body |
| `modules/people/feature/employee-detail/meeting-attendance-tab/` | D16 tab |

---

### Task 1: Contract models and API service

**Files:**
- Create: `models/meeting.models.ts`, `data-access/meeting-api.service.ts` (+ `.spec.ts`), `utils/meeting-error-messages.ts` (+ spec).

**Interfaces:**
- Types named exactly as in `meetings-v1.md`, in camelCase:
  - `CreateMeetingRequest`, `MeetingCreatedResponse`, `MeetingOccurrenceResponse`, `MeetingListItem`, `MeetingSeriesResponse`;
  - `EditMeetingRequest`, `MeetingEditResponse`;
  - `MeetingLinkRequest`, `MeetingLinkResponse`, `MeetingSeriesLinkResponse`;
  - `MeetingAttendanceResponse`, `MeetingAttendanceRow`, `MeetingAttendanceStatusResponse`;
  - `MeetingActionItemView`, `MeetingBulkPreviewRequest/Response`, `EmployeeMeetingAttendanceItem`.
- `type MeetingScope = 'this' | 'following' | 'all'`.
- `MeetingApiService` methods, one per route, returning `Observable<T>`:
  - `create`, `list(from, to, projectId?)`, `getOccurrence(taskId)`, `getOccurrenceAt(seriesId, originalStart)`, `getSeries`;
  - `edit`, `cancel`, `move`;
  - `addSeriesAttendees`, `removeSeriesEmployee`, `removeSeriesGuest`, `addOccurrenceAttendees`, `removeOccurrenceEmployee`, `removeOccurrenceGuest`, `respond`;
  - `getLink`, `createLink`, `retryLink`, `removeLink`, `confirmLinkCleanup`;
  - `getAttendance`, `recheckAttendance`;
  - `listActionItems`, `unlinkActionItem`, `bulkPreview`, `employeeMeetingAttendance(employeeId, from?, to?)`.
- `originalStart` is always sent as UTC ISO (`toISOString()`), URL-encoded.
- `meetingErrorMessage(code: string | undefined): string` covers every code listed in `meetings-v1.md`, with a generic fallback.

- [ ] **Step 1: Write failing specs:** `MeetingApiService` hits the exact URL and verb for every method; `getOccurrenceAt` encodes `originalStart` as UTC; `removeLink` maps 204 to `null`; `meetingErrorMessage` returns the 404 copy for `meeting_not_found` and never mentions access.
- [ ] **Step 2:** `npx ng test --include=src/app/modules/work/meetings/**` → FAIL.
- [ ] **Step 3:** Implement, following `modules/work/data-access/task-api.service.ts` (base URL from the environment, typed `HttpClient`).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(meetings): API service and contract models`.

### Task 2: Meeting form (create and edit) with bulk invite

**Files:**
- Create: `ui/meeting-form-modal/` (+ spec), `utils/recurrence-presets.ts` (+ spec), `ui/meeting-scope-dialog/` (+ spec).
- Modify:
  - the "New" menus: project board/tree toolbar and `modules/calendar` "New event", which gets a "Meeting (work item)" choice;
  - the Phase 0 `calendar-participant-picker` (reused as is).

**Interfaces:**
- `MeetingFormModalComponent`, inputs `mode: 'create' | 'edit'`, `placement?: { projectId: string | null; objectiveId: string | null }` and `occurrence?: MeetingOccurrenceResponse`. Output `saved: MeetingCreatedResponse | MeetingEditResponse`.
- Fields:
  - placement: a project + module picker reusing `module-task-picker`'s tree, or "Office" (`projectId: null`);
  - title, agenda (`rich-text-editor`), location, private toggle;
  - start, duration (15–480 min), all-day, timezone (default: the browser IANA zone);
  - recurrence preset: none, daily, weekly on the start day, every 2 weeks, monthly on the date, or custom RRULE (validated server-side);
  - participants (picker), guest emails, platform (none / Teams / Zoom).
- `recurrencePresets.toRule(preset, start): string | null` and `fromRule(rule): Preset` (unknown → `custom`).
- Bulk panel:
  - choose departments or managers (the existing org pickers);
  - "Preview" calls `bulkPreview`;
  - show the counts line `Selected {selected} · {duplicatesRemoved} duplicates removed · {inactiveExcluded} inactive excluded · {alreadyInvited} already invited`;
  - list people with remove buttons;
  - "Add {n} people" merges the ids into participants;
  - `capacityWarning` is shown inline, and add stays enabled;
  - hide the panel on `bulk_invite_unavailable`.
- Save:
  - create → outcome `created` (toast "Meeting created", open the drawer) or `requested` (toast "Sent for approval");
  - any `skipped[]` → "N people were not invited: inactive".
- Edit: the scope dialog first for a recurring meeting. `revision` comes from the occurrence (`this`) or the series (otherwise). A 409 `meeting_changed` reloads and tells the user.

- [ ] **Step 1: Write failing specs:**
  - `builds the create request for an Office meeting`;
  - `weekly preset produces FREQ=WEEKLY;BYDAY=<start day>`;
  - `bulk preview merges only the kept people and shows the counts line`;
  - `capacity warning does not block`;
  - `requested outcome shows the approval toast`;
  - `edit asks for scope only for recurring meetings`;
  - `meeting_changed reloads instead of overwriting`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement (reactive forms; the existing modal shell used by `task-form-modal`).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(meetings): create and edit form with recurrence and bulk invite`.

### Task 3: Occurrence drawer — details, attendees, RSVP, cancel, move, link, notes

**Files:**
- Create:
  - `feature/meeting-occurrence-drawer/` (+ spec);
  - `ui/meeting-link-panel/` (+ spec);
  - a `meeting-occurrence.store.ts` signal store (+ spec).

**Interfaces:**
- `MeetingOccurrenceDrawerComponent`, input `key: { seriesId: string; originalStart: string } | { taskId: string }`.
- It loads `getOccurrenceAt` or `getOccurrence`. A 404 shows the not-available state.
- Sections:
  - header with status pill (`scheduled`, `in_progress`, `completed`, `cancelled`) and the ShortId;
  - when/where;
  - attendees with responses (series vs occurrence source);
  - My RSVP (accept or decline with reason);
  - agenda/notes;
  - link panel;
  - the attendance section (Task 6);
  - the action-items section (Task 6).
- Organizer-only controls are visible only when `myRole === 'organizer'`:
  - edit (`canEdit`);
  - cancel (scope dialog);
  - move (module picker; outcome `moved`/`requested`);
  - attendee add/remove (series or this occurrence);
  - link management;
  - after the end, a "Notes" editor only (D19).
- Link panel:
  - an attendee sees "Join" (`joinUrl`) or nothing;
  - the organizer sees the status, "Add Teams/Zoom link", and `regenerate`;
  - Retry appears on `provisioning_unknown`/`failed`;
  - Remove shows "Kept for other meetings in this series" when `releasePending`;
  - `unresolved` → "Delete it in Teams/Zoom, then confirm";
  - while `provisioning`, poll `getLink` every 3 s for at most 2 min.
- After an edit with `split: true`, the drawer reloads by `{newSeriesId, originalStart}`.

- [ ] **Step 1: Write failing specs:**
  - `opens a virtual occurrence by series and original start`;
  - `hides organizer actions for attendees` (Review Focus 4);
  - `stops polling on a terminal status and offers the next action` (Review Focus 2);
  - `follows the new series after a splitting edit` (Review Focus 3);
  - `after the end only notes are editable`;
  - `decline keeps the drawer open with read access`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(meetings): occurrence drawer with link management`.

### Task 4: Board card, Work Tree row and My Work "Today's meetings"

**Files:**
- Create: `ui/meeting-card/` (+ spec).
- Modify:
  - `feature/task-board` and `ui/task-board-column`: render `MeetingListItem`s from `list(from, to, projectId)` as `meeting-card`s, excluded from drag (`cdkDragDisabled`);
  - `ui/task-tree-row` / the milestone tree: meetings under their module with an "M" badge;
  - `modules/dashboard/feature/todays-schedule-card`: a "Today's meetings" group from `list(todayStart, todayEnd)`, and legacy calendar events marked "Calendar event".

**Interfaces:**
- `MeetingCardComponent`, input `item: MeetingListItem`, output `open: { seriesId, originalStart }`. It shows title (or "Private meeting" when `isPrivate` and `myRole` is absent — defensive), time, status pill, and the my-response chip.
- Board / tree / My Work window: from 1 day ago to 7 days ahead.

- [ ] **Step 1: Write failing specs:** `meeting cards are not draggable`; `status pill follows status`; `today's meetings lists only today's items, sorted by start`; `legacy events are labelled Calendar event`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(meetings): meetings on the board, tree and My Work`.

### Task 5: Calendar merge and conflicts

**Files:**
- Modify:
  - `modules/calendar/models/calendar-event.model.ts` (+ `itemKind`, `meetingSeriesId`, `meetingTaskId`, `meetingStatus`);
  - `state/calendar-event.store.ts`;
  - the month/week/day views (open the meeting drawer for `itemKind === 'meeting'`);
  - the event form's conflict list (`isBusyOnly` → "Busy", no link);
  - `modules/work/feature/project-calendar`.

**Interfaces:**
- A calendar item with `itemKind === 'meeting'` opens `MeetingOccurrenceDrawerComponent` with `{ seriesId: meetingSeriesId, originalStart }`. It never calls `/calendar/{id}` endpoints. Edit and cancel go through the drawer.
- Conflicts: `isBusyOnly` rows render "Busy · {time}" without a title or link.

- [ ] **Step 1: Write failing specs:**
  - `opens a virtual occurrence by series and original start` (Review Focus 1, through the calendar view);
  - `meeting items do not use legacy event actions`;
  - `busy-only conflicts show Busy and no title`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(calendar): show Phase 2 meetings and busy-only conflicts`.

### Task 6: Attendance panels, action items and the employee "Meeting attendance" tab

**Files:**
- Create:
  - `ui/meeting-attendance-table/` (+ spec);
  - `ui/meeting-action-items/` (+ spec);
  - `modules/people/feature/employee-detail/meeting-attendance-tab/` (+ spec).
- Modify: `employee-detail.component.ts` (tab visible when the API answers; a 404 hides it), and `ui/task-form-modal` (optional `sourceMeetingOccurrenceId` input, default module = the meeting's module).

**Interfaces:**
- Attendance table:
  - the organizer sees every row and the "Sessions not matched to a meeting" list (`unmatched`);
  - an attendee sees their own row;
  - columns: person, time attended (`attendedSeconds` → `h m`), outcome label;
  - Recheck appears for the organizer up to 7 days after the end; it handles `attendance_recheck_expired` and `attendance_check_in_progress`.
- Outcome labels: `joined` → "Joined", `no_join_recorded` → "No join recorded", `unknown` → "Unknown". Display labels follow `display`.
- Action items:
  - a list from `listActionItems` (title, ShortId, status, assignee);
  - "Add action item" opens `task-form-modal` with `sourceMeetingOccurrenceId`;
  - unlink for the link creator or the organizer;
  - no count is shown beyond the listed rows.
- Employee tab:
  - a 30-day default window (up to 92);
  - rows: date, title ("Private meeting" when the API masks it), organizer (hidden when null), outcome and time.

- [ ] **Step 1: Write failing specs:**
  - `uses No join recorded only when synced` (Review Focus 5);
  - `never renders the word Absent`;
  - `recheck is hidden after 7 days`;
  - `action item create passes the source occurrence`;
  - `employee tab hides itself on 404`;
  - `private rows show Private meeting and no organizer`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(meetings): attendance, action items and the employee attendance tab`.

### Task 7: Approval snapshot and full verification

**Files:**
- Create: `ui/meeting-approval-snapshot/` (+ spec).
- Modify: `modules/work/feature/work-approvals` and `ui/approval-type-icon`: action types `meeting.create` / `meeting.move` render the snapshot; payload edit is hidden (`canEditPayload: false`).

**Interfaces:**
- The snapshot (D5) shows exactly these fields: `{ title, description, start, durationMinutes, timezone, recurrenceSummary, organizerName, modulePath, participantNames[], guestEmails[], platform }`. No link, no attendance, no action items.

- [ ] **Step 1: Write failing specs:** `renders only the snapshot fields`; `no edit-payload control for meeting actions`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Full verification:
  - `npx ng test` (all specs) → green;
  - `npx ng build` → no errors;
  - `npx ng lint`, if configured → clean;
  - against a local backend (ports in the backend `PORTS.md`, `npm start` on 4200), drive create → link → attendance → action item in the in-app browser and record the result.
- [ ] **Step 6:** Commit `feat(meetings): meeting approval snapshot`. Push the branch; never open a PR.
