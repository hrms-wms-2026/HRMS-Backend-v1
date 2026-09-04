# Landing Page "Go to Workspace" Shortcut

Status: Approved for implementation
Date: 2026-09-04
Repos touched: `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`

## Problem

The root marketing/landing page (served on the root frontend host, no tenant
subdomain) always shows a "Login" button in its header, even for a visitor
whose browser already holds a valid, active session on a tenant subdomain
(e.g. `dapi.localhost:4200`). There is no equivalent of the "Go to Jira"
shortcut Atlassian's central account portal shows once you're already
signed in somewhere.

This is expected given the current architecture — confirmed by reading
`TenantAuthResponseWriter.cs` and `AuthenticationExtensions.cs` — session
cookies (`onevo_session`, `onevo_csrf`) are deliberately host-scoped with no
`Domain` attribute, so nothing set on a tenant subdomain is visible to the
root host, or vice versa. The root landing page has no way today to know a
tenant session exists anywhere.

## Goals

- After a user logs in on a tenant host, the landing page's header should
  offer a one-click "Go to Workspace" shortcut back into that tenant instead
  of (or in addition to) "Login" — matching the Jira reference UX.
- No change to how the actual `onevo_session` authentication cookie works.
  Zero new trust decisions are made based on this feature.

## Non-goals

- Real cross-origin session verification (the landing page confirming a
  session is *actually* still valid before showing the button). Rejected in
  favor of the lighter option below — see "Approach" for the trade-off this
  accepts.
- Remembering more than one tenant per browser (no Jira-style multi-site
  picker). Single most-recently-used tenant only.
- Any change to logout-invalidation behavior, MFA, legal-acceptance, or the
  existing base-host → tenant-host session-exchange flow.

## Approach

A lightweight, non-authoritative "hint" cookie, set on the parent domain
(e.g. `Domain=.onevo.com` in production, `Domain=.localhost` in dev — reusing
the existing `Tenancy:RootDomain` config value `HostTenantResolutionMiddleware`
already uses), carrying only a tenant slug. It is deliberately **not** the
session cookie and **not** `HttpOnly`, so:

- It grants no access by itself. If tampered with, the worst case is the
  visitor gets navigated to the wrong tenant's own login page — the same
  outcome as clicking a stale bookmark.
- The landing page can read it via `document.cookie` and use it purely to
  decide which button/link to render and where it points.
- Clicking the resulting link is a plain top-level navigation to that
  tenant's `/dashboard`. If the real session there is still valid, the
  tenant app's existing `authGuard` lets the visitor straight in (already
  correct, verified in the prior "new tab asks me to log in" fix). If it
  expired, they land on that subdomain's own login form — never worse than
  today's behavior, and no new session/security surface introduced there.

This was chosen over real cross-origin session verification because it
avoids touching the actual session/cookie security model entirely (no new
lookup endpoint, no relaxing of `SameSite`/`Domain` on the authoritative
`onevo_session` cookie), at the accepted cost of the button being a *hint*
rather than a guarantee — an expired session behind a stale hint just means
one extra login screen, not a security gap.

## Design

### Backend (`HRMS-Backend-v1`)

**New cookie helpers** in `TenantAuthResponseWriter.cs`, following the
existing `SetMfaChallengeCookie` / `DeleteTenantCookie` style:

```
SetLastTenantHintCookie(controller, tenantSlug, rootDomain, env)
  -> Response.Cookies.Append("onevo_last_tenant", tenantSlug, new CookieOptions {
       HttpOnly = false,
       Secure = !env.IsDevelopment(),
       SameSite = SameSiteMode.Lax,
       Domain = "." + rootDomain,
       Path = "/",
       Expires = DateTimeOffset.UtcNow.AddDays(180)
     })

ClearLastTenantHintCookie(controller, rootDomain, env)
  -> Response.Cookies.Delete("onevo_last_tenant", new CookieOptions {
       HttpOnly = false, Secure = !env.IsDevelopment(),
       SameSite = SameSiteMode.Lax, Domain = "." + rootDomain, Path = "/"
     })
```

`rootDomain` comes from the same `Tenancy:RootDomain` configuration value
`HostTenantResolutionMiddleware` already reads — threaded into
`TenantAuthResponseWriter`'s static methods as a parameter, the same way
`IWebHostEnvironment env` already is.

**Call sites:**

- `TenantAuthResponseWriter.SignInAsync` (existing method, ~line 50-60):
  immediately after the existing `onevo_csrf` cookie is appended, call
  `SetLastTenantHintCookie` using `ITenantContext.Slug` — already resolved
  and DI-available on every tenant-host request via
  `HostTenantResolutionMiddleware`, so no new tenant-resolution logic is
  needed.
- `AuthSessionController.Logout` (existing method, ~line 76-84): call
  `ClearLastTenantHintCookie` alongside the controller's existing
  `DeleteTenantCookie` calls for `onevo_csrf`/`onevo_mfa`/`onevo_legal_*`.

No other write points. Per the earlier decision, the hint is set only at
login — not refreshed on every authenticated page load.

### Frontend (`Hrms--Web-application---front-end---v1`)

**`landing-header.component.ts`**: on init, read `document.cookie` for
`onevo_last_tenant`. If present and non-empty, expose a signal/computed
(e.g. `workspaceUrl(): string | null`) built via the **already-existing**
`buildTenantUrl(slug, '/dashboard')` helper in
`core/auth/utils/tenant-redirect.ts` — no new URL-construction logic.

**`landing-header.component.html`**: both the desktop nav link (currently
`<a routerLink="/auth/login" class="landing-header__signin">Login</a>`,
line 52) and the mobile one (line 83) conditionally render:

- `workspaceUrl()` present → `<a [href]="workspaceUrl()" class="landing-header__signin">Go to Workspace</a>`
- otherwise → the existing `Login` link, unchanged.

Plain `href` navigation (not `routerLink`), since the target is a different
origin (the tenant subdomain).

## Error handling

- Malformed/unexpected cookie value (e.g. doesn't match the tenant-slug
  pattern): treat as absent — fall back to showing "Login". No parsing
  errors should be possible from a plain string read of `document.cookie`.
- Cookie present but that tenant no longer exists, or the visitor's session
  there is invalid/expired: the linked tenant subdomain handles this itself
  via its own existing login flow. The landing page makes no assumption
  about validity.

## Testing

**Backend** (`ONEVO.Tests.Unit`):
- `SignInAsync` sets `onevo_last_tenant` with the resolved tenant slug and
  the configured root domain as `Domain`.
- `Logout` clears `onevo_last_tenant` (same `Domain`).
- Cookie is not set on any of the early-return paths in
  `HandleSessionResultAsync` (password-change/MFA/legal/tenant-session-exchange
  pending states) — only on an actual completed sign-in.

**Frontend** (Vitest):
- `landing-header.component`: renders "Go to Workspace" linking to the
  correct tenant URL when the cookie is present; renders "Login" when it's
  absent or empty, for both the desktop and mobile markup.

## Implementation note

`Domain=.localhost` cookie-sharing behavior has had inconsistent history
across browsers for single-label/TLD-like hosts. `localhost` itself is not
on the Public Suffix List, so modern Chrome/Firefox should honor it, but
this must be confirmed empirically (real browser, dev environment) during
implementation, not just asserted from unit tests — unit tests only prove
the backend sets the header correctly, not that the browser actually shares
the cookie across `dapi.localhost:4200` and `localhost:4200`.

## Security review notes

- `onevo_last_tenant` is never read by any backend authorization/session
  code — grep-verified only the two new call sites above write it, and no
  read site is added anywhere in this design.
- Not `HttpOnly` is intentional and safe here specifically because the
  cookie carries no authority; this must not be used as a precedent for any
  cookie that does.
- `SameSite=Lax` (not `Strict`) is used only because this cookie is read via
  `document.cookie`, not attached to cross-site requests — the exact
  `SameSite` value has no security effect for this cookie's actual use, kept
  as `Lax` for consistency with normal top-level-navigation cookies rather
  than `Strict`, which is reserved here for the authoritative session/CSRF
  cookies.
