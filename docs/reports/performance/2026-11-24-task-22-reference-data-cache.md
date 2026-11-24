# Task 22: cache reference data (2026-11-24)

Improvements task 22 (person-2). Branch `perf/task-22-reference-data-cache`, stacked on `perf/task-06-request-timing` (PR #245, the SQL command counter). Decisions: DEC-102 to DEC-105.

## What changed

- `ITenantCache` (Application) and `TenantCache` (Infrastructure). This is a thin layer over the existing `MemoryCacheService`, which already sends removals to every instance through the task 21 invalidation bus. Every tenant key carries the tenant id. `ReferenceDataCacheKeys` builds a key only with a tenant id and refuses `Guid.Empty`.
- All keys are in `Application/Common/Caching/ReferenceDataCacheKeys.cs`. The layout is `ref:{name}:{tenantId}:{variant}` for tenant data and `ref:{name}:global:` for platform-wide data.
- Entries are removed by `ReferenceDataCacheInvalidationInterceptor`, following the task 03 pattern. It looks at the entity type of every tracked insert, update or delete. It removes the entries when the change is saved, and again when the transaction commits or rolls back. Command handlers do not need to remember to remove anything. No `ExecuteUpdate`/`ExecuteDelete` touches these tables today.
- Entries live for 5 minutes. `Caching:ReferenceData:Enabled=false` sends every read to the database (`appsettings.json`).
- Entities are cached, but a caller never receives the cached object. `ReferenceDataCopy` hands each caller its own copy: arrays, child collections and the private domain-event list are copied too. This matters because some callers edit what they read; for example, the reorder handler edits the task categories it listed.

## Safety rules built in

- **Wrong-tenant reads never fill the cache.** A request whose tenant context is not the key's tenant skips the cache and goes to the database. This also covers system mode, where RLS returns no tenant rows. Without this, a background job could cache an empty module list for a tenant for 5 minutes. Admin mode reads every tenant's rows, so it may use the cache.
- **A save during a read cannot bring old data back.** `MemoryCacheService` now counts removals. If a removal happened while a value was being loaded, that value is not stored.
- **Nothing that depends on the caller's permissions is cached.** Per-user permission resolution (`PermissionResolver`) is not cached. Only the tenant's module list is, and it feeds that resolution.
- **No inbox-count cache** (DEC-053).

## Which lookups (DEC-102)

The task 06 Part B baseline does not exist yet. I read the hot paths and chose the standalone lookups that return the same rows on every request:

- **Tenant active modules** is read by the `[RequireAnyModule]` filter (20 controllers), tray presence enforcement (middleware), dashboard widget gates, the My Team capability handler and its 4 action sources. Several of these run in the same request.
- **Permission catalogue** lookups are read by role screens, the permission catalogue and role template commands.
- **Per-tenant lists** (leave types, work modes, clock-in policies, holiday settings, task and project categories) are read by settings screens, leave balance and policy views, clock-in and attendance, calendar, and planner/meetings.

Not cached:

- Task statuses (36 references): almost every read is a `join` inside a larger query, so a cache would not remove a single SQL command.
- The tenant row: already cached by slug in `HostTenantResolutionMiddleware`.
- Monitoring toggles: already cached for 2 minutes per user.

## Measurement

The SQL commands were counted with the task 06 `SqlCommandMetricsInterceptor` on SQL Server (Testcontainers). The test is `tests/ONEVO.Tests.Integration/Performance/ReferenceDataCacheIntegrationTests.cs`. Each request is a new `DbContext`, as in the API. "Off" is the same code with `Caching:ReferenceData:Enabled=false`, which is the behaviour before task 22.

| Lookup | Off: 1st | Off: repeat | On: 1st | On: repeat |
|---|---|---|---|---|
| Tenant active modules (`ModuleEntitlementService`) | 1 | 1 | 1 | 0 |
| Assignable permissions (`ModuleEntitlementService`) | 2 | 2 | 2 | 0 |
| Permission catalogue incl. universal rows (`TenantPermissionCatalogService`) | 3 | 3 | 3 | 0 |
| Active module catalogue (`ModuleCatalogService`) | 1 | 1 | 1 | 0 |
| Leave types (`EfLeaveTypeRepository.ListAsync`) | 1 | 1 | 1 | 0 |
| Work modes (`EfWorkModeRepository.ListByLegalEntityAsync`) | 1 | 1 | 1 | 0 |
| Clock-in policies (`EfClockInPolicyRepository.ListActiveOnDateAsync`) | 1 | 1 | 1 | 0 |
| Holiday calendar settings (`EfHolidayCalendarSettingsRepository`) | 1 | 1 | 1 | 0 |
| Task categories (`EfTaskCategoryRepository.GetByProjectIdAsync`) | 1 | 1 | 1 | 0 |
| Project categories (`EfProjectCategoryRepository.GetAllForTenantAsync`) | 1 | 1 | 1 | 0 |

The same test file also covers two more cases:

- `A_saved_change_is_visible_on_the_very_next_read` checks that the next read sees each change. Four changes are made through tracked writes, as the API's command handlers make them:
  - the subscription's modules change
  - a new leave type is added
  - a task category is renamed inside an explicit transaction
  - a new platform-wide permission is added
- `Two_tenants_never_share_an_entry` uses two tenants in one process cache. It also checks that a request resolved to tenant A, asking for tenant B, returns no rows and does not fill B's entry.

**Not measured:** wall-clock time per request, and how many times one real API request calls each lookup. The table proves that the repeat SQL commands go away; it does not show milliseconds saved. That needs the task 06 Part B baseline (after #238) or UAT request logs.

## Known limits

- **Multi-instance.** Removals reach other instances over the Redis bus (task 21). If a message is lost, another instance keeps serving its entry for up to 5 minutes. For example, a subscription moved to `past_due` would still show its modules there until the entry expires. The task 21 reconnect handler drops everything after a lost connection, which narrows this.
- **Bulk writes are not covered automatically.** Raw SQL or `ExecuteUpdate` on these tables would bypass the interceptor. None exists today. Code that adds one must call `ITenantCache.InvalidateAsync`.
- **Clock-in policies are cached per day.** The cached result is for one date, so a policy whose effective date starts tomorrow is read fresh tomorrow.

## Tests

- Unit: `TenantCacheTests`, `ReferenceDataCopyTests` (19 tests). Full unit suite 8496 passed, architecture 841 passed.
- Integration (SQL Server): `ReferenceDataCacheIntegrationTests` 3/3. Touched areas (Leave, TimeAttendance, Calendar, WorkManagement, Auth.Permission, Billing, Monitoring, Tenancy, SharedState, ApiBoot, Performance filters): 572/572 passed. The full integration suite was not run.
