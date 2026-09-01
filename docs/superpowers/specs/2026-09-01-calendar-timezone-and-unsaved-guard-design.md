# Calendar Timezone Correctness and Unsaved-Changes Guard Design

## Goal

Make Calendar event times mean the same real-world instant for every viewer, each shown in *their own* timezone (Google/Outlook-style), by connecting the module to the timezone infrastructure that already exists elsewhere in the app (`LegalEntity.Timezone`, `Employee.DisplayTimezone`) instead of the decorative, never-consulted `CalendarEvent.Timezone` field it has today. Separately, add a "discard changes?" guard to the event form so closing it with unsaved edits isn't silent data loss.

## Background (what already exists, confirmed by direct investigation)

- **`LegalEntity.Timezone`** (`ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity`) — IANA id, e.g. `"Asia/Colombo"`. Company-level source of truth, set via the General Settings screen.
- **`Employee.DisplayTimezone`** (`ONEVO.Domain.Features.CoreHr.Employee.Entities.Employee`) — nullable IANA id; null means "inherit the legal entity's timezone." Editable via the employee's own Personal Information form.
- **`GetMyProfileQueryHandler`** already resolves both and returns them (`ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs:73-78`):
  ```csharp
  string? legalEntityTimezone = null;
  if (employee.LegalEntityId is Guid legalEntityId)
  {
      var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
      legalEntityTimezone = legalEntity?.Timezone;
  }
  ```
  This is the resolution pattern Calendar will reuse (own lightweight query, not by calling the full `GetMyProfile` endpoint, which also loads payroll/security/dependents Calendar has no business touching).
- **Attendance/Leave already do real `TimeZoneInfo` math** against `LegalEntity.Timezone` (`AttendanceScheduleResolver`, `LeaveBusinessDateResolver`, `LeaveCancellationOptions.ResolveTimezone`) — Calendar's backend changes below follow the same "resolve legal-entity/employee timezone server-side, do real `TimeZoneInfo` work, never trust a client-supplied timezone string" convention.
- **Frontend has real UTC↔local conversion code already**, but it's private to `time-tracking.component.ts` (`formatDateTime`, `localDateTimeToIso`, `dateOnlyForZone`, `validDisplayTimezone`) and duplicated (not shared) in `correction-approvals.component.ts`. Calendar will extract a shared, generic version of this into `src/app/shared/utils/timezone.util.ts` — Attendance's own two copies are **not** migrated to it in this plan (see Out of Scope).
- **`CalendarEvent.Timezone`** exists on the entity and round-trips through every Calendar command/query today, but is never read by anything — confirmed via `IcalNetRecurrenceExpander` (pure UTC math) and the frontend (hardcodes `'UTC'` on save, treats raw UTC clock-digits as local on display). It is decorative.
- **No unsaved-changes-exit pattern exists anywhere in this frontend.** The building block to reuse is the existing generic `src/app/shared/ui/confirm-modal/confirm-modal.component.ts` (currently only used for delete confirmations) — not a new modal component.

## Scope

**In scope:**
- A new lightweight backend endpoint resolving "my effective timezone" (`Employee.DisplayTimezone ?? LegalEntity.Timezone ?? "UTC"`), scoped to the Calendar feature (not a new cross-cutting shared endpoint - see Global Constraints).
- Backend: an event's `Timezone` is set once, server-side, from the **organizer's** effective timezone at creation, and never mutated by any later edit/occurrence-split path (Update, EditRecurringOccurrence in any scope).
- Removing the now-dead `Timezone` request field from `CreateCalendarEventRequest`/`UpdateCalendarEventRequest`/`EditRecurringOccurrenceRequest` (backend contracts + frontend models + the frontend's hardcoded `'UTC'` senders) — the client no longer supplies this value at all.
- Frontend: a shared, generic timezone conversion utility (`src/app/shared/utils/timezone.util.ts`), extracted from the existing Attendance implementation's proven approach.
- Frontend: fetch and cache the **viewer's own** effective timezone once per Calendar session; every display (Month/Week/Day/Agenda grids, the event form) and every save-time conversion uses it — replacing the three currently-inconsistent, un-timezone-aware code paths found in the form modal and hour-grid.
- Frontend: a small, visible "times shown in {timezone}" hint in the event form so the behavior isn't invisible to users.
- Frontend: an unsaved-changes confirmation (reusing `ConfirmModalComponent`) before discarding a dirty create/edit form, covering the Cancel button, the modal's own X/backdrop/Escape close paths.

**Explicitly out of scope (documented, not solved here):**
- **DST-safe recurrence expansion.** `IcalNetRecurrenceExpander` will continue expanding in pure UTC (adding fixed offsets), so a recurring event's wall-clock time in the organizer's zone can still drift across a DST boundary. This is a real, separate bug, deferred to its own follow-up per explicit user decision.
- **Migrating `time-tracking.component.ts`/`correction-approvals.component.ts`** to the new shared `timezone.util.ts`. Their existing private copies keep working as-is; only the new shared file is added. De-duplicating those two call sites is a good future cleanup, not part of this change.
- **Per-event timezone override UI** (e.g., "schedule this specific meeting in America/New_York regardless of my own timezone"). Every event is anchored to its organizer's timezone at creation; there is no UI to pick a different one for a single event.
- Changing how `StartDate`/`EndDate` are stored (`DateTimeOffset`/UTC instant) - this design only changes what's done with the *display* and the *organizer-anchor* timezone, never the storage representation.

## Global Constraints

- The new "my effective timezone" endpoint lives under the Calendar feature/controller (`GET /api/v1/calendar/my-timezone`) - **not** a new cross-cutting shared endpoint. No other module asked for one; adding a generic "current user timezone" service now would be solving a problem nobody has yet (YAGNI). If a second module needs the same resolution later, it can be lifted out then.
- Backend timezone resolution always follows the `Employee.DisplayTimezone ?? LegalEntity.Timezone ?? "UTC"` order, matching `GetMyProfileQueryHandler`'s existing precedent exactly - do not invent a different fallback order.
- Use `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository.GetDefaultForUserAsync` for "resolve the caller's employee" (the convention already established throughout the rest of the Calendar backend, e.g. `RespondToCalendarEventCommandHandler`) - not `Common.RepositoryInterfaces.IEmployeeRepository.GetByUserIdAsync` (what `GetMyProfileQueryHandler` happens to use), to stay consistent within the Calendar feature area.
- `ILegalEntityRepository.GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct)` is the real, confirmed method for resolving a legal entity by id (`ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs:31`).
- Frontend timezone conversion must go through the new shared `timezone.util.ts` - no component may hand-roll its own `Intl.DateTimeFormat` conversion once this ships.
- An event's `Timezone` is written exactly once, at creation, from the organizer's resolved effective timezone. Every other write path (`UpdateCalendarEventCommandHandler`, `EditRecurringOccurrenceCommandHandler`'s three scopes) must leave the existing value alone, including when constructing a new detached child or a new split-off master (those inherit the **original master's** `Timezone`, never a request value).
- Removing `Timezone` from a request DTO is a breaking wire-contract change - since both repos are updated together in this same body of work, this is safe; do not leave one side stale.

---

## Part 1 — Backend: resolve and expose the caller's effective timezone

New query, following the exact `GetMyProfileQueryHandler` resolution pattern:

```csharp
public sealed record GetMyEffectiveTimezoneQuery : IRequest<Result<MyEffectiveTimezoneResponse>>;
public sealed record MyEffectiveTimezoneResponse(string Timezone);
```

Handler resolves: `employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct)` → if `employee.LegalEntityId` is set, `legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, employee.LegalEntityId.Value, ct)` → `timezone = employee.DisplayTimezone ?? legalEntity?.Timezone ?? "UTC"`.

New endpoint: `GET /api/v1/calendar/my-timezone` → `200 { "timezone": "Asia/Colombo" }`, `[RequirePermission("calendar:read")]`.

A small, reusable helper (not a new service - just a private static method or a tiny internal class shared by this handler and Part 2's handlers, since the same three-line resolution repeats in both).

## Part 2 — Backend: anchor `Timezone` to the organizer, freeze it on every edit path

- `CreateCalendarEventCommandHandler`: resolve the caller's effective timezone the same way as Part 1, assign it to `calendarEvent.Timezone` (replacing the current `Timezone = request.Timezone`). Remove `Timezone` from `CreateCalendarEventCommand`/`CreateCalendarEventRequest`.
- `UpdateCalendarEventCommandHandler`: delete the line `existing.Timezone = request.Timezone;` entirely - the field is simply never touched here. Remove `Timezone` from `UpdateCalendarEventCommand`/`UpdateCalendarEventRequest`.
- `EditRecurringOccurrenceCommandHandler`:
  - Remove `target.Timezone = request.Timezone;` from `ApplyFields` (shared by all three scopes - this line currently overwrites the field on every path, which is exactly the bug being fixed).
  - `EditAllEventsAsync`: no explicit assignment needed - the master's existing `Timezone` is untouched, matching Update's behavior.
  - `EditThisEventOnlyAsync`: when constructing a brand-new detached child (`child is null` branch), explicitly set `child.Timezone = master.Timezone`.
  - `EditThisAndFollowingAsync`: when constructing the new split-off master, explicitly set `newMaster.Timezone = master.Timezone`.
  - Remove `Timezone` from `EditRecurringOccurrenceCommand`/`EditRecurringOccurrenceRequest`.

## Part 3 — Frontend: shared timezone utility

New file `src/app/shared/utils/timezone.util.ts`, generalized from `time-tracking.component.ts`'s proven (but private) implementation:

```typescript
export function isValidTimezone(timezone: string): boolean; // try/catch around `new Intl.DateTimeFormat(undefined, { timeZone: timezone })`

export function utcIsoToLocalParts(isoUtc: string, timezone: string): { date: string; time: string };
// -> { date: 'YYYY-MM-DD', time: 'HH:mm' } as rendered in `timezone`, via Intl.DateTimeFormat 'en-CA' + formatToParts

export function localPartsToUtcIso(date: string, time: string, timezone: string): string;
// -> the reverse: iterative Intl.DateTimeFormat.formatToParts correction (same algorithm as
// time-tracking.component.ts's localDateTimeToIso), returns a full ISO instant with a real UTC offset

export function dateOnlyInTimezone(isoUtc: string, timezone: string): string;
// -> 'YYYY-MM-DD', the calendar date this UTC instant falls on *in `timezone`*

export function minutesSinceLocalMidnight(isoUtc: string, timezone: string): number;
// -> minutes since 00:00 *in `timezone`* on the date returned by dateOnlyInTimezone - used by the
// hour-grid for vertical positioning, replacing its current implicit browser-local Date math
```

## Part 4 — Frontend: fetch and cache the viewer's effective timezone

- `CalendarEventApiService.getMyTimezone(): Observable<{ timezone: string }>` → `GET {baseUrl}/my-timezone`.
- `CalendarEventStore` gains `myTimezone: string` state (initial `'UTC'`) and `async loadMyTimezone(): Promise<void>`, called once by `CalendarPageComponent` alongside its existing range-load effect (on component construction, not re-fetched on every range change).

## Part 5 — Frontend: the event form uses the viewer's timezone

- `CalendarEventFormModalComponent` gains `@Input() myTimezone = 'UTC'` (fed from `store.myTimezone()` via the page).
- `ngOnChanges`: replace the raw `event.startDate.split('T')` / `event.endDate.split('T')` parsing with `utcIsoToLocalParts(event.startDate, this.myTimezone)` / same for `endDate`.
- `onSubmit`: replace the hardcoded `` `${raw.startDate}T${raw.startTime}:00Z` `` construction with `localPartsToUtcIso(raw.startDate, raw.startTime, this.myTimezone)` / same for end.
- Add a small muted hint in the "When" section: `Times shown in {{ myTimezone }}`.
- The conflict-check payload (`checkConflicts.emit(...)`) must build its `startDate`/`endDate` the same converted way, not the old hardcoded-`Z` way - otherwise the conflict check would compare wrong instants once this ships.

## Part 6 — Frontend: Month/Week/Day/Agenda views bucket and position consistently

Audit and fix **every** view that currently derives "which day does this event belong to" or "how far down the hour column" from a raw UTC-string slice or an un-timezone-aware `Date`:
- `calendar-hour-grid.component.ts` (Week/Day): day-bucketing (`event.startDate.slice(0, 10)`) → `dateOnlyInTimezone(event.startDate, timezone)`; the grid's `dayStart`/`dayEnd` boundaries computed in the given timezone, not `new Date(`${date}T00:00:00`)` (implicit browser-local).
- `calendar-hour-grid.util.ts`'s `layoutDayEvents`/`minutesSince`: switch to `minutesSinceLocalMidnight(event.startDate, timezone)` instead of native `Date` arithmetic relative to a browser-local `dayStart`.
- `calendar-month-grid` and `calendar-agenda-view`: confirm during implementation whether they do their own UTC-date-slicing for day-bucketing (the same class of bug found in the hour-grid) and fix identically if so.
- `CalendarHourGridComponent`/month-grid/agenda-view all gain a `@Input() timezone: string`, fed from `store.myTimezone()` via `CalendarPageComponent`.

## Part 7 — Frontend: unsaved-changes exit guard

- `CalendarEventFormModalComponent` tracks dirtiness: `this.form.dirty || this.participantIds().length > 0` (participant selection lives outside the `FormGroup`, so it needs an explicit OR).
- All three close paths - the Cancel button, and the `app-modal`'s own `(closed)` event (which fires for the X button, backdrop click, and Escape) - route through one method, e.g. `attemptClose()`: if dirty, show a local `ConfirmModalComponent` ("Discard changes?" / "Keep editing" / "Discard"); only emit the real `cancel` output after confirmation, or immediately if not dirty.
- No change needed to `CalendarPageComponent` - it still just listens to `(cancel)="closeForm()"`, now fired later (after confirmation) instead of always-immediately.

## Testing Strategy

- Backend: `GetMyEffectiveTimezoneQueryHandlerTests` (employee has DisplayTimezone → wins; employee has null DisplayTimezone, legal entity has one → wins; employee has no legal entity → falls back to UTC). Add one assertion each to `CreateCalendarEventCommandHandlerTests`/`UpdateCalendarEventCommandHandlerTests`/`EditRecurringOccurrenceCommandHandlerTests` confirming `Timezone` is resolved-and-set on create, untouched on update/edit-all, and inherited-from-master on a new detached child / new split master.
- Frontend: new `timezone.util.spec.ts` covering all five exported functions against known UTC↔zone conversions (including a DST-transition date, to prove the *display* math itself is DST-correct even though recurrence expansion isn't). Update `calendar-event-form-modal.component.spec.ts` for the new `myTimezone` input's effect on pre-fill/submit, plus new tests for the discard-confirmation flow. Update `calendar-hour-grid.util.spec.ts` for the new timezone-aware positioning.
- Manual E2E: create an event as a user whose profile has a non-UTC `DisplayTimezone`; confirm the times shown in the create form match what was typed; confirm another user (different timezone) viewing the same event sees it converted correctly to their own zone; confirm editing and cancelling with a dirty form prompts to discard; confirm editing and cancelling with no changes closes immediately.

## Open Items

- DST-safe recurrence expansion (`IcalNetRecurrenceExpander`) - deferred, tracked as a known follow-up.
- De-duplicating `time-tracking.component.ts`/`correction-approvals.component.ts` onto the new shared `timezone.util.ts` - good future cleanup, not required for Calendar to be correct.
- Whether `CalendarEvent.Timezone` should ever be exposed as an editable per-event override (e.g., "schedule this meeting for a New York audience") - no current requirement for it; today it is purely the organizer's own anchor, invisible except via the "Times shown in {tz}" hint (which always reflects the *viewer's* zone, not the stored organizer zone).
