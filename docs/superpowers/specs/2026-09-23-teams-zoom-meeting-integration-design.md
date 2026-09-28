# Microsoft Teams / Zoom Meeting Integration — Implementation Spec

**Goal:** Let an event organizer auto-generate a real Teams or Zoom meeting when creating a
calendar event (join link populates `calendar_events.MeetingLink` automatically), and track who
actually joined/how long once the meeting has happened.

**Architecture:** Standard MediatR CQRS (Domain/Application/Infrastructure/Api), tenant-scoped via
`TenantPolicy` + RLS, following the exact shape of the existing Calendar external-sync feature
(`docs/superpowers/specs/2026-08-28-calendar-core-external-sync-design.md`, PR #117). Reuses the
already-built per-employee OAuth connection model (`external_calendar_connections`,
`ICalendarOAuthTokenExchangeClient`, `IPlatformOAuthAppResolver`) rather than inventing a parallel
one — see "Why extend, not fork" below.

**Tech Stack:** .NET 10 / EF Core / PostgreSQL (RLS), MediatR, ASP.NET Data Protection (OAuth
state), `IEncryptionService` (token-at-rest), `BackgroundService` + `PeriodicTimer` (attendance
polling job, mirrors `CalendarSyncJob`).

**Spec history:** Brainstormed in chat 2026-09-23. Two prior decisions carried in from that
conversation and treated as settled requirements, not open questions:
1. Scope = auto meeting-link generation **and** attendance (join/leave) tracking, for both providers.
2. Connection model = **per-employee personal OAuth** (the organizer's own Microsoft/Zoom account
   hosts the meeting), reusing `external_calendar_connections` — not a company-wide platform
   credential.

## Global Constraints

- Tenant-side routes: `api/v1/calendar/...`, `[Authorize(Policy = "TenantPolicy")]`, reuse existing
  `calendar:read`/`calendar:write`/`calendar:admin` permission codes — no new permissions to seed.
- Token fields encrypted at rest via `IEncryptionService`, same as the existing
  `external_calendar_connections` columns — never returned by any API response.
- Snake_case DB columns via the existing EF convention.
- No Hangfire — background job shape mirrors `CalendarSyncJob`.
- Branch off current `development` (unlike the original Calendar spec, `development` is healthy
  now — no known reintroduced-bug branch-point caveat applies here).

---

## Scope

**In scope (this spec, Phase 1 — Microsoft Teams):**
1. Extend `external_calendar_connections`' existing Microsoft OAuth connection to also cover
   online-meeting creation (incremental scope, same connection row).
2. `calendar_event_meetings` + `calendar_event_meeting_attendances` tables (provider-agnostic —
   built to also hold Zoom rows in Phase 2, see below).
3. Create/remove a Teams meeting for a calendar event; `MeetingLink` auto-populates.
4. Background job polling Microsoft Graph's per-meeting attendance report once a meeting's
   scheduled end has passed; writes attendance rows.
5. Event-form UI: "Add Teams meeting" action, reconnect prompt when the organizer's Microsoft
   connection lacks the new scope, read-only auto-link display, "Attendance" section on the event
   detail view post-meeting.

**Explicitly out of scope for this spec's *implementation* (Phase 2 — Zoom):**
- `IZoomMeetingClient` (Zoom REST API meeting creation).
- Confirming Zoom's exact current OAuth scope names for meeting creation — **now resolved, see
  below**; implementation itself is still Phase 2, not built in this spec.

**Zoom OAuth scopes — verified live 2026-09-24** against a real Zoom Marketplace "General App"
(User-managed, Client secret auth) created for this project. Supersedes the earlier placeholder
(`PlatformOAuthProviderCatalog`'s existing `zoom` entry only lists `meeting:read`, which is
insufficient for creating a meeting). The correct scopes for this feature:
- `meeting:write:meeting` — create a meeting for a user.
- `meeting:delete:meeting` — cancel/remove a meeting (mirrors `RemoveEventMeetingCommand`).
- `meeting:read:meeting` — read meeting details/join URL after creation.
- `meeting:read:list_past_participants` — list a past meeting's participants. This is the Zoom
  equivalent of Microsoft Graph's attendance-report endpoint and is what makes the Teams
  polling-job architecture directly reusable (see "Attendance sync" decision below).
- Deliberately **not** requested: `meeting:read:participant` (live/in-progress participant list) —
  unnecessary since attendance is only synced after `EndDate` has passed, same as Teams.

**Attendance sync architecture decision — webhook dropped, polling reused instead.** The original
placeholder above called for "Zoom's real-time attendance webhook endpoint
(`meeting.participant_joined`/`participant_left`, signature validation, CRC challenge-response) —
a new *public*, non-tenant-scoped controller." That is no longer needed:
`meeting:read:list_past_participants` gives a pull-based, post-meeting participants list — the
direct Zoom analogue of the Graph attendance-report call `TeamsAttendanceSyncJob` already polls.
Phase 2's attendance sync should mirror `TeamsAttendanceSyncJob` exactly (a `ZoomAttendanceSyncJob`
polling `calendar_event_meetings` where `Provider = "zoom"`, `Status = "active"`, event `EndDate <
now`), avoiding an entire new public webhook surface (signature verification, CRC handshake,
Event Subscriptions configuration) for no architectural benefit.

**Blocked on:** a real Zoom Marketplace OAuth app (client id/secret) — **now created** (Development
app "General app 34", User-managed, scopes above configured, OAuth redirect URL registered as
`https://localhost:7229/api/v1/calendar/connections/zoom/callback` for local dev testing; a
Production-tier app + redirect URL will be needed before this ships to real users). The data
model, `Provider` enum value (`"zoom"`), and job/table shapes are Phase-2-ready by design so Phase
2 is additive (new client + new background job, no new webhook controller), not a rework.

**Explicitly out of scope, full stop (no phase):**
- Wiring meeting attendance into the Work Pattern card's "Meeting time" metric (currently a
  process-detection heuristic via `MeetingSignal` — see `GetMyWorkPatternQueryHandler`). Real
  attendance data from this feature is a natural future input to that number, but reconciling two
  independent measurement sources is its own design decision and a separate follow-up spec.
- Recurring-meeting series (one Teams/Zoom meeting per recurrence occurrence vs. one shared
  meeting) — Phase 1 covers single (non-recurring) events only. Recurring events keep manual
  `MeetingLink` entry for now; a follow-up spec should decide the per-occurrence-vs-shared question
  once the base flow is proven.

---

## Why extend `external_calendar_connections`, not fork a new table (Approach A vs B)

Considered a parallel `meeting_provider_connections` table (clean semantic separation: "calendar
sync" vs "meeting creation" are different capabilities even on the same Microsoft account) against
extending the existing table (adds a `"zoom"` provider value + scope, reuses 90%+ of the OAuth
state-protector/token-exchange/encryption/callback-controller plumbing already built and tested).

**Chosen: extend.** The columns a meeting-only connection doesn't need
(`SyncDirection`/`SyncToken`/`DeltaLink`) are already nullable, so the cost is zero, and the
alternative buys no real benefit here — it only duplicates already-correct code. One "Connect
Microsoft" button on the Connections screen requests calendar-sync *and* meeting scopes together;
the employee reconnects once, not twice.

---

## Data Model

### `external_calendar_connections` (existing table — additive changes only)

- `Provider` gains `"zoom"` as a valid value (column is already `varchar`, no migration needed for
  this alone — Phase 2 work).
- No new columns. `Scopes` (existing `jsonb`) already records whatever scopes were actually
  granted, which the attendance/create handlers check before attempting a Graph call.

### `PlatformOAuthProviderCatalog` (existing catalog — additive changes only, Application layer)

- `microsoft` entry's `DefaultScopes` gains `"OnlineMeetings.ReadWrite"` (currently:
  `openid, profile, email, offline_access, User.Read, Calendars.ReadWrite`).
- New capability constant `CapabilityMeetings = "meetings"`, added to `microsoft`'s `Capabilities`
  array now (Phase 1) and `zoom`'s (Phase 2, when its scopes are corrected).
- **Not touched in Phase 1**: `zoom`'s `DefaultScopes` (still needs the real scope-name
  verification noted under Scope, above, before Phase 2 can safely change it).

### New table: `calendar_event_meetings`

One row per event that has an auto-generated meeting attached (not every event — manual
`MeetingLink` entry with no row here remains fully supported, unchanged).

```
Id                            uuid PK
TenantId                      uuid
CalendarEventId               uuid FK -> calendar_events   (unique — one auto meeting per event)
ExternalCalendarConnectionId  uuid FK -> external_calendar_connections
Provider                      varchar(30)   -- "microsoft_teams" | "zoom" (zoom unused until Phase 2)
ExternalMeetingId             varchar(255)  -- Graph onlineMeeting id / (Phase 2) Zoom meeting id
JoinUrl                       varchar(500)
OrganizerJoinUrl              varchar(500)? -- Graph's organizer-specific join link, if distinct
PasscodeOrPin                 varchar(50)?
Status                        varchar(20)   -- "active" | "cancelled" | "failed"
LastAttendanceSyncedAt        timestamptz?
CreatedAt/UpdatedAt           timestamptz
```

On successful creation, the handler also sets `calendar_events.MeetingLink = JoinUrl` — every
existing reader of `MeetingLink` (the event list, the reminder/notification pipeline, the
meeting-link URL validation added earlier) needs zero changes, since this table is purely
*additional* metadata alongside the field they already read.

RLS: standard `tenant_isolation` policy, same pattern as every other tenant table. No soft-delete
(mirrors `external_calendar_connections`/`external_calendar_event_link` — hard-cancelled instead).

### New table: `calendar_event_meeting_attendances`

```
Id                        uuid PK
TenantId                  uuid
CalendarEventMeetingId    uuid FK -> calendar_event_meetings
EmployeeId                uuid?  FK -> employees   -- null = external/unmatched participant
ExternalParticipantName   varchar(200)?
ExternalParticipantEmail  varchar(255)?
JoinedAt                  timestamptz
LeftAt                    timestamptz?
DurationSeconds           int?
CreatedAt                 timestamptz
```

`EmployeeId` resolution: match `ExternalParticipantEmail` against `employees.email` at ingest
time (best-effort — an external guest or an email mismatch simply leaves `EmployeeId` null and the
row still displays by name/email on the event's Attendance section).

### Domain layer file locations

```
ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs
ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeetingAttendance.cs
```

---

## API / Application layer

### Commands

- **`CreateEventMeetingCommand(EventId, Provider)`**
  - Loads the event, verifies the caller is the organizer (or has `calendar:write` on it, matching
    existing event-edit authorization).
  - Loads the organizer's active `external_calendar_connections` row for the given provider.
    - No connection, or connection `Status != "active"`, or `Scopes` missing
      `OnlineMeetings.ReadWrite` → `Result.Conflict("meeting_provider_not_connected")` (Phase 1;
      the "reauth_required" status already models an expired/insufficient-scope connection, so
      existing connections just need the employee to reconnect once after this ships).
  - Calls `ITeamsMeetingClient.CreateMeetingAsync(accessToken, event.Title, event.StartDate,
    event.EndDate, ct)` → Graph `POST /me/onlineMeetings`.
  - Persists `CalendarEventMeeting`, sets `calendar_events.MeetingLink`.
  - Token refresh: reuses the existing `ICalendarOAuthTokenExchangeClient.RefreshTokenAsync` path
    already wired for calendar sync — no new refresh logic needed.

- **`RemoveEventMeetingCommand(EventId)`**
  - Calls `ITeamsMeetingClient.CancelMeetingAsync`, sets `CalendarEventMeeting.Status =
    "cancelled"`, clears `calendar_events.MeetingLink` (organizer can still type a manual link
    afterward — the field goes back to being a plain free-text input once no active auto-meeting
    row exists for that event).
  - Also called from the existing event-delete flow so a deleted event doesn't leave an orphaned
    live Teams meeting.

### New Infrastructure interface

```csharp
// ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs
public sealed record TeamsMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public interface ITeamsMeetingClient
{
    Task<TeamsMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct);
}
```

Implemented in `ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs`,
sibling to the existing `MicrosoftGraphCalendarClient.cs` (same `HttpClient`/auth-header pattern).

### Background job: attendance polling

`TeamsAttendanceSyncJob` (`BackgroundService` + `PeriodicTimer`, mirrors `CalendarSyncJob`'s
shape): every run, selects `calendar_event_meetings` where `Status = "active"`, the parent event's
`EndDate < now`, and `LastAttendanceSyncedAt` is null — calls
`ITeamsMeetingClient.GetAttendanceAsync`, upserts `calendar_event_meeting_attendances`, stamps
`LastAttendanceSyncedAt`.

**Known constraint to carry into the plan, not solve here:** Graph's attendance-report endpoint
needs `OnlineMeetingArtifact.Read.All`. Depending on the tenant's actual Microsoft 365/Teams
licensing and admin-consent posture, this may require **application-level** admin consent (not
just the organizer's own delegated consent) — this must be verified against a real Microsoft 365
tenant during implementation; if delegated-only access proves insufficient, the job degrades to
"meeting link + no attendance data" for that connection rather than failing the whole feature, and
the UI's Attendance section shows "Attendance data unavailable" instead of an error.

---

## Frontend

- Event form modal: "Add video call ▾" control, showing "Microsoft Teams" only when the organizer
  has an active, sufficiently-scoped Microsoft connection; otherwise a "Connect Microsoft to add a
  Teams meeting" prompt linking to the existing Connections settings screen. (Zoom option hidden
  entirely until Phase 2 ships.)
- Once created: `MeetingLink` becomes a read-only chip showing the join URL + a "Remove" action,
  replacing the free-text input for that event. Manual free-text entry is untouched for events
  with no auto-meeting attached — no change to the meeting-link validator built earlier
  (`CalendarEventValidation.IsValidMeetingLink`), since an auto-generated URL trivially satisfies
  it.
- New "Attendance" section on the event detail view, visible only after the event's end time has
  passed and a `calendar_event_meetings` row exists: participant name/email, joined/left time,
  duration; "Attendance data unavailable" empty state per the constraint above.

---

## Error Handling

- No connection / insufficient scope → `meeting_provider_not_connected` conflict, frontend shows
  the reconnect prompt (not a generic error toast).
- Graph API failure on create → event still saves; `CreateEventMeetingCommand` fails independently
  (the event-save and meeting-create are two separate requests from the UI, not one transaction),
  toast error, `MeetingLink` stays empty/manual, retry available from the event detail view.
- Attendance sync failure for one meeting → logged, that meeting's `LastAttendanceSyncedAt` stays
  null so the job retries it next run; does not block other meetings in the same job run (mirrors
  `CalendarSyncJob`'s existing per-connection failure isolation).

---

## Testing

Mirrors the existing Calendar feature's test shape:
- Handler unit tests for `CreateEventMeetingCommandHandler`/`RemoveEventMeetingCommandHandler`
  with a mocked `ITeamsMeetingClient` (pattern: `CompleteCalendarConnectionCommandHandlerTests.cs`).
- `MicrosoftGraphMeetingClientTests.cs` mirroring `MicrosoftGraphCalendarClientTests.cs`.
- `TeamsAttendanceSyncJobTests.cs` mirroring `CalendarSyncJobTests.cs` (per-meeting failure
  isolation, `LastAttendanceSyncedAt` stamping, "no meetings due" no-op case).
- Repository tests for both new tables (`EfCalendarEventMeetingRepositoryTests.cs`,
  `EfCalendarEventMeetingAttendanceRepositoryTests.cs`), following
  `EfExternalCalendarConnectionRepositoryTests.cs`'s pattern.
- Frontend: event-form-modal spec additions for the "Add video call" control's visibility states
  (connected / not connected / reauth required), plus a new Attendance-section component spec.
