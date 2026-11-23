# Single-Host Tenancy Test Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Crownus (web app + admin console) run on one App Service address with the tenant chosen by `?tenant=slug` / an `X-Crownus-Tenant` header, behind a `Tenancy:Mode` switch whose default keeps today's subdomain behaviour unchanged.

**Architecture:** The backend gets a `TenancyMode` (`Subdomain` default, `SingleHost`). In SingleHost, `HostTenantResolutionMiddleware` takes the slug from a header (or the `tenant` query value on `/hubs/*`) and admin mode from the `/admin/` path. The login continue URL, calendar redirect and hint cookie stop using `{slug}.` hosts. The API also serves both SPAs. The web app gets a build-time `tenancyMode`, a per-tab slug store, a header interceptor and mode-aware redirect helpers. The admin console gets a `singlehost` build with base href `/admin-console/`.

**Tech Stack:** .NET 10 / ASP.NET Core, xUnit (unit + Testcontainers SQL Server integration); Angular 21 + Vitest (web); Angular + Jest (admin).

**Spec:** `C:\onevoNew\docs\superpowers\specs\2026-11-19-single-host-tenancy-test-mode-design.md` (approved 2026-11-19). Read it first.

## Global Constraints

- **Default is unchanged.** `Tenancy:Mode` missing or `Subdomain` → byte-for-byte today's behaviour. Every SingleHost branch is guarded by the mode.
- **Accepted values.**
  - Mode values are exactly `Subdomain` and `SingleHost`, case-insensitive. Anything else fails startup with `InvalidOperationException`.
  - Header name: `X-Crownus-Tenant`. Hub query key: `tenant`. Web query key on page URLs: `tenant`.
  - Admin path prefix: `/admin/`. Admin SPA base: `/admin-console/`. Web SPA is served at `/`.
- **The isolation check stays.** `TenantEnforcementMiddleware` (token `tenant_id` == resolved tenant) is not modified.
- **Reserved slugs never resolve via the header:** `admin`, `console`, `www`, `assets`, `api`. Invalid ones are rejected with 400.
- **Startup warning** when SingleHost is on: `"Tenancy:Mode is SingleHost - test mode only; tenant comes from the X-Crownus-Tenant header."`
- **Tray app:** no changes.
- **SPA serving has its own switch.** `Hosting:ServeSpas` (default `false`) is independent of `Tenancy:Mode`. The App Service sets it `true` in both modes; the VM (IIS) leaves it off.
- **Where to work.**
  - Work in fresh clones, not in `C:\onevoNew\<repo>`: other sessions have uncommitted work there (backend and web are on `feat/web-google-login`).
  - Never use git worktrees (user rule).
  - Clone each repo to `C:\onevoNew\<repo>-singlehost` from `origin/development` (backend, admin) or `origin/Development` (web), branch `feature/single-host-tenancy`.
  - When done, move any `.env` the clone needs back, and remove `node_modules` junctions with `cmd /c rmdir` before deleting a clone.
- **Backend gate:** `pwsh ../dev-env/scripts/verify.ps1 backend` (run from the clone; adjust the relative path), plus `-Full` once at the end with Docker running.
- **Spec location in the repo.** Commit the spec into the backend clone as `docs/superpowers/specs/next/2026-11-19-single-host-tenancy-test-mode-design.md`, and this plan as `docs/superpowers/plans/next/2026-11-19-single-host-tenancy-test-mode.md`. Add both to the `SUMMARY.md` files in those folders.

## Review Focus

1. **Redirect loop on one host.** The login and legal-consent pages bounce "tenant host → root host". On one host that must clear the stored slug and land on the page once, never reload forever. Tests in Task 6.
2. **Stale slug vs. another tenant's cookie.** The tab holds `acme` but the cookie belongs to `company1`. The API refuses (403 from TenantEnforcementMiddleware), and the existing error handling must send the user to login, not retry. Test in Task 5 (backend refuses) and Task 6 (logout/login clears slug).
3. **Junk `?tenant=` values** (`ACME`, `a`, `admin`, `x.y`, empty). The web app ignores invalid slugs and sends no header. The backend returns 400 for invalid or reserved header values. Tests in Tasks 2 and 6.
4. **Unknown API paths.** `/api/v1/does-not-exist`, `/admin/v1/nope`, `/health/x` and `/hubs/x` must not be swallowed by the SPA fallback and answered 200 `index.html`. Test in Task 4.
5. **The header is ignored in Subdomain mode.** Sending `X-Crownus-Tenant: other` to `acme.<root>` still resolves `acme`. Test in Task 2.

---

## Backend (clone `HRMS-Backend-v1-singlehost`)

### Task 1: Tenancy mode setting

**Files:**
- Create: `src/ONEVO.Application/Common/Tenancy/TenancyMode.cs` (Application layer, so handlers in Task 3 can use it)
- Modify: `src/ONEVO.Api/Program.cs` (startup warning)
- Test: `tests/ONEVO.Tests.Unit/Features/Api/Tenancy/TenancyModeTests.cs`

**Interfaces — Produces:**
- `public enum TenancyMode { Subdomain, SingleHost }`
- `public static class TenancyModeReader { public static TenancyMode Read(IConfiguration configuration); }`, which reads `Tenancy:Mode`
- `public static class TenancyHeaders { public const string Tenant = "X-Crownus-Tenant"; public const string HubQueryKey = "tenant"; }`

- [ ] **Step 1: Failing tests**
  - `Read_Missing_ReturnsSubdomain`
  - `Read_SingleHost_CaseInsensitive` (`"singlehost"` → `SingleHost`)
  - `Read_Subdomain_ReturnsSubdomain`
  - `Read_Unknown_Throws` (`"Path"` → `InvalidOperationException`)
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~TenancyModeTests"`. Expect a compile failure or FAIL.
- [ ] **Step 3:** Implement the types. In `Program.cs` after `builder.Build()`, log the exact warning from Global Constraints via `app.Logger.LogWarning` when the mode is SingleHost.
- [ ] **Step 4:** Rerun the filter. Expect PASS.
- [ ] **Step 5:** Commit `feat(tenancy): add Tenancy:Mode setting (Subdomain default, SingleHost test mode)`.

### Task 2: SingleHost tenant resolution

**Files:**
- Modify: `src/ONEVO.Api/Middleware/HostTenantResolutionMiddleware.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Api/Middleware/HostTenantResolutionMiddlewareTests.cs` (extend; follow its existing context/cache fakes)

**Interfaces:** Consumes Task 1. The constructor reads the mode once. The existing slug lookup/cache block is extracted to `private async Task<bool> ResolveSlugAsync(HttpContext, string slug, …)` and used by both modes, so 404/cache behaviour is shared.

SingleHost order, stopping at the first match:
1. Host ≠ RootDomain → 400.
2. Path starts with `/admin/` (ordinal, case-insensitive) → `SetAdminMode()`.
3. Slug = header `X-Crownus-Tenant`. If the path starts with `/hubs/` and there's no header, use the query `tenant`.
4. No or blank slug → `SetSystemMode()`.
5. Slug in the reserved set, or failing `SlugPattern` → 400.
6. Otherwise resolve like a subdomain slug.

- [ ] **Step 1: Failing tests** (configure `Tenancy:Mode=SingleHost`, `Tenancy:RootDomain=app.example.net` unless stated)
  - `SingleHost_HeaderSlug_ResolvesTenant`: header `acme`, the tenant exists → context resolved to acme, next called.
  - `SingleHost_HubQuerySlug_ResolvesTenant`: path `/hubs/inbox`, query `tenant=acme`, no header → acme.
  - `SingleHost_QuerySlugOutsideHubs_Ignored`: path `/api/v1/x?tenant=acme` → system mode.
  - `SingleHost_AdminPath_SetsAdminMode`: path `/admin/v1/tenants`, header `acme` → admin mode.
  - `SingleHost_NoSlug_SystemMode`: path `/api/v1/auth/login` → system mode.
  - `SingleHost_ReservedSlug_400`: theory over `admin`, `console`, `www`, `assets`, `api` → 400, next not called.
  - `SingleHost_InvalidSlug_400`: theory over `ACME`, `a`, `x.y`, `-bad` → 400.
  - `SingleHost_UnknownSlug_404`.
  - `SingleHost_OtherHost_400`: host `evil.net`.
  - `Subdomain_HeaderIgnored`: Subdomain mode, host `acme.app.example.net`, header `other` → acme.
- [ ] **Step 2:** Run the filter `HostTenantResolutionMiddlewareTests`. Expect the new tests to FAIL and the existing ones to pass.
- [ ] **Step 3:** Implement as above. The Subdomain path must remain the current code path.
- [ ] **Step 4:** Rerun. Expect all PASS.
- [ ] **Step 5:** Commit `feat(tenancy): resolve tenant from X-Crownus-Tenant header in SingleHost mode`.

### Task 3: Continue URL, calendar redirect and hint cookie

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Login/Services/TenantSessionExchangeService.cs` (`BuildContinueUrl`)
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommandHandler.cs` (`BuildRedirect`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Auth/TenantHintCookieWriter.cs`, plus its two callers (`TenantAuthResponseWriter.cs`, `AuthSessionController.cs`), to pass the mode
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/TenantSessionExchangeServiceTests.cs`, `tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs`, new `tests/ONEVO.Tests.Unit/Features/Auth/TenantHintCookieWriterTests.cs`

**Interfaces:** Consumes `TenancyModeReader`. The hint cookie methods gain a `TenancyMode mode` parameter. In SingleHost, `Domain` is left null (host-only).

- [ ] **Step 1: Failing tests**
  - `BuildContinueUrl_SingleHost_UsesRootHostWithTenantQuery`: AppBaseUrl `https://app.example.net`, RootDomain `app.example.net`, slug `acme` → host `app.example.net`, path `/auth/continue`, query has `code=` and `tenant=acme`.
  - The existing subdomain continue-URL test stays green unchanged.
  - Calendar: `SingleHost_Redirect_UsesRootHostWithTenantQuery`. The same shape, with the handler's existing path and query plus `tenant=<slug>`.
  - Hint cookie:
    - `SingleHost_SetsHostOnlyCookie`: `Set-Cookie` has no `domain=`.
    - `Subdomain_SetsParentDomainCookie`: `domain=.app.example.net`.
- [ ] **Step 2:** Run the three filters. Expect FAIL.
- [ ] **Step 3:** Implement. Keep the existing `InvalidOperationException` messages for missing config.
- [ ] **Step 4:** Rerun. Expect PASS. Also run `ONEVO.Tests.Architecture` (it has `TenantSessionExchangeArchitectureTests`). Expect PASS.
- [ ] **Step 5:** Commit `feat(tenancy): same-host login continue URL, calendar redirect and hint cookie in SingleHost`.

### Task 4: Serve the web app and admin console from the API (both modes)

SPA serving has its own switch, independent of `Tenancy:Mode`, so the same App Service keeps serving both apps after the move to subdomains. On the VM, IIS serves the SPAs, so the switch stays off there.

**Files:**
- Create: `src/ONEVO.Api/Extensions/SpaHostingExtensions.cs`
- Modify: `src/ONEVO.Api/Program.cs`
  - call `app.UseSpaHosting()` immediately after `app.UseForwardedHeaders()`
  - call `app.MapSpaHostingFallbacks()` after the health checks
  - both are no-ops when `Hosting:ServeSpas` is not `true`
- Test: `tests/ONEVO.Tests.Integration/Hosting/SpaHostingTests.cs` (uses the existing `WebApplicationFactory` collection; temp folders hold fake `web/index.html` and `admin-console/index.html` with distinct markers)

**Interfaces — Produces:**
- `WebApplication UseSpaHosting(this WebApplication app)`
- `WebApplication MapSpaHostingFallbacks(this WebApplication app)`
- **Settings.**
  - `Hosting:ServeSpas` (bool, default `false`).
  - `Hosting:SpaRoot` (default `<ContentRoot>/wwwroot`). The web files are in `{SpaRoot}/web`, the admin files in `{SpaRoot}/admin-console`. The same folder layout is used in both modes.
- **Which app a request gets:**
  - **SingleHost:** chosen by path. `/admin-console/**` → the admin files, served under request path `/admin-console`. Everything else → the web files at `/`.
  - **Subdomain:** chosen by host. A host of `admin.{RootDomain}` or `console.{RootDomain}` → the admin files at `/`. Any other host under `RootDomain` (the root and company subdomains) → the web files at `/`. Use the same admin-host set as `HostTenantResolutionMiddleware`'s `AdminSubdomains`, shared rather than copied: expose it as `internal static IReadOnlySet<string> AdminSubdomains`.
- **Fallback rule, both modes.** A GET with no matching endpoint, not starting with `/api/`, `/admin/`, `/hubs/` or `/health`, and with no file extension, serves the `index.html` of the app chosen above. Everything else keeps its normal 404.

- [ ] **Step 1: Failing tests**
  - SingleHost (`Hosting:ServeSpas=true`, RootDomain `localhost`):
    - `SingleHost_Root_ServesWebIndex`: GET `/` → 200 text/html with the web marker.
    - `SingleHost_DeepLink_ServesWebIndex`: GET `/dashboard/team` → the web marker.
    - `SingleHost_AdminConsole_DeepLink_ServesAdminIndex`: GET `/admin-console/tenants/1` → the admin marker.
  - Subdomain (`Hosting:ServeSpas=true`, RootDomain `crownus.test`, requests sent with that Host header):
    - `Subdomain_TenantHost_ServesWebIndex`: host `acme.crownus.test`, GET `/dashboard` → the web marker.
    - `Subdomain_RootHost_ServesWebIndex`: host `crownus.test`, GET `/auth/login` → the web marker.
    - `Subdomain_AdminHost_ServesAdminIndex`: host `admin.crownus.test`, GET `/tenants/1` → the admin marker.
    - `Subdomain_AdminHost_AdminConsolePath_NotSpecial`: host `admin.crownus.test`, GET `/admin-console/x` → the admin marker (plain fallback), never the web marker.
  - Both modes:
    - `UnknownApiPath_Is404NotIndex`: theory over the mode × `/api/v1/does-not-exist`, `/admin/v1/nope`, `/hubs/x`, `/health/x` → status ≠ 200 or the body lacks both markers.
    - `MissingAsset_Is404`: GET `/main.missing.js` → 404.
    - `ServeSpasOff_NoSpaServing`: theory over both modes with `Hosting:ServeSpas` unset → GET `/` never returns either marker.
- [ ] **Step 2:** Run the filter `SpaHostingTests`. Expect FAIL.
- [ ] **Step 3:** Implement.
  - Two `StaticFileOptions`, each with its own `PhysicalFileProvider`.
  - Choose between them with `app.UseWhen(ctx => IsAdminRequest(ctx), …)` and its opposite. `IsAdminRequest` follows the "Which app" rules for the current mode.
  - Fallback: one `MapFallback` handler that applies the fallback rule and sends the chosen `index.html` with `Results.File(..., "text/html")`.
- [ ] **Step 4:** Rerun. Expect PASS.
- [ ] **Step 5:** Commit `feat(hosting): optionally serve web and admin SPAs from the API (path-based in SingleHost, host-based in Subdomain)`.

### Task 5: Isolation integration test and backend gate

**Files:**
- Test: `tests/ONEVO.Tests.Integration/Auth/SingleHostTenantIsolationTests.cs` (reuse the factory and helpers the existing auth integration tests use to create two tenants and sign a user in)

- [ ] **Step 1: Write the tests** (SingleHost)
  - `TokenForTenantA_WithHeaderTenantB_IsRefused`: sign in as an acme user, call `GET /api/v1/auth/me` with header `X-Crownus-Tenant: company1` → 401 or 403, with no company1 data in the body.
  - `TokenForTenantA_WithHeaderTenantA_Succeeds`.
- [ ] **Step 2:** Run with Docker running. Expect PASS. If the refusal test fails, stop: that's a real isolation bug, and you report it rather than patch around it.
- [ ] **Step 3:** Run the gates:
  - `pwsh ../dev-env/scripts/verify.ps1 backend` → all green
  - `-Full` → all green
  - `find-unauthorized-endpoints.ps1 backend` → 0
- [ ] **Step 4:** Commit `test(tenancy): SingleHost header cannot cross tenants`. Commit the spec and plan docs too (see Global Constraints).

## Web app (clone `Hrms--Web-application---front-end---v1-singlehost`)

### Task 6: Single-host mode in the web app

**Files:**
- Modify: `src/app/core/config/runtime-config.model.ts`: add `readonly tenancyMode: 'subdomain' | 'single-host'`. Set it to `'subdomain'` in `environment.ts`, `environment.prod.ts` and `environment.staging.ts`.
- Create: `src/environments/environment.singlehost.ts`: a copy of prod with `tenancyMode: 'single-host'` and `environmentName: 'production'`.
- Modify: `angular.json`: `build.configurations.singlehost` = production plus a fileReplacement to `environment.singlehost.ts`.
- Create: `src/app/core/auth/utils/single-host-tenant.ts` (slug store)
- Modify: `src/app/core/auth/utils/tenant-redirect.ts`, `src/app/core/auth/data-access/auth-api.service.ts` (`getTenantApiUrl`)
- Create: `src/app/core/interceptors/tenant-header.interceptor.ts`, registered first in `app.config.ts` `withInterceptors([...])`
- Modify: `src/app/core/realtime/inbox-realtime.service.ts` (hub URL line ~153)
- Modify: the logout path in the auth store (`src/app/core/auth/state/auth.store.ts`) to call `clearSingleHostTenant()`
- Call `captureSingleHostTenantFromUrl()` once at bootstrap (`main.ts`, before `bootstrapApplication`)
- Tests: a `*.spec.ts` next to each new or changed file (Vitest)

**Interfaces — Produces:**
- **The slug store** (all are no-ops or `null` when `environment.tenancyMode !== 'single-host'`):
  - `isSingleHostMode(): boolean`
  - `captureSingleHostTenantFromUrl(search = window.location.search): void` validates with `isValidTenantSlug` and stores in `sessionStorage` key `crownus.singleHostTenant`. Invalid values are ignored and keep any stored slug.
  - `getSingleHostTenant(): string | null`
  - `setSingleHostTenant(slug: string): void`
  - `clearSingleHostTenant(): void`
- **Redirect helpers in single-host mode** (subdomain-mode behaviour unchanged):
  - `getRootHostname(h)` → `h`
  - `buildTenantUrl(slug, path)` → `${origin}${path}` with `tenant=slug` added to the query
  - `buildRootUrl(path)` → clears the slug, then returns `${origin}${path}`
  - `isTenantHostname()` → `getSingleHostTenant() !== null`
  - `getTenantApiUrl(apiUrl, slug)` → validates the slug, then returns `apiUrl` unchanged
- **The interceptor** adds `X-Crownus-Tenant: <slug>` only when in single-host mode, a slug is stored, and the request URL starts with `environment.apiUrl`'s origin. Outside single-host mode it never adds the header.
- **The hub URL** gets `?tenant=<slug>` appended in single-host mode when a slug is stored.

- [ ] **Step 1: Failing tests**
  - Store:
    - `captures valid ?tenant=acme`
    - `ignores ACME, x.y, -bad, empty` (`admin` passes the web pattern and is stored; the backend rejects it with 400, Task 2)
    - `no-op in subdomain mode`
  - Redirect:
    - `single-host buildTenantUrl keeps host and adds tenant query` (`https://app.azurewebsites.net` → `https://app.azurewebsites.net/dashboard?tenant=acme`)
    - `single-host getRootHostname keeps uksouth-01 host intact` (`onexso-api-x.uksouth-01.azurewebsites.net` unchanged)
    - `single-host buildRootUrl clears slug`
    - `subdomain behaviour unchanged` (existing specs stay green)
  - Interceptor:
    - `adds header for same-origin API call when slug stored`
    - `no header without slug`
    - `no header for other origins`
    - `no header in subdomain mode`
  - Login and legal consent (Review Focus 1): `on single host with a stored slug, login page redirects once to /auth/login and the slug is cleared`, so a second evaluation does not redirect.
  - Auth store: `logout clears single-host slug`.
- [ ] **Step 2:** Run `npx vitest run src/app/core` (the repo's test command). Expect the new specs to FAIL.
- [ ] **Step 3:** Implement per the Interfaces block. Tests mock `environment.tenancyMode` the way the existing specs mock environment (`vi.mock('@environments/environment', …)`).
- [ ] **Step 4:** Rerun. Expect PASS. Then run:
  - the full web test run: no new failures
  - `npx ng build --configuration singlehost`: succeeds
  - `npx ng build --configuration production`: succeeds
- [ ] **Step 5:** Commit `feat(tenancy): single-host test build with tenant header and same-host redirects`.

## Admin console (clone `HRMS-Platform-Administration-Front-End-v1-singlehost`)

### Task 7: Admin single-host build

**Files:**
- Create: `src/environments/environment.singlehost.ts`: the same as `environment.prod.ts` on development (it already uses same-origin `buildAdminApiUrl(protocol, hostname)`).
- Modify: `angular.json`: `build.configurations.singlehost` = production plus `"baseHref": "/admin-console/"` plus a fileReplacement to `environment.singlehost.ts`.
- Test: `src/environments/environment-url.helper.spec.ts` (add a case)

- [ ] **Step 1: Failing test.** `buildAdminApiUrl('https:', 'app.azurewebsites.net')` returns `https://app.azurewebsites.net/admin/v1`. If it already passes, keep it as a guard and note that in the commit.
- [ ] **Step 2:** Run `npx jest src/environments`.
- [ ] **Step 3:** Add the environment and configuration.
- [ ] **Step 4:** Run `npx ng build --configuration singlehost`. Expect success, and `dist/**/index.html` contains `<base href="/admin-console/">`. Check whether router links and asset URLs are relative. If any hard-codes `/assets/...`, fix it to be base-relative and add a spec.
- [ ] **Step 5:** Commit `feat(tenancy): admin console single-host build under /admin-console/`.

## Deployment (after the three branches are reviewed; the user runs the portal steps)

### Task 8: Package and App Service settings

- [ ] **Step 1: Package.** Create `ops/appservice/build-appservice-package.ps1` in the backend clone, taking `-Mode SingleHost|Subdomain` (default `SingleHost`) and the web and admin repo paths. It:
  - runs `dotnet publish src/ONEVO.Api -c Release -o <out>`
  - builds the web app and the admin console with Angular configuration `singlehost` for `-Mode SingleHost`, or `production` for `-Mode Subdomain`
  - copies the web `dist/.../browser` into `<out>/wwwroot/web`
  - copies the admin `dist/.../browser` into `<out>/wwwroot/admin-console` (the same folder in both modes; Task 4 picks the request path or host)
  - fails if the admin `index.html` base href doesn't match the mode (`/admin-console/` vs `/`)
  - zips the result to `C:\onevoNew\crownus-appservice-<date>.zip`
  - prints its SHA-256
- [ ] **Step 2: App settings list for the user.** Generate the list from the VM's `C:\crownus\api\web.config` names, with these overrides:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `Tenancy__Mode=SingleHost`
  - `Hosting__ServeSpas=true`
  - `Tenancy__RootDomain=onexso-api-dthwhydjctg9bub8.uksouth-01.azurewebsites.net`
  - `Urls__AppBaseUrl=https://<that host>`
  - `Urls__AdminConsoleBaseUrl=https://<that host>/admin-console`
  - `ConnectionStrings__…` → `Server=9.223.115.62,51433;Database=CrownusDB_test;…`
  - `ForwardedHeaders__KnownNetworks__0` / `__1` covering App Service's front end (the builder checks what `ForwardedHeadersExtensions` accepts; `0.0.0.0/0` + `::/0` is acceptable only because App Service is the sole ingress)

  Secrets come from the user. They're never echoed or committed.
- [ ] **Step 3: User in portal.**
  - Configuration: WebSockets On, Always On if the tier allows, HTTPS Only On.
  - Deployment Center or Advanced Tools: zip deploy.
- [ ] **Step 4: Verify from this PC.**
  - `GET https://<host>/health/ready` → 200
  - `/` → web login page
  - `/admin-console/` → admin login
  - Sign in as the bootstrap admin, publish legal docs, create tenant `acme`
  - Open `https://<host>/?tenant=acme`, sign in as the acme admin
  - The DevTools Network tab shows `X-Crownus-Tenant: acme` and the inbox hub connects

## Later: switching the same App Service to subdomains (not part of this build)

Runbook for when a domain exists; no code changes needed. Example domain `crownus.example`.

1. **DNS.**
   - `*` CNAME → the app's azurewebsites.net host.
   - Optionally the bare domain → the same host.
   - The `asuid` TXT record with the app's Custom Domain Verification ID.
2. **Azure → Custom domains.** Add `*.crownus.example` (and the bare domain), with a wildcard certificate: Cloudflare proxy or an uploaded PFX.
3. **App settings.**
   - `Tenancy__Mode=Subdomain`
   - `Tenancy__RootDomain=crownus.example`
   - `Urls__AppBaseUrl=https://crownus.example`
   - `Urls__AdminConsoleBaseUrl=https://admin.crownus.example`
   - Keep `Hosting__ServeSpas=true`.
4. **Redeploy** a package built with `build-appservice-package.ps1 -Mode Subdomain`.
5. **Check.**
   - `https://crownus.example/auth/login` → web login
   - `https://admin.crownus.example/` → admin login
   - Sign in → lands on `https://acme.crownus.example/dashboard`
   - Users sign in once more, because the old cookies belong to the old host.
