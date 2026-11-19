# Calendar Small Fixes — Design Spec

**Status:** Approved for planning
**Date:** 2026-09-04
**Scope:** Sub-project 1 of 4 in the Calendar gap-closure effort (see "Deferred Sub-projects" below for the rest)

## Why

An agent-led gap analysis compared the Calendar module's planned spec (`2nd brain/OneVo-HR/modules/calendar/{overview,calendar-events/overview,conflict-detection/overview}.md`) against the actual codebase and found 10 gaps. Given the total scope, the work is split into 4 independently-shippable sub-projects, sequenced smallest/fastest first. This spec covers only the first: four small, mostly-independent fixes that close real product gaps without requiring new external integrations (OAuth, cross-module outbox wiring) — those are separate, larger sub-projects.

## Scope of this sub-project

1. Real country holiday sync (Nager Holidays API), replacing two no-op providers
2. Replace the conflict-check endpoint with the planned contract
3. Wire up the two missing RSVP recipient actions
4. Show conflict markers directly on the calendar grid

## Out of scope for this sub-project (deferred sub-projects, not designed here)

- Time-Off ↔ Calendar conflict integration (`NoOpLeaveRequestConflictProvider` → real) and Leave-approval → Calendar auto-event (`NoOpLeaveApprovalSideEffectOutboxHandler` → real)
- Google/Outlook Calendar OAuth two-way sync (`external_calendar_connections`, `external_calendar_event_links`, OAuth flow, sync scheduler)
- Task due-date planning chips / worked-time blocks projected onto Calendar from Work Management
- Schedule/shift overlays on Calendar

---

## 1. Holiday Sync

### Data model

New table `holiday_calendar_settings` (matches the original plan exactly):

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | FK -> tenants |
| `legal_entity_id` | uuid | FK -> legal_entities |
| `default_country_code` | char(2) | From legal entity country |
| `override_country_code` | char(2) | Nullable admin override |
| `effective_country_code` | char(2) | Computed: override ?? default |
| `holiday_sync_enabled` | boolean | Default true |
| `provider` | varchar(30) | `nager_holidays` (only value for now) |
| `last_synced_year` | integer | Nullable |
| `last_synced_at` | timestamptz | Nullable |
| `updated_by_id` | uuid | FK -> users |
| `created_at` / `updated_at` | timestamptz | |

Unique index on `(tenant_id, legal_entity_id)` — one settings row per legal entity.

### Provider implementation

Replace both no-op registrations with one real implementation:

```csharp
public sealed class NagerHolidaysProvider : ILeaveHolidayProvider, ILeaveCalendarHolidayProvider
{
    // GET https://nagerholidays.com/api/v4/Holidays/{countryCode}/{year}
    // No API key, no rate limit (per vendor docs, confirmed 2026-09-04)
}
```

- HttpClient registered via `IHttpClientFactory` (named client `"NagerHolidays"`), base address `https://nagerholidays.com/api/v4/`.
- Response DTO maps `date`, `name`, `countryCode`, `nationalHoliday` → only `nationalHoliday == true` entries are imported (subdivision-level/optional/bank holidays excluded from Phase 1, matches "public holidays" framing in the original plan).
- Imported holidays are written as `personal_calendar_events` rows: `source_type = 'holiday'`, `external_source = 'country_holiday'`, `is_all_day = true`, `title = <holiday name>`, tenant-scoped, `created_by_id` = system/service account.
- Sync is idempotent: re-syncing the same year deletes and re-inserts that legal entity's `holiday`-sourced events for that year (avoids duplicate rows on repeated sync), matched by `(tenant_id, source_type='holiday', external_source='country_holiday', extract(year from start_date))`.

### Trigger points

- **Automatic**: when a legal entity's country is set/changed (existing `LegalEntityCountrySet` — currently unconsumed by Calendar per the gap analysis; this sub-project adds the first real consumer of it, scoped only to creating/updating the `holiday_calendar_settings` row, not a full outbox rework), a `holiday_calendar_settings` row is created with `default_country_code` from the legal entity, and the current year is synced immediately.
- **Manual**: `POST /api/v1/calendar/holiday-settings/{id}/sync?year={year}` — admin-triggered re-sync (e.g. for the upcoming year, or after changing the override country).

### API endpoints (new)

| Method | Route | Permission | Description |
|---|---|---|---|
| GET | `/api/v1/calendar/holiday-settings` | `calendar:admin` | Get the current legal entity's holiday settings |
| PUT | `/api/v1/calendar/holiday-settings/{id}` | `calendar:admin` | Enable/disable sync, set override country |
| POST | `/api/v1/calendar/holiday-settings/{id}/sync` | `calendar:admin` | Sync a given year now |

### Error handling

- Nager Holidays unreachable/non-200: sync fails with a clear error surfaced to the admin UI ("Could not reach the holiday provider — try again"); `last_synced_at` is not updated; existing holiday events are left untouched (no partial-delete-then-fail).
- Unknown/unsupported country code: Nager Holidays returns an empty array for a code it doesn't recognize (not an error) — surface a distinct "No holidays found for this country" message rather than treating it as success with zero rows silently.

### Testing

- Unit: `NagerHolidaysProvider` against a mocked `HttpMessageHandler` (success, empty response, 500, timeout).
- Unit: sync command handler — idempotent re-sync (no duplicate rows), respects `holiday_sync_enabled = false` (no-op).
- Integration: `POST .../sync` end-to-end against the real Nager Holidays API for one real country/year, gated behind an integration-test category so it doesn't run on every CI build against a live third-party API.

---

## 2. Conflict Endpoint Replacement

### Current state (being replaced)

`POST /api/v1/calendar/check-conflicts` → `CheckCalendarConflictsQueryHandler` → returns `CalendarConflict(EmployeeId, EmployeeName, ConflictingEventId, ConflictingEventTitle)` — no overlap time range.

### New contract

`GET /api/v1/calendar/conflicts?employeeId={id}&startAt={iso}&endAt={iso}`

Response: `CalendarConflictSummaryDto`:
```json
{
  "conflictCount": 2,
  "conflicts": [
    {
      "eventTitle": "Payroll review",
      "overlapStart": "2026-04-10T09:00:00+05:30",
      "overlapEnd": "2026-04-10T09:30:00+05:30",
      "ownerName": "Finance Manager"
    }
  ]
}
```

- `overlapStart`/`overlapEnd` are the intersection of the query range and the conflicting event's range (not the conflicting event's full range) — computed as `max(queryStart, eventStart)` / `min(queryEnd, eventEnd)`.
- `ownerName` resolves to the conflicting event's creator/organizer.
- The existing employee-scoping and recurring-event-expansion logic in `CheckCalendarConflictsQueryHandler` is preserved; only the query shape (GET + params instead of POST + body) and response DTO change.

### Migration approach (full replace, per user decision)

- `POST /check-conflicts` is deleted, not deprecated-and-kept — this is a pre-launch product still in active development, so no external consumers to protect.
- Frontend `calendar-event-api.service.ts`'s `checkConflicts()` method is updated in the same change to call the new `GET /conflicts` and consume the new response shape.
- Frontend conflict-warning banner in `calendar-event-form-modal.component.ts` is updated to read `overlapStart`/`overlapEnd` for a more specific message (e.g. "busy 9:00–9:30 AM", not just "busy").

### Testing

- Backend: handler unit tests — overlap-range computation (partial overlap, full containment, adjacent-non-overlapping), recurring-event expansion still works, multi-conflict response.
- Frontend: `calendar-event-api.service.spec.ts` updated for the new call shape; `calendar-event-form-modal.component.spec.ts` updated for the new banner text.

---

## 3. RSVP — Resolution Request & Nominate Replacement

### Backend

`RespondToCalendarEventCommandHandler` currently maps only `"Accepted"`/`"Rejected"`. Add:

- `"ResolutionRequested"` → sets participant `response_status = 'resolution_requested'`, requires a `response_reason` (message to the organizer), keeps the invitation pending (does not resolve it).
- `"ReplacementNominated"` → sets `response_status = 'replacement_nominated'`, requires a `nomineeEmployeeId` on the command. Validation: the nominee must be an "eligible nominee" — defined here as another participant already on the same event's `calendar_event_participants` list, or (if none) any employee sharing the responder's manager/department, matching the original plan's "nominee picker is scoped by calendar rules and delegation configuration" — this sub-project implements the simplest defensible scoping (existing co-participants first, department-mates as fallback) rather than building a full delegation-configuration system, which is out of scope here.
- Both actions create an Inbox item for the event organizer (reusing whatever Inbox/notification mechanism the codebase already uses for other cross-user actions — confirm the existing pattern during implementation rather than inventing a new one).
- Neither action auto-cancels, auto-moves, or auto-reassigns the event — matches the "no automatic revocation" rule.

### API

`POST /api/v1/calendar/{id}/respond` (existing endpoint) — request body gains optional `reason` (for resolution-requested) and optional `nomineeEmployeeId` (for replacement-nominated), both validated conditionally on `status`.

New read endpoint to power the nominee picker:
`GET /api/v1/calendar/{id}/eligible-nominees` — returns co-participants + department-mates per the scoping rule above, so the frontend doesn't have to reimplement that logic.

### Frontend

- `calendar-event-form-modal.component`: replace the current two-button (Accept/Decline) RSVP row with Accept / Decline / **More** (dropdown: "Request conflict resolution", "Nominate replacement").
- "Request conflict resolution" opens a small reason-text prompt, then calls respond with `ResolutionRequested`.
- "Nominate replacement" calls the new eligible-nominees endpoint; if the list is empty, the "Nominate replacement" option is not shown at all (per the plan: "Do not show disabled nominee text or an explanation") — if non-empty, shows a picker, then calls respond with `ReplacementNominated`.

### Testing

- Backend: handler unit tests for both new status transitions, reason-required validation, nominee-eligibility validation, Inbox item creation.
- Frontend: component tests for the More menu appearing/not appearing based on nominee-eligibility response, and both new flows submitting the right payload.

---

## 4. Conflict Markers on the Grid

### Approach

- `GetCalendarEventsQueryHandler` (or the frontend, client-side) computes, for each event returned in a date-range query, whether it has ANY conflicting event in the same range — a lightweight boolean, not the full conflict detail (full detail is fetched on-demand via the `/conflicts` endpoint only when the user opens that event, per #2).
- Simplest implementation: reuse the same overlap-detection logic as `CheckCalendarConflictsQueryHandler`, but run it once for the whole visible range and tag events with `hasConflict: boolean` in the `GET /api/v1/calendar` response, rather than requiring N client-side round-trips.
- Frontend: `calendar-month-grid`, `calendar-week-view`, `calendar-day-view`, `calendar-agenda-view` event-chip templates render a small red dot/border when `event.hasConflict` is true (visual spec: consistent with the existing design-token system — a border or dot in `var(--color-danger)`, not a new color).
- Clicking a conflict-marked chip still opens the normal event modal, where the full conflict detail (from #2) is shown — the grid marker is a lightweight "heads up," the modal is where the detail lives.

### Testing

- Backend: `GetCalendarEventsQueryHandler` test — events with overlapping ranges get `hasConflict: true`, non-overlapping get `false`.
- Frontend: grid component tests — conflict-marked event renders the visual indicator; non-conflicted doesn't.

---

## Deferred Sub-projects (separate specs, not designed here)

1. **Time-Off Integration** — replace `NoOpLeaveRequestConflictProvider` (wire real Calendar conflict data into Time Off submission) and `NoOpLeaveApprovalSideEffectOutboxHandler` (approved Time Off creates a Calendar event).
2. **Google/Outlook OAuth Sync** — `external_calendar_connections`, `external_calendar_event_links` tables, OAuth connect/callback/disconnect flow, sync-token/delta-link handling, two-way sync scheduler. Largest of the four sub-projects.
3. **Work Management Overlays** — task due-date planning chips and timer-based worked-time blocks projected onto Calendar (read-time only, no `calendar_events` rows).
4. **Schedule/Shift Overlays** — fixed/flexible work-time day overlays from Time & Attendance schedules, with timezone-aware display.
