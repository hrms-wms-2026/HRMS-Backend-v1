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
| 7 | A delegation can have an optional end date. Without one it lasts until it is revoked. (Review, 2026-11-10) |
| 8 | Every action a delegate takes on the principal's meetings, and every grant and revoke, is written to the audit log with both the delegate and the principal. (Review, 2026-11-10) |

## Model

New table `meeting_delegates`: `id`, `tenant_id`, `principal_employee_id`, `delegate_employee_id`, `created_by_user_id`, `created_at`, `expires_at` (nullable), `revoked_at`.

- A row is **active** when `revoked_at` is null, `expires_at` is null or later than now, and both employees are active.
- One unrevoked row per principal and delegate pair. Revoking sets `revoked_at`, and the row is kept for audit.
- `expires_at`, when given, must be in the future. An expired row stays in the list as "Expired" until it is revoked or a new grant replaces it.
- A person cannot be their own delegate.
- Both people must be active employees of the tenant. When either one stops being active, the delegation stops working, and the row does not need to change.
- The table is tenant-owned and gets an RLS policy like every other tenant table (`RlsTableRegistry`).
- The pattern follows `LeaveApprovalDelegate` (`api/v1/leave/approval-delegates`).

## Who manages delegates

Every employee manages their own delegates from a "Meeting delegates" dialog on the Calendar page (decision 5). It follows the Inbox's "Delegate my approvals" dialog, because the app has no profile settings page. The API acts only on the caller's own list: there is no endpoint to grant or revoke for someone else.

## Access

`MeetingAccessResolver` is today the single rule for who may see a meeting: its organizer and its attendees (D6). It gains one case:

- A caller who is an active delegate of the series' organizer gets the **Organizer** role, with a new `ActingForEmployeeId` set to the principal.

Every check that already asks for the Organizer role then works for the delegate without its own change. D6 is otherwise unchanged: no role or HR check is added.

**What the Organizer role gives a delegate today,** which matches decision 2:
- edit, including notes;
- cancel and move;
- add and remove attendees, and bulk invite;
- add, regenerate, retry and remove the online link;
- the organizer's attendance view;
- unlinking action items.

**Review concern: future organizer-only actions.** Inheriting the role is on purpose, because the user asked for everything. The risk is that a later organizer-only action reaches delegates without anyone deciding it. The rule for that is:

- `MeetingAccess.ActingForEmployeeId` is set whenever a delegate acts.
- An action that must stay with the principal personally checks that it is null and returns 403 `meeting_principal_only`.
- No action needs this today.
- A test pins the list above: `OrganizerOnlyActions_AreTheReviewedList` fails when a new `MeetingRoles.Organizer` check appears, so its author has to decide.

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

## Audit log

The existing `audit_logs` table is used (`AuditLog`: `UserId`, `Action`, `ResourceType`, `ResourceId`, `OldValuesJson`, `NewValuesJson`, `IpAddress`, `CreatedAt`). Rows are written the same way `CreateProjectCommandHandler` and `UpdateNotificationSettingCommandHandler` already write them.

- **Same transaction:** a row is added before the write's `SaveChangesAsync`, so an action and its audit row commit or fail together.
- **The actor is the user:** `UserId` is the delegate's user. The principal is in the JSON.
- **Only delegated actions:** a meeting action is logged when `ActingForEmployeeId` is set. The organizer's own actions are not logged, as today. Grants and revokes are always logged.

| Action | When | ResourceType / ResourceId | NewValuesJson | OldValuesJson |
|---|---|---|---|---|
| `meeting_delegate.granted` | A grant | `MeetingDelegate` / row id | `principalEmployeeId`, `delegateEmployeeId`, `expiresAt` | none |
| `meeting_delegate.revoked` | A revoke | `MeetingDelegate` / row id | `principalEmployeeId`, `delegateEmployeeId` | `expiresAt` |
| `meeting.created_for_principal` | A delegate creates | `MeetingSeries` / series id | `actingForEmployeeId`, `title`, `start`, `durationMinutes`, `recurrenceRule`, `isPrivate`, `participantCount`, `guestCount`, `platform`, `outcome` (created or requested) | none |
| `meeting.edited_for_principal` | Edit, including notes | `MeetingSeries` / series id | `actingForEmployeeId`, `scope`, `originalStart`, the changed fields | The same fields before the change |
| `meeting.moved_for_principal` | Move or reschedule | `MeetingSeries` / series id | `actingForEmployeeId`, `scope`, `originalStart`, `newStart`, `newDurationMinutes` | `start`, `durationMinutes` |
| `meeting.cancelled_for_principal` | Cancel | `MeetingSeries` / series id | `actingForEmployeeId`, `scope`, `originalStart` | none |
| `meeting.attendees_changed_for_principal` | Add or remove an attendee, bulk invite | `MeetingSeries` / series id | `actingForEmployeeId`, `scope`, `originalStart`, `addedEmployeeIds`, `removedEmployeeIds`, `addedGuestCount`, `removedGuestCount` | none |
| `meeting.link_changed_for_principal` | Add, regenerate, retry or remove the link | `MeetingSeries` / series id | `actingForEmployeeId`, `change` (added, regenerated, retried or removed), `provider` | none |

**Privacy:**
- Guest emails, descriptions and notes are never copied into the log. Only counts, and the names of changed fields, are written.
- A private meeting's title is logged as `"(private)"`.

**Reading:** the rows appear in the existing tenant audit log listing (`IAuditLogRepository.ListByTenantIdAsync`). No new screen is added.

**One writer:** a small `IMeetingDelegateAudit` service builds every row, so the JSON shape stays the same everywhere. Each meeting write service calls it once after the change, with the `MeetingAccess` it already holds.

## Revocation during a request

**Review concern: a revoke that lands while a write is running.**
- Every meeting write checks access once, at the start of the request, and commits in well under a second, so there is no long-running operation to stop. A revoke that lands during a write is treated like one that lands a moment later.
- The background link provisioning that follows a create runs for the organizer, not the delegate, so it is unaffected by a revoke.
- No extra check is added inside the write transaction. A row lock on the delegation for every meeting write would cost more than this edge case is worth (my call 6).

## Seeing the principal's calendar

- `GET /calendar` and `GET /planner/me/range` gain an optional `forEmployeeId`. It is allowed only for the caller themselves or a principal they are an active delegate of, and anything else gets 403.
- For a principal, the response is exactly what the principal would see, private meetings included (decision 3).
- Leave, planner task blocks and legacy calendar events show read-only. The delegate can act only on meetings.

## Frontend

- **Calendar page, "Meeting delegates" dialog:** add a delegate (the shared people picker, with an optional "Until" date), see the list with each one's state (Active, or Expired), and remove one.
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
- An expired grant gives no access and no notices, and a past `expiresAt` is rejected.
- Each audited action writes one row, with the delegate as `UserId` and the principal in the JSON. A private meeting's title is masked, and guest emails and notes never appear.
- The organizer's own actions write no meeting audit row.
- `OrganizerOnlyActions_AreTheReviewedList` pins the organizer-only checks.

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
4. **The end date is optional** (decision 7). An expired grant is kept and shown as Expired, not deleted.
5. **No limit** on how many delegates a principal can have, or how many principals a delegate can serve.
6. **No extra in-transaction delegation check;** see "Revocation during a request".
7. **Only delegated meeting actions are audited,** not the organizer's own. Auditing every organizer action is a wider change for the audit log work.
