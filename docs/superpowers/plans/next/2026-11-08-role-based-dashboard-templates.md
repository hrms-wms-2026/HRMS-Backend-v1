# Role-based Dashboard Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Seed, reset and auto-sync every user's dashboard from tenant-owned, role-based template tables (No-role
template as the only fallback, multi-role union merge), and remove the code default and the permission-variant
layouts.

**Architecture:** Three new tenant-owned tables hold templates. A scoped resolver picks the user's templates (active
roles by `AssignedAt`, else the No-role template). A pure merger unions them per breakpoint. Seed, Reset and a new
bootstrap auto-sync copy the merged result into the existing user tables. A display-only reading-order reflow
replaces the variant layouts. A startup seeder writes the No-role templates plus mock role templates. All
dashboard docs move to FE `docs/core/Dashboard/`.

**Tech Stack:** .NET 10, EF Core + Npgsql (snake_case), PostgreSQL RLS, MediatR, xUnit + Moq + FluentAssertions,
Testcontainers (integration).

**Spec:** `HRMS-Backend-v1/docs/superpowers/specs/next/2026-11-08-role-based-dashboard-templates-design.md`. Read it
first. Section numbers below (§) refer to it.

## Global Constraints

- **Ownership:** no admin endpoints, no template CRUD, no Administrative-application code. Only the dashboard
  runtime, migration, seeder and docs (§2).
- **No runtime code default:** after Task 8, nothing at runtime reads a hard-coded layout. Layout constants exist
  only as seed data (§8a).
- Mandatory column `is_no_role_template` with CHECK `(is_no_role_template AND role_id IS NULL) OR (NOT
  is_no_role_template AND role_id IS NOT NULL)`, and a partial unique `(tenant_id, dashboard_key) WHERE
  is_no_role_template AND NOT is_deleted` (§4.1, §4.2).
- Every new table is tenant-owned with the `tenant_isolation` RLS policy, copied verbatim from
  `20261020133948_AddCustomizableDashboards.cs`. The architecture suite must pass. Never weaken it.
- The permission check is unchanged: `DashboardWidgetAccess.GetAllowedKeysAsync` + `DashboardLayoutSupport.VisibleWidgets`
  (§7.6).
- Auto-sync **only adds** widgets into free space and never moves or resizes existing ones (§3 anti-goals, §6).
- Don't apply migrations to a shared DB. Create them and report them (repo CLAUDE.md).
- Branch: `feature/role-dashboard-templates` in **both** repos, cut from `development`. Run in a worktree. The BE and
  FE checkouts are currently on `feature/overtime-forecast-widgets`, used by other sessions, so don't switch
  those checkouts.
- Build and test with `pwsh ../dev-env/scripts/verify.ps1 backend [-Filter ...]`. If you see the NuGet "path1 null"
  error, run `dotnet build-server shutdown` first.

## Review Focus

1. **A user with zero active roles whose tenant has no No-role template** (a new tenant before the seeder runs):
   seed must produce an empty dashboard and log an error, never throw. Test in Task 5.
2. **Roles that are expired (`ExpiresAt` past) or soft-deleted** must not contribute a template or count as
   applied. Test in Task 3.
3. **A template widget key that is unknown or belongs to the other dashboard** (the teammate's editor writes rows
   we don't validate): skip it with a warning, and still render the rest. Test in Task 4.
4. **Two concurrent bootstraps both running auto-sync** must not add a widget twice. The partial unique
   `(dashboard_id, widget_key)` plus the `LayoutVersion` check must make the loser retry cleanly. Test in Task 6.
5. **A user saves a layout while a widget is hidden, then gains permission:** the reappearing widget must not
   overlap. It must come back to its stored slot or be auto-placed. Test in Task 7.

---

### Task 1: Template schema, applied-roles columns, migration, RLS

**Files:**
- Create: `src/ONEVO.Domain/Features/Dashboard/Entities/DashboardTemplate.cs`, `DashboardTemplateWidget.cs`, `DashboardTemplateWidgetLayout.cs`
- Modify: `src/ONEVO.Domain/Features/Dashboard/Entities/UserDashboard.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Dashboard/DashboardTemplateConfiguration.cs`, `DashboardTemplateWidgetConfiguration.cs`, `DashboardTemplateWidgetLayoutConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Dashboard/UserDashboardConfiguration.cs`, `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (DbSets beside `UserDashboards`, ~line 350)
- Create: migration `AddRoleDashboardTemplates` (generated)
- Test: `tests/ONEVO.Tests.Architecture/CustomizableDashboardArchitectureTests.cs`

**Interfaces:**
- Produces (all `: BaseEntity`, namespace `ONEVO.Domain.Features.Dashboard.Entities`):
  - `DashboardTemplate { string DashboardKey; Guid? RoleId; bool IsNoRoleTemplate; string Name; bool IsActive = true; int Version = 1; List<DashboardTemplateWidget> Widgets }`
  - `DashboardTemplateWidget { Guid TemplateId; string WidgetKey; string SettingsJson = "{}"; List<DashboardTemplateWidgetLayout> Layouts }`
  - `DashboardTemplateWidgetLayout { Guid TemplateWidgetId; string Breakpoint; int X; int Y; int W; int H }`
  - `UserDashboard` gains `List<Guid> AppliedRoleIds = new()` (column `applied_role_ids`, jsonb, default `'[]'`) and `bool AppliedNoRole` (column `applied_no_role`, default false).
  - Tables `dashboard_templates`, `dashboard_template_widgets`, `dashboard_template_widget_layouts`. Constraints and indexes exactly as in §4.1/§4.3/§4.4. `settings` is jsonb as in `DashboardWidgetConfiguration`. Layout CHECK bounds are copied from `DashboardWidgetLayoutConfiguration`.

- [ ] **Step 1: Write the failing architecture test.** Add `Role_template_tables_have_tenant_isolation_rls` to `CustomizableDashboardArchitectureTests`. It reuses `ReadMigrationSource("AddRoleDashboardTemplates")` and the same three `Assert.Contains` lines plus the `TenantTables` regex, and asserts that the three new table names are covered. Also add `No_role_template_constraints_exist`, asserting the migration source contains `is_no_role_template` inside a CHECK constraint and a filtered unique index whose filter contains `is_no_role_template`.
- [ ] **Step 2:** Run `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~CustomizableDashboardArchitecture"`. Expected: FAIL ("Migration AddRoleDashboardTemplates was not found").
- [ ] **Step 3: Implement the entities and configurations, then generate the migration** with `dotnet ef migrations add AddRoleDashboardTemplates -p src/ONEVO.Infrastructure -s src/ONEVO.Api`. Then hand-edit it:
  - Add `private static readonly string[] TenantTables = ["dashboard_templates", "dashboard_template_widgets", "dashboard_template_widget_layouts"];` and the RLS `foreach` block, copied verbatim from `AddCustomizableDashboards`. In `Down`, drop the policies.
  - Add the CHECK constraint (§4.1) via `migrationBuilder.Sql` if it wasn't modelled with `HasCheckConstraint`.
  - Add the backfill SQL (§8 step 3), run after the column is added:
    ```sql
    UPDATE user_dashboards d SET applied_no_role = true,
      applied_role_ids = COALESCE((SELECT jsonb_agg(ur.role_id ORDER BY ur.assigned_at, ur.role_id)
        FROM user_roles ur JOIN roles r ON r.id = ur.role_id AND r.is_deleted = false
        WHERE ur.user_id = d.user_id AND ur.tenant_id = d.tenant_id
          AND (ur.expires_at IS NULL OR ur.expires_at > now())), '[]'::jsonb)
    WHERE d.is_deleted = false;
    ```
    (Check the real `user_roles` / `roles` column names in the snapshot before relying on these.)
  - Check that `ApplicationDbContextModelSnapshot.cs` contains only dashboard changes. If unrelated modules show up, the snapshot is stale (repo CLAUDE.md gotcha); regenerate from a clean checkout.
- [ ] **Step 4:** Re-run the filter from Step 2. Expected: PASS. Then run the full architecture suite: `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~ONEVO.Tests.Architecture"`. Expected: all green, including `TenantIsolationArchitectureTests`.
- [ ] **Step 5: Commit** `feat(dashboard): role dashboard template tables with RLS and applied-role tracking`.

### Task 2: Template read repository

**Files:**
- Create: `src/ONEVO.Application/Features/Dashboard/Layout/RepositoryInterfaces/IDashboardTemplateRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Dashboard/EfDashboardTemplateRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (beside the `IUserDashboardRepository` registration, ~line 523)
- Test: `tests/ONEVO.Tests.Integration/Dashboard/DashboardTemplateRepositoryTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IDashboardTemplateRepository
  {
      /// Active, non-deleted templates (widgets + layouts, AsNoTracking) for these roles and dashboard key.
      Task<IReadOnlyList<DashboardTemplate>> ListForRolesAsync(Guid tenantId, string dashboardKey, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);
      /// The tenant's active No-role template for the key (IsNoRoleTemplate = true), or null.
      Task<DashboardTemplate?> GetNoRoleAsync(Guid tenantId, string dashboardKey, CancellationToken ct = default);
  }
  ```
  Child widgets and layouts with `IsDeleted` are excluded.

- [ ] **Step 1: Write the failing integration tests.** Use the `UserDashboardRepositoryTestsFixture` pattern (`SharedPostgresTemplate`):
  - `ListForRolesAsync_returns_only_active_templates_of_given_roles_and_key`
  - `GetNoRoleAsync_returns_flagged_template_not_null_role_rows`
  - `Second_no_role_template_for_same_tenant_and_key_violates_unique_index` (expects `DbUpdateException`)
  - `Template_with_role_and_no_role_flag_violates_check_constraint`
  - `Tenant_context_B_cannot_read_tenant_A_templates`: under tenant mode for B, `ListForRolesAsync(A, ...)` returns empty.
- [ ] **Step 2:** Run `pwsh ../dev-env/scripts/verify.ps1 backend -Full` (Docker), or the integration project filtered to `DashboardTemplateRepository`. Expected: FAIL (type missing).
- [ ] **Step 3: Implement `EfDashboardTemplateRepository`** with `Include(Widgets).ThenInclude(Layouts)` and `AsNoTracking`, then register it as scoped.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5: Commit** `feat(dashboard): dashboard template read repository`.

### Task 3: Template resolver

**Files:**
- Create: `src/ONEVO.Application/Features/Dashboard/Layout/Templates/IDashboardTemplateResolver.cs`, `DashboardTemplateResolver.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs` (scoped, beside `DashboardSeeder`, line 43)
- Test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardTemplateResolverTests.cs`

**Interfaces:**
- Consumes: `IDashboardTemplateRepository` (Task 2), `IUserRoleRepository.ListActiveByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken)`, `ICurrentUser`, `IDateTimeProvider`.
- Produces:
  ```csharp
  public sealed record ResolvedTemplates(
      IReadOnlyList<DashboardTemplate> Ordered,   // role templates by AssignedAt then RoleId, or [noRole], or []
      IReadOnlyList<Guid> ActiveRoleIds,          // ALL active non-deleted roles, same order, with or without a template
      bool UsesNoRole);
  public interface IDashboardTemplateResolver
  { Task<ResolvedTemplates> ResolveAsync(string dashboardKey, CancellationToken ct); }  // cached per key per scope
  ```

- [ ] **Step 1: Write the failing tests** (Moq):
  - `Orders_role_templates_by_AssignedAt_then_RoleId`
  - `Roles_without_template_are_skipped_but_listed_in_ActiveRoleIds`
  - `No_role_template_used_only_when_no_role_has_a_template` (`UsesNoRole == true`, `Ordered == [noRole]`)
  - `No_role_template_never_merged_when_any_role_template_exists`
  - `Expired_or_soft_deleted_roles_are_ignored` (Review Focus 2). The role repo returns a `UserRole` whose `Role.IsDeleted == true`; assert it is absent from both lists. If `ListActiveByUserIdAsync` already filters deleted roles via the composed query filter (see `TenantIsolationArchitectureTests.UserRoleAndRolePermission_ComposedQueryFilter_RetainsRoleSoftDeleteCondition`), keep the explicit filter anyway.
  - `Missing_no_role_template_returns_empty_Ordered`
  - `Second_call_same_key_hits_repository_once`
- [ ] **Step 2:** Run `verify.ps1 backend -Filter "FullyQualifiedName~DashboardTemplateResolver"`. Expected: FAIL.
- [ ] **Step 3:** Implement and register as scoped.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5: Commit** `feat(dashboard): resolve dashboard templates from active roles`.

### Task 4: Template merger (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/Dashboard/Layout/Templates/DashboardTemplateMerger.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardTemplateMergerTests.cs`

**Interfaces:**
- Consumes: `DashboardGrid.AutoPlace/Compact/Overlaps`, `DashboardWidgetRegistry.Get/Derive`, `Breakpoints`.
- Produces:
  ```csharp
  public sealed record MergedWidget(string WidgetKey, string SettingsJson, IReadOnlyDictionary<string, GridRect> Rects); // key = breakpoint
  public sealed record MergeResult(IReadOnlyList<MergedWidget> Widgets, IReadOnlyList<string> SkippedKeys);
  public static class DashboardTemplateMerger
  {
      public static MergeResult Merge(string dashboardKey, IReadOnlyList<DashboardTemplate> ordered);
      /// Per-breakpoint rects of one template: stored rows, missing breakpoints via DashboardWidgetRegistry.Derive of its desktop list.
      public static IReadOnlyDictionary<string, IReadOnlyList<(string Key, GridRect Rect)>> TemplateRects(string dashboardKey, DashboardTemplate template);
      /// Desktop W×H for a key from the first template that has it, else the definition's MinW×MinH.
      public static (int W, int H) SizeFor(string widgetKey, IReadOnlyList<DashboardTemplate> ordered);
  }
  ```
  Algorithm per §5.2: per breakpoint, base rects as-is; for each later template, each widget not yet merged is kept at
  its own rect if in bounds and free, else `AutoPlace` at its W×H; duplicates keep the earlier template's rect and
  settings; finally `Compact`. Unknown, other-dashboard and deleted widget keys go to `SkippedKeys`.

- [ ] **Step 1: Write the failing tests:**
  - `Five_plus_four_with_two_shared_gives_seven` (the user's example: A has 5 keys, B has 4 with 2 shared, so the result has 7 distinct keys)
  - `Shared_widget_keeps_earlier_template_rect_and_settings`
  - `Later_widget_keeps_own_rect_when_free`
  - `Later_widget_overlapping_is_auto_placed_without_overlap` (assert that no pair in the result overlaps, per breakpoint)
  - `Missing_tablet_mobile_rows_are_derived_from_desktop`
  - `Unknown_and_other_dashboard_keys_are_skipped` (Review Focus 3)
  - `Empty_input_gives_empty_result`
  - `SizeFor_uses_first_template_then_definition_minimum`
- [ ] **Step 2:** Run `-Filter "FullyQualifiedName~DashboardTemplateMerger"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5: Commit** `feat(dashboard): union merge of role dashboard templates`.

### Task 5: Startup template seeder (No-role + mock role templates)

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Seeders/DashboardTemplateSeeder.cs`, `DashboardTemplateSeedData.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`. Add `services.AddHostedService<DashboardTemplateSeeder>();` **after** `WorkManagementSampleDataSeeder` (~line 1140), so the Dapi roles exist first.
- Test: `tests/ONEVO.Tests.Integration/Dashboard/DashboardTemplateSeederTests.cs`

**Interfaces:**
- Produces: `DashboardTemplateSeedData.Desktop(string dashboardKey) : IReadOnlyList<(string Key, GridRect Rect)>`. Its values are **copied verbatim** from `DashboardWidgetRegistry.MyWorkDefaults` / `MyTeamDefaults` **as they are on `development` at execution time** (include any widgets the overtime-forecast work added). Also `internal static Task<SeedCounts> SeedAsync(ApplicationDbContext db, IWritableTenantContext tenantContext, ILogger logger, CancellationToken ct)`, returning `record SeedCounts(int NoRoleCreated, int RoleTemplatesCreated)`.
- Behaviour (§8a): runs in **all** environments. Uses `IWritableTenantContext.SetAdminMode()` like `DapiHistorySeeder`. For every non-deleted tenant and both keys: create the No-role template if missing (desktop rows only, `Name = "No role"`); for every non-deleted role of that tenant with no template for the key, create a mock copy (`Name = "<role name> (mock)"`). Idempotent; never updates existing rows. On exception, log an error and continue startup (don't throw).

- [ ] **Step 1: Write the failing integration tests:**
  - `Creates_no_role_templates_for_both_keys_per_tenant`
  - `Creates_one_mock_template_per_role_per_key`
  - `Second_run_creates_nothing` (counts are (0,0) on rerun)
  - `Existing_template_for_a_role_is_not_overwritten`
  - `No_role_layout_equals_seed_data_and_passes_DashboardGrid_Validate`
- [ ] **Step 2:** Run (integration, Docker). Expected: FAIL.
- [ ] **Step 3:** Implement and register.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5: Commit** `feat(dashboard): seed no-role and mock role dashboard templates`.

### Task 6: Seed, Reset and auto-sync from templates

**Files:**
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Services/DashboardSeeder.cs`, `Commands/ResetDashboard/ResetDashboardCommandHandler.cs`, `Queries/GetDashboardBootstrap/GetDashboardBootstrapQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Dashboard/Layout/Templates/DashboardTemplateSync.cs`
- Modify tests: `DashboardSeederTests.cs`, `ResetDashboardCommandHandlerTests.cs`, `GetDashboardBootstrapQueryHandlerTests.cs`, `GetMyDashboardsQueryHandlerTests.cs`, `DashboardMutationFixture.cs` (its `Seed` must build widgets from a fake `ResolvedTemplates` via `DashboardSeeder.BuildWidgets`, not `BuildDefaultWidgets`)
- Create test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardTemplateSyncTests.cs`

**Interfaces:**
- Consumes: `IDashboardTemplateResolver` (Task 3), `DashboardTemplateMerger` (Task 4).
- Produces:
  - `DashboardSeeder(IUserDashboardRepository, IDashboardTemplateResolver, ICurrentUser, ILogger<DashboardSeeder>)`.
    `BuildDefaultWidgets` is **replaced** by `public static List<DashboardWidget> BuildWidgets(Guid tenantId, MergeResult merged)`.
    `EnsureAsync` sets `AppliedRoleIds = resolved.ActiveRoleIds` and `AppliedNoRole = resolved.UsesNoRole`. Empty `Ordered`
    gives an empty dashboard and `LogError` naming the tenant and key (§7.4, Review Focus 1). Skipped keys are logged as warnings.
  - Reset: same resolve + merge; keeps the existing reuse-rows logic; `SettingsJson` is set from the merged widget (not `"{}"`); resets `AppliedRoleIds` / `AppliedNoRole`.
  - `public static class DashboardTemplateSync { public static bool Apply(UserDashboard dashboard, ResolvedTemplates resolved); }`
    returns true when anything changed. Rules per §6: new roles = `ActiveRoleIds − AppliedRoleIds`; for each new role's template in order, add widgets the dashboard lacks (no non-deleted row), per breakpoint at the template rect if free in the **current** stored layout, else `AutoPlace`; existing rows are never touched; then `AppliedRoleIds = ActiveRoleIds` (a role without a template still counts as applied). If `UsesNoRole` and `!AppliedNoRole`, apply the No-role template the same way. Set `AppliedNoRole = UsesNoRole`.
  - Bootstrap calls `DashboardTemplateSync.Apply` right after loading the dashboard, before `VisibleWidgets`, and saves with the existing version check (on a lost race, keep going with the old version, as today).

- [ ] **Step 1: Write the failing tests:**
  - Seeder: `Seeds_merged_role_templates_and_records_applied_roles`, `Seeds_no_role_template_when_user_has_no_template_roles`, `Missing_no_role_template_seeds_empty_dashboard_and_logs_error`
  - Reset: `Reset_restores_merged_templates_including_settings`, `Reset_removes_widgets_not_in_templates`, `Reset_resets_applied_roles`
  - Sync: `New_role_adds_only_missing_widgets`, `Existing_widgets_never_move_or_resize`, `Removed_widget_not_readded_without_new_role`, `Role_removed_then_readded_brings_widgets_back`, `Role_without_template_counts_as_applied`, `Template_created_later_for_held_role_does_not_push`, `No_change_returns_false`
  - Bootstrap: `Bootstrap_persists_sync_additions_with_version_check`, `Concurrent_sync_loser_does_not_duplicate_widget` (Review Focus 4: the second `SaveWithVersionAsync` returns false; assert no duplicate key in the returned DTO and no throw)
- [ ] **Step 2:** Run `-Filter "FullyQualifiedName~Features.Dashboard.Layout"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement; update DI for the new seeder constructor.
- [ ] **Step 4:** Re-run the filter. Expected: all Dashboard.Layout tests PASS.
- [ ] **Step 5: Commit** `feat(dashboard): seed, reset and auto-sync dashboards from role templates`.

### Task 7: Display reflow replaces variants; template-based sizes

**Files:**
- Modify: `Services/DashboardLayoutSupport.cs` (delete `VariantRects` and `MaterializeVariants`), `Queries/GetDashboardBootstrap/GetDashboardBootstrapQueryHandler.cs`, `Commands/AddDashboardWidget/AddDashboardWidgetCommandHandler.cs`, `Commands/RemoveDashboardWidget/RemoveDashboardWidgetCommandHandler.cs`, `Queries/GetAvailableWidgets/GetAvailableWidgetsQueryHandler.cs`
- Delete test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardVariantMaterializationTests.cs`
- Create test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardReflowTests.cs`. Modify the Add, Remove, AvailableWidgets and Bootstrap tests.

**Interfaces:**
- Produces in `DashboardLayoutSupport`:
  - `public static bool HasHiddenWidgets(UserDashboard dashboard, IReadOnlySet<string> allowed)`: some non-deleted widget is a known registry key that is not allowed.
  - `public static Dictionary<Guid, GridRect> ReflowRects(IReadOnlyList<DashboardWidget> visible, string breakpoint)`: reading order (stored Y, then X; widgets without a row go last), each `AutoPlace`d at its stored W×H clamped to the column count (fallback size = `PlacementSize` logic moved here), then `Compact`. Deterministic.
  - `public static void MaterializeReflow(UserDashboard dashboard, IReadOnlyList<DashboardWidget> visible, IReadOnlySet<string> allowed)`: when `HasHiddenWidgets`, writes the `ReflowRects` result into the **visible** widgets' rows for every breakpoint. Hidden rows are untouched.
- Bootstrap: when `HasHiddenWidgets` is true, `final = ReflowRects(visible, bp)` and **nothing is saved**; otherwise use the existing `ResolveRects` + save path.
- Add/Remove: call `MaterializeReflow` where `MaterializeVariants` was. Add's default size = `DashboardTemplateMerger.SizeFor(def.Key, (await resolver.ResolveAsync(dashboard.DashboardKey, ct)).Ordered)`.
- `GetAvailableWidgets` and bootstrap `PlacementSize` use `SizeFor` the same way. Inject `IDashboardTemplateResolver`.
- Save (`SaveDashboardLayoutCommandHandler`): no logic change. Builder: confirm it only rewrites rows for the widgets the client sent and leaves hidden widgets' rows alone. If it doesn't, make it do so.

- [ ] **Step 1: Write the failing tests:**
  - `Hidden_action_center_leaves_no_horizontal_hole` (My Work layout with `action-center` at (0,3,5,3) hidden: the reflowed `active-projects` gets x = 0)
  - `Reflow_is_not_saved` (`SaveWithVersionAsync` never called when a widget is hidden and sync changed nothing)
  - `Reflow_is_deterministic` (same input twice gives equal output)
  - `Regained_permission_widget_returns_to_stored_slot`
  - `Save_while_hidden_then_permission_regained_has_no_overlap` (Review Focus 5)
  - `Add_while_hidden_bases_on_visible_layout_no_gap`
  - `AvailableWidgets_size_comes_from_templates`
- [ ] **Step 2:** Run `-Filter "FullyQualifiedName~Features.Dashboard.Layout"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement; delete the variant code paths and their test file.
- [ ] **Step 4:** Re-run the filter. Expected: PASS.
- [ ] **Step 5: Commit** `feat(dashboard): display-only reflow for permission-hidden widgets`.

### Task 8: Remove the code default from the registry

**Files:**
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs`. Delete `MyWorkDefaults`, `MyTeamDefaults`, `MyWorkWithoutActionCenter`, `MyTeamWithoutTeamStatus`, `MyTeamQuickActionsOnly`, `VariantDesktop`, `HasVariant`, `DefaultLayoutFor`, `WithTeamPlanToday`, `TeamPlanToday`/`TeamPlanTodaySize`, `DefaultLayout`, `DesktopDefaults`, and `DefaultSize`. Keep `Definitions`, `Get`, `ForDashboard`, `TabletWidth`, `Derive`.
- Modify test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardWidgetRegistryTests.cs`. Drop the default/variant facts and keep the catalog facts. Move any "default layout is valid" facts to target `DashboardTemplateSeedData` instead.

- [ ] **Step 1: Write the failing test:** `Registry_exposes_no_default_layout_api` (reflection: no public static member named `DefaultLayout`, `DefaultLayoutFor`, `DefaultSize`, `HasVariant`), and `Seed_data_layouts_are_valid_per_DashboardGrid_Validate` for both keys.
- [ ] **Step 2:** Run `-Filter "FullyQualifiedName~DashboardWidgetRegistry"`. Expected: the first test FAILS.
- [ ] **Step 3:** Delete the members and fix any compile errors (there should be none left after Tasks 6–7).
- [ ] **Step 4:** Run the full gate: `pwsh ../dev-env/scripts/verify.ps1 backend`. Expected: build OK, unit + architecture all green. Then `pwsh ../dev-env/scripts/verify.ps1 backend -Full` (Docker). Expected: integration green.
- [ ] **Step 5: Commit** `refactor(dashboard): remove hard-coded default and variant layouts`.

### Task 9: Dashboard docs folder (frontend repo)

**Files (FE repo `Hrms--Web-application---front-end---v1`):**
- Create: `docs/core/Dashboard/README.md`, `DASHBOARD_ARCHITECTURE.md`, `ROLE_TEMPLATES.md`, `PERMISSIONS.md`, `CUSTOMIZATION_RULES.md`, `ADMIN_PLATFORM_TEMPLATE_GUIDE.md`
- Move (`git mv`): `docs/design/WIDGET_CREATION_RULES.md` → `docs/core/Dashboard/WIDGET_CREATION_RULES.md`
- Copy-in: BE `docs/superpowers/specs/finished/2026-10-20/2026-10-20-customizable-dashboard-design.md` → FE `docs/core/Dashboard/CUSTOMIZABLE_DASHBOARD_DESIGN.md`. Then delete it in BE with `git rm` and leave a one-line pointer file at the old BE path, because the move is cross-repo.
- Modify references: FE `CLAUDE.md` line 16, FE `docs/design/README.md` (if it lists the file), FE `docs/superpowers/plans/next/2026-11-08-overtime-forecast-widgets-frontend.md`, BE `docs/superpowers/specs/next/2026-11-08-overtime-forecast-widgets-design.md`. Find any others with `grep -rn "WIDGET_CREATION_RULES\|customizable-dashboard-design" --include=*.md` in both repos.

Content (each file ≤ ~200 lines, plain English, link to the others):
- `README.md`: an index and a one-paragraph mental model ("templates place, permissions show").
- `DASHBOARD_ARCHITECTURE.md`: the user tables, breakpoints/grid, and the seed → bootstrap (sync, permission filter, reflow or resolve) → save/add/remove/reset flow, plus `LayoutVersion` concurrency.
- `ROLE_TEMPLATES.md`: the three tables, **a dedicated "Why `is_no_role_template` is mandatory" section** (§4.2 verbatim reasons), resolution (§5.1), merge with the 5+4→7 example (§5.2), auto-sync rules (§6), the seeder (§8a).
- `PERMISSIONS.md`: `DashboardWidgetAccess` keys and capability gates, the FE `my-team-widget-visibility.util` mirror, the hidden-widget reflow (§7.3).
- `CUSTOMIZATION_RULES.md`: user add/remove/resize/save/reset behaviour and what never changes the template.
- `ADMIN_PLATFORM_TEMPLATE_GUIDE.md`: every bullet of §9a, plus the writer contract §4.5.

- [ ] **Step 1:** Write the files, do the moves, update the references.
- [ ] **Step 2:** Verify: `grep -rn "docs/design/WIDGET_CREATION_RULES" --include=*.md` across both repos returns nothing. Run `npm run check:warnings` in FE (docs-only, but the gate must stay green).
- [ ] **Step 3: Commit** in each repo: `docs(dashboard): consolidate dashboard docs under docs/core/Dashboard`.

### Task 10: Final verification and report

- [ ] **Step 1:** `pwsh ../dev-env/scripts/verify.ps1 backend -Full`. Expected: all green. Also `pwsh ../dev-env/scripts/security-scan.ps1 backend -Baseline origin/development`. Expected: no new findings.
- [ ] **Step 2:** In FE run `ng build` and the dashboard specs. Expected: green, with no FE code change needed (§10). If a spec depended on variant layouts or `hasGated` semantics, fix it here.
- [ ] **Step 3:** Write the report `HRMS-Backend-v1/docs/reports/dashboard/2026-11-08-role-dashboard-templates-summary.md`: what changed, the migration name (**not applied**), test counts, and the browser-pass checklist (two users with different roles, multi-role merge, role added → widget appears, hidden widget → no hole, Reset). Move the spec and plan `next/` → `finished/2026-11-08/` only after the user's browser pass.
- [ ] **Step 4: Commit** `docs(dashboard): role dashboard templates summary`.
