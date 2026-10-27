# Customizable Grid Dashboard (My Work + My Team) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Home page's two dashboards (My Work `/dashboard`, My Team `/dashboard/my-team`) into server-persisted, per-user customizable grid dashboards with a Widget Store, and serve every widget's initial data through one bounded bootstrap call — without changing what any card shows or who may see it.

**Architecture:** Backend gets a new `Dashboard/Layout` feature: three tenant-owned tables (dashboards, widget instances, per-breakpoint layouts), a code-defined widget registry, pure grid rules, MediatR commands/queries, and a widget-data orchestrator that calls one `IWidgetDataProvider` per widget. Providers live in the API layer and call the **same MediatR query + `ToViewModel()` the existing controller calls**, re-checking that controller's permission/module gates, so payloads are byte-identical to today. Frontend replaces the old localStorage customize code with an in-house CSS-grid engine (pure TS grid math + pointer drag/resize), a per-page dashboard store, a widget registry and a Widget Store drawer; cards read their first payload from bootstrap and refresh through the per-widget endpoint.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core / PostgreSQL (RLS) / MediatR / xUnit + Moq; Angular 21 standalone + signals / Tailwind / Jasmine-Karma (existing `ng test`).

**Spec:** `HRMS-Backend-v1/docs/superpowers/specs/2026-10-20-customizable-dashboard-design.md` (read it first). Source guide: `C:\Users\User\Downloads\ONEXSO_Customizable_Grid_Dashboard_Implementation_Guide.md`.

## Global Constraints

- Repos: backend `C:\Users\User\Desktop\build\HRMS-Backend-v1`, frontend `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`. Work on a new branch `feature/customizable-dashboard` in each; ask the user which base branch before creating it (both repos currently sit on unpushed `feature/wm-team-progress-hours`).
- What every card displays and does in View mode must be identical to today (spec §4). Card internals may change only to accept data from the widget data context (Task F7) and minor CSS to fill their frame.
- Permission truth stays in `GetMyTeamCapabilitiesQuery`; no new permission tables (spec §5.3).
- My Work and My Team catalogs are disjoint (spec §5.1/§5.2). Quick Actions = two definitions: `quick-actions` (My Work) and `team-quick-actions` (My Team).
- One instance per widget type per dashboard (spec §3). `settings jsonb` column exists but is always `{}`.
- Owner key = `ICurrentUser.UserId` (spec §5.3).
- Seeding and Reset store the **full** default layout regardless of current permissions; not-allowed widgets are stored but never returned or rendered (spec §5.3).
- Grid: desktop 12 cols (≥1200px content width), tablet 6 cols (768–1199), mobile 4 cols (<768). Row height 96px, gap 12px. Integer x,y ≥ 0, w,h ≥ 1, x+w ≤ cols, no overlap.
- Layout is gravity-compacted (each widget floats up while the cells above are free) after every add/remove/move/resize and when rendering with hidden widgets filtered out. *(Plan decision: keeps the old "no holes when Action Center is not allowed" behavior.)*
- Backend: tables get the standard `tenant_isolation` RLS policy (copy the SQL block from `src/ONEVO.Infrastructure/Migrations/20261020074523_AddModuleHourSummaries.cs:135-157`); architecture suite must stay green.
- Providers bypass controller filters — each provider must re-check the exact `[RequirePermission]` / `[RequireAnyModule]` of the endpoint it mirrors (see table in Task B9).
- Frontend: zero template warnings — run `npm run check:warnings` before every FE commit. Do not use an imported const as component `styles` (use `styleUrl`) — known ng build hang.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Viewer without Action Center / without team access** — My Work shows no hole where Action Center would be; My Team shows Team Quick Actions + the "No team to show yet" empty state; Widget Store never lists a not-allowed widget. Tests: B4, B6, F3, F8.
2. **Capability flips between visits** (`HasWorkApprovals` only true while approvals pend) — a stored-but-hidden default widget reappears without the user doing anything, at its stored cell or the first free slot, with no overlap. Tests: B6 `Hidden_widget_that_becomes_allowed_is_reinserted_without_overlap`.
3. **Two tabs / two devices editing** — second Save gets 409 and the UI reloads instead of silently overwriting. Tests: B7, F3.
4. **One widget's provider throws or is slow** — bootstrap still returns 200 with that widget in `error` state; others have data. Tests: B9.
5. **Phone width** — 4-column layout derived from desktop, no horizontal scroll, every widget full width, editing works with keyboard/buttons. Tests: B1 (derived layouts valid), F2, F5.

---

## File Structure

Backend (new unless marked):
- `src/ONEVO.Domain/Features/Dashboard/Entities/UserDashboard.cs`, `DashboardWidget.cs`, `DashboardWidgetLayout.cs`
- `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs` — definitions, default layouts, derived breakpoints
- `.../Layout/Grid/DashboardGrid.cs` — pure rules: validate, collide, auto-place, compact
- `.../Layout/Access/IDashboardWidgetAccess.cs` + `DashboardWidgetAccess.cs`
- `.../Layout/RepositoryInterfaces/IUserDashboardRepository.cs`
- `.../Layout/DTOs/*.cs`, `.../Layout/Queries/*`, `.../Layout/Commands/*`
- `.../Layout/Data/IWidgetDataProvider.cs`, `WidgetDataOrchestrator.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/Dashboard/*Configuration.cs`, `Persistence/Repositories/Dashboard/EfUserDashboardRepository.cs`, migration `AddCustomizableDashboards`
- `src/ONEVO.Api/Controllers/Tenant/Dashboard/DashboardLayoutController.cs`
- `src/ONEVO.Api/Dashboard/Widgets/*WidgetDataProvider.cs`
- Tests under `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/` and `tests/ONEVO.Tests.Architecture/CustomizableDashboardArchitectureTests.cs`

Frontend (under `src/app/modules/dashboard/`):
- `models/dashboard-layout.model.ts`, `data-access/dashboard-layout-api.service.ts`
- `grid/grid-math.ts` (pure), `grid/breakpoint.ts`
- `state/dashboard.store.ts`
- `widgets/widget-registry.ts`, `widgets/widget-data.ts` (data context)
- `ui/widget-frame/`, `ui/dashboard-grid/`, `ui/unsupported-widget/`
- `feature/widget-store-drawer/`, `feature/dashboard-shell/`
- Modified: `feature/dashboard-home/*`, `feature/my-team-page/*`, cards listed in F7
- Deleted: see Task F9

---

## Part B — Backend

### Task B1: Widget registry and default layouts

**Files:**
- Create: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardWidgetRegistryTests.cs`

**Interfaces:**
- Produces:
  - `public static class DashboardKeys { public const string MyWork = "my-work"; public const string MyTeam = "my-team"; }`
  - `public static class Breakpoints { public const string Desktop="desktop", Tablet="tablet", Mobile="mobile"; public static int Columns(string bp); public static readonly string[] All; }` (12/6/4)
  - `public sealed record GridRect(int X, int Y, int W, int H);`
  - `public sealed record WidgetDefinition(string Key, string DashboardKey, string Title, string Description, int MinW, int MinH, int MaxW, int MaxH, bool HasProvider);` (sizes are desktop; tablet/mobile clamp W to the column count)
  - `DashboardWidgetRegistry.Get(string key) -> WidgetDefinition?`, `.ForDashboard(string dashboardKey) -> IReadOnlyList<WidgetDefinition>`, `.DefaultLayout(string dashboardKey, string breakpoint) -> IReadOnlyList<(string Key, GridRect Rect)>`, `.Derive(IReadOnlyList<(string Key, GridRect Rect)> desktop, string breakpoint) -> same type`

Definitions (desktop, columns × rows; default = position in default layout):

| Key | Dashboard | Default rect (x,y,w,h) | Min w×h | Max w×h | Provider |
|---|---|---|---|---|---|
| today-at-a-glance | my-work | 0,0,3,4 | 3×3 | 6×5 | yes |
| action-center | my-work | 3,0,6,4 | 4×3 | 12×6 | no (client-sourced) |
| quick-actions | my-work | 9,0,3,4 | 3×3 | 6×4 | no |
| todays-schedule | my-work | 0,4,6,4 | 3×3 | 12×6 | yes |
| today-total-time | my-work | 6,4,3,4 | 3×3 | 6×5 | yes |
| active-projects | my-work | 9,4,3,4 | 3×3 | 6×6 | yes |
| my-tasks | my-work | 0,8,6,4 | 3×3 | 12×6 | yes |
| work-pattern | my-work | 0,12,12,4 | 6×3 | 12×5 | yes |
| notifications | my-work | not placed | 3×3 | 6×6 | no |
| wellbeing | my-work | not placed | 3×3 | 6×4 | yes |
| team-status | my-team | 0,0,8,6 | 6×4 | 12×8 | yes |
| team-quick-actions | my-team | 8,0,4,4 | 3×3 | 6×4 | no |
| team-progress | my-team | 8,4,4,6 | 4×4 | 12×8 | yes |

`Derive`: tablet — sort by (y,x), `w = desktop.w >= 6 ? 6 : 3`, place each with `DashboardGrid.AutoPlace` in order; mobile — sort by (y,x), `w = 4`, stack. `h` unchanged.

- [ ] **Step 1: Write failing tests**
  - `Catalogs_are_disjoint_and_every_key_belongs_to_exactly_one_dashboard`
  - `Default_layouts_are_valid_for_every_dashboard_and_breakpoint` — for each dashboard × breakpoint, `DashboardGrid.Validate(...)` returns no errors (bounds, min/max, no overlap).
  - `Default_rects_respect_each_definitions_min_and_max`
  - `Mobile_derived_layout_is_single_column_in_desktop_reading_order` — my-work mobile keys order equals `today-at-a-glance, action-center, quick-actions, todays-schedule, today-total-time, active-projects, my-tasks, work-pattern`, all `X==0, W==4`.
  - `Notifications_and_wellbeing_are_in_catalog_but_not_in_default_layout`
- [ ] **Step 2: Run** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DashboardWidgetRegistryTests"` — expect FAIL (types missing). (This task and B2 compile together; write B2's `DashboardGrid` signatures first if needed.)
- [ ] **Step 3: Implement** the registry as static readonly data.
- [ ] **Step 4: Run** the same filter — expect PASS.
- [ ] **Step 5: Commit** `feat(dashboard): code-defined widget registry and default layouts`

### Task B2: Pure grid rules

**Files:**
- Create: `src/ONEVO.Application/Features/Dashboard/Layout/Grid/DashboardGrid.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardGridTests.cs`

**Interfaces:**
- Produces (all static, no I/O):
  - `IReadOnlyList<string> Validate(IReadOnlyList<(string Key, GridRect Rect)> items, string breakpoint)` — error codes: `OUT_OF_BOUNDS`, `SIZE_NOT_ALLOWED`, `OVERLAP`, `UNKNOWN_WIDGET`, `DUPLICATE_WIDGET`. Size rule: `MinW ≤ w ≤ min(MaxW, cols)`, `MinH ≤ h ≤ MaxH`; on tablet/mobile `MinW` is `min(MinW, cols)`.
  - `bool Overlaps(GridRect a, GridRect b)`
  - `GridRect AutoPlace(IEnumerable<GridRect> occupied, int w, int h, int columns)` — first free slot scanning y = 0.., x = 0..cols-w (top-left).
  - `IReadOnlyList<(string Key, GridRect Rect)> Compact(IReadOnlyList<(string Key, GridRect Rect)> items)` — process sorted by (y,x); move each up while `y > 0` and no overlap with already-placed items.

- [ ] **Step 1: Failing tests:** `Overlap_detected_for_shared_cell`, `Touching_edges_do_not_overlap`, `Out_of_bounds_when_x_plus_w_exceeds_columns`, `Size_below_min_rejected` (team-status w=5 → `SIZE_NOT_ALLOWED`), `AutoPlace_picks_first_free_top_left_slot` (occupied `0,0,6,4` + `6,0,6,2`, w=6 h=2 → `6,2,6,2`), `Compact_floats_widget_into_hole` (items `a 0,0,3,4`, `b 6,4,3,4` → b becomes `6,0,3,4`), `Compact_never_creates_overlap`.
- [ ] **Step 2:** Run filter `DashboardGridTests` — FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run — PASS (and B1 tests PASS).
- [ ] **Step 5:** Commit `feat(dashboard): grid validation, auto-place and compaction rules`

### Task B3: Entities, EF configuration, migration with RLS, repository

**Files:**
- Create: Domain entities (3), `Configurations/Dashboard/UserDashboardConfiguration.cs`, `DashboardWidgetConfiguration.cs`, `DashboardWidgetLayoutConfiguration.cs`; `Application/.../Layout/RepositoryInterfaces/IUserDashboardRepository.cs`; `Infrastructure/Persistence/Repositories/Dashboard/EfUserDashboardRepository.cs`; migration via `dotnet ef migrations add AddCustomizableDashboards`.
- Modify: `ApplicationDbContext.cs` (3 `DbSet`s), `Infrastructure/DependencyInjection.cs` (register repo next to line 433 pattern).
- Test: `tests/ONEVO.Tests.Architecture/CustomizableDashboardArchitectureTests.cs`

**Interfaces:**
- Entities (all `: BaseEntity`):
  - `UserDashboard { Guid UserId; string DashboardKey; long LayoutVersion = 1; List<DashboardWidget> Widgets }` → table `user_dashboards`, unique index `(tenant_id, user_id, dashboard_key)`.
  - `DashboardWidget { Guid DashboardId; string WidgetKey; string SettingsJson = "{}"; List<DashboardWidgetLayout> Layouts }` → `dashboard_widgets`, `settings jsonb`, unique `(dashboard_id, widget_key)`, FK cascade.
  - `DashboardWidgetLayout { Guid WidgetId; string Breakpoint; int X; int Y; int W; int H }` → `dashboard_widget_layouts`, unique `(widget_id, breakpoint)`, CHECKs `x>=0, y>=0, w>0, h>0, breakpoint in ('desktop','tablet','mobile')`, FK cascade.
- Repository:
  - `Task<UserDashboard?> GetAsync(Guid tenantId, Guid userId, string dashboardKey, CancellationToken ct)` (includes widgets + layouts)
  - `Task<UserDashboard?> GetByIdAsync(Guid tenantId, Guid dashboardId, CancellationToken ct)` (same includes)
  - `Task<IReadOnlyList<UserDashboard>> ListForUserAsync(Guid tenantId, Guid userId, CancellationToken ct)` (no includes)
  - `Task<bool> TryAddAsync(UserDashboard dashboard, CancellationToken ct)` — returns false on unique-violation (seeding race)
  - `Task<bool> SaveWithVersionAsync(UserDashboard dashboard, long expectedVersion, CancellationToken ct)` — sets `LayoutVersion = expectedVersion + 1` and saves only if the stored version still equals `expectedVersion` (configure `LayoutVersion` as a concurrency token; catch `DbUpdateConcurrencyException` → false).

- [ ] **Step 1: Failing architecture test** `Dashboard_tables_have_tenant_isolation_rls` — follow how existing architecture tests assert RLS for a migration (search `tests/ONEVO.Tests.Architecture` for `tenant_isolation`) and assert all three table names are covered in the `AddCustomizableDashboards` migration.
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Architecture` — the new test FAILS.
- [ ] **Step 3:** Implement entities, configurations, DbSets, repo, DI; generate migration; append the RLS loop (TenantTables = the three names).
- [ ] **Step 4:** `dotnet build src/ONEVO.Application` then `dotnet test tests/ONEVO.Tests.Architecture` — all PASS. If `dotnet build` fails with NuGet.targets "path1 null", run `dotnet build-server shutdown` and retry. Inspect `ApplicationDbContextModelSnapshot.cs` diff: only the three new entities may appear.
- [ ] **Step 5:** Commit `feat(dashboard): persistence for user dashboards, widgets and layouts (RLS)`

### Task B4: Widget access resolver

**Files:**
- Create: `.../Layout/Access/IDashboardWidgetAccess.cs`, `DashboardWidgetAccess.cs`; register in Application DI.
- Test: `tests/.../Layout/DashboardWidgetAccessTests.cs`

**Interfaces:**
- Consumes: `GetMyTeamCapabilitiesQuery` via `IMediator`.
- Produces: `Task<IReadOnlySet<string>> GetAllowedKeysAsync(string dashboardKey, CancellationToken ct)` — memoized per request (scoped service).

Rules (mirror `my-team-widget-visibility.util.ts` + spec §5): my-work → all my-work keys except `action-center`, plus `action-center` when `CanReviewPeopleApprovals || CanReviewExceptions || HasWorkApprovals || LeadsWork`. my-team → `team-quick-actions` always; `team-status` when `IsAvailable && CanViewPeopleStatus`; `team-progress` when `IsAvailable && LeadsWork`. Capabilities query failure → treat all flags false (team widgets hidden, never an exception).

- [ ] **Step 1: Failing tests:** `Plain_employee_gets_my_work_without_action_center`, `Lead_gets_action_center_and_team_progress`, `Approval_only_viewer_gets_action_center_but_no_team_status_or_progress`, `Team_quick_actions_always_allowed`, `Capabilities_failure_hides_gated_widgets`.
- [ ] **Step 2:** Run — FAIL. **Step 3:** Implement. **Step 4:** Run — PASS.
- [ ] **Step 5:** Commit `feat(dashboard): widget access from My Team capabilities`

### Task B5: List dashboards with lazy race-safe seeding

**Files:**
- Create: `.../Layout/Queries/GetMyDashboards/GetMyDashboardsQuery.cs` + handler; `.../Layout/Services/DashboardSeeder.cs` (`Task<UserDashboard> EnsureAsync(string dashboardKey, CancellationToken ct)`)
- Test: `GetMyDashboardsQueryHandlerTests.cs`, `DashboardSeederTests.cs`

**Interfaces:**
- Produces: `record DashboardSummaryDto(Guid Id, string Key, string Title, long LayoutVersion)`; query returns `Result<IReadOnlyList<DashboardSummaryDto>>` ordered my-work, my-team. Titles: "My Work", "My Team".
- `DashboardSeeder.EnsureAsync`: get; if missing build from **full** `DefaultLayout` for all 3 breakpoints (desktop = registry, tablet/mobile = `Derive`), `TryAddAsync`; if false, re-read and return the winner.

- [ ] **Step 1: Failing tests:** `First_call_seeds_both_dashboards_with_full_default_including_action_center_for_plain_employee`, `Seeding_race_returns_existing_dashboard`, `Unauthenticated_is_forbidden`.
- [ ] **Steps 2–4:** run FAIL → implement → run PASS.
- [ ] **Step 5:** Commit `feat(dashboard): list my dashboards with lazy default seeding`

### Task B6: Bootstrap (layout part)

**Files:**
- Create: `.../Layout/Queries/GetDashboardBootstrap/GetDashboardBootstrapQuery.cs` + handler, `DTOs/DashboardBootstrapDto.cs`
- Test: `GetDashboardBootstrapQueryHandlerTests.cs`

**Interfaces:**
- `GetDashboardBootstrapQuery(Guid DashboardId, string Breakpoint)`.
- Produces:
  - `record GridSpecDto(string Breakpoint, int Columns, int RowHeight, int Gap)` — 96 / 12.
  - `record WidgetSizeDto(int MinW, int MinH, int MaxW, int MaxH)` (already clamped to the breakpoint's columns)
  - `record WidgetDataStateDto(string Status, DateTimeOffset? FetchedAt, string? Error)` — Status ∈ `success|error|not_loaded|client`
  - `record BootstrapWidgetDto(Guid InstanceId, string WidgetKey, string Title, GridRect Layout, WidgetSizeDto Size, WidgetDataStateDto DataState, object? Data)`
  - `record DashboardBootstrapDto(Guid Id, string Key, string Title, long LayoutVersion, GridSpecDto Grid, IReadOnlyList<BootstrapWidgetDto> Widgets, bool HasAllowedGatedWidgets)` — `HasAllowedGatedWidgets` false on my-team when neither team-status nor team-progress is allowed (drives the empty state).

Algorithm: load dashboard (404 if not found or `UserId != currentUser.UserId`); 422 on unknown breakpoint; filter instances to allowed keys and existing registry keys (unknown registry key → include with Status `error`, Error `UNSUPPORTED_WIDGET`, so the FE shows the placeholder); a widget with no layout row for this breakpoint gets `AutoPlace`; then for widgets whose stored rect overlaps an earlier (y,x-sorted) visible widget → `AutoPlace`; then `Compact`. If any rect changed vs stored, persist via `SaveWithVersionAsync` (on false, ignore — next load retries) and return the new version. In this task Data = null, Status = `not_loaded` (B9 fills it).

- [ ] **Step 1: Failing tests:** `Returns_only_allowed_widgets_for_breakpoint`, `Other_users_dashboard_is_not_found`, `Hidden_action_center_leaves_no_hole_after_compaction` (plain employee: `todays-schedule` y==4→ stays, `today-total-time` floats to y 0), `Hidden_widget_that_becomes_allowed_is_reinserted_without_overlap`, `Unknown_breakpoint_is_unprocessable`, `My_team_without_gated_widgets_reports_HasAllowedGatedWidgets_false`.
- [ ] **Steps 2–4:** FAIL → implement → PASS.
- [ ] **Step 5:** Commit `feat(dashboard): bootstrap query with permission filtering and compaction`

### Task B7: Mutations — add, remove, save layout, reset

**Files:**
- Create under `.../Layout/Commands/`: `AddDashboardWidget`, `RemoveDashboardWidget`, `SaveDashboardLayout`, `ResetDashboard` (command + handler each)
- Test: one test class per handler.

**Interfaces:**
- `AddDashboardWidgetCommand(Guid DashboardId, string WidgetKey, long ExpectedVersion, GridRect? DesktopRect)` → `Result<AddWidgetResultDto(Guid InstanceId, long LayoutVersion)>`. Rejects: not in this dashboard's catalog / not allowed → 403; already placed → 409 `WIDGET_ALREADY_PLACED`. Places on every breakpoint: desktop uses `DesktopRect` if given and valid against visible widgets, else `AutoPlace` with definition default size; tablet/mobile `AutoPlace` with derived width.
- `RemoveDashboardWidgetCommand(Guid DashboardId, Guid InstanceId, long ExpectedVersion)` → `Result<long>` (new version).
- `SaveDashboardLayoutCommand(Guid DashboardId, string Breakpoint, long ExpectedVersion, IReadOnlyList<LayoutItemDto(Guid InstanceId, int X, int Y, int W, int H)> Widgets)` → `Result<long>`. Must equal exactly the set of **visible** (allowed) instances, else 422 `LAYOUT_SET_MISMATCH`; `DashboardGrid.Validate` errors → 422 with codes; hidden instances untouched.
- `ResetDashboardCommand(Guid DashboardId, long ExpectedVersion)` → `Result<long>`: replace instances/layouts with full default (B5 builder), bump version.
- All: owner check (404), version mismatch → `Result.Conflict("LAYOUT_VERSION_CONFLICT")` with the current version surfaced (follow how other handlers attach extra data to a conflict; if none, include it in the message as `currentVersion=N`).

- [ ] **Step 1: Failing tests** (per handler): happy path bumps version; `Stale_version_conflicts`; `Foreign_dashboard_not_found`; Add: `Team_widget_on_my_work_forbidden`, `Not_allowed_widget_forbidden`, `Duplicate_conflicts`, `New_widget_placed_on_all_breakpoints_without_overlap`; Save: `Overlap_rejected`, `Missing_visible_instance_rejected`, `Hidden_instance_untouched`, `Below_min_size_rejected`; Reset: `Restores_full_default_including_not_allowed_widgets`.
- [ ] **Steps 2–4:** FAIL → implement → PASS.
- [ ] **Step 5:** Commit `feat(dashboard): add/remove/save-layout/reset with optimistic concurrency`

### Task B8: Controller

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Dashboard/DashboardLayoutController.cs`; available-widgets query `.../Queries/GetAvailableWidgets` returning `record AvailableWidgetDto(string Key, string Title, string Description, WidgetSizeDto Size, bool Placed)`.
- Test: extend `CustomizableDashboardArchitectureTests` with `Dashboard_layout_controller_requires_tenant_policy`; handler tests for available widgets (`Lists_only_allowed_catalog_for_dashboard`, `Marks_placed`).

Routes (`[Route("api/v1/dashboards")]`, `[Authorize(Policy = "TenantPolicy")]`, same `result.IsSuccess ? Ok : Problem(...)` shape as `TeamDashboardController`):
`GET me` · `GET {id:guid}/bootstrap?breakpoint=desktop` · `GET {id:guid}/widgets/available` · `POST {id:guid}/widgets` (body `{widgetKey, expectedVersion, desktopRect?}`, 201) · `DELETE {id:guid}/widgets/{instanceId:guid}?expectedVersion=` · `PUT {id:guid}/layouts/{breakpoint}` (body `{expectedVersion, widgets}`) · `POST {id:guid}/reset` (body `{expectedVersion}`) · `GET /api/v1/dashboard-widgets/{instanceId:guid}/data` (B9; use an absolute route on the action). All responses `Cache-Control: no-store`.

- [ ] Steps: failing tests → implement → `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Dashboard"` and architecture suite PASS → commit `feat(dashboard): dashboard layout API`.

### Task B9: Widget data orchestrator, providers, refresh endpoint

**Files:**
- Create: `.../Layout/Data/IWidgetDataProvider.cs`, `WidgetDataOrchestrator.cs`; `.../Queries/GetWidgetData` (+ handler); providers in `src/ONEVO.Api/Dashboard/Widgets/` registered in Api DI.
- Modify: `GetDashboardBootstrapQueryHandler` to call the orchestrator.
- Test: `WidgetDataOrchestratorTests.cs`, `GetWidgetDataQueryHandlerTests.cs`, one test class per provider in `tests/ONEVO.Tests.Unit/Api/Dashboard/Widgets/` (or wherever Api-layer unit tests live — search for an existing one first).

**Interfaces:**
- `public interface IWidgetDataProvider { string WidgetKey { get; } Task<Result<object>> GetAsync(CancellationToken ct); }`
- `WidgetDataOrchestrator.LoadAsync(IReadOnlyList<string> widgetKeys, CancellationToken ct) -> Task<IReadOnlyDictionary<string, (WidgetDataStateDto State, object? Data)>>` — max 4 concurrent (`SemaphoreSlim`), per-provider deadline 3 s (linked CTS), exception/timeout/failed Result → `error` state with a generic message (never the exception text), widget without provider → `client`.
- `GetWidgetDataQuery(Guid InstanceId)` → `Result<WidgetDataResponseDto(Guid InstanceId, string Status, DateTimeOffset FetchedAt, object? Payload)>`: resolve instance → dashboard → owner (404) → allowed (403) → provider (404 `NO_PROVIDER`).

Providers — each calls exactly what the existing endpoint calls, with the parameters the FE sends today (read them from the FE `data-access/*-api.service.ts` and card components), re-checks the endpoint's gates, and returns the same view model:

| Widget | Mirrors endpoint | Query + mapping | Gates to re-check |
|---|---|---|---|
| my-tasks | `GET work/my-tasks` | `GetMyActiveTasksQuery(upcomingDays)` → `.ToViewModel()` | any WM module active (`IModuleEntitlementService.GetActiveModuleKeysForTenantAsync`, keys as `TasksController`) |
| team-progress | `GET work/led-progress` | `GetLedWorkProgressQuery(overdueLimit)` | same WM module set |
| team-status | `GET attendance/time-tracking/team/today` | `GetCoveredTeamTodayQuery(limit)` | `attendance:read` |
| today-at-a-glance, today-total-time | `GET monitoring/activity/my-timeline` | `GetMyActivityTimelineQuery(date)` | `activity:read:self` |
| work-pattern | `GET monitoring/activity/my-work-pattern` | `GetMyWorkPatternQuery(from,to)` | `activity:read:self` |
| wellbeing | `GET monitoring/activity/my-focus-status` | `GetMyFocusStatusQuery()` | `activity:read:self` |
| todays-schedule | `GET calendar?from&to` (today in viewer tz) | `GetCalendarEventsQuery(from,to)` | `calendar:read` |
| active-projects | the `ProjectApiService` call in `active-projects-card.component.ts` | that endpoint's query + mapping | that endpoint's gates |

Gate failure → `Result.Forbidden()` → widget `error` state, which the card renders exactly as it renders a 403 today. Where today's endpoint takes a date/time-zone the card computes client-side (schedule, timeline, work pattern), the provider computes it the same way server-side; if that can't match exactly, mark the widget `HasProvider = false` in B1 and leave it client-sourced — note it in the final report.

While wrapping, inventory each underlying handler for unbounded lists / N+1; fix only clear, local problems with a test, list the rest in the final report.

- [ ] **Step 1: Failing tests:** orchestrator `Provider_exception_becomes_widget_error_and_others_succeed`, `Slow_provider_times_out_into_error`, `Widget_without_provider_is_client`, `Never_more_than_4_concurrent`; refresh `Foreign_instance_not_found`, `Not_allowed_widget_forbidden`; per provider `Missing_permission_is_forbidden` and `Returns_same_view_model_as_endpoint` (mock mediator, assert the exact query args and mapped type).
- [ ] **Steps 2–4:** FAIL → implement → PASS; full `dotnet test tests/ONEVO.Tests.Unit` + architecture PASS.
- [ ] **Step 5:** Commit `feat(dashboard): bounded widget data providers in bootstrap and per-widget refresh`

---

## Part F — Frontend (`src/app/modules/dashboard/`)

Run tests with the repo's existing command (check `package.json`, e.g. `npx ng test --watch=false --include=<glob>`).

### Task F1: Models and API service

**Files:** Create `models/dashboard-layout.model.ts`, `data-access/dashboard-layout-api.service.ts` (+ spec).

**Interfaces:**
- Produces TS mirrors of B5–B9 DTOs: `DashboardKey = 'my-work' | 'my-team'`, `Breakpoint = 'desktop'|'tablet'|'mobile'`, `GridRect {x,y,w,h}`, `WidgetSize`, `WidgetDataState {status: 'success'|'error'|'not_loaded'|'client'|'loading'; fetchedAt: string|null; error: string|null}`, `BootstrapWidget`, `DashboardBootstrap`, `DashboardSummary`, `AvailableWidget`.
- `DashboardLayoutApiService`: `listMine()`, `bootstrap(id, bp)`, `available(id)`, `addWidget(id, key, expectedVersion, desktopRect?)`, `removeWidget(id, instanceId, expectedVersion)`, `saveLayout(id, bp, expectedVersion, items)`, `reset(id, expectedVersion)`, `widgetData(instanceId)` — all `Observable`, base `${environment.apiUrl}/dashboards`.
- [ ] Spec with `HttpTestingController` asserting URL/method/body per call → run FAIL → implement → PASS → commit `feat(dashboard): layout API client and models`.

### Task F2: Grid math (pure) and breakpoint signal

**Files:** Create `grid/grid-math.ts` (+ spec), `grid/breakpoint.ts` (+ spec).

**Interfaces:**
- `overlaps(a, b)`, `autoPlace(occupied: GridRect[], w, h, cols): GridRect`, `compact<T extends {rect: GridRect}>(items: T[]): T[]`, `clampSize(rect, size: WidgetSize, cols): GridRect`, `moveTo(items, id, x, y, cols)` (moves item, pushes colliding items down, then compacts), `resizeTo(items, id, w, h, size, cols)` (snap to integers within size, push colliders down, compact), `pixelToCell(px, cellPx, gapPx): number` — same semantics as backend B2.
- `breakpoint.ts`: `injectBreakpoint(host: ElementRef): Signal<Breakpoint>` using `ResizeObserver` on the grid container width (≥1200 desktop, ≥768 tablet, else mobile).
- [ ] Specs mirroring B2's cases plus `moveTo_pushes_collider_down_without_overlap`, `resizeTo_never_exceeds_max_or_columns` → FAIL → implement → PASS → commit `feat(dashboard): grid math and breakpoint detection`.

### Task F3: Dashboard store

**Files:** Create `state/dashboard.store.ts` (+ spec). Component-provided (one instance per dashboard page), not `providedIn: 'root'`.

**Interfaces:**
- `load(key: DashboardKey)`; signals `bootstrap`, `editing`, `draft: BootstrapWidget[]`, `widgets` (draft while editing else saved), `available: AvailableWidget[]`, `saving`, `error`.
- `startEditing()`, `cancel()`, `add(key)` (draft-only: `autoPlace` with definition default size, temp id `new:<key>`), `remove(instanceId)` (draft-only), `move(...)`, `resize(...)`, `resetToDefault()` (calls `reset` API after confirm — reset is immediate, not draft), `save()`:
  1. DELETE each removed instance, chaining `expectedVersion` from each response;
  2. POST each added widget with its draft desktop rect (only when current breakpoint is desktop; otherwise no rect);
  3. PUT the full layout for the current breakpoint with real instance ids;
  4. re-bootstrap. Any 409 → toast "Your dashboard changed in another tab or device — reloaded." and re-bootstrap, discarding the draft.
- Breakpoint change while not editing → re-bootstrap; while editing → keep editing the current breakpoint's draft.
- [ ] Spec cases: `cancel_discards_all_draft_changes`, `save_sends_deletes_then_adds_then_one_put`, `no_api_call_during_move_or_resize`, `conflict_reloads_and_exits_editing`, `available_excludes_placed_and_draft_added` → FAIL → implement → PASS → commit `feat(dashboard): dashboard store with draft/save/cancel/409`.

### Task F4: Widget registry, data context, frame, placeholder

**Files:** Create `widgets/widget-registry.ts`, `widgets/widget-data.ts`, `ui/widget-frame/`, `ui/unsupported-widget/` (+ specs).

**Interfaces:**
- `WIDGET_REGISTRY: Record<string, { component: Type<unknown>; inputs?: Record<string, unknown> }>` — the 13 keys from B1 → existing card components (`team-quick-actions` → `QuickActionsCardComponent` with `{ onTeamDashboard: true }`). Unknown key → `UnsupportedWidgetComponent`.
- `widget-data.ts`: `WidgetDataContext` provided by the frame per widget: `{ instanceId: string; state: Signal<WidgetDataState>; data: Signal<unknown>; refresh(): void }` (refresh calls `widgetData(instanceId)`, dedupes in-flight, aborts on destroy). `injectWidgetData<T>(): WidgetDataContext<T> | null` — returns null when the card is rendered outside a dashboard frame.
- `WidgetFrameComponent` inputs `widget: BootstrapWidget`, `editing: boolean`; renders the registry component via `NgComponentOutlet` with inputs; in edit mode shows drag handle, ✕ remove button (`aria-label="Remove <title>"`), resize handle, and an overlay blocking clicks into the card; status `error` with `UNSUPPORTED_WIDGET` → placeholder.
- [ ] Specs: `renders_registered_component_with_inputs`, `unknown_key_renders_placeholder`, `edit_mode_blocks_card_clicks_and_shows_remove`, `refresh_dedupes_concurrent_calls` → FAIL → implement → PASS → commit `feat(dashboard): widget registry, frame and per-widget data context`.

### Task F5: Grid component (render, drag, resize, keyboard)

**Files:** Create `ui/dashboard-grid/` (+ spec).

**Interfaces:**
- Inputs: `widgets: BootstrapWidget[]`, `columns`, `rowHeight`, `gap`, `editing`. Outputs: `moved(id, x, y)`, `resized(id, w, h)`, `removed(id)`.
- CSS grid: `grid-template-columns: repeat(columns, minmax(0,1fr))`, `grid-auto-rows: rowHeight`, each frame `grid-column: x+1 / span w; grid-row: y+1 / span h`.
- Drag: pointer events on the frame handle; ghost preview cell computed with `pixelToCell`; emit `moved` on pointerup only. Resize: bottom-right handle, same pattern, emit `resized`.
- Keyboard (edit mode, focused frame): arrows move by one cell, Shift+arrows resize by one cell, Delete removes. Respects `prefers-reduced-motion` (no transition).
- [ ] Specs: `places_widget_by_grid_lines`, `keyboard_arrow_emits_moved`, `shift_arrow_emits_resized`, `no_emit_during_pointer_move_only_on_up` → FAIL → implement → PASS → commit `feat(dashboard): editable CSS grid with drag, resize and keyboard`.

### Task F6: Widget Store drawer, toolbar, Customize button

**Files:** Create `feature/widget-store-drawer/`, `feature/dashboard-shell/` (+ specs).

**Interfaces:**
- `DashboardShellComponent` input `dashboardKey`; provides `DashboardStore`; hosts toolbar (edit mode: "Customize your dashboard", buttons `Add widget`, `Reset`, `Cancel`, `Save`), `dashboard-grid`, drawer. Registers the navbar Customize button via `PageToolbarService.set({label: 'Customize', disabled, onClick})` exactly like today's `dashboard-home` (and clears it in edit mode and on destroy); Escape cancels.
- Drawer: right side panel listing `store.available()` (title, description, default size), `Add` button per item; empty state "All widgets are on your dashboard".
- Reset → confirm dialog using the app's existing confirm dialog component (search `shared/ui` for it).
- Shell renders an `emptyState` content projection slot when `bootstrap().hasAllowedGatedWidgets === false` (used by My Team).
- [ ] Specs: `customize_button_enters_edit_mode`, `drawer_lists_only_available`, `add_from_drawer_places_in_draft`, `reset_requires_confirmation`, `escape_cancels` → FAIL → implement → PASS → commit `feat(dashboard): widget store drawer and dashboard shell`.

### Task F7: Cards read bootstrap data

**Files (modify):** `my-tasks-card`, `team-progress-card` (+ `state/my-team-led-progress.store.ts`), `team-status-card` (+ `state/my-team-today.store.ts`), `today-at-a-glance-card`, `today-total-time-card`, `work-pattern-card`, `wellbeing-carousel-card`, `todays-schedule-card`, `active-projects-card` — only those whose B1 `HasProvider` stayed true.

Pattern per card: `const ctx = injectWidgetData<Payload>();` — if `ctx` and `ctx.state().status === 'success'`, use `ctx.data()` instead of the first fetch; reload/polling paths call `ctx.refresh()`; if `ctx` is null or status is `client`/`not_loaded`, keep today's fetch unchanged. Payload types are the existing FE models (backend returns the same view model).

- [ ] For each card: spec `uses_bootstrap_payload_without_calling_its_api` and `falls_back_to_own_api_outside_dashboard` → FAIL → implement → PASS. Existing card specs must still pass unchanged except for added providers.
- [ ] Commit `feat(dashboard): cards take their first payload from the dashboard bootstrap`.

### Task F8: Host pages

**Files (modify):** `feature/dashboard-home/dashboard-home.component.{ts,html,css}` → `<app-dashboard-shell dashboardKey="my-work" />` only (drop viewport-height tracking and the CDK edit template); `feature/my-team-page/my-team-page.component.{ts,html}` → `<app-dashboard-shell dashboardKey="my-team">` with today's empty-state and "Try again" markup in the projected slot (keep `my-team-empty` / `my-team-retry` test ids).
- [ ] Specs: home `renders_my_work_shell`; my-team `shows_empty_state_when_no_team_widgets`, `retry_reloads` → FAIL → implement → PASS → commit `feat(dashboard): My Work and My Team pages host the customizable shell`.

### Task F9: Remove old customization code

**Delete** (after `grep -r` shows no remaining imports): `state/dashboard-layout.service.ts` (+spec), `utils/dashboard-layout.util.ts` (+spec), `feature/dashboard-customize-panel/`, `ui/dashboard-grid-item/`, `feature/dashboard-card/`, `utils/my-team-widget-preference.util.ts` (+spec), `resolveVisibleWidgets` in `my-team-widget-visibility.util.ts` if unused, old CSS in `dashboard-home.component.css`. Add a one-time cleanup in `DashboardShellComponent` constructor removing localStorage keys starting with `onexso.dashboard_layout` and `onexso.my_team_widget_order` (try/catch).
- [ ] Then scan `modules/dashboard` for any other component/service/util with zero references (`grep` each export) — list findings in the final report; delete only those that are clearly orphaned by this change.
- [ ] Run `npm run check:warnings`, `npx ng build`, full `ng test` — all green (note pre-existing failures separately with evidence they fail on the base branch too).
- [ ] Commit `refactor(dashboard): remove the old localStorage customize implementation`.

### Task V: Verification

- [ ] Backend: `dotnet test tests/ONEVO.Tests.Unit` and `tests/ONEVO.Tests.Architecture` full suites green; migration applied locally (ask the user before applying to any shared DB).
- [ ] Browser pass on dev (`dapi.onexso.com:4200` style host — use Playwright if the built-in pane can't reach it): plain employee (My Work, no Action Center, no hole); lead (both dashboards, Action Center + team widgets); customize → add Wellbeing → move → resize → Save → reload in a second browser shows same layout; second tab Save → 409 toast; tablet + phone widths; Reset.
- [ ] Final report: providers that stayed client-sourced and why, slow-query findings, any orphan code found but not deleted.
