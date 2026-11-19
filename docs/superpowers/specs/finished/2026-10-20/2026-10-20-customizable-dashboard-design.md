# Customizable Grid Dashboard (My Work + My Team) — Design

Date: 2026-10-20
Repos: `HRMS-Backend-v1` (ASP.NET / EF Core / PostgreSQL) and `Hrms--Web-application---front-end---v1` (Angular 21, signals, standalone components, Tailwind).
Source standard: `C:\Users\User\Downloads\ONEXSO_Customizable_Grid_Dashboard_Implementation_Guide.md` (the "Guide"). Where this design and the Guide differ, this design wins; anything not mentioned here follows the Guide.

## 1. Intent

The app's Home page (`/dashboard`) is currently a dual dashboard built by a teammate (pirakee, Oct 2026):

- **My Work** (`/dashboard`) — the viewer's personal cards.
- **My Team** (`/dashboard/my-team`) — team cards for leads/managers, reached from Quick Actions → My Team.

We are turning **both** into fully customizable grid dashboards: a **Customize** button puts the page into edit mode and opens a **Widget Store**; the user adds widgets they want, removes ones they don't, drags to move, resizes by snapping to allowed sizes, then Save / Cancel / Reset. Layouts are persisted **server-side per user, per dashboard, per breakpoint**, so they follow the user across devices.

This is a **presentation/customization redesign only**. What each card shows, how it gets its data, and who may see it must not change.

## 2. Goals

1. Both My Work and My Team are independently customizable dashboards with their own layout, Save and Reset.
2. Customize mode: Widget Store drawer (add), ✕ on each widget (remove), drag-move, snap-resize, Save / Cancel / Reset. Draft is local; one atomic save on Save; Cancel discards.
3. 12 / 6 / 4 column grid (desktop / tablet / mobile), independent persisted layout per breakpoint (Guide §2.1, §12.3).
4. Backend persistence + API per the Guide §5–§8, adapted to ONEXSO conventions (see §6).
5. Every existing permission and hierarchy rule keeps working unchanged (see §5).
6. All old customization code and any unused dashboard logic/components are deleted (see §8).

## 3. Non-goals (v1)

- Changing any card's content, data, behavior or the APIs it calls.
- Redis/cache layer, load testing harness, SSE/WebSocket refresh.
- Revision history / restore endpoints (Guide marks optional). `layout_version` for optimistic concurrency **is** in scope.
- Per-instance widget settings and duplicate instances of the same widget type. Today no card has user settings, so v1 allows **one instance per widget type per dashboard**. Schema keeps a `settings jsonb` column so this can be added later without migration.
- Admin-editable default templates. Defaults are code-defined.
- Migrating users' existing localStorage layouts. Everyone starts from the new default once; old localStorage keys are ignored (and may be cleared).

## 4. Anti-goals (a failure even if it "works")

- A viewer sees, or can add from the store, a widget they are not allowed to see today.
- Any card behaves differently from today in View mode (data, actions, links, polling).
- Team widgets appear on My Work, or personal widgets appear on My Team.
- Old customization code left lying around next to the new code.
- Saving a layout fires an API call per drag/resize movement.

## 5. Dashboards, widgets and permissions

The two dashboards have **disjoint widget catalogs** — no widget is common to both. The decision of what a viewer may see stays exactly the current logic, now evaluated on the server as well as the client.

### 5.1 My Work (`dashboardKey = "my-work"`) — every authenticated tenant user

| widgetKey | Existing component | Allowed when |
|---|---|---|
| `today-at-a-glance` | today-at-a-glance-card ("Good morning") | always |
| `todays-schedule` | todays-schedule-card | always |
| `today-total-time` | today-total-time-card | always |
| `active-projects` | active-projects-card | always |
| `my-tasks` | my-tasks-card | always |
| `notifications` | dashboard-notifications-card | always |
| `quick-actions` | quick-actions-card (`onTeamDashboard=false`) | always |
| `wellbeing` | wellbeing-carousel-card | always |
| `work-pattern` | work-pattern-card | always |
| `action-center` | action-center-card | today's rule: `allowedWidgetKeys(caps)` includes `actionCenter` (any of canReviewPeopleApprovals / canReviewExceptions / hasWorkApprovals / leadsWork) |

### 5.2 My Team (`dashboardKey = "my-team"`) — exists for every user, like today's page

Today `/dashboard/my-team` has no route guard: a viewer without team access still gets Quick Actions plus the "No team to show yet" empty state; capability-load failure shows "Try again". **That behavior is preserved** (design call): the `my-team` dashboard is created for every user; only its team widgets are gated. When the viewer has no allowed team widget, the shell renders today's empty state below the grid (and "Try again" on capability error). `IsAvailable` is not used to gate the dashboard itself, because it partly depends on the volatile `HasWorkApprovals` flag.

| widgetKey | Existing component | Allowed when |
|---|---|---|
| `team-status` | team-status-card | `CanViewPeopleStatus` |
| `team-progress` | team-progress-card | `LeadsWork` |
| `team-quick-actions` | quick-actions-card (`onTeamDashboard=true`) | always (on this dashboard) |

Quick Actions is the same Angular component on both dashboards but **two separate widget definitions**, so the "no common widgets" rule holds. *(Design call.)*

### 5.3 Rules

- Permission truth stays in `GetMyTeamCapabilitiesQuery`. The new dashboard feature calls it (server-side) to compute allowed widget keys; it must **not** create a new permission table or duplicate the rule (Guide §4 item 7).
- Capabilities flicker (e.g. `HasWorkApprovals` is true only while approvals are pending). Today a default card appears automatically the moment it becomes allowed. To keep that: **seeding and Reset store the full default layout regardless of current permissions**; widgets not currently allowed are stored but filtered out of bootstrap/rendering (design call). A widget not in the default that the user never added stays only in the store.
- A stored widget the viewer cannot currently see is **kept, not rendered, not returned** — and the layout save ignores it (it is not part of the submitted set; absence is never a removal; overlap is checked only among visible widgets, so hidden widgets never block drops).
- When a hidden widget becomes visible again, bootstrap places it at its stored position if those cells are free; otherwise at the first free top-left slot (Guide §2.1 auto-place), and the server persists that new position, bumping `layout_version` (design call).
- Owner key = **UserId** (a user with no employee record must still get My Work).
- Widgets that appear in a saved layout but whose definition no longer exists render the Guide's safe "unsupported widget" placeholder.
- Each card's own APIs keep authorizing their own data exactly as today — the dashboard layer does not grant data access.

## 6. Backend

Location: new feature under `ONEVO.Application/Features/Dashboard/Layout/` (+ Domain entities, Infrastructure configuration, controller under `ONEVO.Api/Controllers/Tenant/Dashboard/`). Follow existing MediatR command/query + `Result` + controller `Problem(...)` conventions used by `TeamDashboardController`. Do not use the Guide's `{success,data}` envelope if the codebase's convention differs — **match the codebase**.

### 6.1 Tables (adapt names/types to codebase conventions; tenant column = whatever existing tenant-owned tables use)

- `user_dashboards` — tenant, owner (the codebase's current identity convention, EmployeeId or UserId — use whatever other per-user WM/dashboard data uses; a user with no employee record must still get My Work), `dashboard_key` (`my-work` | `my-team`), `layout_version bigint`, timestamps. Unique (tenant, owner, dashboard_key).
- `dashboard_widgets` — instance id, dashboard id, `widget_key`, `settings jsonb default '{}'`, timestamps. Unique (dashboard_id, widget_key) for the v1 one-instance rule.
- `widget_layouts` — widget instance id, `breakpoint` (`desktop|tablet|mobile`), x, y, w, h with CHECK constraints. Unique (instance, breakpoint).
- **Widget definitions live in code**, not a DB table (a static registry class: key, dashboardKey, default/min/max/allowed sizes per breakpoint, default position). Design call: no admin UI edits them, so a table adds a migration and seed for nothing. The Guide's `widget_definitions` / `dashboard_definitions` tables are deliberately dropped.
- RLS: all three tables are tenant-owned → add RLS policies and coverage per the repo's existing RLS migration pattern; the architecture test suite must stay green.

### 6.2 Default layouts / seeding

- First call to `GET /dashboards/me` (or bootstrap) for a user lazily creates both dashboards, seeded from the code-defined **full** default layout (see §5.3). Must be race-safe (two tabs).
- My Work desktop default mirrors today's default: row 1 Good morning · Action Center · Quick Actions; row 2 Today's Schedule · Total time · Active projects; row 3 My Tasks; Work Pattern full width. Notifications and Wellbeing start **not placed** (available in store). Exact x/y/w/h is the builder's call within that order.
- My Team desktop default mirrors today's page: Team Status wide-left, Quick Actions + Team Progress on the right; if no Team Status, Team Progress takes the wide slot.
- Tablet/mobile defaults derived deterministically from desktop (Guide §12.3).
- Reset = delete the dashboard's instances/layouts and re-seed from default; bump version.

### 6.3 Endpoints (prefix follows existing `api/v1/dashboard/...` style)

| Method | Route | Purpose |
|---|---|---|
| GET | `dashboards/me` | The viewer's two dashboards (my-work, my-team), each with id, key, layoutVersion |
| GET | `dashboards/{id}/bootstrap?breakpoint=` | Grid spec + all **allowed** widget instances with layout for that breakpoint + definition metadata (title, size constraints). No business data in v1 |
| GET | `dashboards/{id}/widgets/available` | Widget Store: allowed definitions for this dashboard, flagged `placed: true/false` |
| POST | `dashboards/{id}/widgets` | Add (widgetKey, expectedVersion) → server auto-places on all 3 breakpoints, returns instance + layouts + new version. Reject not-allowed / wrong-dashboard / already-placed |
| DELETE | `dashboards/{id}/widgets/{instanceId}` | Remove (expectedVersion via body or If-Match — pick one, be consistent) |
| PUT | `dashboards/{id}/layouts/{breakpoint}` | Atomic save of full layout for the visible (allowed) set: bounds, allowed sizes, no overlap, set equality with allowed placed instances; version check → 409 on mismatch |
| POST | `dashboards/{id}/reset` | Reset to default (expectedVersion) |

Every endpoint resolves tenant + actor from auth; dashboard ownership is checked on every call; knowing an id never grants access. Validation errors → 422/400 per codebase convention; version mismatch → 409 with current version.

### 6.4 Limits

Max widgets per dashboard = size of its catalog (one instance per type), so no separate quota needed in v1.

### 6.5 Widget data providers — improving the old dashboard APIs (user decision 2026-10-20)

The user asked that the existing dashboard card APIs be improved as part of this work. Follow Guide §6, §7, §9:

- Inventory every API the dashboard cards call today (team capabilities/action-items, team today/led-progress, my tasks, task progress, schedule, total time, active projects, work pattern, notifications, wellbeing, attendance trend, etc.).
- For each widget, a backend `IWidgetDataProvider` (widgetKey, bounded `GetData(ctx, ct)`) that **reuses the existing query handler logic** — same authorization, same scope, same result fields. The existing endpoints stay working (they may delegate to the same handler) so nothing else in the app breaks.
- Improve while wrapping: bounded payloads (counts + top-N, never full lists), aggregate in SQL, no N+1, per-provider timeout; fix obvious slow queries found during the inventory and report them.
- Bootstrap returns bounded data for every visible widget (fetched concurrently with a concurrency cap and per-provider deadline); a provider failure becomes a widget-level `error` state, never a page 500.
- `GET dashboard-widgets/{instanceId}/data` refreshes one widget (ownership + permission checked).
- Frontend cards accept initial data from bootstrap and use the refresh endpoint for reload/polling instead of their own APIs where a provider exists. **What each card displays and does must be identical to today.**
- No cache layer in v1; `fetchedAt` + `dataState` per Guide §6.

## 7. Frontend

### 7.1 Grid engine

Angular CDK (already a dependency) has no 2D grid with resize. Builder chooses a maintained 2D grid library compatible with Angular 21 (e.g. `gridstack` or `angular-gridster2`) **or** a small in-house engine on CDK drag. Requirements regardless: 12/6/4 columns, snap to each widget's allowed sizes, no overlap, deterministic top-left auto-place, keyboard move/resize alternative (Guide §2.1, §12.4), no layout shift in View mode.

### 7.2 Structure (adapt to existing `modules/dashboard` layout: `data-access/`, `feature/`, `state/`, `ui/`, `models/`, `utils/`)

- `data-access/dashboard-api.service.ts` — the endpoints above.
- `state/dashboard.store.ts` — one store instance per dashboard page: persisted layout, draft layout, editing flag, version, breakpoint; Save sends one PUT; 409 → toast "Dashboard changed elsewhere" + reload.
- `widgets/widget-registry.ts` — static allowlist mapping `widgetKey` → existing card component (+ inputs, e.g. Quick Actions `onTeamDashboard`). Unknown key → placeholder.
- `ui/dashboard-grid`, `ui/widget-frame` (title bar, drag handle, ✕ in edit mode, skeleton, error boundary), `feature/widget-store-drawer`, `feature/dashboard-toolbar` (Save / Cancel / Reset with confirm).
- `dashboard-home` and `my-team-page` both become thin hosts of the same shell with a different `dashboardKey`.
- The top-navbar **Customize** button keeps its place and toggles customize mode for whichever dashboard is on screen; hidden on pages that aren't a dashboard.

### 7.3 Customize mode

- Live cards stay mounted but are **not interactive** (overlay) while editing, so nothing navigates mid-drag. *(Design call — today's code swaps to static tiles; real widgets give a true preview. Builder may fall back to tiles if a card misbehaves at small sizes.)*
- Widget Store: drawer from the right listing only allowed, not-yet-placed widgets for this dashboard, with name, short description and size. Click "Add" → auto-placed in the draft immediately (persisted on Save — add/remove in the draft are sent as POST/DELETE on Save, then the layout PUT, or the builder may batch them; the user-visible rule is **nothing persists until Save, Cancel undoes everything**).
- Reset asks for confirmation.

### 7.4 Cards at different sizes

Cards must remain usable at every size their definition allows. Builder sets each widget's allowed sizes from what the card can actually render; minor CSS changes inside cards to fill their frame are allowed, logic changes are not.

## 8. Removal list (delete, don't leave dead code)

Frontend, after confirming no remaining references:
- `state/dashboard-layout.service.ts` (+ spec), `utils/dashboard-layout.util.ts` (+ spec)
- `feature/dashboard-customize-panel/`
- `ui/dashboard-grid-item/`
- `feature/dashboard-card/` (key→component switch) if superseded by the widget registry
- `utils/my-team-widget-preference.util.ts` (localStorage My Team order — appears unused) and `resolveVisibleWidgets` if unused
- Old customize templates/CSS in `dashboard-home` and the hand-laid-out `my-team-page` template
- Any localStorage keys `onexso.dashboard_layout*`, `onexso.my_team_widget_order*` usage
- Specs for removed code; update specs that referenced them.

Keep `allowedWidgetKeys` / `actionCenterRailVisible` if still used by cards or the store. Anything else found unused in `modules/dashboard` during the work: list it in the final report before deleting.

## 9. Testing & done

- Backend: unit tests for handlers (ownership, tenant, permission filter, overlap/bounds/size validation, 409, seeding race, reset, add/remove rejects); architecture suite (RLS coverage) green; migration added.
- Frontend: specs for grid placement/collision utils, store (draft/save/cancel/409), widget store filtering by capabilities, registry fallback; `npm run check:warnings` clean (zero-warnings rule); `ng build` green.
- Manual: as a plain employee (My Work only, no Action Center), as a lead (both dashboards), customize → save → reload on another browser shows same layout; tablet/mobile widths.

## 10. Left to the builder

Grid library choice; exact default coordinates; allowed sizes per widget; table/column naming per conventions; envelope/error shape per conventions; whether add/remove are batched on Save; visual design of store drawer and frame (match existing Tailwind look).
