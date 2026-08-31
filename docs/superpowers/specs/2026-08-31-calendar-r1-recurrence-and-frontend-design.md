# Calendar R1: Recurrence Engine + Frontend Design

## Goal

Ship a usable Calendar feature end-to-end: a month-view calendar page in the tenant frontend, backed by full create/edit/delete of events (including participants), with real recurring-event support (RFC 5545 RRULE expansion, per-occurrence edit/cancel, whole-series edit) — replacing the `/calendar` sidebar placeholder that already exists today.

This spec extends `docs/superpowers/specs/2026-08-28-calendar-core-external-sync-design.md`'s "Calendar Core" section, which is already built and shipped on `feature/calendar-core` (`calendar_events`/`calendar_event_participants` tables, CRUD commands/query, `CalendarController`). External calendar sync (Google/Outlook OAuth, background sync job) remains out of scope here, unchanged from the original spec.

## Scope

This is the frozen R1 scope agreed with the user — nothing here moves to a later phase.

**In scope:**
- Navigation: replace the existing `/calendar` route's `loadPlaceholder` with a real `modules/calendar` feature module. Sidebar item and route path (`/calendar`) already exist and are unchanged.
- Calendar UI: month view only (previous/next month, jump to today, selected-date highlight), multiple events per day, all-day event styling, day-cell overflow ("+3 more").
- Event CRUD: create, edit, delete, with title/description/start/end/all-day/location/meeting link/color/participants — all already built in Calendar Core; the frontend consumes the existing API.
- Recurrence: Does not repeat / Daily / Weekly / Monthly, each optionally "Ends never" or "Ends on [date]". RRULE-based expansion server-side. Per-occurrence edit ("this event only"), per-occurrence cancellation, and whole-series edit ("all events"). "This and following" split is supported by the data model and command layer (needed for the edit-mode command's correctness) even though the R1 UI only exposes "this event" and "all events" as user-facing choices — see Frontend section.

**Explicitly not in R1 (unchanged from the user's freeze — no further discussion needed):**
- A custom RRULE builder (arbitrary `BYDAY` multi-day-of-week, `BYSETPOS`, etc.) in the UI.
- Calendar sharing, multiple calendars, invitation/RSVP response workflow (participant rows are created but there's still no accept/reject UI — same deferral as the original Calendar Core spec).
- External Google/Outlook sync.
- Drag-and-drop rescheduling.
- Week/Day/Agenda views (the architecture leaves room for them, see Frontend Architecture, but only Month is built now).

## Global Constraints

- Backend: branch off `feature/calendar-core` (this session's completed Calendar Core work), not `development`.
- Frontend: branch off the frontend repo's current `development` tip.
- Recurrence expansion uses the `Ical.Net` NuGet package (RFC 5545) rather than hand-rolled date math — chosen for correctness on DST/month-end/leap-year edge cases.
- The existing `GET /api/v1/calendar?from=&to=` endpoint stays a generic date-range query — it is not month-specific. The frontend is free to request whatever range the active view needs (a full 6-week month grid range for Month view today; a 7-day range if Week view is added later).
- All new backend writes that touch more than one row atomically (the series-split operation) must run inside `IUnitOfWork.ExecuteInTransactionAsync`.
- Every new tenant-scoped table/column follows the existing RLS + snake_case conventions already used by `calendar_events`.

---

## Part 1 — Backend: Recurrence Data Model

### Schema change (migration on top of Calendar Core)

Three new nullable columns on the existing `calendar_events` table — no new table:

| Column | Type | Meaning |
|---|---|---|
| `recurrence_parent_id` | `uuid`, nullable, FK → `calendar_events.id` | Set only on a detached-occurrence or cancellation-marker row. Null on a normal event and on a recurring master. |
| `recurrence_original_start` | `timestamptz`, nullable | On a detached/cancelled row, the `StartDate` the *virtual* occurrence would have had before it was overridden — used to suppress that one virtual occurrence when expanding the parent's RRULE. Named `OriginalStart`, not `OriginalDate`, because it carries the full original start instant (date + time), avoiding any ambiguity from a date-only value combined with a separate time zone. |
| `is_recurrence_cancelled` | `boolean`, default `false` | True on a cancellation-marker row: this occurrence is deleted, but the row exists so the parent's expansion can find and skip it. Cancellation-marker rows are never returned by `GetCalendarEventsQuery` regardless of date range. |

`Recurrence` (existing `none/daily/weekly/monthly` string column, already shipped) is kept as-is and now serves purely as coarse UI/display metadata — "what kind of repeat is this at a glance" — populated by the frontend alongside `RecurrenceRule`. `RecurrenceRule` (existing nullable text column, already shipped, currently always null) becomes the source of truth: whenever `Recurrence != none`, `RecurrenceRule` must hold a valid RFC 5545 `RRULE` value string (the part after `RRULE:`, e.g. `FREQ=WEEKLY;UNTIL=20261231T000000Z`), and all expansion reads only `RecurrenceRule`. Keeping both avoids a rename migration on a column already in production use, and matches the user's own "either works, `Recurrence` as UI/helper metadata is fine" guidance.

A **master recurring event** = `Recurrence != none`, `RecurrenceRule` set, `RecurrenceParentId == null`.
A **detached occurrence** = `RecurrenceParentId` set, `RecurrenceOriginalStart` set, `IsRecurrenceCancelled == false`, `Recurrence == none` (it is a standalone event now — it does not itself recur).
A **cancellation marker** = same shape as a detached occurrence but `IsRecurrenceCancelled == true`.
A **plain manual event** = `Recurrence == none`, `RecurrenceParentId == null` (unchanged from Calendar Core).

RLS: no new table, so no new policy — the three columns ride on `calendar_events`' existing `tenant_isolation` policy.

### Domain/EF changes

`CalendarEvent` (existing entity) gains:
```csharp
public Guid? RecurrenceParentId { get; set; }
public DateTimeOffset? RecurrenceOriginalStart { get; set; }
public bool IsRecurrenceCancelled { get; set; }
```
`CalendarEventConfiguration` adds an index on `(TenantId, RecurrenceParentId)` (looking up a master's children is the hot path during expansion and during series-split) and a self-referencing FK (`HasOne<CalendarEvent>().WithMany().HasForeignKey(e => e.RecurrenceParentId).OnDelete(DeleteBehavior.Cascade)` — deleting a master deletes its detached/cancelled children, matching the "delete all events" edit mode).

### Recurrence expansion

A new `ICalendarRecurrenceExpander` service (Application layer interface, Infrastructure implementation wrapping `Ical.Net`):
```csharp
public interface ICalendarRecurrenceExpander
{
    /// <summary>Occurrence start instants for a master's RRULE within [from, to], inclusive.
    /// Does not know about detached/cancelled children - the caller filters those out.</summary>
    IReadOnlyList<DateTimeOffset> Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to);
}
```
Implementation parses `recurrenceRule` into an `Ical.Net.DataTypes.RecurrencePattern`, builds a single-occurrence `CalendarEvent` (Ical.Net's, not ours) with `DtStart = seriesStart` and that pattern, and calls its `GetOccurrences(from, to)`. A hard cap of 500 generated occurrences per call guards against a pathological `UNTIL`-less daily series queried over a huge range — return only the first 500 and log a warning; R1's month-grid queries are always bounded to ~6 weeks so this cap is never realistically hit.

`EfCalendarEventRepository.GetInDateRangeForCallerAsync` (existing method) changes shape: it still returns real rows for plain manual events and detached occurrences exactly as today, but now additionally loads master recurring rows whose series could produce an occurrence in range (`Recurrence != none AND RecurrenceParentId IS NULL AND StartDate <= to`) plus, for each such master, its detached/cancelled children. `GetCalendarEventsQueryHandler` (existing handler) is the layer that actually calls `ICalendarRecurrenceExpander` per master and merges: for each expanded occurrence date, if a detached child's `RecurrenceOriginalStart` matches, skip it (the detached row itself, already fetched as a normal row, represents that date instead); if a cancellation marker's `RecurrenceOriginalStart` matches, skip the date entirely; otherwise synthesize a `CalendarEventItem` for that virtual occurrence (same fields as the master, `StartDate`/`EndDate` shifted to the occurrence's start keeping the master's original duration, a synthetic `Id` = deterministic GUID derived from `(masterId, occurrenceStart)` so the frontend has a stable per-occurrence identity to pass back on edit/cancel actions).

### Edit-mode commands

Three commands replace/extend `UpdateCalendarEventCommand`'s role for recurring events (the existing command is unchanged and still handles plain manual events and edits to a master's own fields under "all events" mode):

**`EditRecurringOccurrenceCommand(Guid MasterId, DateTimeOffset OriginalStart, EditScope Scope, <same fields as UpdateCalendarEventCommand>)`**, `EditScope` enum = `ThisEventOnly | AllEvents` (R1 UI only offers these two; `ThisAndFollowing` exists on the enum and is fully implemented per the review feedback, ready for a future UI entry point).

- `ThisEventOnly`: find-or-create a detached row (`RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart`), then apply the edited fields to it exactly like `UpdateCalendarEventCommandHandler` does today.
- `AllEvents`: apply the edited fields directly to the master row. Existing detached/cancelled children are untouched.
- `ThisAndFollowing`: runs inside `IUnitOfWork.ExecuteInTransactionAsync` as a single atomic operation:
  1. Re-fetch the master with `GetTrackedByIdForTenantAsync` (row is now locked for the transaction's duration by Postgres's normal read-committed + `UPDATE` semantics once step 2 writes it).
  2. Parse the master's `RecurrenceRule`, set its `UNTIL` to the instant just before `OriginalStart`, save.
  3. Create a brand-new master row: same recurrence pattern (frequency/interval, `UNTIL` from the original series if any), `StartDate = OriginalStart` (shifted by the edited fields if the start time itself was changed), the edited field values, `RecurrenceParentId = null`.
  4. Re-parent every existing detached/cancelled child of the old master whose `RecurrenceOriginalStart >= OriginalStart` to the new master's `Id`.
  5. Commit.

**`CancelRecurringOccurrenceCommand(Guid MasterId, DateTimeOffset OriginalStart)`**: find-or-create a cancellation-marker row (`RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart, IsRecurrenceCancelled = true`) — same find-or-update-first pattern as the edit command so cancelling twice is idempotent rather than creating duplicate markers.

**`DeleteCalendarEventCommand`** (existing, unchanged signature) gains one branch: if the target `Id` is a master (`Recurrence != none && RecurrenceParentId == null`), the EF cascade delete (configured above) removes its detached/cancelled children in the same `SaveChangesAsync` — this is "delete all events," already correct with no new command needed.

All three new/changed write paths are creator-only, matching the existing `UpdateCalendarEventCommandHandler`/`DeleteCalendarEventCommandHandler` ownership check (`existing.CreatedById != currentUser.UserId` → Forbidden) — a detached/cancellation-marker row's own `CreatedById` is stamped by the interceptor from whoever performs the edit/cancel, but the *authorization* check for `EditRecurringOccurrenceCommand`/`CancelRecurringOccurrenceCommand` is always against the **master's** `CreatedById`, not the (possibly not-yet-existing) child row's — otherwise the first edit to a series nobody else has touched would incorrectly authorize based on a row that doesn't exist yet.

### API surface additions

| Method | Route | Purpose |
|---|---|---|
| `GET` | `api/v1/calendar?from=&to=` | Unchanged. Now also returns expanded virtual occurrences for recurring masters in range. |
| `PUT` | `api/v1/calendar/{id}/occurrence` | Body carries `originalStart`, `scope` (`this`/`all`), and the same edit fields as today's `PUT /api/v1/calendar/{id}`. `{id}` is the master's id (the frontend always knows the master id for a recurring occurrence — see the synthetic id note above; the request body's `originalStart` disambiguates which occurrence). |
| `DELETE` | `api/v1/calendar/{id}/occurrence?originalStart=` | Cancels one occurrence of the master `{id}`. |
| `PUT` `DELETE` | `api/v1/calendar/{id}` | Unchanged — used for plain manual events, detached-occurrence rows (edited directly by their own real id once detached), and "delete all events" on a master. |

---

## Part 2 — Frontend Architecture

New Angular feature module `src/app/modules/calendar/`, lazy-loaded, replacing the `{ path: 'calendar', loadComponent: loadPlaceholder, ... }` route entry with a `loadChildren` entry pointing at `calendar.routes.ts` — same shape as `people.routes.ts`/`leave.routes.ts` already use. No sidebar/nav-config change needed (`nav-items.config.ts`'s existing `calendar` entry already points at `/calendar` and is already gated by `requiredModules: ['calendar']`).

```
modules/calendar/
├── calendar.routes.ts
├── feature/
│   └── calendar-page/                 - the routed page: header + view switcher (Month only, wired for future views) + CalendarMonthGrid
├── ui/
│   ├── calendar-month-grid/            - pure presentational month grid: takes events + selected month, emits day-click/event-click
│   ├── calendar-day-cell/              - one grid cell: date number, up to N event chips, "+N more" overflow
│   ├── event-card-chip/                - the small colored chip shown in a day cell
│   ├── event-details-panel/            - read view when an event/occurrence is clicked (title, time, location, participants, edit/delete actions)
│   └── event-form-modal/               - create/edit form: title, description, start/end, all-day toggle, location, meeting link, color picker, recurrence dropdown, participant picker
├── data-access/
│   ├── calendar-event-api.service.ts   - thin HTTP wrapper: getEvents(from,to), create, update, editOccurrence, deleteEvent, cancelOccurrence
│   └── calendar-event.model.ts         - TS interfaces mirroring CalendarEventViewModel + the occurrence-edit request/response shapes
├── state/
│   └── calendar.store.ts               - signal-based store (matching this codebase's existing store pattern, e.g. leave-policy.store.ts): holds the active month's events, loading/error state, selected date; exposes load(from,to)/createEvent/editOccurrence/etc.
└── utils/
    └── recurrence-rule.util.ts         - pure functions: buildRruleString(frequency, endMode, untilDate) -> string | null, and a tiny display helper (e.g. "Weekly until Dec 31, 2026") for the event details panel
```

This mirrors the "view-independent core" the user asked for: `CalendarPageComponent` owns a `view: 'month' | 'week' | 'day' | 'agenda'` signal (only `'month'` is ever set in R1 — no UI control offers the others yet), and `calendar.store.ts`'s `load(from, to)` takes an arbitrary range, so adding Week/Day later means adding a new `ui/calendar-week-grid` component and a range calculation for it, with zero changes to `data-access`/`state`.

### Recurrence UI (R1)

`event-form-modal`'s recurrence control is a single dropdown: **Does not repeat / Daily / Weekly / Monthly**. Selecting anything but "Does not repeat" reveals an end-condition sub-control: **Never** / **On [date]**. `recurrence-rule.util.ts#buildRruleString` turns that into the RRULE string sent to the backend:
- Daily → `FREQ=DAILY` (+ `;UNTIL=<until>` if an end date was chosen)
- Weekly → `FREQ=WEEKLY` (+ `;UNTIL=<until>`) — no `BYDAY`; per RFC 5545, an RRULE with no `BYDAY` defaults to the weekday of `DTSTART`, which is exactly "repeats on the same day it started."
- Monthly → `FREQ=MONTHLY` (+ `;UNTIL=<until>`) — same reasoning, defaults to the day-of-month of `DTSTART`.

No "Custom" option exists in R1. This is a pure frontend-scope decision: the backend's `RecurrenceRule` column and `ICalendarRecurrenceExpander` already accept and correctly expand any valid RRULE, including a future `BYDAY=MO,WE,FR`-style value — adding a "Custom" builder later is a frontend-only addition (a richer `event-form-modal` control plus a richer `buildRruleString`), with no backend change required.

### Editing/cancelling an occurrence

When the details panel is opened for a virtual/recurring occurrence, Edit and Delete both prompt **"This event" / "All events"** (a two-option choice, not the three-option Google Calendar picker — "This and following" has no entry point in the R1 UI even though the backend command supports it). "This event" calls `PUT .../occurrence` / `DELETE .../occurrence`; "All events" calls the existing plain `PUT`/`DELETE .../{id}` against the master's id. For a plain manual event or an already-detached occurrence (no `recurrenceParentId` on the item the frontend received), Edit/Delete skip the prompt entirely and call the plain endpoints directly, matching today's Calendar Core behavior unchanged.

---

## Testing Strategy

- **Backend unit tests**: `ICalendarRecurrenceExpander` gets its own test class covering daily/weekly/monthly, with and without `UNTIL`, and a range that starts/ends mid-series. `GetCalendarEventsQueryHandlerTests` gains cases for a master-with-occurrences-in-range, a master with one detached override (verifies the virtual date is suppressed and the detached row's own values appear instead), and a master with one cancelled occurrence (verifies that date is absent entirely). `EditRecurringOccurrenceCommandHandlerTests` and `CancelRecurringOccurrenceCommandHandlerTests` cover create-vs-update-existing-child idempotency and the creator-only-on-the-master authorization rule. A dedicated `ThisAndFollowingSplitTests` (or a case group within the edit-command tests) verifies the old series' `UNTIL`, the new series' `RecurrenceRule`/`StartDate`, and that only children with `RecurrenceOriginalStart >= splitPoint` are re-parented.
- **Frontend unit tests**: `recurrence-rule.util.spec.ts` covers all four dropdown selections crossed with both end-conditions. `calendar.store.spec.ts` covers `load`/`createEvent`/`editOccurrence` against a mocked `CalendarEventApiService`, matching this codebase's existing store-test pattern. Component specs for `calendar-month-grid` (day layout, overflow chip) and `event-form-modal` (recurrence sub-control show/hide, validation) follow the existing Angular Testing Library conventions already used elsewhere in this repo.
- **Manual E2E** (same style as Calendar Core's verification): log in as the seeded `dapi` tenant owner, create a weekly recurring event with an end date, confirm it appears on the correct weekdays across two months in the UI, edit one occurrence "this event only" and confirm only that date changed, cancel one occurrence and confirm it disappears, then edit "all events" and confirm every remaining (non-detached) occurrence picks up the change.

## Open Items

- Exact 500-occurrence expansion cap is a starting guess, not load-tested; revisit if a real tenant hits it.
- The synthetic per-occurrence id scheme (deterministic GUID from `(masterId, occurrenceStart)`) needs to be pinned down as an exact algorithm (e.g. GUID v5 / name-based UUID over the two values) during planning, so both the backend generator and any frontend code that needs to recompute/compare ids agree byte-for-byte.
