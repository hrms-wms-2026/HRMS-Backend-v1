# Single-host tenancy test mode — design

Date: 2026-11-19. Status: approved 2026-11-19.
Repos touched: HRMS-Backend-v1, Hrms--Web-application---front-end---v1, HRMS-Platform-Administration-Front-End-v1. Tray app: not in scope.

## Intent

The team wants to test Crownus on the Azure App Service `onexso-api` (`onexso-api-dthwhydjctg9bub8.uksouth-01.azurewebsites.net`) before a real domain exists. On `azurewebsites.net` there are no per-company subdomains, so a company can't be picked by `acme.<root>`. This design adds a **switch**:

- `Tenancy:Mode = Subdomain`: the current behaviour, unchanged, and the default.
- `Tenancy:Mode = SingleHost`: test mode. Everything runs on one address and the company comes from `?tenant=acme` (first page) and then from a request header.

When a domain is bought, the switch goes back to `Subdomain` and the web app and admin console are rebuilt with their normal configuration. No code is removed.

## Goals

- Web app and admin console work on one App Service address, with no domain.
- Opening `https://<host>/?tenant=acme` lands on Acme; normal login also works.
- Tenant isolation stays exactly as strong. The existing check that the token's `tenant_id` equals the resolved tenant (`TenantEnforcementMiddleware`) stays in force in both modes.
- Switching back to subdomains is configuration plus a rebuild only.

## Non-goals

- Tray app support. It builds tenant URLs from the subdomain (API client, enrolment, portal links); that comes later.
- Several companies signed in at once in one browser. On one address the login cookies are shared, so signing into company 2 signs out company 1. Use a second browser or a private window.
- Production use. SingleHost is for team testing only.

## Anti-goals (a failure even if it works)

- Any path where the header lets a user read another company's data. The header only *names* the company; the token check decides access.
- Subdomain mode behaving differently in any way after this change.
- Admin mode reachable by a client-chosen header value.

## Approaches considered

- **A. Company sent with every request (chosen by the user).** The web app remembers the slug from `?tenant=` or the login hand-off and sends it as a header; live connections send it as a query value. The admin console is served at `/admin-console`, and API paths under `/admin/` run in admin mode. Everything sits on one App Service. Smallest change, easy to switch off. Trade-off: the company isn't visible in the address bar after the first page.
- **B. Company in every page path (`/t/acme/...`).** Visible and shareable, but every route and link in the web app moves. Much larger and harder to undo.
- **C. Web app and API on the two separate App Services.** Rejected: login cookies across two addresses need weaker cookie and CSRF settings.

## Design

### Backend (HRMS-Backend-v1)

1. **Setting.** `Tenancy:Mode` takes `Subdomain` (default when missing) or `SingleHost`. `Tenancy:RootDomain` is set to the App Service host in both modes. Startup logs a warning when `SingleHost` is on.
2. **Tenant resolution** (`HostTenantResolutionMiddleware`). Subdomain mode is untouched. In SingleHost mode:
   - Any host other than `RootDomain` → 400 (same as today).
   - Path starts with `/admin/` → admin mode. This replaces the `admin.` subdomain. All admin controllers already route under `admin/v1`.
   - Otherwise the slug comes from the `X-Crownus-Tenant` header. For `/ws` and `/hubs` requests, where browsers can't set headers, it comes from the `tenant` query value instead.
   - Slug present → the same validation, cache and lookup as today (`SlugPattern`, 404 for unknown, cached registry entry).
   - No slug → system mode, the same as the bare root domain today, so login works.
   - Reserved names (`admin`, `console`, `www`, `assets`, `api`) are rejected as slugs, so the header can't select admin or system mode.
3. **Login hand-off** (`TenantSessionExchangeService.BuildContinueUrl`). In SingleHost mode the URL is `https://<RootDomain>/auth/continue?code=…&tenant=<slug>` instead of `https://<slug>.<RootDomain>/auth/continue?code=…`.
4. **Calendar connection redirect** (`CompleteCalendarConnectionCommandHandler`). The same change: root host plus `tenant=<slug>`.
5. **Last-tenant hint cookie** (`TenantAuthResponseWriter`, `AuthSessionController`). In SingleHost mode it's a host-only cookie, with no parent-domain `Domain`.
6. **CORS and forwarded headers.** Everything is same-origin, so there's no change in logic. The App Service front end must be trusted for `X-Forwarded-Proto`: set `ForwardedHeaders:KnownNetworks` in App Service settings. This is a deployment step, not code.
7. **Serving the two web apps.** This has its own switch, `Hosting:ServeSpas` (default off), independent of `Tenancy:Mode`, so the App Service keeps serving both apps after the move to subdomains. The VM leaves it off, because IIS serves them there. Files come from `wwwroot/web` and `wwwroot/admin-console` in both modes.
   - **SingleHost:** chosen by path. The web app is served at `/`, the admin console at `/admin-console/`.
   - **Subdomain:** chosen by host. `admin.<root>` / `console.<root>` → the admin console at `/`. The root and company subdomains → the web app at `/`.
   - **Fallbacks:** an SPA fallback to the chosen `index.html`, never catching `/api/`, `/admin/`, `/hubs` or `/health`.
8. **Tests.** Unit tests for the middleware in both modes cover: header slug, query slug on `/ws`, `/admin/` path, reserved slug rejected, unknown slug 404, other host 400, and subdomain mode unchanged. Also unit tests for the continue URL and the calendar redirect in both modes, and an integration test showing a token for tenant A sent with header tenant B is refused.

### Web app (Hrms--Web-application---front-end---v1)

1. **Build setting.** A new Angular configuration `singlehost` with `tenancyMode: 'single-host'` and a same-origin `apiUrl` (`/api/v1`) and `wsUrl`. Existing configurations get `tenancyMode: 'subdomain'`.
2. **Remembering the company.** A small tenant-slug store reads `?tenant=` on page load, validates it with the existing `isValidTenantSlug`, and keeps it in `sessionStorage` (per tab).
3. **Sending it.** An HTTP interceptor adds `X-Crownus-Tenant` to API calls when in single-host mode and a slug is known. Live connections (`/ws`, SignalR hubs) add `tenant=<slug>` to their URL.
4. **Login redirects** (`tenant-redirect.ts`). In single-host mode:
   - `buildTenantUrl(slug, path)` returns the same host with `?tenant=slug`.
   - `buildRootUrl` returns the same host.
   - `isTenantHostname` is true when a slug is stored.

   Login, session exchange and legal consent then work without changing hosts.
5. **Logout** clears the stored slug.
6. **Tests (Vitest).** Cover the slug store, the interceptor, `tenant-redirect` in both modes, and the session-exchange flow in single-host mode.

### Admin console (HRMS-Platform-Administration-Front-End-v1)

1. A new Angular configuration `singlehost`: `baseHref` `/admin-console/` and same-origin `apiUrl` `/admin/v1`.
2. No tenant header is needed, because the `/admin/` path selects admin mode.
3. Tests (Jest): the environment URL helper for the new configuration.

### Deployment (App Service `onexso-api`, Linux, .NET 10)

- One zip holds the API publish output, the web build in `wwwroot/web` and the admin build in `wwwroot/admin-console`.
- **App settings** are copied from the VM's `web.config`, with:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `Tenancy__Mode=SingleHost`
  - `Hosting__ServeSpas=true`
  - `Tenancy__RootDomain=<app host>`
  - `Urls__AppBaseUrl` and `Urls__AdminConsoleBaseUrl` set to the app host (the latter with `/admin-console`)
  - the connection string to `9.223.115.62,51433` / `CrownusDB_test`
- Turn on WebSockets and Always On, if the plan tier allows it.
- `onexso-angular-web` is unused for now.

## Switching back when a domain exists

Configuration and redeploy only, on the same App Service. The full runbook is at the end of the plan.

1. **DNS and Azure.** Add the wildcard DNS (`*` CNAME plus `asuid` TXT), the custom domain `*.<domain>` and a wildcard certificate.
2. **App settings.**
   - `Tenancy__Mode=Subdomain`
   - `Tenancy__RootDomain=<domain>`
   - `Urls__AppBaseUrl=https://<domain>`
   - `Urls__AdminConsoleBaseUrl=https://admin.<domain>`
   - Keep `Hosting__ServeSpas=true`.
3. **Redeploy** a package with the web app and admin console built in their normal `production` configuration. The admin console then lives at `admin.<domain>`, not `/admin-console/`.

## Left to the builder

- Exact placement of the static-file and fallback middleware in `Program.cs`, keeping it after tenant resolution or excluded from it.
- Whether SignalR uses `/hubs` or `/ws`. Check both and apply the query rule to whichever exist.
- Names of the new Angular files and the interceptor, following each repo's conventions.

## Calls I made (not discussed)

- Header name `X-Crownus-Tenant`.
- The slug is stored per tab in `sessionStorage`, not `localStorage`, so two tabs can hold different companies until the cookie clash signs one out.
- `onexso-angular-web` is left unused.
- The admin console lives at `/admin-console/` on the same app.
