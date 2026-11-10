# Meeting delegates: scheduling on someone's behalf

**Date:** 2026-11-10
**Status:** Approved 2026-11-10. No code has been written.
**Builds on:** `2026-10-18-meeting-task-phase2-design.md` (meetings as tasks, D6 access rule, spec 5.7 links)

## Why

A CEO's secretary plans the CEO's meetings and sends the links. Today a meeting's organizer is whoever creates it. If the secretary creates it, the secretary is the organizer: the invite says the secretary invited you, the link comes from the secretary's Teams or Zoom account, and the CEO cannot edit or cancel the meeting.

People invited should see the meeting exactly as if the CEO had scheduled it.

## Goal

Any employee (the **principal**) can give another employee (a **delegate**) permission to schedule meetings for them. The delegate can then run the principal's meetings as if they were the principal. A CEO and their secretary are the main example, but nothing is limited to a role: a TL can give a team member the same permission.

## Non-goals

- Legacy calendar events created before meetings-as-tasks. Delegates work on meetings only.
- Doing anything else on the principal's behalf: leave or time off, planner task blocks, approvals, clock-in.
- A delegate naming their own delegates.
- Syncing delegation with Outlook or Google calendar delegation.
- Telling invitees that a delegate did it. They see the principal only.

## Decisions (agreed with the user)

| # | Decision |
|---|---|
| 1 | Participants see the principal as the organizer everywhere: in-app notices, invite emails, the ICS organizer and reminders. |
| 2 | A delegate can create, edit, reschedule, move and cancel the principal's meetings, add and remove attendees, and add or change the online link. |
| 3 | A delegate sees all of the principal's meetings, private ones included. |
| 4 | A meeting made by a delegate is the principal's own: it is on the principal's calendar and planner, and the principal gets its reminders. |
| 5 | Any employee can give the permission to any other employee, for their own meetings only. No role or admin approval is needed, and nobody can grant it for someone else. |
| 6 | Notices addressed to the organizer also go to each active delegate. |

## Model

New table `meeting_delegates`: `id`, `tenant_id`, `principal_employee_id`, `delegate_employee_id`, `created_by_user_id`, `created_at`, `revoked_at`.

- One active row per principal and delegate pair. Revoking sets `revoked_at`, and the row is kept for audit.
- A person cannot be their own delegate.
- Both people must be active employees of the tenant. When either one stops being active, the delegation stops working, and the row does not need to change.
- The table is tenant-owned and gets an RLS policy like every other tenant table (`RlsTableRegistry`).
- The pattern follows `LeaveApprovalDelegate` (`api/v1/leave/approval-delegates`).

## Who manages delegates

Every employee manages their own delegates from a "Meeting delegates" dialog on the Calendar page (decision 5). It follows the Inbox's "Delegate my approvals" dialog, because the app has no profile settings page. The API acts only on the caller's own list: there is no endpoint to grant or revoke for someone else.

## Access

`MeetingAccessResolver` is today the single rule for who may see a meeting: its organizer and its attendees (D6). It gains one case:

- A caller who is an active delegate of the series' organizer gets the **Organizer** role, with a new `ActingForEmployeeId` set to the principal.

Every check that already asks for the Organizer role (edit, cancel, move, attendees, bulk invite, link, attendance view) then works for the delegate without its own change. D6 is otherwise unchanged: no role or HR check is added.

## Creating a meeting for the principal

`CreateMeetingRequest` gains an optional `organizerEmployeeId`. Empty, or equal to the caller, means today's behaviour.

When it names someone else:

- The caller must be an active delegate of that person, or the request gets 403 `meeting_delegate_required`.
- **Placement** (project and module rules, D4) is decided for the principal, not the caller. If the principal owns the module, the meeting is created directly.
- If the principal would have to ask for approval, the request is made in the principal's name, and the decision notice goes to the principal.
- An **Office meeting** (no project) needs `calendar:write`. The draft checks the delegate's permission, because the principal's permissions are not loaded in the delegate's request (my call 2).
- The series' organizer is the principal. `CreatedById` stays the delegate's user, so the record shows who really did it.

## The online link

Today, adding a link uses the caller's own calendar connection (`SeriesMeetingLinkService`, `GetByTenantUserProviderAsync(tenantId, callerUserId, ...)`), and create uses the organizer's. Both must use the **organizer's** connection. The link then always comes from the principal's Teams or Zoom account.

- If the principal has not connected Teams or Zoom, the delegate sees "Connect your calendar account" with the principal's name in it. A delegate cannot connect an account for the principal.

## Notifications

- Invitees already see the organizer's name (`OrganizerNameAsync` reads the series organizer), so they see the principal without change.
- Notices addressed to the organizer (RSVP accepted or declined, link failed or unknown, meeting unresolved, replacement nominated) go to the principal today. They also go to each active delegate (decision 6), because the delegate is the one who acts on them.

## Seeing the principal's calendar

- `GET /calendar` and `GET /planner/me/range` gain an optional `forEmployeeId`. It is allowed only for the caller themselves or a principal they are an active delegate of, and anything else gets 403.
- For a principal, the response is exactly what the principal would see, private meetings included (decision 3).
- Leave, planner task blocks and legacy calendar events show read-only. The delegate can act only on meetings.

## Frontend

- **Calendar page, "Meeting delegates" dialog:** add a delegate (the shared people picker), see the list, and remove one.
- **Calendar page:** shown only to someone who is a delegate. A selector beside the title reads "My calendar" or "<Principal>'s calendar". Viewing a principal's calendar:
  - Everything shown is the principal's.
  - Dragging a time creates a meeting for the principal. Only the meeting tab is offered in the create popover, because time off and tasks are out of scope.
  - The meeting drawer works as it does for an organizer.
- **Meeting form:** for someone who is a delegate, an "Organizer" field (Me or a principal), defaulting to Me.
- **Meeting drawer:** shows "Organized by <Principal>". The principal and the delegates also see "Scheduled by <Delegate>". Invitees never see that line. The occurrence response gains `scheduledByName`, filled only for the organizer role and only when the creator is not the organizer.
- **The create popover is not changed** (its form is frozen by the team planner decision). On a principal's calendar it opens with the meeting tab only, and the meeting goes to the principal because of which calendar is open. The organizer choice exists only in the full meeting form.

## Edge cases

- **Delegation revoked:** the delegate loses access straight away. The meetings they made stay as the principal's and are not changed.
- **Principal leaves the company:** existing rules for a departed organizer apply, unchanged.
- **Delegate is also an attendee:** they keep their own attendee row, and access gives them Organizer.
- **Two delegates edit at once:** the existing series lock and revision checks apply.
- **Action items:** a delegate can raise and unlink action items like the organizer.
- **Weekly report and meeting hours:** hours belong to the attendees, as today. The delegate gets no hours for a meeting they are not in.

## Tests

Backend:
- Grant, list and revoke, with the self and inactive-employee rules.
- Access: a delegate gets Organizer for the principal's series and occurrences. A revoked delegate, an inactive one, or the delegate of someone else gets nothing.
- Create with `organizerEmployeeId`: the organizer is the principal, `CreatedById` is the delegate, placement is checked for the principal, and a non-delegate gets 403.
- The link uses the principal's connection on create and on add.
- Invite notice and email name the principal.
- Organizer notices reach the principal and each active delegate, and not a revoked one.
- `forEmployeeId` on calendar and planner: allowed for the principal's delegates, 403 for anyone else.
- RLS covers the new table.

Frontend:
- The settings list, add and remove.
- The calendar selector appears only for delegates.
- Creating from the principal's calendar sends `organizerEmployeeId`.
- The drawer's "Scheduled by" line shows only to the delegate.

## Delivery

1. **Backend:** the table, migration and RLS policy; the delegate API; access; create on behalf; the link change; organizer notices; `forEmployeeId`.
2. **Frontend:** the settings page, the calendar selector, the meeting form organizer field and the drawer line.

Each is its own PR. The frontend PR depends on the backend PR.

## Calls made without asking

Each can be changed on review.

1. **Meetings only.** Legacy calendar events are out, because new meetings are created as meetings.
2. **Office meetings** check the delegate's `calendar:write`, not the principal's.
3. **A delegate cannot connect an account for the principal.** No Teams or Zoom link is possible until the principal connects one.
4. **Delegation has no end date,** unlike leave approval delegates. It stays until it is revoked.
5. **No limit** on how many delegates a principal can have, or how many principals a delegate can serve.
