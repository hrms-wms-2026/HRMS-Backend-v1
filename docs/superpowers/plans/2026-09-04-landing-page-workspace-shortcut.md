# Landing Page "Go to Workspace" Shortcut Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the root landing page offer a one-click "Go to Workspace" link back into a browser's most-recently-used tenant, instead of always showing "Login", by having tenant-host login set a small non-authoritative parent-domain "hint" cookie the landing page can read.

**Architecture:** A new cookie, `onevo_last_tenant`, is set on the parent domain (`Domain=.{Tenancy:RootDomain}`) whenever a tenant-host session is created, and cleared on logout. It carries a tenant slug only, is never `HttpOnly`, and is never consulted by any authorization/session code — it is purely a UX hint the landing page's header component reads via `document.cookie` to decide which link to render.

**Tech Stack:** ASP.NET Core (cookie middleware, `ControllerBase` extension methods), Angular 21 (standalone component, signals), xUnit + Testcontainers (backend integration tests), Vitest (frontend unit tests).

**Spec:** `HRMS-Backend-v1/docs/superpowers/specs/2026-09-04-landing-page-workspace-shortcut-design.md`

## Global Constraints

- The cookie must never be `HttpOnly` (the landing page's JS must read it).
- The cookie must never be read by any backend authorization/session-validation code — write-only from the backend's perspective.
- `Domain` must be `"." + Tenancy:RootDomain` (the same config value `HostTenantResolutionMiddleware` already uses), not hardcoded.
- Written only at login (`TenantAuthResponseWriter.SignInAsync`) and cleared only at logout (`AuthSessionController.Logout`) — no other write points.
- Frontend must use the existing `buildTenantUrl` helper (`core/auth/utils/tenant-redirect.ts`) to build the workspace link — no new URL-construction logic.

---

### Task 1: Backend — set `onevo_last_tenant` on tenant-host login

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Auth/TenantAuthResponseWriter.cs:1-85`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs:87-128`

**Interfaces:**
- Consumes: `ITenantContext.Slug` (`ONEVO.Application.Common.ServiceInterfaces`, already DI-registered and resolved per-request by `HostTenantResolutionMiddleware`); `IConfiguration["Tenancy:RootDomain"]` (already used the same way by `HostTenantResolutionMiddleware.cs:34`).
- Produces: `SetLastTenantHintCookie(this ControllerBase controller, string tenantSlug, string rootDomain, IWebHostEnvironment env)` and `ClearLastTenantHintCookie(this ControllerBase controller, string rootDomain, IWebHostEnvironment env)` — both added to `TenantAuthResponseWriter` for Task 2 to call.

- [ ] **Step 1: Write the failing integration test assertion**

Open `HRMS-Backend-v1/tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs`. Find the test `ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingReturnsTenantSessionExchange_ThenExchangeIssuesSessionAndCsrfCookies` (around line 87). Add this line immediately after the existing `onevo_csrf` assertion (currently line 123, `setCookies.Should().Contain(c => c.StartsWith("onevo_csrf=", StringComparison.Ordinal));`):

```csharp
        setCookies.Should().Contain(c =>
            c.StartsWith("onevo_last_tenant=one-match-tenant", StringComparison.Ordinal) &&
            c.Contains("domain=.localhost", StringComparison.OrdinalIgnoreCase));
```

(`"one-match-tenant"` is the tenant slug `SeedActiveUserAsync("one-match-tenant", ...)` already seeds at the top of this test — see `SeedActiveUserAsync`'s implementation at line 525-535, which sets `tenant.Slug = tenantSlug`. `"domain=.localhost"` matches because the integration test environment sets `Tenancy__RootDomain=localhost` — see `IntegrationTestEnvironmentScope.cs:71`.)

Also extend the existing `AssertNoTenantSessionCookies` helper (currently lines 615-631) to prove the hint cookie is never set on any of the early-return paths (password-change/MFA/legal-pending/tenant-session-exchange-pending) — this helper is already called at every one of those checkpoints (e.g. lines 114, 204, 252), so extending it covers all of them at once:

```csharp
    private static void AssertNoTenantSessionCookies(HttpResponseMessage response)
    {
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : new List<string>();
        cookies.Should().NotContain(
            c => c.StartsWith("onevo_session=", StringComparison.Ordinal),
            "the base host must never set onevo_session");
        cookies.Should().NotContain(
            c => c.StartsWith("onevo_csrf=", StringComparison.Ordinal),
            "the base host must never set onevo_csrf");
        cookies.Should().NotContain(
            c => c.StartsWith("onevo_last_tenant=", StringComparison.Ordinal),
            "the last-tenant hint must only be set on a completed sign-in, never a pending gate");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run (requires Docker):
```bash
cd HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingReturnsTenantSessionExchange_ThenExchangeIssuesSessionAndCsrfCookies"
```
Expected: FAIL — the assertion added in Step 1 fails because no `onevo_last_tenant` cookie is present yet in `setCookies`.

- [ ] **Step 3: Add the cookie helpers and usings**

In `TenantAuthResponseWriter.cs`, add three usings after the existing ones (after `using ONEVO.Infrastructure.Identity.Sessions;`):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
```

Add these two new methods to the `TenantAuthResponseWriter` class, immediately after the existing `SignInAsync` method (which currently ends at line 85 with the closing `}` after the `onevo_csrf` cookie append):

```csharp
    public static void SetLastTenantHintCookie(
        this ControllerBase controller, string tenantSlug, string rootDomain, IWebHostEnvironment env)
    {
        controller.Response.Cookies.Append("onevo_last_tenant", tenantSlug, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Domain = "." + rootDomain,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(180)
        });
    }

    public static void ClearLastTenantHintCookie(
        this ControllerBase controller, string rootDomain, IWebHostEnvironment env)
    {
        controller.Response.Cookies.Delete("onevo_last_tenant", new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Domain = "." + rootDomain,
            Path = "/"
        });
    }
```

- [ ] **Step 4: Call `SetLastTenantHintCookie` from `SignInAsync`**

In the same file, `SignInAsync` currently ends with:

```csharp
        controller.Response.Cookies.Append("onevo_csrf", dto.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = dto.ExpiresAt
        });
    }
```

Change it to:

```csharp
        controller.Response.Cookies.Append("onevo_csrf", dto.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = dto.ExpiresAt
        });

        var configuration = controller.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var tenantContext = controller.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var rootDomain = configuration["Tenancy:RootDomain"];
        if (!string.IsNullOrEmpty(rootDomain) && !string.IsNullOrEmpty(tenantContext.Slug))
            controller.SetLastTenantHintCookie(tenantContext.Slug, rootDomain, env);
    }
```

(Resolved via `HttpContext.RequestServices` rather than adding parameters to `SignInAsync`/`HandleSessionResultAsync`, because `HandleSessionResultAsync` has 7 call sites across 6 controllers — `AuthSessionController`, `AuthPendingLegalController`, `AuthPasswordController`, `AuthLoginController` (×3), `AuthMfaController`, `AuthInvitationController` — none of which need to know about this new cookie. `IConfiguration` and `ITenantContext` are both already DI-registered.)

- [ ] **Step 5: Run the test to verify it passes**

```bash
cd HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingReturnsTenantSessionExchange_ThenExchangeIssuesSessionAndCsrfCookies"
```
Expected: PASS.

- [ ] **Step 6: Run the full integration Auth folder to check for regressions**

```bash
cd HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ONEVO.Tests.Integration.Auth"
```
Expected: all pass (this exercises every other login-flow test in the same file/folder — none of them assert an *absence* of extra `Set-Cookie` headers in a way this new cookie could break, but confirm no regressions).

- [ ] **Step 7: Commit**

```bash
cd HRMS-Backend-v1
git add src/ONEVO.Api/Controllers/Tenant/Auth/TenantAuthResponseWriter.cs tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs
git commit -m "$(cat <<'EOF'
Set onevo_last_tenant hint cookie on tenant-host login

Lets the root landing page later offer a "Go to Workspace" shortcut
back into this tenant. Parent-domain-scoped, non-HttpOnly, carries no
authority - never read by any auth/session code.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Backend — clear `onevo_last_tenant` on logout

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs:1-85`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs` (new test method)

**Interfaces:**
- Consumes: `SetLastTenantHintCookie`/`ClearLastTenantHintCookie` from Task 1; `SeedActiveUserAsync`, `PostLoginAsync`, `CompleteLegalAcceptanceAsync`, `CompleteTenantSessionExchangeAsync`, `ExtractCookieValue` (all existing private helpers already in `BaseDomainLoginIntegrationTests.cs`).
- Produces: nothing new consumed by later tasks.

- [ ] **Step 1: Write the failing integration test**

Add this new test method to `BaseDomainLoginIntegrationTests.cs`, placed after the existing `ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingReturnsTenantSessionExchange_ThenExchangeIssuesSessionAndCsrfCookies` test (i.e. after its closing `}` at line 128):

```csharp
    [Fact]
    public async Task ExactOneMatch_LogsIn_ThenLogout_ClearsLastTenantHintCookie()
    {
        var user = await SeedActiveUserAsync(
            "logout-hint-tenant", "logouthint@test.onevo.dev", "CorrectPass1!");

        var response = await PostLoginAsync(user.Email, "CorrectPass1!");
        var legalCompleted = await CompleteLegalAcceptanceAsync(response);
        var exchanged = await CompleteTenantSessionExchangeAsync(legalCompleted);

        exchanged.StatusCode.Should().Be(HttpStatusCode.OK, await exchanged.Content.ReadAsStringAsync());
        var exchangeCookies = exchanged.Headers.TryGetValues("Set-Cookie", out var exchangeCookieValues)
            ? exchangeCookieValues.ToList()
            : new List<string>();
        exchangeCookies.Should().Contain(c =>
            c.StartsWith("onevo_last_tenant=logout-hint-tenant", StringComparison.Ordinal) &&
            c.Contains("domain=.localhost", StringComparison.OrdinalIgnoreCase));

        var sessionCookie = ExtractCookieValue(exchanged, "onevo_session");
        var csrfCookie = ExtractCookieValue(exchanged, "onevo_csrf");

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Host = "logout-hint-tenant.localhost";
        logoutRequest.Headers.Add("Cookie", $"onevo_session={sessionCookie}; onevo_csrf={csrfCookie}");
        logoutRequest.Headers.Add("X-CSRF-Token", csrfCookie);
        var logout = await _client.SendAsync(logoutRequest);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var logoutCookies = logout.Headers.TryGetValues("Set-Cookie", out var logoutCookieValues)
            ? logoutCookieValues.ToList()
            : new List<string>();
        logoutCookies.Should().Contain(c =>
            c.StartsWith("onevo_last_tenant=", StringComparison.Ordinal) &&
            c.Contains("domain=.localhost", StringComparison.OrdinalIgnoreCase));
        ExtractCookieValue(logout, "onevo_last_tenant").Should().BeEmpty();
    }
```

(`/api/v1/auth/logout` requires the `X-CSRF-Token` header to match the session's stored `csrf_token_hash` — see `CsrfProtectionMiddleware.cs:93-108`; using the `onevo_csrf` cookie value as that header, matching the existing `CompleteLegalAcceptanceAsync`/legal-acceptance tests' pattern in this same file.)

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ExactOneMatch_LogsIn_ThenLogout_ClearsLastTenantHintCookie"
```
Expected: FAIL — `logoutCookies` does not contain an `onevo_last_tenant=` entry (Logout doesn't clear it yet).

- [ ] **Step 3: Wire `ClearLastTenantHintCookie` into `Logout`**

In `AuthSessionController.cs`, add an `IConfiguration` field and constructor parameter. Current constructor (lines 22-32):

```csharp
    public AuthSessionController(
        IMediator mediator,
        IWebHostEnvironment env,
        ITenantContext tenantContext,
        ITenantSessionExchangeService tenantSessionExchange)
    {
        _mediator = mediator;
        _env = env;
        _tenantContext = tenantContext;
        _tenantSessionExchange = tenantSessionExchange;
    }
```

Change to:

```csharp
    public AuthSessionController(
        IMediator mediator,
        IWebHostEnvironment env,
        ITenantContext tenantContext,
        ITenantSessionExchangeService tenantSessionExchange,
        IConfiguration configuration)
    {
        _mediator = mediator;
        _env = env;
        _tenantContext = tenantContext;
        _tenantSessionExchange = tenantSessionExchange;
        _configuration = configuration;
    }
```

Add the field next to the other private fields (lines 17-20):

```csharp
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSessionExchangeService _tenantSessionExchange;
    private readonly IConfiguration _configuration;
```

Add `using Microsoft.Extensions.Configuration;` to the file's usings.

Change the `Logout` method (currently lines 76-84):

```csharp
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("TenantScheme");
        this.DeleteTenantCookie("onevo_csrf", httpOnly: false, _env);
        this.DeleteTenantCookie("onevo_mfa", httpOnly: true, _env, path: "/api/v1/auth/mfa/verify");
        this.DeleteTenantCookie("onevo_legal_pending", httpOnly: true, _env, path: "/api/v1/legal/acceptances/complete-login");
        this.DeleteTenantCookie("onevo_legal_csrf", httpOnly: false, _env);
        return NoContent();
    }
```

to:

```csharp
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await HttpContext.SignOutAsync("TenantScheme");
        this.DeleteTenantCookie("onevo_csrf", httpOnly: false, _env);
        this.DeleteTenantCookie("onevo_mfa", httpOnly: true, _env, path: "/api/v1/auth/mfa/verify");
        this.DeleteTenantCookie("onevo_legal_pending", httpOnly: true, _env, path: "/api/v1/legal/acceptances/complete-login");
        this.DeleteTenantCookie("onevo_legal_csrf", httpOnly: false, _env);
        var rootDomain = _configuration["Tenancy:RootDomain"];
        if (!string.IsNullOrEmpty(rootDomain))
            this.ClearLastTenantHintCookie(rootDomain, _env);
        return NoContent();
    }
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ExactOneMatch_LogsIn_ThenLogout_ClearsLastTenantHintCookie"
```
Expected: PASS.

- [ ] **Step 5: Run the full unit + integration Auth suites to check for regressions**

```bash
cd HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Auth"
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~ONEVO.Tests.Integration.Auth"
```
Expected: all pass. (The constructor signature change to `AuthSessionController` is source-compatible for ASP.NET Core's controller activation — controllers are instantiated by the framework's DI container, not `new`'d directly anywhere in tests, so no other call sites need updating. Confirm this by grepping for `new AuthSessionController(` — expect zero matches outside the file itself.)

- [ ] **Step 6: Commit**

```bash
cd HRMS-Backend-v1
git add src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs
git commit -m "$(cat <<'EOF'
Clear onevo_last_tenant hint cookie on logout

Prevents the landing page's "Go to Workspace" shortcut from pointing
at a tenant the visitor explicitly signed out of.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Frontend — landing header shows "Go to Workspace" when the hint cookie is present

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/public/ui/landing-header/landing-header.component.ts:1-213`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/public/ui/landing-header/landing-header.component.html:52,83`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/public/ui/landing-header/landing-header.component.spec.ts`

**Interfaces:**
- Consumes: `buildTenantUrl(slug: string, path: string): string` from `core/auth/utils/tenant-redirect.ts` (existing, unchanged).
- Produces: `LandingHeaderComponent.workspaceUrl: Signal<string | null>` — not consumed elsewhere in this plan, but is the component's new public surface.

- [ ] **Step 1: Write the failing component tests**

In `landing-header.component.spec.ts`, add `import { DOCUMENT } from '@angular/common';` after the existing `import { By } from '@angular/platform-browser';` line. Change the `createComponent` helper and the `afterEach` (currently):

```ts
describe('LandingHeaderComponent', () => {
  async function createComponent() {
    await TestBed.configureTestingModule({
      imports: [LandingHeaderComponent],
      providers: [provideRouter([])]
    }).compileComponents();
    const fixture = TestBed.createComponent(LandingHeaderComponent);
    setScrollY(0);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  afterEach(() => {
    setScrollY(0);
  });
```

to:

```ts
describe('LandingHeaderComponent', () => {
  let document: Document;

  // The hint cookie is read once, in the component's constructor, so it must be set on the
  // TestBed-injected Document *before* TestBed.createComponent runs - hence the optional param
  // here rather than setting document.cookie after the fixture already exists.
  async function createComponent(lastTenantCookie?: string) {
    await TestBed.configureTestingModule({
      imports: [LandingHeaderComponent],
      providers: [provideRouter([])]
    }).compileComponents();
    document = TestBed.inject(DOCUMENT);
    if (lastTenantCookie !== undefined) {
      document.cookie = `onevo_last_tenant=${lastTenantCookie}`;
    }
    const fixture = TestBed.createComponent(LandingHeaderComponent);
    setScrollY(0);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  afterEach(() => {
    setScrollY(0);
    document.cookie = 'onevo_last_tenant=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });
```

Then add these three new test cases, placed after the existing `'points the sign-in CTA at the existing login route'` test (currently lines 43-47):

```ts
  it('shows "Go to Workspace" linking to the remembered tenant when onevo_last_tenant is set', async () => {
    const fixture = await createComponent('acme');

    const signIn = fixture.debugElement.query(By.css('.landing-header__signin')).nativeElement as HTMLAnchorElement;
    expect(signIn.textContent?.trim()).toBe('Go to Workspace');
    expect(signIn.getAttribute('href')).toContain('acme');
    expect(signIn.getAttribute('href')).toContain('/dashboard');
  });

  it('still shows Login when onevo_last_tenant is absent', async () => {
    const fixture = await createComponent();

    const signIn = fixture.debugElement.query(By.css('.landing-header__signin')).nativeElement as HTMLAnchorElement;
    expect(signIn.textContent?.trim()).toBe('Login');
  });

  it('still shows Login when onevo_last_tenant is present but empty', async () => {
    const fixture = await createComponent('');

    const signIn = fixture.debugElement.query(By.css('.landing-header__signin')).nativeElement as HTMLAnchorElement;
    expect(signIn.textContent?.trim()).toBe('Login');
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd Hrms--Web-application---front-end---v1
npx ng test --watch=false --include="**/landing-header.component.spec.ts"
```
Expected: the `'points the sign-in CTA...'` and other pre-existing tests still pass; the three new tests FAIL — `'Go to Workspace'` test fails because the text is still `'Login'`; the two `'still shows Login'` tests currently pass trivially (no code change needed for them yet) since that's already the default behavior — that's fine, they act as regression pins for Step 3's implementation.

- [ ] **Step 3: Add the cookie-read helper and `workspaceUrl` signal**

In `landing-header.component.ts`, change the imports (currently lines 1-15):

```ts
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  PLATFORM_ID,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgOptimizedImage, isPlatformBrowser } from '@angular/common';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
```

to:

```ts
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  PLATFORM_ID,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DOCUMENT, NgOptimizedImage, isPlatformBrowser } from '@angular/common';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { buildTenantUrl } from '../../../../core/auth/utils/tenant-redirect';

const LAST_TENANT_COOKIE = 'onevo_last_tenant';

function readLastTenantSlug(document: Document): string | null {
  const cookies = document.cookie.split(';').map((c) => c.trim());
  const match = cookies.find((c) => c.startsWith(`${LAST_TENANT_COOKIE}=`));
  if (!match) return null;
  const value = decodeURIComponent(match.slice(LAST_TENANT_COOKIE.length + 1));
  return value.length > 0 ? value : null;
}
```

(`readLastTenantSlug` mirrors the existing `readCsrfToken` helper in `core/interceptors/csrf.interceptor.ts:9-18` — same cookie-parsing idiom already established in this codebase.)

Add `private readonly document = inject(DOCUMENT);` to the existing field-injection block (after `private readonly router = inject(Router);`, currently line 41):

```ts
  private readonly platformId = inject(PLATFORM_ID);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
```

Add the new signal to the existing signal block (after `readonly isScrolled = signal(false);`, currently line 46):

```ts
  readonly menuOpen = signal(false);
  readonly headerVisible = signal(true);
  readonly isDarkMode = signal(false);
  readonly isScrolled = signal(false);
  readonly workspaceUrl = signal<string | null>(null);
```

Change the constructor (currently lines 55-62):

```ts
  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed()
      )
      .subscribe(() => this.reset());
  }
```

to:

```ts
  constructor() {
    const lastTenantSlug = readLastTenantSlug(this.document);
    this.workspaceUrl.set(lastTenantSlug ? buildTenantUrl(lastTenantSlug, '/dashboard') : null);

    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed()
      )
      .subscribe(() => this.reset());
  }
```

- [ ] **Step 4: Update the template**

In `landing-header.component.html`, change the desktop sign-in link (currently line 52):

```html
      <a routerLink="/auth/login" class="landing-header__signin">Login</a>
```

to:

```html
      @if (workspaceUrl(); as url) {
        <a [href]="url" class="landing-header__signin">Go to Workspace</a>
      } @else {
        <a routerLink="/auth/login" class="landing-header__signin">Login</a>
      }
```

Change the mobile sign-in link (currently line 83):

```html
      <a routerLink="/auth/login" class="landing-header__signin landing-header__signin--mobile" (click)="closeMenu()">Sign in</a>
```

to:

```html
      @if (workspaceUrl(); as url) {
        <a [href]="url" class="landing-header__signin landing-header__signin--mobile" (click)="closeMenu()">Go to Workspace</a>
      } @else {
        <a routerLink="/auth/login" class="landing-header__signin landing-header__signin--mobile" (click)="closeMenu()">Sign in</a>
      }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd Hrms--Web-application---front-end---v1
npx ng test --watch=false --include="**/landing-header.component.spec.ts"
```
Expected: all tests PASS, including the three new ones and all pre-existing ones (in particular `'points the sign-in CTA at the existing login route'`, which exercises the no-cookie default path).

- [ ] **Step 6: Run the full frontend test suite to check for regressions**

```bash
cd Hrms--Web-application---front-end---v1
npx ng test --watch=false
```
Expected: all pass, same total count as before plus the 3 new tests.

- [ ] **Step 7: Verify `Domain=.localhost` cookie sharing in a real browser (manual, dev environment)**

This step cannot be automated by a unit test — it proves the browser actually honors the parent-domain cookie across `dapi.localhost:4200` and `localhost:4200`, which the spec's "Implementation note" flags as needing empirical confirmation.

1. Run both the backend (`HRMS-Backend-v1`) and frontend (`Hrms--Web-application---front-end---v1`) dev servers locally.
2. In a browser, log in on a tenant subdomain (e.g. `dapi.localhost:4200`).
3. Open browser devtools → Application/Storage → Cookies → check `localhost` (the root, not the subdomain) for an `onevo_last_tenant` cookie. Confirm it's present with the correct tenant slug value.
4. Navigate to `localhost:4200` (the root landing page) in the same browser. Confirm the header shows "Go to Workspace" instead of "Login", and that clicking it navigates to `dapi.localhost:4200/dashboard` and lands there without a login prompt (since the tenant session is still valid).
5. Log out from the tenant app, then revisit `localhost:4200`. Confirm the header now shows "Login" again (cookie was cleared).

If step 3 shows no cookie at all, or a cookie scoped only to `dapi.localhost` rather than `localhost`, `Domain=.localhost` is not being honored as expected in this environment — stop and report back rather than proceeding, since this would mean the entire approach needs re-examination for the dev environment (production's real domain, e.g. `.onevo.com`, is a normal multi-label domain and won't have this specific `.localhost` edge case, but this must still be confirmed working in dev to develop against).

- [ ] **Step 8: Commit**

```bash
cd Hrms--Web-application---front-end---v1
git add src/app/modules/public/ui/landing-header/landing-header.component.ts src/app/modules/public/ui/landing-header/landing-header.component.html src/app/modules/public/ui/landing-header/landing-header.component.spec.ts
git commit -m "$(cat <<'EOF'
Show "Go to Workspace" shortcut when a tenant session hint exists

Reads the onevo_last_tenant hint cookie (set by tenant-host login,
cleared on logout) and swaps the header's Login link for a direct
shortcut back into that tenant, matching the Jira "Go to Jira" UX.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```
