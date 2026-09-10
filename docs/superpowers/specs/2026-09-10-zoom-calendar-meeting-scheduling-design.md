# Zoom Meeting Scheduling for the Calendar

Status: Draft — awaiting review
Date: 2026-09-10
Repos touched: `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`

## Problem

When a user schedules a calendar event today, the only way to attach a video
call is to paste a link into the free-text `meetingLink` field by hand. There
is no way to have the platform create the meeting for them. We want: scheduling
an event in the calendar can create a Zoom meeting through the Zoom API and
attach its join link to the event, and keep that Zoom meeting in step with
later edits and deletions.

## Goals (Phase 1, locked)

- A user connects their own Zoom account once (per-user OAuth), then can toggle
  "Add Zoom meeting" while creating a calendar event.
- On create, a Zoom meeting is created **in that user's Zoom account** and its
  `join_url` is stored on the event and shown in the UI and the invite email.
- Editing the event's time / title / duration re-syncs the Zoom meeting,
  including the recurring-edit scopes `AllEvents` and `ThisEventOnly`.
- Deleting the event, or cancelling a single occurrence, deletes the
  corresponding Zoom meeting.
- A recurring series maps to **one** Zoom recurring meeting (one join link for
  the whole series).
- Zoom being unreachable never blocks the calendar: the event always saves; a
  failed Zoom call is surfaced as a non-blocking warning with Retry / Reconnect
  and is retried automatically by the outbox.

## Non-goals (explicitly out of Phase 1)

- Inbound Zoom webhooks (`meeting.deleted`, `meeting.updated`, recording ready).
- Cloud-recording links on the event.
- `ThisAndFollowing` recurring-edit scope (not exposed in the frontend today).
- Google Meet / Microsoft Teams providers (Phase 3 — the abstraction below
  leaves room for them).
- Org-level Server-to-Server Zoom (one shared account). Phase 3 fallback for
  users with no personal Zoom.
- Two-way calendar sync with Google/Outlook — that is a separate, larger
  feature; see `2026-08-26-calendar-outlook-google-integration-notes.md`. This
  design shares only the OAuth / integration-credential framework with it.

## Decisions carried in from brainstorming

| Topic | Decision |
| --- | --- |
| Auth model | Per-user OAuth. Meetings are created as the connecting user, in their Zoom account. Users without Zoom connected get the manual link field, unchanged. |
| Recurring mapping | One Zoom recurring meeting per series. If the series exceeds Zoom's limit (~1 year / 60 occurrences), fall back to a single "recurring, no fixed time" Zoom meeting — still one reusable join link. |
| `ThisEventOnly` edit | The detached occurrence gets its **own** standalone Zoom meeting in Phase 1 (provisioned on edit, deleted on cancel). |
| Failure behaviour | Non-blocking. Event saves; `conference_status = failed`; warning + Retry / Reconnect; outbox auto-retry with backoff. |
| Provider shape | A generic `IConferencingProvider` abstraction with Zoom as the first (only) implementation, so Meet / Teams slot in later without touching the calendar handlers. |

## OAuth scopes — what each is for

Requested at the OAuth **authorize** step by this feature's connect flow. We do
**not** widen `PlatformOAuthProviderCatalog`'s `zoom` default (`meeting:read`) —
same precedent as GitHub, whose catalog comment says broader scopes must be
requested by the consuming feature, not the provider-wide default.

| Scope | Why it is needed | Zoom API calls |
| --- | --- | --- |
| `user:read` | Identify the connected account on connect and after each token refresh; store `providerEmail`; the meeting host is this user. | `GET /v2/users/me` |
| `meeting:write` | Create the Zoom meeting when an event is scheduled with Zoom; reschedule it on event edit (`AllEvents` re-patches the series meeting); delete it on event delete / occurrence cancel. | `POST /v2/users/me/meetings`, `PATCH /v2/meetings/{id}`, `DELETE /v2/meetings/{id}` (with `?occurrence_id=` for a single occurrence) |
| `meeting:read` | Read a meeting back for the Retry / repair path and for status display; list a recurring meeting's occurrences to resolve the `occurrence_id` for a single-occurrence cancel. | `GET /v2/meetings/{id}`, `GET /v2/meetings/{id}` (occurrences in the body) |

Granular-scope equivalents, if the Zoom app is created with granular scopes:
`user:read:user`, `meeting:write:meeting`, `meeting:update:meeting`,
`meeting:delete:meeting`, `meeting:read:meeting`, `meeting:read:list_meetings`.

## Architecture

### 1. Per-user Zoom connection (reuses the existing integration framework)

- New controller `ZoomIntegrationController` at `api/v1/integrations/zoom`,
  `[Authorize(Policy = "TenantPolicy")]`, mirroring `GitHubIntegrationController`
  action for action: `POST connect/start`, `GET connect/callback`, `GET status`,
  `POST disconnect`, `POST refresh`.
- Tokens stored in the existing `UserIntegrationConnection` entity
  (`integrationKey = "zoom"`): `AccessTokenEncrypted`, `RefreshTokenEncrypted`,
  `TokenExpiresAt`, `ScopesGranted`, `ProviderUserId`, `ProviderEmail`,
  `Status`, `ErrorMessage`. No new table.
- Encryption: the same encryption service the GitHub `CompleteGitHubUserOAuth`
  handler uses for `UserIntegrationConnection` — string in, string out, matching
  the existing column types on that table. (This differs from the `bytea`
  columns planned for the separate external-calendar-sync tables; we follow the
  table we are reusing.)
- OAuth `state`: reuse `IOAuthStateProtector`. The state record
  (`GitHubOAuthState`) is renamed to a shared `OAuthConnectState` (fields are
  already provider-agnostic: nonce, tenant, user, integrationKey, provider,
  returnUrl, timestamps, sessionBinding). Small mechanical refactor; GitHub
  keeps working.
- Return-URL validation: extract `GitHubUserOAuthRules.ValidateReturnUrl`
  (relative-path only, no `//`, no `\`) into a shared
  `OAuthReturnUrlRules.Validate`.
- `IZoomOAuthClient` (typed `HttpClient`, `AddHttpClient<IZoomOAuthClient,
  ZoomOAuthClient>`): `ExchangeCodeAsync`, `RefreshTokenAsync`,
  `GetCurrentUserAsync`. Zoom specifics: token endpoint uses HTTP Basic
  (`base64(clientId:clientSecret)`); **the refresh token rotates on every
  refresh — the new one must be persisted each time**; access token lives ~1 h,
  refresh token ~90 days.
- Client id / secret come from `platform_oauth_apps` (provider `zoom`),
  configured by an operator through the existing DevPlatform "OAuth Apps"
  screen. Protocol metadata (authorize / token URLs) is already in
  `PlatformOAuthProviderCatalog`.
- `integration_catalog` seed row: `integration_key = "zoom"`,
  `connection_scope = "user"`, `onevo_app_provider = "zoom"`,
  `display_name = "Zoom"`, `is_active = true`.

### 2. Conferencing provider abstraction

```
// Application/Features/Calendar/ServiceInterfaces
interface IConferencingProvider
{
    string Key { get; }  // "zoom"
    // hostUserId is the user whose provider connection owns the meeting. On create
    // it is the caller; on update/delete it is read from conference_host_user_id.
    Task<ConferenceMeeting> CreateAsync(ConferenceMeetingRequest req, Guid hostUserId, CancellationToken ct);
    Task<ConferenceMeeting> UpdateAsync(string externalId, ConferenceMeetingRequest req, Guid hostUserId, CancellationToken ct);
    Task DeleteAsync(string externalId, Guid hostUserId, string? occurrenceId, CancellationToken ct);
}

record ConferenceMeetingRequest(
    string Title, DateTimeOffset StartUtc, TimeSpan Duration, string Timezone,
    ConferenceRecurrence? Recurrence, string? Agenda);

record ConferenceRecurrence(
    ConferenceFrequency Frequency,      // Daily | Weekly | Monthly
    DateTimeOffset? Until,              // from RRULE UNTIL
    bool NoFixedTime);                  // true -> Zoom "recurring, no fixed time"

record ConferenceMeeting(string ExternalId, string JoinUrl, string? StartUrl, string? HostEmail);

interface IConferencingProviderResolver
{
    IConferencingProvider ForEvent(CalendarEvent e);   // Phase 1: always the "zoom" provider
}
```

- `ZoomConferencingProvider` (Infrastructure) implements `IConferencingProvider`
  by calling `IZoomMeetingsClient` and resolving/refreshing the caller's
  `UserIntegrationConnection` token (see token handling below).
- `IZoomMeetingsClient` (typed `HttpClient`): `CreateMeetingAsync`,
  `UpdateMeetingAsync`, `DeleteMeetingAsync`, `GetMeetingAsync`. Talks to
  `https://api.zoom.us/v2`.

### 3. Lifecycle via the transactional outbox

New outbox message types (alongside `CalendarEventInviteEmail`):

| Type | Payload | Handler action |
| --- | --- | --- |
| `ConferenceMeetingProvision` | tenantId, eventId, hostUserId | Create the Zoom meeting; patch `MeetingLink`, `ExternalId`, `ConferenceProvider`, `ConferenceStartUrl`, `ConferenceStatus = active`, `ExternalUpdatedAt`. Idempotent: no-op when the event already has a non-null `ExternalId`. |
| `ConferenceMeetingUpdate` | tenantId, eventId, hostUserId | `UpdateAsync(existing ExternalId, …)`; bump `ExternalUpdatedAt`. |
| `ConferenceMeetingCancel` | tenantId, externalId, hostUserId, occurrenceId? | `DeleteAsync`. Event row may already be gone — payload carries `externalId` and `hostUserId` directly, not `eventId`. |

`hostUserId` is the meeting owner's user id: the caller on create, and
`conference_host_user_id` (read from the row before it changes/deletes) on
update and cancel.

- **Create path** also does an *eager, best-effort* provision immediately after
  the event transaction commits, so the join link is normally present on the
  create response. If that inline call fails or times out (short timeout, e.g.
  5 s), the event still returns with `conferenceStatus = pending` and the
  outbox message drives the retry. `SyncHolidayCalendarCommandHandler` is the
  precedent for a direct external call from a handler; notifications are the
  precedent for outbox staging.
- **No double-create:** the `ConferenceMeetingProvision` row is enqueued only
  when the eager attempt did **not** already set the event `active`; the outbox
  handler additionally no-ops when the event's `ExternalId` is already set. A
  short claim window on the outbox row (existing `OutboxProcessor` behaviour)
  covers the remaining race between an in-flight eager call and the worker.
- **Update / delete / cancel-occurrence** paths only enqueue — never inline —
  so a slow Zoom never slows down editing the calendar.
- All enqueues ride the handler's existing `SaveChangesAsync` (the outbox
  writer only stages rows), exactly like `CalendarNotificationSender`.

### 4. Token handling

- Before every `IZoomMeetingsClient` call, `ZoomConferencingProvider` checks
  `TokenExpiresAt`; if it is within a 5-minute skew, it refreshes inline and
  persists the new access token, **new rotated refresh token**, and expiry, on
  the `UserIntegrationConnection` row, guarded by optimistic concurrency on that
  row (two concurrent provision jobs must not both consume the old refresh
  token).
- `ZoomTokenRefreshJob : BackgroundService` with a 15-minute `PeriodicTimer`,
  registered via `AddHostedService<T>()` — **no Hangfire** (codebase convention;
  templates: `AgentCommandExpiryJob.cs`, `SprintLifecycleJob.cs`). It
  proactively refreshes connections expiring within the next hour and, on a
  failed refresh (revoked / expired refresh token), sets `Status =
  needs_reconnect` and `ErrorMessage`. Because `UserIntegrationConnection` is
  tenant-scoped (RLS) and the job runs outside a request, it combines the
  `BackgroundService` + `PeriodicTimer` shape with the per-tenant
  `tenantContext.Resolve(...)` loop (pattern from
  `WorkManagementSampleDataSeeder`), `SaveChangesAsync` per tenant.

### 5. Recurrence mapping (RRULE -> Zoom)

Our `buildRecurrenceRule` only ever emits
`FREQ=DAILY|WEEKLY|MONTHLY[;UNTIL=yyyyMMddThhmmssZ]` — no `INTERVAL`, no
`BYDAY` list, no `COUNT`. So the mapping is:

- `FREQ=DAILY`  -> Zoom `recurrence.type = 1`, `repeat_interval = 1`.
- `FREQ=WEEKLY` -> `type = 2`, `repeat_interval = 1`,
  `weekly_days = <series start weekday, 1=Sun..7=Sat>`.
- `FREQ=MONTHLY` -> `type = 3`, `repeat_interval = 1`,
  `monthly_day = <series start day-of-month>`.
- End: if `UNTIL` is present, is within ~1 year of the series start, **and** the
  resulting occurrence count is <= 60 -> `recurrence.end_date_time = UNTIL`
  (Zoom meeting `type = 8`, recurring with fixed time).
- Otherwise (no `UNTIL`, or beyond Zoom's limit) -> **fall back**: create a
  single Zoom meeting `type = 3` ("recurring, no fixed time"), no `recurrence`
  block. One `join_url`, reused every occurrence, no per-occurrence times in
  Zoom.

`ThisEventOnly` detached occurrences are always non-recurring Zoom meetings
(`type = 2`, scheduled).

### 6. Data model — one migration on `personal_calendar_events`

Reuse: `MeetingLink` = Zoom `join_url`; `ExternalId` = Zoom meeting id (numeric,
stored as text); `ExternalUpdatedAt` = last successful sync.

Add nullable columns:

| Column | Type | Purpose |
| --- | --- | --- |
| `conference_provider` | `text` null | `"zoom"`. Distinct from `external_source`, which also means `country_holiday`. |
| `conference_start_url` | `text` null | Host start URL (carries a ZAK host token). **Serialised only to `CreatedById`** — never in list responses for others, never in emails. |
| `conference_status` | `text` null | `pending` \| `active` \| `failed`. Null = no conferencing on this event. |
| `conference_error` | `text` null | Last failure message; drives the warning + Retry CTA. |
| `conference_host_user_id` | `uuid` null | The user whose Zoom account owns the meeting (the connector). Needed by update/cancel jobs and to gate `start_url`. |

`personal_calendar_events` already has a `tenant_isolation` RLS policy and is
already in the `TenantTables` convention list for the architecture test — new
columns on the same table need no policy change. The migration follows the
`TenantTables` + `foreach` shape used by `20260906134431_AddHolidayCalendarSettings`.

### 7. Handler changes (`Features/Calendar`)

| Handler | Change |
| --- | --- |
| `CreateCalendarEventCommandHandler` | Accept `ConferenceProvider` on the command. If set and the caller has an active `zoom` `UserIntegrationConnection`: after commit, set `conference_status = pending`, `conference_host_user_id = caller`, do the eager best-effort provision; enqueue `ConferenceMeetingProvision` unless the eager attempt already set the event `active`. If the caller is **not** connected -> `Result.Failure("Connect Zoom to add a meeting.", 400)` (the UI should not offer the toggle, this is the guard). |
| `UpdateCalendarEventCommandHandler` | If the event has `conference_provider` set and start/end/title changed -> enqueue `ConferenceMeetingUpdate`. |
| `DeleteCalendarEventCommandHandler` | If `external_id` + `conference_provider` set -> enqueue `ConferenceMeetingCancel` (with the event's `external_id`) before `Remove`. |
| `EditRecurringOccurrenceCommandHandler` — `AllEvents` | If the series master has conferencing -> enqueue `ConferenceMeetingUpdate` for the master (re-patches the one recurring Zoom meeting). |
| `EditRecurringOccurrenceCommandHandler` — `ThisEventOnly` | After the detached child row is written -> set its `conference_*` fields to `pending` and enqueue `ConferenceMeetingProvision` for the **child** (its own standalone Zoom meeting). |
| `CancelRecurringOccurrenceCommandHandler` | If a detached child with its own `external_id` exists -> enqueue `ConferenceMeetingCancel` for the child. If it is a pure virtual occurrence of a conferencing series -> enqueue `ConferenceMeetingCancel` with the series `external_id` + the resolved `occurrence_id` (provider lists occurrences and matches by start instant). |
| `GetCalendarEventsQueryHandler` / DTO | Add `conferenceProvider`, `conferenceStatus`, `conferenceJoinUrl` (= `MeetingLink` when provider set) to `CalendarEventItem`. `conferenceStartUrl` only when `item.CreatedById == currentUser.UserId`. |

New: `RetryConferenceMeetingCommand` (`POST api/v1/calendar/{id}/conference/retry`,
`calendar:write`) — re-enqueues `ConferenceMeetingProvision`/`Update` for an
event whose `conference_status = failed`.

### 8. Notifications

`CalendarEventInviteEmailOutboxHandler` /
`CalendarNotificationSender.NotifyParticipantsAddedAsync` already carry the
event's title, time and location. Add the join URL (`MeetingLink`) to the
`CalendarEventInviteEmailPayload` and the in-app `calendar_event_participant_added`
data bag, rendered as a "Join Zoom meeting" line when present. If the event was
created with Zoom but provisioning has not finished yet, the invite is still
sent without the link (Phase 1: no second "here is your link" email).

### 9. Frontend (`modules/calendar`, plus an integrations settings surface)

- **Integrations card** — a "Zoom" entry wherever the GitHub connect UI lives
  (mirror it): Connect -> Zoom consent -> callback -> "Connected as
  user@example.com" + Disconnect. `ZoomIntegrationApiService` calling the five
  `api/v1/integrations/zoom` endpoints; a small `ZoomConnectionStore` exposing
  `status()`.
- **`calendar-event-form-modal`** — replace the free-text *Meeting link* input:
  - Zoom connected -> a toggle **"Add Zoom meeting"**. On -> hide the manual
    link field, show "A Zoom meeting will be created". After save, show the
    join URL (copyable) and, for the organizer, the start URL.
  - Not connected -> keep the manual link field + a subtle "Connect Zoom to
    auto-create meetings" hint linking to the integrations card.
  - Edit mode with an existing Zoom meeting -> show it read-only (join / start),
    plus a "Retry" button when `conferenceStatus === 'failed'`.
- **Model changes** — `CreateCalendarEventRequest.conferenceProvider?: 'zoom' |
  null`; `CalendarEventResponse` gains `conferenceProvider`, `conferenceStatus`,
  `conferenceJoinUrl`, `conferenceStartUrl`. Store: thread the new field through
  `createEvent`; add `retryConference(id)`.
- Non-blocking failure UX: on a create/update response with `conferenceStatus
  === 'failed'`, a toast "Event saved. The Zoom meeting couldn't be created —
  Retry" wired to `retryConference`.

## Data flow — sequences

**Connect**: `connect/start` -> signed `state` -> Zoom authorize URL ->
Zoom redirects to `connect/callback?code&state` -> `IZoomOAuthClient.ExchangeCodeAsync`
-> `GetCurrentUserAsync` -> upsert `UserIntegrationConnection` (`status = active`).

**Create with Zoom**: `POST /calendar` (`conferenceProvider = "zoom"`) ->
validate caller connected -> insert event (tx commit) -> set
`conference_status = pending` -> eager `ZoomConferencingProvider.CreateAsync`
(5 s budget) -> on success patch `MeetingLink` / `ExternalId` /
`conference_start_url` / `conference_status = active`; on failure leave
`pending` + `conference_error` and enqueue `ConferenceMeetingProvision`
-> response carries `conferenceJoinUrl` when already active, else `pending`.

**Outbox provision**: `OutboxProcessor` picks `ConferenceMeetingProvision` ->
if event already `active`, no-op -> else `CreateAsync` -> patch columns ->
on failure increment attempts, backoff; after N attempts set
`conference_status = failed` + `conference_error`.

**Edit time**: `PUT /calendar/{id}` -> update event -> enqueue
`ConferenceMeetingUpdate` -> handler `UpdateAsync(ExternalId, new times)`.

**Delete**: `DELETE /calendar/{id}` -> enqueue `ConferenceMeetingCancel`
(event `ExternalId`) -> `Remove` event -> outbox `DeleteAsync`.

**ThisEventOnly**: `PUT /calendar/{id}/occurrence` scope `ThisEventOnly` ->
detached child row written -> child `conference_status = pending` -> enqueue
`ConferenceMeetingProvision` for the child -> child gets its own meeting + link.

## Error handling (Point 4 — non-blocking)

| Failure | Behaviour |
| --- | --- |
| Caller not connected but `conferenceProvider = zoom` | `400` at create — UI should not have offered the toggle; this is a guard, not a user-facing flow. |
| Token refresh fails (revoked / expired) | Connection `status = needs_reconnect`; event save unaffected; `conference_status = failed`; UI shows "Reconnect Zoom". |
| Zoom 5xx / timeout / rate-limit | Event save unaffected; outbox retries with backoff; after N attempts `conference_status = failed` + Retry CTA. |
| Event deleted before its provision job runs | Provision handler no-ops (event gone); any cancel job for a not-yet-created meeting no-ops. |
| Concurrent refresh race | Optimistic concurrency on the `UserIntegrationConnection` token row; the loser re-reads and proceeds. |

## Permissions

- `connect/start`, `connect/callback`, `status`, `disconnect`, `refresh` — any
  authenticated tenant user (own connection only), like GitHub's own-user
  endpoints. No new permission.
- Adding Zoom to an event and the Retry endpoint — existing `calendar:write`.
- Operator configuring the Zoom OAuth app — existing DevPlatform platform-admin
  auth on the `PlatformOAuthApps` feature.

## Config / ops

- Zoom Marketplace app type: **User-managed OAuth** (not Server-to-Server, not
  Account-level).
- Redirect URL: `{apiBaseUrl}/api/v1/integrations/zoom/connect/callback`.
- Scopes added in the Marketplace app: `user:read`, `meeting:read`,
  `meeting:write` (or the granular equivalents listed above).
- For internal use on a single company Zoom account the app can stay
  unpublished (development app) — each user in that account authorises it.
  Distributing to external Zoom accounts requires Marketplace review.
- Client id / secret entered via DevPlatform Admin -> OAuth Apps (provider
  `zoom`); stored encrypted in `platform_oauth_apps`.

## Security & privacy

- Access / refresh tokens encrypted at rest (same service as the GitHub
  `UserIntegrationConnection` flow).
- OAuth `state` is signed/protected via `IOAuthStateProtector`; `returnUrl`
  restricted to same-site relative paths.
- `conference_start_url` (host ZAK token) is returned only to the event's
  `CreatedById`, never listed for other participants, never emailed.
- No event content is sent to Zoom beyond title, agenda (optional), start time,
  duration, timezone, and recurrence — no participant PII, no description by
  default.

## Testing

- **Unit** — RRULE -> Zoom recurrence mapper (every branch + the no-fixed-time
  fallback); `ZoomConferencingProvider` against a faked `IZoomMeetingsClient`;
  token refresh persists the rotated refresh token; `start_url` gating.
- **Handler** — create enqueues provision + guards "not connected"; update /
  delete / editOccurrence(`AllEvents`, `ThisEventOnly`) / cancelOccurrence
  enqueue the right message; a failed eager provision does **not** roll back the
  event.
- **Outbox handler** — provision success patches columns; repeated failure ends
  in `conference_status = failed`; cancel deletes; provision for a deleted event
  no-ops.
- **Architecture** — `personal_calendar_events` still passes the tenant-table /
  RLS coverage test after the migration.
- **Frontend** — form toggle states (connected / not connected / edit with
  meeting / failed+Retry); store threading `conferenceProvider` and
  `retryConference`; integrations card connect / disconnect.

## Conventions followed (verified against the codebase)

- No Hangfire — `BackgroundService` + `PeriodicTimer` + `AddHostedService<T>()`
  (`AgentCommandExpiryJob.cs`, `SprintLifecycleJob.cs`).
- Tenant routes `api/v1/integrations/zoom`, `[Authorize(Policy =
  "TenantPolicy")]` + per-action `[RequirePermission(...)]`
  (`GitHubIntegrationController.cs`).
- Typed `HttpClient` via `AddHttpClient<TInterface, TImpl>` (`NagerHolidaysClient`,
  `GitHubOAuthTokenClient`).
- Transactional outbox message types + handlers
  (`CalendarEventInviteEmailOutboxHandler`, `CalendarNotificationSender`).
- MediatR command/query handlers returning `Result` / `Result<T>`; EF repository
  interfaces in `Application`, implementations in `Infrastructure`.
- Migration uses the `TenantTables` + `foreach` RLS-declaration shape
  (`20260906134431_AddHolidayCalendarSettings`).

## Phasing

- **Phase 1 (this design):** per-user connect/disconnect/refresh; conferencing
  abstraction + Zoom implementation; create / update / delete / cancel-occurrence
  lifecycle via outbox with eager create; recurring series -> one Zoom recurring
  meeting (with no-fixed-time fallback); `ThisEventOnly` -> own meeting;
  migration; invite email carries the join URL; frontend toggle + connect card;
  Retry endpoint + UX.
- **Phase 2:** inbound Zoom webhooks (`endpoint.url_validation`,
  `meeting.deleted`, `meeting.updated`) to repair `MeetingLink` /
  `conference_status`; cloud-recording links on the event; precise single-
  occurrence deletion without list-and-match; `ThisAndFollowing` once the
  frontend exposes it; retry-UX polish.
- **Phase 3:** Google Meet and Microsoft Teams as sibling `IConferencingProvider`
  implementations (the OAuth catalog already grants google / microsoft the
  `calendar` capability); org-level Server-to-Server Zoom as a fallback for users
  with no personal Zoom; private-event "Busy" parity.

## Risks / open items

- **Zoom API rate limits** — "Create a Meeting" is a Medium-rate endpoint
  (per-account QPS). Mitigation: outbox backoff; the eager create is best-effort
  and single-shot.
- **Refresh-token rotation races** — mitigated with optimistic concurrency on
  the token row; confirm the `UserIntegrationConnection` config has a
  concurrency token or add one.
- **`occurrence_id` resolution** for single-occurrence cancel needs a
  `GET /meetings/{id}` + match-by-start; acceptable for Phase 1, tightened in
  Phase 2.
- **Zoom Basic (free) hosts** — 40-minute cap for 3+ participants. Not ours to
  solve; the connect card copy should mention it.
- **`IOAuthStateProtector` / `GitHubOAuthState` rename** — touches the GitHub
  integration; keep it a pure mechanical rename in one commit with the GitHub
  tests green.
