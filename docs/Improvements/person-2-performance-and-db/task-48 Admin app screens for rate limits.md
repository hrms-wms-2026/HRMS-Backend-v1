# Task 48: Admin app screens for rate limits

| | |
|---|---|
| Priority | 48 (lower number = build first). Added 2026-11-17 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Admin frontend |
| Repository | `HRMS-Platform-Administration-Front-End-v1` (Angular 21, signals, `@ngrx/signals`). This task file lives in the backend repo with the rest of the set |
| Depends on | 07, 47 (admin API) |
| Guide | [Admin-managed rate limits guide](<../../superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md>) |

## What we found

The admin frontend was read on branch `docs/organize-md-files` on 2026-11-17. The 2026-11-15 audit did not cover it. Its layout:

- Routes are in one file, `src/app/app.routes.ts`. Each route lazy-loads a component, has `canActivate: [permissionGuard]` and `data: { breadcrumb, permission }`. Platform config pages use `path: 'system-config/<name>'` and `breadcrumb: { section: 'Platform Configuration', page: '...' }`.
- The side menu is `src/app/layouts/main-layout/sidebar/sidebar.ts`. The "Platform Configuration" group lists Providers, Integration Catalog, Service Keys, Tray Releases, OAuth Apps, Payment Gateways and Tenant Integrations, each with `permission: 'platform.system_config.read'`.
- Feature code is in `src/app/modules/<module>/`, with `data/` (model and service files) and `feature/` (one folder per component: list pages, drawers, modals). Example: `modules/system-config/data/service-keys.service.ts` and `modules/system-config/feature/service-keys-list/`.
- Services use `HttpClient` with `environment.apiUrl` (already ends in `/admin/v1`), paths from `src/app/core/config/api-endpoints.ts` (`API_ENDPOINTS.systemConfig.*`), and `{ withCredentials: true }`.
- Components check permissions with `inject(PermissionStore)` and `permissionStore.hasPermission('platform.system_config.manage')` (see `service-keys-list.ts`).
- Shared UI is in `src/app/shared/ui/`: `table`, `modal`, `confirmation-dialog`, `error-banner`, `empty-state`, `status-badge`, `metric-card`, `pagination`, `table-skeleton`.
- The tenant detail page (`modules/tenants/feature/tenant-detail/tenant-detail.html`) is a stack of panels such as `app-tenant-subscription-panel`, `app-tenant-sessions-panel` and `app-tenant-audit-log-panel`, each taking `[tenantId]`.
- The plan detail page is `modules/subscription-plans/feature/subscription-plan-detail/`.
- Tests: Jest (`npm test`, `*.spec.ts` next to each file) and Playwright (`npm run e2e`, `e2e/system-config/` already exists).

## What to do

1. Data layer, in `modules/system-config/data/`:
   - `rate-limit.model.ts`: `RateLimitName` (`'Anonymous' | 'SignedIn' | 'TrayDevice' | 'TenantCeiling' | 'AdminAnonymous' | 'AdminSignedIn'`, the backend's `RateLimitSettings` property names), `RateLimitSetting` (`perMinute`, `burst?`), `RateLimitBounds`, `EffectiveRateLimit` (values plus `source: 'tenant' | 'plan' | 'global' | 'appsettings'`), `RateLimitChange`.
   - `rate-limits.service.ts`: one method per route in task 47.
   - Add the paths to `API_ENDPOINTS.systemConfig.rateLimits`, `API_ENDPOINTS.subscriptionPlans.rateLimits(id)` and `API_ENDPOINTS.tenants.rateLimits(id)`.
2. Global page `system-config/rate-limits`:
   - Add a route in `app.routes.ts` with `permission: 'platform.system_config.read'` and breadcrumb `{ section: 'Platform Configuration', page: 'Rate Limits' }`.
   - Add a sidebar item "Rate Limits" in the Platform Configuration group.
   - Component `modules/system-config/feature/rate-limits-list/`: a table with one row for each of the six limits. Columns:
     - the limit, with a plain-English label from the guide (for example `SignedIn` is "Signed-in user, each user" and `TenantCeiling` is "Whole tenant, all users and devices together");
     - per minute;
     - burst (blank for `Anonymous` and `AdminAnonymous`, which have none);
     - the `appsettings` value;
     - the allowed range.
   - An Edit button (shown only with `platform.system_config.manage`) opens `edit-rate-limit-modal/`, which has a "per minute" input, a "burst" input for token-bucket limits (with the hint "how many requests at once, for example one page load"), `min` and `max` from the API bounds, a required reason field, and an inline error from the API's validation problem body.
   - Show this warning on the modal: "Limits are counted per API server until shared counters are added (task 21). With two servers, users get up to twice this number."
   - A "Recent changes" section with the paginated history read, using `shared/ui/pagination`.
3. Plan detail: add a "Rate limits" card to `subscription-plan-detail` with the plan's overrides for `SignedIn`, `TrayDevice` and `TenantCeiling`. For each one, show the override or "Inherits global: N". Edit and Remove use the same modal component. Show the buttons only with `platform.system_config.manage`, even if the user can manage plans.
4. Tenant detail: add `modules/tenants/feature/tenant-rate-limits-panel/` and place it in `tenant-detail.html` next to the sessions and audit panels. It shows the effective `SignedIn`, `TrayDevice` and `TenantCeiling` values and where each one comes from, plus Override and Remove actions. Show it only with `platform.system_config.read`.
5. Do not offer the login, MFA or password reset limits anywhere, or the `RateLimiting:Enabled` master switch. They are not editable (guide, section 3).
6. After a save, reload the affected list. Do not patch the local state by hand. The backend is the source of truth for "effective" values.

## Tests

- Jest: the service calls the right URLs with `withCredentials`. The modal blocks Save for an empty reason or a value outside the bounds. Edit buttons are hidden without `platform.system_config.manage`. The tenant panel shows the right source label for tenant, plan and global values.
- Playwright (`e2e/system-config/`): sign in as a platform admin, change the global `SignedIn` limit, see the new value and a history row, then change it back.

## Done when

- An admin with `platform.system_config.manage` can view and change global, plan and tenant limits from the admin app, and every change asks for a reason.
- An admin with only `platform.system_config.read` sees the values with no edit actions.
- `npm test`, `npm run lint` and the new Playwright test pass in the admin frontend repository.
