# Calendar Core + Google/Outlook External Sync — Implementation Spec

**Goal:** Build the Calendar module's core (events + participants) and Google Calendar /
Outlook Calendar external sync, per the scope decision recorded in
`docs/superpowers/specs/2026-08-26-calendar-outlook-google-integration-notes.md`.

**Architecture:** Standard MediatR CQRS (Domain/Application/Infrastructure/Api), tenant-scoped
via `TenantPolicy` + RLS for everything except the OAuth callback, which is a new pattern:
a fixed, non-tenant-scoped endpoint that recovers tenant/user identity from a signed state
payload and switches tenant context mid-request via the existing `ITenantContextSwitcher`.

**Tech Stack:** .NET 10 / EF Core / PostgreSQL (RLS), MediatR, ASP.NET Data Protection
(`IDataProtector`) for OAuth state, `IEncryptionService` for token-at-rest encryption,
`BackgroundService` + `PeriodicTimer` for the sync job (no Hangfire in this codebase).

## Global Constraints

- Tenant-side routes: `api/v1/calendar/...` (no `tenant/` segment), `[Authorize(Policy = "TenantPolicy")]`, per-action `[RequirePermission("calendar:...")]`.
- Reuse existing permission codes `calendar:read`, `calendar:write`, `calendar:admin` (already seeded in `PermissionSeeder.cs`) — do not invent new ones.
- Token fields (`access_token_encrypted`, `refresh_token_encrypted`, `sync_token_encrypted`, `delta_link_encrypted`) use `IEncryptionService.EncryptBytes(string)`/`DecryptBytes(byte[])` → `bytea` columns. Never returned by any API response.
- Snake_case DB columns via the existing `UseSnakeCaseNamingConvention()` EF convention — entity properties are PascalCase as usual, no manual column-name overrides needed.
- No Hangfire. Background job shape mirrors `src/ONEVO.Infrastructure/Services/Monitoring/Screenshots/AgentCommandExpiryJob.cs` (simplest existing example) and `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs`.
- Module/permission plumbing already exists end-to-end — verified 2026-08-28, no seeder task
  needed: `calendar:read`/`calendar:write`/`calendar:admin` are already in `PermissionSeeder.cs`
  under the `"calendar"` module; `"calendar"` is already a `ModuleCatalogSeeder.cs` entry
  (`Phase = "phase_1"`) with all three permissions in its ownership list; `calendar:read` is
  already a `ModuleAutoGrants` self-service entry; `"calendar"` is already in the
  `starter_51_200` plan's `IncludedModulesJson`. `RequirePermission("calendar:...")` on the new
  controller actions is all that's needed — nothing to seed.
- Branch the implementation off `feature/calendar-oauth-scopes` (not bare `development`) —
  `development` still has the `SubscriptionPlanConfiguration.cs` `"activity_monitoring"` module-
  key bug that an earlier session fixed on that branch lineage; building fresh off `development`
  would silently reintroduce it for local testing.

---

## Scope

**In scope (this spec):**
1. Calendar core — `calendar_events`, `calendar_event_participants`; create/update/delete/list CRUD.
2. External Calendar Integration — `external_calendar_connections`, `external_calendar_event_links`; Google Calendar + Microsoft Graph (Outlook) OAuth connect/callback; list/change-sync-mode/disconnect/reconnect/manual-sync; background sync job (pull/push/two-way).

**Explicitly out of scope (separate future specs):**
- Holiday sync (`holiday_calendar_settings`, Nager.Date) — independent table/flow, can follow later.
- `ICalendarConflictService` (conflict detection for events/invitations/Time Off) — needs Calendar core to exist first; Time Off's own conflict UI depends on it too.
- Schedule overlays, task due-date/worked-time read-time projections (business rules 16–21 in the source vault doc) — these are pure read-time projections layered on top of a working Calendar core; deferred to keep this pass buildable.
- `calendar_event_participants.response_status` workflow (Accept anyway / Reject / Request conflict resolution / Nominate replacement) beyond the bare column — the recipient-response UI and its notification/inbox plumbing is a follow-on; this pass creates/stores participants but does not build the response workflow.

Frontend Calendar UI (month/week/day grid, event creation, Connections modal) is a separate task after the backend API exists — noted here for completeness but detailed in the implementation plan's frontend tasks, not this backend-focused spec.

---

## Data Model

### `calendar_events`

```
Id                  uuid PK
TenantId            uuid
Title               varchar(200)
Description         text?
StartDate           timestamptz
EndDate             timestamptz
SourceType          varchar(30)   -- "manual" | "external_sync" (only these two are ever written by this pass; "holiday"/"schedule_overlay"/"time_off_request" are reserved column values for later specs)
SourceId             uuid?         -- polymorphic reference; null for "manual"
Color               varchar(7)?
Recurrence          varchar(20)   -- "none" | "daily" | "weekly" | "monthly" (manual events only)
ExternalId           varchar(255)?
ExternalSource       varchar(30)?  -- "google_calendar" | "outlook_calendar"
IsAllDay            bool          -- default false
Timezone            varchar(50)?  -- IANA; null for all-day
EventStatus          varchar(20)?  -- "confirmed" | "tentative" | "cancelled"
IsPrivate            bool          -- default false
OrganizerName        varchar(200)?
OrganizerEmail       varchar(255)?
Location            varchar(500)?
MeetingLink          varchar(500)?
ExternalAttendees     jsonb?        -- [{name, email, status}]
RecurrenceRule       text?         -- RRULE from external provider
ExternalUpdatedAt     timestamptz?
CreatedById          uuid          -- FK -> users
CreatedAt/UpdatedAt  timestamptz
```

### `calendar_event_participants`

```
Id            uuid PK   -- vault doc omits this; add it, every other table in this codebase has a surrogate PK
EventId       uuid FK -> calendar_events
EmployeeId    uuid FK -> employees
ResponseStatus varchar(30)  -- "pending" | "accepted" | "rejected" | "resolution_requested" | "replacement_nominated"
ResponseReason text?
CreatedAt/UpdatedAt timestamptz
```

### `external_calendar_connections`

```
Id                        uuid PK
TenantId                  uuid
UserId                    uuid FK -> users
Provider                  varchar(30)   -- "google_calendar" | "outlook_calendar"
ExternalAccountEmail       varchar(255)
ExternalCalendarId         varchar(255)?
ExternalCalendarName       varchar(255)?
AccessTokenEncrypted        bytea?
RefreshTokenEncrypted       bytea
Scopes                    jsonb
SyncDirection              varchar(20)   -- "pull_only" | "push_only" | "two_way" | "disabled"
Status                    varchar(20)   -- "active" | "reauth_required" | "paused" | "revoked" | "failed"
SyncTokenEncrypted          bytea?        -- Google incremental sync token
DeltaLinkEncrypted          bytea?        -- Microsoft Graph delta link
FailureCount               int           -- reset to 0 on success
LastSyncedAt                timestamptz?
LastSuccessfulSyncAt         timestamptz?
LastError                  text?
ExpiresAt                  timestamptz?
CreatedAt/UpdatedAt        timestamptz
```

### `external_calendar_event_links`

```
Id                             uuid PK
TenantId                       uuid
CalendarEventId                 uuid FK -> calendar_events
ExternalCalendarConnectionId     uuid FK -> external_calendar_connections
Provider                       varchar(30)
ExternalCalendarId               varchar(255)
ExternalEventId                 varchar(255)
ExternalEtag                    varchar(255)?
SyncDirection                   varchar(20)   -- "inbound" | "outbound"
SyncStatus                     varchar(20)   -- "synced" | "pending" | "failed" | "skipped" | "conflict"
LastSyncedAt                    timestamptz?
LastError                      text?
CreatedAt/UpdatedAt             timestamptz
```

All four tables: standard tenant RLS policy (`tenant_isolation`, same pattern as every other tenant table — `USING (current_setting('app.tenant_context_mode') = 'admin' OR tenant_id = current_setting('app.current_tenant_id')::uuid)`), soft-delete via the existing `SoftDeleteInterceptor` convention (`IsDeleted`/`DeletedAt` on `calendar_events` only — connections/links don't soft-delete, they get hard-disconnected/removed).

### Domain layer file locations

```
ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs
ONEVO.Domain/Features/Calendar/Entities/CalendarEventParticipant.cs
ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarConnection.cs
ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarEventLink.cs
```

Each entity file also carries its own `public static class` of string constants for its enum-like
columns (mirrors `TaskStatusVisibilities`/`WorkTaskPriorities` convention), e.g.:

```csharp
public static class CalendarEventSourceTypes
{
    public const string Manual = "manual";
    public const string ExternalSync = "external_sync";
}
public static class CalendarExternalSources
{
    public const string GoogleCalendar = "google_calendar";
    public const string OutlookCalendar = "outlook_calendar";
}
public static class CalendarSyncDirections
{
    public const string PullOnly = "pull_only";
    public const string PushOnly = "push_only";
    public const string TwoWay = "two_way";
    public const string Disabled = "disabled";
}
public static class ExternalCalendarConnectionStatuses
{
    public const string Active = "active";
    public const string ReauthRequired = "reauth_required";
    public const string Paused = "paused";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
}
```

---

## Calendar Core (CQRS)

```
ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/
ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/   -- also used for drag-and-drop reschedule
ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/
ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/      -- date-range list, merges manual + external_sync rows
ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs
ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs
ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventConfiguration.cs
ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventParticipantConfiguration.cs
ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs
ONEVO.Api/Contracts/Calendar/CalendarContracts.cs
```

`CreateCalendarEventCommand` accepts an optional `IReadOnlyList<Guid> ParticipantEmployeeIds` —
when non-empty, creates one `calendar_event_participants` row per employee with
`ResponseStatus = "pending"` (the response *workflow* is out of scope, but the row must exist
so a later spec can add it without a data migration). `UpdateCalendarEventCommand` does not
touch participants in this pass (no participant-list editing yet — out of scope, matches the
deferred response-workflow scope cut above).

`GetCalendarEventsQuery(DateOnly from, DateOnly to)` returns every `calendar_events` row for
the caller's employee where `[StartDate, EndDate]` overlaps `[from, to]` **and** either
(a) `CreatedById` is the caller, or (b) the caller is a participant. Manager/HR "team calendar"
visibility (per the vault doc's "Managers see team schedules... allowed by management coverage")
is deferred along with conflict detection — this pass is single-employee-view only.

---

## External Calendar Integration

### The OAuth callback problem (why this can't reuse the GitHub integration's pattern)

The existing `GitHubIntegrationController` (`api/v1/integrations/github/connect/{start,callback}`)
builds its `redirect_uri` from the *current request's own host* (`Url.ActionLink`), so both the
authorize redirect and the provider's callback happen on the same tenant subdomain
(`https://{slug}.{rootDomain}/...`). That only works because it implicitly assumes the GitHub
OAuth App's registered callback URL can match every tenant subdomain — which real GitHub/Google/
Microsoft OAuth app registrations do not support (they require one exact, pre-registered
redirect URI, not a wildcard). This is a latent gap in the existing integration, not a pattern
to copy for Calendar.

**Calendar's OAuth flow uses a fixed, single redirect URI** (e.g.
`https://localhost:7229/api/v1/calendar/connections/{provider}/callback` in dev,
`https://api.{prod-root-domain}/api/v1/calendar/connections/{provider}/callback` in prod —
this is the one URL registered in the Google Cloud Console / Azure App Registration). Tenant
identity is recovered from the OAuth `state` parameter instead of the Host header, using the
existing `ITenantContextSwitcher` to establish tenant context mid-request.

### Connect flow

```
ONEVO.Application/Features/Calendar/Commands/StartCalendarConnection/
ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/
ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthStateProtector.cs
ONEVO.Infrastructure/Security/CalendarOAuthStateProtector.cs   -- new IDataProtector purpose string "ONEVO.CalendarOAuth.State.v1", same shape as OAuthStateProtector.cs but its own protector instance (never share a purpose string across features)
```

```csharp
public sealed record CalendarOAuthState(
    string Nonce,
    Guid TenantId,
    Guid UserId,
    string Provider,           // "google_calendar" | "outlook_calendar"
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);
```

**1. `POST api/v1/calendar/connections/{provider}/connect`** (tenant-scoped, `TenantPolicy`,
authenticated — same as every other tenant route):
- Resolve `IPlatformOAuthAppResolver.GetActiveAppForProviderAsync("google"|"microsoft")` for the
  authorize URL, client id, and scopes (already carries `CapabilityCalendar` + the calendar
  scope added in `feature/calendar-oauth-scopes`).
- Build `CalendarOAuthState` (10-minute `ExpiresAtUtc`, matching the GitHub flow's TTL), protect
  it via `ICalendarOAuthStateProtector.Protect(state)`.
- Build the authorize URL with the **fixed** `redirect_uri` (from new config
  `Urls:CalendarOAuthCallbackBaseUrl`, e.g. `https://localhost:7229` in dev — add alongside the
  existing `Urls:AppBaseUrl`/`Urls:AdminConsoleBaseUrl` in `appsettings.Development.json`).
- Return `{ authorizeUrl }` in the response body. Frontend does `window.location.href = authorizeUrl`
  (or opens it in a popup per the vault doc's "OAuth flow opens in a new window/popup" - popup is
  the frontend's choice, doesn't change this backend contract).

**2. `GET api/v1/calendar/connections/{provider}/callback`** (**new controller, NOT under
`TenantPolicy`** — `[AllowAnonymous]`, self-validating via the encrypted state):
```
ONEVO.Api/Controllers/Public/Calendar/CalendarOAuthCallbackController.cs
[Route("api/v1/calendar/connections")]
```
- Decrypt `state` via `ICalendarOAuthStateProtector.TryUnprotect` → 400 if invalid/expired
  (mirrors `CompleteGitHubUserOAuthCommandHandler.ValidateState`'s expiry check).
- Load the `Tenant` by `state.TenantId` (`ITenantRepository.GetByIdAsync`, no tenant context
  needed yet since this call runs in `SystemMode` by default at the bare host) → 400 if missing/
  inactive.
- **`await _tenantContextSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct)`**
  (the 4th `TenantRegistryEntry` field is `PlanCode` — `Tenant` the domain entity only carries
  `SubscriptionPlanId`, not a resolved plan code string; `HostTenantResolutionMiddleware` passes
  `null` here too for the same reason, so this matches existing convention, not a shortcut)
  — this is the exact mechanism `TenantSessionExchangeService`'s consumer already relies on; it
  resets the DbContext connection so RLS session GUCs apply to the new tenant, not whatever
  system-mode connection this request started with.
- Exchange `code` for tokens: `POST` to the provider's `TokenUrl` (from
  `IPlatformOAuthAppResolver`) with `client_id`/`client_secret` (from
  `GetActiveCredentialForProviderAsync`, decrypted server-side only) + the same fixed
  `redirect_uri` used in step 1 (OAuth2 spec requires exact match).
- Call the provider's "who am I" / calendar-list endpoint to get `external_account_email` and the
  list of the user's calendars (for `external_calendar_name` — Phase 1 auto-selects the primary
  calendar; the vault doc's "user picks which calendar during setup" second step is a follow-up
  `PUT` after this callback lands them back in the app, not part of the callback itself).
- Upsert `external_calendar_connections` (encrypt tokens via `IEncryptionService.EncryptBytes`),
  `SyncDirection = "two_way"` default, `Status = "active"`.
- **302 redirect** to `https://{tenant.Slug}.{rootDomain}:{tenantAppPort}/calendar?connected={provider}`
  (`Urls:AppBaseUrl` gives scheme+port; swap in the tenant slug the same way
  `TenantSessionExchangeService.BuildContinueUrl` already does).

This callback controller needs its own thin integration test double for "what if the state's
tenant no longer exists / is suspended" and "what if the token exchange fails" — both must still
302 the browser somewhere sane (`.../calendar?connectionError=1`), never leave the user on a raw
500 page.

### Connection management (tenant-scoped, `TenantPolicy`, all under the caller's own `UserId`)

```
ONEVO.Application/Features/Calendar/Queries/GetMyCalendarConnections/
ONEVO.Application/Features/Calendar/Commands/UpdateCalendarConnection/     -- sync direction / selected calendar
ONEVO.Application/Features/Calendar/Commands/DisconnectCalendarConnection/
ONEVO.Application/Features/Calendar/Commands/TriggerCalendarSync/          -- manual "Sync now"
```

`DisconnectCalendarConnectionCommandHandler`: deletes the `external_calendar_connections` row,
then deletes every `calendar_events` row reachable via its `external_calendar_event_links`
**except** ones whose `SourceType` was flipped to a OneVo-owned event (the vault's "unless they
were converted into OneVo-owned events" rule — this pass has no explicit "convert" action, so in
practice this exception clause is dead code until a later spec adds a convert command; implement
the check anyway so the query is correct once that exists, but don't build a UI for it yet).

### Provider clients

```
ONEVO.Application/Features/Calendar/ServiceInterfaces/IGoogleCalendarClient.cs
ONEVO.Application/Features/Calendar/ServiceInterfaces/IMicrosoftGraphCalendarClient.cs
ONEVO.Infrastructure/ExternalServices/Calendar/GoogleCalendarClient.cs
ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphCalendarClient.cs
```

```csharp
public interface IGoogleCalendarClient
{
    Task<GoogleCalendarPage> ListEventsAsync(string accessToken, string calendarId, string? syncToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
    Task<GoogleCalendarEventDto> InsertEventAsync(string accessToken, string calendarId, GoogleCalendarEventDto @event, CancellationToken ct);
    Task<GoogleCalendarEventDto> PatchEventAsync(string accessToken, string calendarId, string eventId, GoogleCalendarEventDto @event, CancellationToken ct);
    Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct);
}

public interface IMicrosoftGraphCalendarClient
{
    Task<GraphCalendarPage> ListEventsAsync(string accessToken, string? deltaLink, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct);
    Task<GraphEventDto> CreateEventAsync(string accessToken, GraphEventDto @event, CancellationToken ct);
    Task<GraphEventDto> UpdateEventAsync(string accessToken, string eventId, GraphEventDto @event, CancellationToken ct);
    Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct);
}
```

Both implementations use `HttpClient` via `IHttpClientFactory` (named clients
`"GoogleCalendar"`/`"MicrosoftGraphCalendar"`, registered in
`ONEVO.Infrastructure/DependencyInjection.cs` next to the other named-client registrations).
`GoogleCalendarPage`/`GraphCalendarPage` carry `(IReadOnlyList<...Dto> Events, string? NextSyncTokenOrDeltaLink)`.

### Background sync job

```
ONEVO.Infrastructure/Services/Calendar/CalendarSyncJob.cs
```

```csharp
public sealed class CalendarSyncJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SyncWindowPast = TimeSpan.FromDays(30);
    private static readonly TimeSpan SyncWindowFuture = TimeSpan.FromDays(180);
    private const int BatchLimitPerConnection = 200;
    private const int MaxConsecutiveFailures = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "CalendarSyncJob run failed."); }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        // ITenantRepository has no "get all active" method - ListAsync is the paged query
        // every other admin tenant-list screen already uses (statusFilter, searchTerm, skip,
        // take). Page through it here too rather than adding a new unpaged repo method.
        const int PageSize = 100;
        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(TenantStatus.Active, searchTerm: null, skip, PageSize, ct);
            if (page.Count == 0) break;

            foreach (var tenant in page)
            {
                await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct);

                var connections = scope.ServiceProvider.GetRequiredService<IExternalCalendarConnectionRepository>();
                foreach (var connection in await connections.GetActiveAsync(ct))
                {
                    if (connection.SyncDirection == CalendarSyncDirections.Disabled) continue;
                    await SyncOneConnectionAsync(scope.ServiceProvider, connection, ct);
                }
            }

            skip += PageSize;
        }
    }
}
```

This reuses `ITenantContextSwitcher` for the per-tenant loop instead of the seeder's manual
`IWritableTenantContext.Resolve()` + implicit reliance on the interceptor re-applying GUCs on
next connection open — the switcher already does the connection-reset step explicitly, which is
exactly the gap the scope notes flagged ("neither existing `BackgroundService` job actually does
this... needs to **combine** the shape with the seeder's per-tenant loop"). One
`IServiceScope` for the whole run (not one per tenant) keeps `IGoogleCalendarClient`/
`IMicrosoftGraphCalendarClient` (stateless, no DB dependency) from being re-resolved per tenant;
repositories resolved inside the scope still see the switched tenant context on each iteration
since they query on-demand, not at scope-creation time.

`SyncOneConnectionAsync`:
1. If `ExpiresAt` within 5 minutes → refresh via `refresh_token_encrypted`; on failure, set
   `Status = "reauth_required"`, `LastError`, continue to next connection (don't throw).
2. Branch on `SyncDirection`:
   - `pull_only`: fetch external events since `sync_token`/`delta_link` (or, if null, a fresh
     list bounded by `[now - 30d, now + 180d]`) → upsert `calendar_events` +
     `external_calendar_event_links` (`SyncDirection = "inbound"`).
   - `push_only`: find local `calendar_events` where `SourceType = "manual"` and
     `UpdatedAt > connection.LastSyncedAt`, not yet linked (or linked+stale) → create/patch on
     the provider, upsert the link (`SyncDirection = "outbound"`).
   - `two_way`: pull first, then push. On pull, if `external_etag` differs from the stored link's
     `ExternalEtag` **and** the local `calendar_events.UpdatedAt > connection.LastSyncedAt`
     (both sides changed) → conflict: `pull_wins` (overwrite local), set
     `external_calendar_event_links.SyncStatus = "conflict"`, log in `LastError` — no
     UI for viewing/resolving conflicts in this pass (vault doc's "Admin can view conflicts in
     Calendar settings" is deferred with Conflict Detection).
3. Cap each direction at 200 events per run per connection (page/continue next run via the
   stored sync token/delta link — never fetch "everything" in one run).
4. On success: `LastSyncedAt = now`, `FailureCount = 0`. On error: `FailureCount++`; at 3 →
   `Status = "failed"` (no notification channel exists yet in this codebase for a generic
   "notify user" — skip that vault requirement for this pass, log only).

Private events (`is_private = true` from the provider): store only
`{ExternalId, ExternalCalendarId, StartDate, EndDate, IsAllDay, Timezone, IsPrivate}` — never
title/description/attendees/location. `Title` is hardcoded to `"Busy"` at write time, not
computed at read time, so the display rule can't accidentally leak real data through a future
query change.

---

## API Endpoints

| Method | Route | Permission | Notes |
|:-------|:------|:-----------|:------|
| GET | `/api/v1/calendar` | `calendar:read` | `?from=&to=` date range |
| POST | `/api/v1/calendar` | `calendar:write` | Create event |
| PUT | `/api/v1/calendar/{id}` | `calendar:write` | Update/reschedule |
| DELETE | `/api/v1/calendar/{id}` | `calendar:write` | Delete |
| GET | `/api/v1/calendar/connections` | Authenticated | Caller's own connections |
| POST | `/api/v1/calendar/connections/{provider}/connect` | Authenticated | Returns `{authorizeUrl}` |
| GET | `/api/v1/calendar/connections/{provider}/callback` | **AllowAnonymous, fixed host** | Not under `api/v1/calendar` tenant assumptions — see controller note above |
| PUT | `/api/v1/calendar/connections/{id}` | Authenticated, owns connection | Sync mode / selected calendar |
| DELETE | `/api/v1/calendar/connections/{id}` | Authenticated, owns connection | Disconnect |
| POST | `/api/v1/calendar/connections/{id}/sync` | Authenticated, owns connection | Manual sync now |

`provider` route values are `google`/`microsoft` (matching `PlatformOAuthProviderCatalog`'s
keys), not `google_calendar`/`outlook_calendar` (the `external_source`/`Provider` column values
on the Calendar tables) — the controller maps between the two; don't conflate them.

---

## Testing Strategy

- Unit: repository tests (SQLite in-memory, existing pattern) for
  `EfCalendarEventRepository`/`EfExternalCalendarConnectionRepository`; handler tests (Moq) for
  every command/query, including the callback's state-expiry/invalid-tenant/token-exchange-
  failure branches; `CalendarSyncJob`'s per-branch sync logic extracted into a testable
  `CalendarSyncService` (the job itself stays a thin `BackgroundService` wrapper, matching how
  `SprintLifecycleJob` splits job-loop from testable logic — verify this split exists before
  copying it verbatim).
- Provider clients: interface-mocked in handler/service tests; no live Google/Microsoft calls in
  the test suite. If a smoke test against the real APIs is wanted later, gate it behind an
  environment flag the way AWS Rekognition liveness tests are gated — not part of this pass.
- Architecture tests: confirm `ONEVO.Api/Controllers/Public/Calendar/CalendarOAuthCallbackController.cs`
  doesn't leak into the existing "every Tenant controller requires TenantPolicy" architecture
  rule (it must be excluded deliberately, not by accident) — add/adjust the relevant
  `ONEVO.Tests.Architecture` rule if one already asserts that blanket policy.

---

## Open Items For The Implementation Plan

- Exact `Urls:CalendarOAuthCallbackBaseUrl` value per environment (dev/staging/prod) — dev is
  `https://localhost:7229`; confirm staging/prod values against how `Urls:AppBaseUrl` is set
  there before writing the plan's config-file tasks.
- Google's calendar-list endpoint (`calendarList.list`) and Microsoft Graph's
  (`/me/calendars`) response shapes for the "auto-select primary calendar" step in the callback —
  confirm the `primary: true` / `isDefaultCalendar` field names against current API docs when
  writing the actual client implementation (both are stable, long-standing API fields, low risk,
  but verify at build time rather than trusting this spec's memory of them).
