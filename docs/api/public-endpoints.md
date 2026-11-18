# Public endpoints

Every endpoint in the API requires a signed-in caller unless it opts out. This page lists every
endpoint that opts out, and how each one protects itself.

## How deny by default works

- `AuthorizationExtensions` sets a **fallback authorization policy**: an endpoint with no
  `[Authorize]`, no named policy and no `[AllowAnonymous]` requires an authenticated caller.
- The fallback authenticates with `FallbackScheme` (`src/ONEVO.Api/Auth/FallbackAuthenticationScheme.cs`),
  a policy scheme that forwards to the one scheme matching the request:
  - `Authorization: Bearer ...`, or `access_token` on `/hubs/agent-commands`, goes to `TrayDeviceScheme`.
  - A path under `/admin` goes to `AdminScheme`.
  - Anything else goes to `TenantScheme`.

  So a tray or admin endpoint is never refused just because the fallback only looked at the
  tenant cookie, and only one scheme writes the 401 body.
- The fallback only requires *some* authenticated caller. Endpoints that need a particular kind
  of caller still need their own policy (`TenantPolicy`, `AdminPolicy`, `AdminMfaEnrollmentPolicy`,
  `TrayDevicePolicy`) or `[RequirePermission]` / `[RequirePlatformPermission]`.
- A request that matches no route is refused with 401 for an anonymous caller (404 once signed in).
- `ControllerAuthorizationCoverageArchitectureTests` (in `ONEVO.Tests.Architecture`) fails when:
  - a controller action has no authorization attribute after class and method attributes are combined, or
  - the set of `[AllowAnonymous]` actions differs from the reviewed list below.

  Adding a public endpoint therefore means updating this page and the test's `ReviewedAnonymousActions` together.

## Rate limits referred to below

| Name | What it is |
|---|---|
| **Auth limiter** | `AuthRateLimitingMiddleware`: per-route POST limits by IP and by a second key (email, token, challenge cookie or path). |
| **General per-IP limit** | Today, `PublicApiRateLimitingMiddleware`: 300 requests per minute per IP on `/api/v1`, 120 on `/admin/v1`. After task 07 (PR #237) merges, this is the pre-auth per-IP guard plus the anonymous by-IP partition of the built-in limiter (`docs/deployment/rate-limits.md`). |

Both limiters only cover `/api/v1` and `/admin/v1`. Paths outside them (health, hubs, Stripe webhooks) are not rate limited by the app.

## Controller actions marked `[AllowAnonymous]` (32)

### Tenant sign-in (`/api/v1/auth`)

| Endpoint | Protection | Rate limit | Unknown vs known account |
|---|---|---|---|
| `POST /api/v1/auth/login` | Base host only. Password check gives a generic failure. | Auth limiter: 20 per 5 min per IP, 5 per 5 min per email | Same generic 401 |
| `POST /api/v1/auth/login/select-workspace` | Needs the short-lived `login_challenge` issued by a proven login. | Auth limiter: IP + challenge | Not applicable (needs a challenge) |
| `POST /api/v1/auth/login/google` | Base host only. Google ID token is verified server-side. | Auth limiter: IP + token | Same generic 401 |
| `POST /api/v1/auth/mfa/verify` | Needs the `crownus_mfa` challenge cookie and a TOTP code. | Auth limiter: IP + challenge cookie | Not applicable |
| `POST /api/v1/auth/forgot-password` | Always returns the same 200 message. | Auth limiter: 10 per 15 min per IP, 3 per 15 min per email | Same 200 |
| `POST /api/v1/auth/reset-password` | Single-use reset token, stored hashed. | Auth limiter: IP + token | Not applicable (token only) |
| `POST /api/v1/auth/force-change-password` | Tenant host only. Needs the current temporary password; generic "Invalid credentials." | Auth limiter: IP + email | Same 401 (see finding 3) |
| `POST /api/v1/auth/session-exchange` | Single-use exchange code from a completed base-host login, bound to the tenant resolved from the host. | Auth limiter: 20 per 5 min per IP (added in task 15) | Not applicable |
| `POST /api/v1/legal/acceptances/complete-login` | Needs the `crownus_legal_pending` challenge cookie and a matching CSRF token. | Auth limiter: 20 per 5 min per IP (added in task 15) | Not applicable |

### Tenant invitations (`/api/v1/auth/invitations/{token}`)

| Endpoint | Protection | Rate limit |
|---|---|---|
| `GET  .../invitations/{token}` | Random invitation token from the email link. Shows the invitation to the token holder only. | General per-IP limit (the auth limiter covers POST only) |
| `POST .../accept-password` | Single-use invitation token, checked against the tenant host. | Auth limiter: IP + path |
| `POST .../accept-employee` | Single-use invitation token, checked against the tenant host. | Auth limiter: IP + path |
| `POST .../accept-google` | Invitation token and a verified Google ID token. | Auth limiter: IP + path |

### Legal documents

| Endpoint | Protection | Rate limit |
|---|---|---|
| `GET /api/v1/legal/documents/current` | Published legal text only. The table has no tenant data and no RLS. | General per-IP limit |
| `GET /api/v1/legal/documents/{documentType}/{version}` | Same. | General per-IP limit |

### Calendar

| Endpoint | Protection | Rate limit |
|---|---|---|
| `GET /api/v1/calendar/connections/{provider}/callback` | OAuth `state` is data-protected (signed, encrypted, expiring) and must match the provider. Without a valid state it returns a generic 400. | General per-IP limit |

### Tray app

| Endpoint | Protection | Rate limit |
|---|---|---|
| `POST /api/v1/monitoring/activation/exchange` | Single-use, short-lived activation code (8 characters, 40 bits), stored hashed. | General per-IP limit (see finding 2) |
| `POST /api/v1/monitoring/activation/refresh` | Refresh token stored hashed and bound to the device fingerprint. | General per-IP limit |
| `POST /api/v1/monitoring/device-authorization/start` | Creates a pending request only. A signed-in user must approve it (`approve` needs `TenantPolicy`). | General per-IP limit |
| `POST /api/v1/monitoring/device-authorization/token` | Random device code and fingerprint. The server answers `slow_down` to fast polling. | General per-IP limit |
| `GET  /api/v1/tray/releases/latest` | Public installer metadata. No tenant data. The client checks the sha256. | General per-IP limit |
| `GET  /api/v1/tray/releases/check` | Same. | General per-IP limit |

### Admin console (`/admin/v1/auth`)

| Endpoint | Protection | Rate limit | Unknown vs known account |
|---|---|---|---|
| `GET  /admin/v1/auth/google-config` | Returns only whether Google SSO is on and the public client id. | General per-IP limit | Not applicable |
| `POST /admin/v1/auth/google-callback` | Google ID token verified server-side. A Google subject or verified email must map to a platform user. | Auth limiter: IP + token | Same 401 for a bad token |
| `POST /admin/v1/auth/login` | Password with lockout. A locked, missing or wrong account all return the same "Invalid email or password." | Auth limiter: IP + email | Same 401 (see finding 3) |
| `POST /admin/v1/auth/forgot-password` | Always returns the same 200 message. | Auth limiter: IP + email | Same 200 |
| `POST /admin/v1/auth/reset-password` | Single-use reset token, stored hashed. | Auth limiter: IP + token | Not applicable |
| `POST /admin/v1/auth/accept-invite` | Single-use invite token, stored hashed. | Auth limiter: IP + token | Not applicable (token only) |
| `POST /admin/v1/auth/mfa/verify` | Needs the `admin_mfa` challenge cookie and a TOTP code. | Auth limiter: IP + challenge cookie | Not applicable |

### Machine-to-machine

| Endpoint | Protection | Rate limit |
|---|---|---|
| `POST /admin/v1/tray-releases/ingest` | `X-Release-Token` compared in constant time (SHA-256 + `FixedTimeEquals`). Returns 404 when no token is configured. Never activates a build. | General per-IP limit |
| `POST /api/payment-gateways/stripe/{gatewayKey}/webhook` | `Stripe-Signature` checked against the gateway's webhook secret with `EventUtility.ConstructEvent` **before** the event is used (see "Stripe webhook" below). | **None** (see finding 1) |
| `POST /api/payment-gateways/stripe/{gatewayConfigId:guid}/webhook` | Same. | **None** (see finding 1) |

## Other endpoints outside controllers

| Endpoint | Behaviour under the fallback |
|---|---|
| `GET /health`, `GET /health/ready` | Explicitly `.AllowAnonymous()`. They return only `Healthy` / `Unhealthy`. |
| `/hubs/inbox` | `.RequireAuthorization("TenantPolicy")` on the endpoint, and `[Authorize]` on the hub. Browsers connect with the tenant cookie. |
| `/hubs/agent-commands` | `.RequireAuthorization("TrayDevicePolicy")` on the endpoint, and the same policy on the hub. The tray sends its JWT as `access_token`. |
| `/swagger/...` | Swagger is middleware that answers before routing and authorization, so the fallback never sees it. It is only on in Development or when `Swagger:Enabled=true`. Keep it off in production. |

## Stripe webhook

`StripeWebhookController.ProcessWebhookWithSecretAsync` reads the raw body as a string, then calls
`EventUtility.ConstructEvent(payload, signature, secret)`. That call checks the HMAC signature and
the timestamp tolerance (default 300 seconds) and throws on mismatch, and the controller returns
400. Nothing reads the event data (`ExtractCommand`, `ProcessStripeEventCommand`) until the
signature has passed. The only work before the signature check is resolving the webhook secret
for the gateway key or config id, which is one database lookup.

## Open findings (reported, not fixed in task 15)

1. **Stripe webhooks are not rate limited.** `/api/payment-gateways/...` is outside `/api/v1`, so
   neither today's limiter nor task 07's covers it. Each request costs a secret lookup and an HMAC
   check before it is refused. Suggested fix: add the path to task 07's pre-auth per-IP guard, or
   move the routes under `/api/v1`. This needs a Stripe dashboard change, so it was left to the owner.
2. **Tray activation-code exchange has only the general per-IP limit.** The code has 40 bits and a
   short lifetime, so online guessing is impractical. But it is a credential exchange with no
   dedicated limit. Per-IP limits here can block offices where many trays share one NAT address,
   so the limit size is a product decision.
3. **Small timing differences in login.** `AdminLoginCommandHandler` and
   `ForcePasswordChangeCommandHandler` return early without hashing when the email is unknown. The
   response body is identical, but response time differs. Suggested fix: verify against a dummy
   hash on the unknown-account path.
4. **Stripe webhook error text tells callers whether a gateway is configured.** It returns
   "Stripe webhook is not configured." versus "Invalid Stripe signature.". Unit tests pin both
   messages. Low impact: gateway keys are not secret, and nothing is processed without a valid signature.
