# Role-based dashboard templates — design

Date: 2026-11-08
Repos: HRMS-Backend-v1 (code), Hrms--Web-application---front-end---v1 (docs only, see section 9)
Status: design, review questions answered, awaiting user approval

## 1. Intent

Today every user's dashboard (My Work, My Team) is seeded and reset from a layout hard-coded in
`DashboardWidgetRegistry` (`MyWorkDefaults`, `MyTeamDefaults` plus the permission "variant" layouts). The same
default goes to everyone in every tenant.

We want the default layout to come from **tables**, per tenant and per **role**: a tenant's "Manager" role gets one
default dashboard, "Employee" another. A user's own drag/resize/add/remove keeps going to their own tables
(`user_dashboards`, `dashboard_widgets`, `dashboard_widget_layouts`) and never changes the template.

## 2. Ownership — what is ours and what is not

- **Ours (this work):** the dashboard runtime only. That means the template tables (schema, migration, RLS), reading
  templates during seed, reset and load, the multi-role merge, the automatic sync when a role is added, removing the
  code default, and the docs.
- **Not ours (the user's teammate):** the Administrative application and every admin/back-office API. That covers
  creating and editing templates, creating a template when a tenant, legal entity or default role is provisioned,
  and any template editor UI. We add **no** admin endpoints and no CRUD for templates.
- The teammate's work will write rows into our tables, so section 4.5 is the contract they code against.

## 3. Goals, non-goals, anti-goals

Goals
- The default layout for a user comes from tenant-owned template tables, chosen by the user's active roles.
- **No code default.** Every tenant has a **No-role template** per dashboard key. It is the default for a user who
  has no role with a template.
- A user with several roles gets the **union** of their role templates (section 5).
- A role added later automatically brings its extra widgets into the user's existing, customized dashboard
  (section 6).
- The permission check still runs. A widget the user may not see is hidden and the rest close the gap.
- Strict tenant isolation (RLS) on every new table.

Non-goals
- Template authoring, admin APIs and admin UI (teammate).
- Propagating a later template edit into dashboards that already exist. A template edit reaches only new
  dashboards, a Reset, or a newly added role.
- Role hierarchy or priority. Roles are merged, never ranked.

Anti-goals (a failure even if it "works")
- A user's customization is moved, resized or lost when a role is added. Auto-sync only **adds** widgets, into free
  space.
- A widget the user removed on purpose comes back on its own when nothing about their roles changed.
- Any template row readable across tenants.
- A gated widget shown to a user without permission because a template contains it.

## 4. Data model

All three tables are tenant-owned (`ITenantOwnedEntity` / `BaseEntity` with `TenantId`) and soft-deletable like the
existing dashboard tables. Names are snake_case, matching `user_dashboards`.

### 4.1 `dashboard_templates`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| tenant_id | uuid NOT NULL | RLS |
| dashboard_key | varchar(50) NOT NULL | `my-work` / `my-team` (`DashboardKeys`) |
| role_id | uuid NULL, FK → roles | null only for the No-role template |
| **is_no_role_template** | boolean NOT NULL default false | **mandatory column, see 4.2** |
| name | varchar(200) NOT NULL | display only |
| is_active | boolean NOT NULL default true | inactive = ignored at runtime |
| version | int NOT NULL default 1 | bumped by the teammate's editor; informational for us |
| created/updated/is_deleted | standard BaseEntity columns | |

Constraints
- CHECK: `(is_no_role_template AND role_id IS NULL) OR (NOT is_no_role_template AND role_id IS NOT NULL)`.
- Partial unique: `(tenant_id, dashboard_key) WHERE is_no_role_template AND NOT is_deleted`. Exactly one No-role
  template per tenant per dashboard.
- Partial unique: `(tenant_id, role_id, dashboard_key) WHERE role_id IS NOT NULL AND NOT is_deleted`. One template
  per role per dashboard.

### 4.2 Why `is_no_role_template` is a must

There is no code default any more, so the No-role template is the only fallback. A nullable `role_id` alone is not
enough:
- A null `role_id` cannot be told apart from a bad row (a role deleted, or a template saved half-way). An explicit
  flag makes "this is the tenant's fallback" a deliberate statement.
- A partial unique index on a nullable column alone does not stop two null-role rows in Postgres. The flag-based
  partial unique index enforces **exactly one** fallback per tenant per dashboard.
- The CHECK constraint ties the two together, so a row can never be both a role template and the fallback.
- The runtime query for the fallback is `is_no_role_template = true`, never `role_id IS NULL`.

The teammate's tenant-provisioning flow **must** create the No-role template for both dashboard keys. Section 7.4
covers a tenant missing one.

### 4.3 `dashboard_template_widgets`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| tenant_id | uuid NOT NULL | RLS |
| template_id | uuid NOT NULL FK → dashboard_templates (cascade) | |
| widget_key | varchar(100) NOT NULL | registry key, same as `dashboard_widgets.widget_key` |
| settings | jsonb NOT NULL default '{}' | copied to the user widget |

Partial unique `(template_id, widget_key) WHERE NOT is_deleted`.

### 4.4 `dashboard_template_widget_layouts`

| Column | Type | Notes |
|---|---|---|
| id | uuid PK | |
| tenant_id | uuid NOT NULL | RLS |
| template_widget_id | uuid NOT NULL FK → dashboard_template_widgets (cascade) | |
| breakpoint | varchar(20) NOT NULL | `desktop` / `tablet` / `mobile` (`Breakpoints`) |
| x, y, w, h | int NOT NULL | same CHECK bounds as `dashboard_widget_layouts` |

Partial unique `(template_widget_id, breakpoint) WHERE NOT is_deleted`. **Desktop is required. Tablet and mobile are
optional.** A missing breakpoint is derived from the template's desktop layout with the existing
`DashboardWidgetRegistry.Derive` (builder: confirm `Derive` works without the code defaults; if it reads them, change
it to take the desktop list only).

### 4.5 Contract for template writers (the teammate's admin app)

A valid template:
- has a desktop row for every widget;
- uses only widget keys that exist in `DashboardWidgetRegistry` **for that dashboard key**;
- respects each widget's MinW/MinH/MaxW/MaxH and the grid's column count per breakpoint;
- has no overlapping rects;
- is **gap-free** (already compacted). The user confirmed templates are always authored without empty space.

The runtime is defensive anyway (section 7.5), but authoring must meet this contract.

### 4.6 `user_dashboards` addition

New column `applied_role_ids` (jsonb array of uuid, NOT NULL default `'[]'`) plus `applied_no_role` (boolean NOT
NULL default false). Together they record which **roles** (not template ids) have already been merged into this
user's dashboard, and whether the No-role template was. They drive auto-sync (section 6).
Tracking roles rather than template ids is deliberate (Claude's call). If the teammate's editor ever re-creates a
template with a new id ("save as new version", delete-and-insert), tracking by id would treat it as a new role and
re-add widgets every holder removed. Tracking by role makes an edit invisible to existing dashboards whatever the
editor does.
*(Builder's call: a small child table instead of jsonb is fine if it fits the repo's patterns better. Behaviour is
what matters.)*

## 5. Resolving and merging templates

### 5.1 Which templates apply to a user

1. Load the user's **active** roles (`IUserRoleRepository.ListActiveByUserIdAsync(userId, now, ct)`, which honours
   `ExpiresAt`). Builder: that method does not itself exclude soft-deleted `Role` rows. Check for a global query filter,
   otherwise filter explicitly.
2. Order them by `UserRole.AssignedAt` ascending, ties broken by `RoleId`. The earliest-assigned role is the **base**.
3. For each role, take its active, non-deleted template for this dashboard key. Roles without one are skipped.
4. If that leaves **no** templates, use the tenant's No-role template.
   **The No-role template is never merged with role templates.** If the user has at least one role template, the
   No-role template is not part of the result.

### 5.2 Merge (union)

The user's example: template A has 5 widgets, template B has 4, and 2 of B's are already in A. The user gets the 2
new ones added, **7 widgets** in total.

Per breakpoint, independently:
1. Start with the base template's rects as they are.
2. For each next template in order, for each widget **not already in the merged set**:
   - if its own rect from that template is in bounds and overlaps nothing accepted, keep it there;
   - otherwise place it with `DashboardGrid.AutoPlace` (first free spot) at that template's W×H.
3. A widget present in several templates keeps the **earlier** template's rect and settings. Later templates'
   versions of the same widget are ignored.
4. Compact the result (`DashboardGrid.Compact`).

The merged, **unfiltered** set is what gets stored (same rule as today: store everything, filter by permission at
display, so a widget the user later gains permission for already has its slot).

## 6. Auto-sync when roles change (user decision: automatic)

On dashboard bootstrap (`GetDashboardBootstrapQueryHandler`, before visibility filtering):
1. Resolve the user's current active roles and their templates (5.1).
2. `newRoles = current roles − applied_role_ids`. A role counts as applied **even if it had no template at the
   time** (Claude's call, consistent with "edits don't propagate"). So a template created later for a role a user
   already holds does **not** push into that user's dashboard. It reaches them on Reset, and reaches new dashboards
   and new role assignments.
3. For each new role's template, in role order, add each of its widgets that the dashboard does **not** currently
   have (no non-deleted row). Placement per breakpoint: the template's rect if it is free in the user's **current**
   layout, else `AutoPlace`. Existing widgets are **never moved or resized**.
4. Set `applied_role_ids = current roles`, so a removed role drops out. If that role is added again later, its
   widgets come back. This is intended.
5. Save with the existing optimistic `LayoutVersion` check. On a lost race the next load retries, same as today's
   rect persistence.

Consequences (confirm in review)
- A widget the user removed comes back only when a role newly brings it. Nothing re-adds it otherwise.
- Removing a role does **not** remove widgets. If the user loses permission, the permission filter hides them.
- Moving from "no role" to "has a role": the No-role id leaves the set and the role template's missing widgets are
  added. No-role widgets already on the dashboard stay.

## 7. Runtime changes (backend)

### 7.1 Seed (`DashboardSeeder.EnsureAsync`)
`BuildDefaultWidgets` is replaced by a template-based builder: resolve (5.1), merge (5.2), copy into the user
tables, and set `applied_template_ids`. The seeding-race handling stays as it is.

### 7.2 Reset (`ResetDashboardCommandHandler`)
Same resolve and merge. Keep the existing "reuse rows, never remove and re-create the same key in one save" logic.
Settings are reset to the template's settings, not `{}`. `applied_template_ids` is reset to the current set.

### 7.3 Remove the code default and the variant logic
Delete `MyWorkDefaults`, `MyTeamDefaults`, the `*Without*` / `QuickActionsOnly` variants, `VariantDesktop`,
`HasVariant`, `DefaultLayoutFor`, `WithTeamPlanToday`, `DefaultLayout`, `DashboardLayoutSupport.VariantRects` /
`MaterializeVariants`, and their callers (bootstrap, Add, Remove).
**Hidden-widget display reflow (user decision).** `DashboardGrid.Compact` only moves widgets **up**, never sideways,
and `ResolveRects` **saves** the rects it computes. So without the variants, a widget hidden by permission would leave
a horizontal hole and the visible widgets would take over its saved slot. Replacement:
- When **no** stored widget is hidden by permission, bootstrap behaves as today (`ResolveRects`, saved).
- When **at least one** stored widget is hidden by permission, the visible widgets are re-flowed **for display only**:
  take them in reading order (stored Y, then X), place each with `AutoPlace` at its stored W×H into a fresh grid, then
  `Compact`. That fills holes left to right and top to bottom. These rects are returned to the client and **not
  saved**. Stored rects, including the hidden widget's slot, stay intact, so a widget the user later gains
  permission for returns to its own slot.
- A user save (drag/resize) while a widget is hidden stores what the user sees for the visible widgets and leaves
  hidden widgets' rows untouched. If a hidden widget later overlaps when it reappears, the existing `ResolveRects`
  auto-place handles it.
- Add / Remove no longer call `MaterializeVariants`. Builder: make sure Add/Remove base their placement on the
  layout the user currently sees, so no gap appears after an add.
- The reflow is per breakpoint and deterministic (same input, same output), so the layout doesn't jump between
  reloads.
`DashboardWidgetRegistry` remains the widget **catalog** (keys, titles, sizes, ShownOnWeb, providers).

`DefaultSize` is used by Add, by `GetAvailableWidgets`, and by the bootstrap fallback size in `PlacementSize`. It
changes to: the widget's desktop W×H from the user's resolved templates if any of them has it, else the definition's
MinW×MinH. Resolve the templates once per request and pass them in (Claude's call: one extra query per request is
acceptable).

### 7.4 No-role template missing
This should not happen once provisioning and backfill are done. If it does: seed or reset produces an **empty**
dashboard, logs an error naming the tenant and dashboard key, and does not throw. The user can still add widgets
from the catalog.

### 7.5 Defensive reading
Skip template widgets whose key is unknown or belongs to the other dashboard, with a warning log. Clamp
out-of-bounds rects through the existing `ResolveRects`/`AutoPlace` path. Never fail the load over bad template
data.

### 7.6 Permission check — unchanged
`DashboardWidgetAccess.GetAllowedKeysAsync` and `DashboardLayoutSupport.VisibleWidgets` stay the source of truth.
Templates decide **where** widgets go; permissions decide **whether** they show.

## 8. Migration and backfill

1. Create the three tables, add `user_dashboards.applied_template_ids`, plus indexes and constraints.
2. RLS on all three tables with the same `tenant_isolation` policy as `AddCustomizableDashboards`, and the
   architecture-suite RLS coverage entry. The architecture test must pass.
3. **Applied-roles backfill (in the migration, SQL):** for every existing `user_dashboards` row, set
   `applied_no_role = true` and `applied_role_ids` = the user's currently active role ids (from `user_roles`, not
   expired). Otherwise the first auto-sync would push the seeded templates (section 8a) into dashboards users
   already customized, and re-add widgets they removed.
4. Templates themselves are created by the seeder (section 8a), not by the migration.

## 8a. Dashboard template seeder (user decision: mock templates)

New `DashboardTemplateSeeder` in `ONEVO.Infrastructure/Persistence/Seeders/`, a hosted service registered in
`DependencyInjection.cs` like the existing seeders (`DapiHistorySeeder`, `RoleTemplateSeeder`, ...). Idempotent:
it only inserts what is missing and never overwrites an existing template.

For **every tenant**, for both `my-work` and `my-team`:
1. **No-role template**, if missing: today's default layout (the `MyWorkDefaults` / `MyTeamDefaults` rects) with a
   desktop row per widget. Tablet/mobile are left to runtime derive.
   The layout constants **move** from `DashboardWidgetRegistry` into the seeder's data file. They become seed data,
   not a runtime code default. Nothing at runtime reads them.
2. **A mock template for every existing role** that has none: the same layout as the No-role template (a copy).
   These are placeholders for the teammate's admin app to replace. *(Claude's call: a plain copy keeps the mock
   honest and lets a real template replace it later. Builder: optionally give one or two Dapi demo roles a
   different widget set, so the multi-role merge can be seen in a browser pass.)*

Runs in all environments, since No-role templates are mandatory everywhere. *(Claude's call. Mock role templates
also seed everywhere, because HRMS is pre-production. Gate them to dev/demo if that changes.)* Because it runs at
startup, it also covers tenants created between releases (see 9a) on the next app start.

## 9. Documentation (frontend repo)

Create `Hrms--Web-application---front-end---v1/docs/core/Dashboard/` and put all dashboard knowledge there:
- `README.md`: index of the folder.
- How the dashboard works: tables, seed / load / reset / auto-sync flow, breakpoints, grid, concurrency
  (`LayoutVersion`).
- Role templates: the three tables, **why `is_no_role_template` is mandatory** (4.2), resolution and merge rules,
  auto-sync rules, and the writer contract (4.5).
- Permission checking: `DashboardWidgetAccess` / capabilities, the FE `my-team-widget-visibility` mirror, and
  "templates place, permissions show".
- Customization rules (user add/remove/resize/save/reset behaviour).
- **Move** the existing `docs/design/WIDGET_CREATION_RULES.md` into this folder and update every reference to it
  (FE `CLAUDE.md`, open plans/specs that link it, and the BE overtime-forecast spec).
- **Move** the existing customizable-dashboard design
  (`HRMS-Backend-v1/docs/superpowers/specs/finished/2026-10-20/2026-10-20-customizable-dashboard-design.md`) into
  this folder as the dashboard customization reference *(pending confirmation of which files the user meant)*.

## 9a. Administrative platform guide (user decision: document it, don't build it)

Inside `docs/core/Dashboard/`, add `ADMIN_PLATFORM_TEMPLATE_GUIDE.md`, written for the teammate who owns the
Administrative application. It covers dashboard template **creation and customization** from the admin side. We
build none of it. It must say:
- **Tenant provisioning** (`ConfirmTenantProvisioningCommandHandler` flow): create the No-role template
  (`is_no_role_template = true`, `role_id = null`) for `my-work` and `my-team` in the same transaction as the
  tenant. This is mandatory, and section 4.2 explains why.
- **Default roles / role templates:** when a role is created for a tenant (from a `RoleTemplate`, a legal-entity /
  position template pack, or by hand), create its dashboard templates next to its permission template.
- **Editing a template:** update rows **in place** (same template id, bump `version`). Don't delete and re-insert.
  Explain that edits never reach existing dashboards, only new dashboards, Reset and new role assignments.
- **Deleting / deactivating:** soft-delete or `is_active = false`. Never delete the No-role template. Replace it
  in place instead.
- The **writer contract** (section 4.5): valid keys per dashboard, desktop row required, min/max sizes, no overlap,
  gap-free.
- **Tenant isolation:** every row carries the tenant's `tenant_id`, and admin writes must run under the tenant
  context so RLS accepts them.
- **The release gap:** until provisioning creates No-role templates, a new tenant's users see an empty dashboard
  until the next app start, when `DashboardTemplateSeeder` fills it in.
- How the runtime uses templates (merge, auto-sync, permission filtering), with a link to the other docs in the
  folder.

## 10. Frontend code

No behaviour change is expected: the FE renders whatever bootstrap returns. Builder: confirm nothing in the FE
depends on the removed variant layouts or the `hasGated` flag's meaning, and keep `ng build` and the tests green.

## 11. Testing

- Unit: resolution (ordering by AssignedAt, skipped roles, No-role fallback, No-role never merged), merge (the 5+4→7
  example, overlap → AutoPlace, duplicate keeps the earlier one, per-breakpoint derive), auto-sync (add only, never
  move, removed widget not re-added without a new role, role removed then re-added), missing No-role → empty, bad
  keys skipped.
- Existing seed, reset, bootstrap, add and remove tests are rewritten from code defaults to templates.
- Architecture: RLS coverage for the three tables.
- Integration (Docker): RLS isolation of the template tables between two tenants, and the backfill.

## 12. Risks

- A parallel session is adding overtime-forecast widgets on `feature/overtime-forecast-widgets` and may edit the same
  `DashboardWidgetRegistry` defaults this work deletes. Coordinate the merge: those widgets must then be put into
  the No-role backfill or templates, not the deleted code defaults.
- Teammate dependency: role templates exist only once their admin flow writes them. Until then everyone gets the
  No-role template, which equals today's behaviour.

## 13. Decisions from review

- **Q1 (user):** a hidden-by-permission widget → display-only reading-order reflow, never saved (7.3).
- **Q2 (user):** the admin side is documented, not built (9a). The startup seeder covers the gap.
- **Q3 (user):** a mock template is seeded for every existing role (8a). (Claude's call: a template created later
  for a role users already hold doesn't push into their dashboards, see section 6 step 2.)

## 14. Calls made by Claude (please check)

- Base = earliest-assigned active role (`AssignedAt`). A widget in several templates keeps the earlier template's
  rect and settings.
- The No-role template is never merged when the user has any role template.
- Removing a role never removes widgets.
- Template edits do not propagate into existing dashboards (only new dashboards, Reset, or a newly added role).
- Missing No-role template → empty dashboard plus an error log, not a crash.
- Tablet/mobile template rows are optional and derived from desktop when absent.
- Auto-sync tracks applied **roles** (`applied_role_ids` + `applied_no_role`), not template ids, so template
  re-creation by the editor can't re-add removed widgets.
- `DefaultSize` uses template sizes on every read path, with templates resolved once per request.
- A role counts as applied even without a template, so a later-created template doesn't push into existing users.
- The migration backfills `applied_role_ids` from current roles, so the seeded templates don't touch existing
  customized dashboards.
- Mock role templates are copies of the No-role layout, and they seed in all environments.
- Docs folder lives in the frontend repo `docs/core/Dashboard/`.
