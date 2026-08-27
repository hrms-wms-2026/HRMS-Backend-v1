# Calendar Core + Outlook/Google Integration — Scope Notes (in progress)

**Status:** Discovery complete, spec not yet written. Paused to prioritize Employee Dashboard.
**Date:** 2026-08-26

## Goal (scoped, per user decision)

Build just enough Calendar to support Outlook/Google Calendar sync:
1. **Calendar core** — `calendar_events` + `calendar_event_participants`, minimal CRUD.
2. **External Calendar Integration** — `external_calendar_connections` + `external_calendar_event_links`, OAuth connect/callback, background sync job, Google Calendar API + Microsoft Graph Calendar API.

**Deliberately out of scope for this pass** (separate sub-projects, decided during brainstorming):
- Holiday sync (`holiday_calendar_settings`, Nager.Date) — independent, can follow later.
- Conflict Detection (`ICalendarConflictService`) — depends on Calendar core, needed by Time Off too.
- Schedule overlays, task due-date/worked-time projections (Calendar-as-projection-surface rules 16-21 in the source doc) — read-time projections, not needed for basic sync.

## Source of truth — already fully designed, do not redesign

Two docs in the "2nd brain" vault contain a complete, ready-to-implement spec (schema, 21 business rules, API endpoint list, cross-module events, sync-job pseudocode, conflict-resolution rules):
- `2nd brain/OneVo-HR/modules/calendar/overview.md`
- `2nd brain/OneVo-HR/Userflow/Calendar/calendar-integrations.md`

Read both in full before writing the implementation spec — they already answer nearly every design question (sync modes, default sync window past-30/future-180 days, batch limit 200 events/run, disconnect/reconnect behavior, private-event "Busy" display rule, etc.).

## Current codebase state (verified 2026-08-26)

- **Zero implementation exists.** No `calendar_events` table, no entities under `Features/Calendar`, no migrations, no controller. Confirmed via DB query (`information_schema.tables`) and file search — only unrelated `release_calendar` (Work Management) matches "calendar".
- **Tenant permission codes already exist** in `PermissionSeeder.cs`: `calendar:read`, `calendar:write`, `calendar:admin` (colon-separated convention, matching `employees:read`, `org:manage`, etc. — NOT the dot-separated `PlatformPermissionCatalog` convention, which is admin-console-only). Reuse these; do not invent new ones.

## Codebase conventions the spec must follow (verified, deviates from the vault doc in one place)

1. **No Hangfire** — despite the vault doc saying "Hangfire recurring job," this codebase has zero Hangfire usage. The real pattern is a `BackgroundService` + `PeriodicTimer` class, registered via `services.AddHostedService<T>()`. Exact templates: `src/ONEVO.Infrastructure/Services/Monitoring/Screenshots/AgentCommandExpiryJob.cs` (2-min interval, simplest) and `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs` (5-min interval, explicitly documented as mirroring the first). Shape:
   ```csharp
   sealed class CalendarSyncJob : BackgroundService
   {
       private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
       private readonly IServiceProvider _services;
       private readonly ILogger<CalendarSyncJob> _logger;

       protected override async Task ExecuteAsync(CancellationToken stoppingToken)
       {
           using var timer = new PeriodicTimer(Interval);
           while (await timer.WaitForNextTickAsync(stoppingToken))
           {
               try
               {
                   await using var scope = _services.CreateAsyncScope();
                   // ... work, see tenant-loop note below ...
               }
               catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
               catch (Exception ex) { _logger.LogError(ex, "CalendarSyncJob encountered an error."); }
           }
       }
   }
   ```
2. **Tenant-side API routes**: `api/v1/{feature}` (no `tenant/` segment — contrast with admin's `admin/v1/...`). Examples: `PositionsController.cs` → `[Route("api/v1/org/legal-entities/{legalEntityId:guid}/positions")]`; `GitHubIntegrationController.cs` → `[Route("api/v1/integrations/github")]`. Both use `[Authorize(Policy = "TenantPolicy")]` + per-action `[RequirePermission("code")]` (attribute at `src/ONEVO.Api/Filters/RequirePermissionAttribute.cs`, resolves `ICurrentUser.HasPermission(...)`). So Calendar routes should be `api/v1/calendar`, `api/v1/calendar/connections`, etc. — matching the vault doc's paths exactly, just confirmed against real sibling controllers.
3. **Token encryption**: `IEncryptionService` already has the byte[]-returning pair needed for the `bytea` token columns — `byte[] EncryptBytes(string plainText)` and `string DecryptBytes(byte[] cipherBytes)`. No new interface method needed for `access_token_encrypted`/`refresh_token_encrypted`/`sync_token_encrypted`/`delta_link_encrypted`.
4. **Per-tenant RLS context in the sync job — new composition, no exact precedent**: `external_calendar_connections`/`calendar_events` are tenant-scoped (RLS), but the job runs outside any request (no Host header). `WorkManagementSampleDataSeeder` shows the pattern for switching tenant context mid-loop (`tenantContext.SetAdminMode()` then `tenantContext.Resolve(new TenantRegistryEntry(...))` per tenant, `SaveChangesAsync` before moving to the next tenant since RLS is per-DB-session). Neither existing `BackgroundService` job actually does this (they use admin/system mode with explicit `tenantId` repo params instead) — so `CalendarSyncJob` needs to **combine** the `BackgroundService`+`PeriodicTimer` shape with the seeder's per-tenant `Resolve()` loop. Document this in the spec as a deliberate new composition, not "copy an existing job."

## Open items when resuming

- Write the actual spec doc (architecture, entity definitions, EF configs, CQRS commands/queries, controller, OAuth connect/callback sequence, `IGoogleCalendarClient`/`IMicrosoftGraphCalendarClient` interfaces for the two provider APIs, sync job pseudocode adapted to the composition above).
- Confirm whether `PlatformOAuthProviderCatalog`'s `microsoft`/`google` `DefaultScopes` need a calendar scope added (currently only `openid, profile, email, offline_access` for microsoft; no calendar scope) — likely yes, since the connect flow needs to request `Calendars.ReadWrite` (Microsoft Graph) / `https://www.googleapis.com/auth/calendar` (Google) at OAuth-authorize time. Check whether this is a platform-level catalog change (affects the shared "microsoft" OAuth app) or needs a Calendar-specific scope override at connect time.
- The user has real Outlook/Google client secrets ready to configure via the existing Admin UI → OAuth Apps screen (mechanism already verified working generically for any approved provider, via `ConfigurePlatformOAuthAppCommandHandler`) — that configuration step is independent of this backend work and can happen any time.
