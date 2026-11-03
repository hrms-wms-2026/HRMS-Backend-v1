# Calendar Invites, RSVP, and Conflict Detection Design

## Goal

Make Calendar events behave like real meetings, not just personal blocks: participants get notified (in-app + email) when added/updated/cancelled, can accept/decline, and the organizer sees a non-blocking warning if a participant is already busy. Builds on `feature/calendar-frontend-module` (Month/Week/Day/Agenda views, already shipped) and the backend recurrence engine (already shipped).

## Scope

**In scope (this release):**
- In-app notifications on participant-add, event-update ("all events" scope), and event-cancellation (delete or per-occurrence cancel) - reusing the existing `Notification`/`NotificationTemplate`/Outbox infrastructure, no new schema.
- Email invites on participant-add, via a new dedicated `IEmailService` method (following this codebase's existing per-type-method convention) - not the generic notification→email bridge, which is unbuilt, unrelated infrastructure and out of proportion to this feature.
- RSVP: participants can Accept/Decline an invitation. Uses the already-existing `calendar_event_participants.response_status` column (`Pending/Accepted/Rejected` already defined) - no new migration for the status itself.
- Participants exposed in the event read model (`GET /api/v1/calendar`) - currently write-only (set at creation, never returned). Needed for any RSVP UI to exist at all.
- Conflict detection: a non-blocking warning shown when creating/editing an event if a selected participant already has an overlapping event as creator or accepted/pending participant. Warning only - never blocks save.

**Explicitly out of scope (documented limitation, not solved here):**
- **Participants on detached recurring occurrences.** When a single occurrence is edited "this event only," the resulting detached `calendar_events` row does not copy the master's participants - it has none. RSVP/notifications on a detached occurrence will show zero participants until this is addressed in a later pass. This is a real, known gap in how recurrence and participants currently intersect - not something this spec silently papers over.
- Reminder notifications before an event starts (a scheduled/background-job concern, not a request-time one).
- "Nominate a replacement" workflow (mentioned in the original Calendar vault doc, still deferred).
- Any change to the generic `NotificationDispatcher` → email bridge (`NotificationTemplate.Mail*` fields stay unused dead columns outside Calendar's own email path).

## Global Constraints

- Branch off `feature/calendar-frontend-module` (frontend) / `feature/calendar-recurrence-engine` (backend) - the latest shipped work in each repo.
- In-app notifications go through the existing `IOutboxWriter.EnqueueAsync(...)` → outbox processor → `INotificationDispatcher.SendTemplatedAsync(...)` pipeline (`src/ONEVO.Application/...Outbox...`, `NotificationDispatcher`) - confirm the exact `EnqueueAsync`/`WorkNotificationPayload` signatures by reading those files before writing the calendar-side call sites; do not guess parameter order.
- New `NotificationTemplate` rows are added via `NotificationTemplateSeeder`'s existing idempotent-by-`Code` pattern, not a migration.
- The new email method lives on `IEmailService`/`TransactionalEmailService` matching the shape of its existing per-type methods (e.g. the password-reset or admin-invite email) - read one of those in full before writing the new method, to match its exact calling convention (sync vs fire-and-forget, error handling on send failure).
- Conflict detection never blocks a save - it is informational only, returned alongside the create/update response or as a separate pre-check the frontend calls before submitting.
- Every new write path that touches more than one row atomically runs inside `IUnitOfWork.ExecuteInTransactionAsync`.

---

## Part 1 — Notifications

### Template codes (seeded)

| Code | Trigger | In-app title/body |
|---|---|---|
| `calendar_event_participant_added` | A participant row is created (event create, or "this event only"/"all events" edit that adds new participants - not built yet, see Open Items) | "You were added to {{eventTitle}}" / "{{organizerName}} added you to an event on {{eventDate}}." |
| `calendar_event_updated` | `UpdateCalendarEventCommand` succeeds, or `EditRecurringOccurrenceCommand` with `scope=AllEvents` succeeds | "{{eventTitle}} was updated" / "{{organizerName}} updated the event details." |
| `calendar_event_cancelled` | `DeleteCalendarEventCommand` succeeds, or `CancelRecurringOccurrenceCommand` succeeds, or `EditRecurringOccurrenceCommand`'s implicit "all events" delete-of-series path | "{{eventTitle}} was cancelled" / "{{organizerName}} cancelled the event." |

### Wiring

Each of `CreateCalendarEventCommandHandler`, `UpdateCalendarEventCommandHandler`, `DeleteCalendarEventCommandHandler`, `EditRecurringOccurrenceCommandHandler` (`AllEvents` scope only - `ThisEventOnly` only affects one detached occurrence's own participants, which per the Out-of-scope note above don't exist yet), and `CancelRecurringOccurrenceCommandHandler` gains a step, inside the same transaction as the write: resolve each participant's `UserId` (via `IEmployeeRepository.GetByIdForTenantAsync(tenantId, participant.EmployeeId, ct)` → `.UserId`, matching the existing employee-lookup pattern used elsewhere in Calendar) and enqueue one outbox notification per participant with the corresponding template code and placeholders (`eventTitle`, `eventDate`, `organizerName` - resolved from the current user's own display name via whatever existing "get current user's display name" helper this codebase already uses, e.g. `ICurrentUser` or a lightweight employee lookup - confirm the exact source during implementation).

### Email

New method on `IEmailService`:
```csharp
Task SendCalendarEventInviteAsync(string toEmail, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? timezone, string? location, string organizerName, CancellationToken ct = default);
```
Called from `CreateCalendarEventCommandHandler` for each newly-added participant (resolve `Employee.Email` via the same employee lookup used for `UserId` above), inside the transaction's participant-loop, alongside the in-app notification enqueue. Matches the existing per-type `IEmailService` method shape exactly - read `SendPasswordResetEmailAsync` (or the closest existing analog) in full before writing this, to copy its template-rendering and provider-dispatch call pattern precisely rather than reinventing it.

---

## Part 2 — Participants in the read model

`CalendarEventItem`/`CalendarEventViewModel` gain a new field:
```csharp
IReadOnlyList<CalendarEventParticipantSummary> Participants
```
```csharp
public sealed record CalendarEventParticipantSummary(Guid EmployeeId, string EmployeeName, string ResponseStatus);
```
`GetCalendarEventsQueryHandler` loads participants for every real row it returns (a new `ICalendarEventRepository.GetParticipantsForEventsAsync(tenantId, IReadOnlyList<Guid> eventIds, ct)` batched lookup, joined against `IEmployeeRepository` for display names) and attaches them per item. Virtual (synthesized) occurrences show the **master's** participants (same participant rows, since they belong to the master's `EventId`) - consistent with the Part 1 Out-of-scope note that detached occurrences don't yet have their own.

---

## Part 3 — RSVP (Accept/Decline)

New commands, mirroring `AcceptObjectiveInvitationCommandHandler`/`RejectObjectiveInvitationCommandHandler`'s exact shape (resolve caller's employee id → load the participant row → verify it belongs to the caller → verify it's still `Pending` → wait, actually: RSVP should be re-answerable (a participant can change Accept→Decline later), so **skip** the "must still be Pending" guard that `ProjectMemberInvitation` uses for one-time invitations - any authenticated participant on the row may set it to Accepted or Rejected at any time, not just once):

```csharp
public sealed record RespondToCalendarEventCommand(Guid EventId, string ResponseStatus) : IRequest<Result>; // ResponseStatus: "Accepted" | "Rejected"
```
Handler: resolve caller's employee id (`IEmployeeRepository.GetDefaultForUserAsync`, matching `GetCalendarEventsQueryHandler`'s own pattern) → find the `calendar_event_participants` row for `(EventId, EmployeeId)` → 404 if none (caller isn't a participant) → update `ResponseStatus` → save. `EventId` here is always a **real** row's id: the frontend passes the item's own `id` for a plain/detached event, or `recurrenceMasterId` for a virtual occurrence (same resolution the frontend already does for edit/delete `scope` decisions).

Endpoint: `POST api/v1/calendar/{id}/respond` with body `{ "responseStatus": "Accepted" | "Rejected" }`.

---

## Part 4 — Conflict detection

New query, callable independently of create/update so the frontend can pre-check before submitting:

```csharp
public sealed record CheckCalendarConflictsQuery(IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate) : IRequest<Result<CalendarConflictsResponse>>;
public sealed record CalendarConflictsResponse(IReadOnlyList<CalendarConflict> Conflicts);
public sealed record CalendarConflict(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle);
```
For each `ParticipantEmployeeIds` entry, reuse `ICalendarEventRepository.GetInDateRangeForCallerAsync`-style logic (a new `GetInDateRangeForEmployeeAsync(tenantId, employeeId, from, to, ct)` overload scoped by participant/creator membership for one specific employee rather than "the current caller") over `[StartDate, EndDate]`, plus the same recurring-master expansion `GetCalendarEventsQueryHandler` already does (reuse `ICalendarRecurrenceExpander` the same way) - a conflict check must see virtual occurrences too, not just literal rows, or it would miss "busy every Tuesday" conflicts entirely. Any hit becomes one `CalendarConflict` entry. No new endpoint route beyond `POST api/v1/calendar/check-conflicts` (a POST since `ParticipantEmployeeIds` is a list, awkward as query params).

**Frontend usage:** the event form calls this endpoint (debounced) whenever participants and both dates are set, and renders a non-blocking amber warning line per conflicting employee - "Ada Lovelace is busy at this time (Standup)." Create/Save buttons remain enabled regardless.

---

## Part 5 — Frontend

- `CalendarEventFormModalComponent` gains: a participants list display (names + response-status badges, read-only for now - re-inviting/removing an existing participant after creation is not built, matching the "participants are set at creation only" scope already established in Calendar Core), a conflict-warning area wired to Part 4's endpoint, and - for the *viewer's own* invitation, when they're a participant rather than the creator - Accept/Decline buttons calling Part 3's endpoint instead of the save/delete button row (a participant who isn't the creator cannot edit/delete the event at all, matching the existing creator-only authorization already enforced server-side for update/delete/edit-occurrence/cancel-occurrence).
- The existing notification bell (`notification-bell.component.ts`) and its store need **no changes** - it already renders whatever `Notification` rows exist for the current user, and the new calendar template codes flow through the exact same contract.

## Testing Strategy

- Backend: notification-enqueue assertions (verify the outbox writer is called with the right template code/recipient per participant) added to the existing Create/Update/Delete/EditRecurringOccurrence/CancelRecurringOccurrence handler test classes - not a new test class, since it's one more assertion on already-tested handlers. `RespondToCalendarEventCommandHandlerTests` (new): not-a-participant → 404, valid participant → status updates, re-answering already-answered invitation → succeeds (no "already decided" guard, unlike `ProjectMemberInvitation`). `CheckCalendarConflictsQueryHandlerTests` (new): no conflict → empty list, direct overlap → one conflict entry, conflict against a recurring master's virtual occurrence → one conflict entry (proves expansion is actually used, not just literal rows).
- Frontend: `calendar-event-form-modal` gains cases for the conflict-warning display and the Accept/Decline button branch (participant, not creator).
- Manual E2E: create an event with a participant on the seeded `dapi` tenant's second user, confirm a `Notification` row appears (check via `GET /api/v1/notifications`) and the email-send path is invoked (dev environment likely has no real provider configured - confirm the call is attempted and logged, not necessarily that a real email lands, per this session's existing SendGrid-key setup context), accept the invitation as that second user, confirm `response_status` updates, then create a second event for the same participant at an overlapping time and confirm the conflict warning appears without blocking creation.

## Open Items

- Participants-on-detached-occurrences (Part 1's explicit out-of-scope note) needs its own follow-up design - likely "copy the master's current participants onto the new detached row at split time," but that has its own edge cases (what if the master's participant list changed since the series started?) worth a dedicated discussion rather than a rushed decision here.
- `organizerName`/employee display-name resolution for notification placeholders should use whatever this codebase's established "resolve a display name for the current user" helper already is - confirm the exact source (likely on `ICurrentUser` or a shared employee-lookup) before implementation rather than assuming a new one is needed.
