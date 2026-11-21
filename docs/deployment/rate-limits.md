# API rate limits

The API has three rate limiters, in this order.

1. **Pre-authentication IP guard** (task 07): `PreAuthenticationRateLimitingMiddleware`, right
   after `UseRouting`. It applies a coarse per-IP ceiling to all `/api/v1` and `/admin/v1` traffic
   before any cookie or token is validated, so a flood of forged credentials is cut without
   costing any authentication work. Its limit is a setting, `PreAuthentication`.
2. **Global limiter** (task 07): ASP.NET Core `AddRateLimiter`, set up in
   `src/ONEVO.Api/RateLimiting/ApiRateLimitingExtensions.cs`. It runs right after
   `UseAuthentication`, so signed-in requests are counted per user, per tray device and per
   tenant, not per IP. All of its limits are settings and can be tuned without a release.
3. **Auth endpoint limiter**: `AuthRateLimitingMiddleware`. It has strict, fixed limits on login, MFA,
   password reset, invitations and a few tray endpoints. It runs between the IP guard and the
   global limiter. Its
   limits are in code, not in settings, and are listed at the end of this page.

Both limiters keep counters in process memory. That is correct only while the API runs as one
instance. Task 21 moves the counters to a shared store before horizontal scaling.

## Which rule applies

| Request | Rule (setting) | Partitioned by | Algorithm |
|---|---|---|---|
| `/api/v1/...`, not signed in | `Anonymous` | client IP | sliding window |
| `/api/v1/...`, tenant user session cookie | `SignedIn`, then `TenantCeiling` | user (and tenant) | token bucket |
| `/api/v1/...`, tray device bearer token | `TrayDevice`, then `TenantCeiling` | device registration id | token bucket |
| `/admin/v1/...`, not signed in as a platform admin | `AdminAnonymous` | client IP | sliding window |
| `/admin/v1/...`, platform admin session | `AdminSignedIn` | admin user | token bucket |
| `/health`, `/health/ready`, SignalR hubs, anything outside `/api/v1` and `/admin/v1` | none (exempt) | | |

`TenantCeiling` is chained. A tenant user or tray device request must pass its own rule **and**
the tenant's ceiling. The ceiling is shared by every user and device of that tenant. It is a
safety net against one tenant swamping the instance, not a per-user limit.

The client IP is the one `UseForwardedHeaders` resolves, so behind a proxy it is the real client
only when `ForwardedHeaders` is configured for that proxy.

## Settings

Section `RateLimiting` in `appsettings.json`. Override any key per environment in the usual way:
an environment variable uses `__` for `:`, for example `RateLimiting__SignedIn__TokenLimit=150`.

| Setting key | Default | Meaning |
|---|---|---|
| `RateLimiting:Enabled` | `true` | Master switch for the global limiter. `false` lets everything through. The auth endpoint limiter still applies. |
| `RateLimiting:PreAuthentication:PermitLimit` | `6000` | All `/api/v1` and `/admin/v1` requests per IP in any rolling window, counted before authentication, signed in or not. Keep it well above an office's combined traffic. |
| `RateLimiting:PreAuthentication:WindowSeconds` | `60` | |
| `RateLimiting:PreAuthentication:SegmentsPerWindow` | `6` | |
| `RateLimiting:Anonymous:PermitLimit` | `300` | Requests allowed per IP in any rolling window. |
| `RateLimiting:Anonymous:WindowSeconds` | `60` | Window length. |
| `RateLimiting:Anonymous:SegmentsPerWindow` | `6` | Window slices (10 s each by default). More slices make the window smoother. |
| `RateLimiting:SignedIn:TokenLimit` | `100` | Burst: requests a user can make at once. A dashboard load is about 15. |
| `RateLimiting:SignedIn:TokensPerPeriod` | `10` | Tokens added back each period. |
| `RateLimiting:SignedIn:ReplenishmentPeriodSeconds` | `1` | Refill period. Sustained rate = 10 x 60 / 1 = **600 per minute per user**. |
| `RateLimiting:TrayDevice:TokenLimit` | `60` | Burst per tray device. |
| `RateLimiting:TrayDevice:TokensPerPeriod` | `5` | |
| `RateLimiting:TrayDevice:ReplenishmentPeriodSeconds` | `1` | Sustained **300 per minute per device**. |
| `RateLimiting:TenantCeiling:TokenLimit` | `3000` | Burst per tenant, across all users and devices. |
| `RateLimiting:TenantCeiling:TokensPerPeriod` | `200` | |
| `RateLimiting:TenantCeiling:ReplenishmentPeriodSeconds` | `1` | Sustained **12,000 per minute per tenant**. |
| `RateLimiting:AdminAnonymous:PermitLimit` | `120` | `/admin/v1` requests per IP in any rolling window, when not signed in. |
| `RateLimiting:AdminAnonymous:WindowSeconds` | `60` | |
| `RateLimiting:AdminAnonymous:SegmentsPerWindow` | `6` | |
| `RateLimiting:AdminSignedIn:TokenLimit` | `60` | Burst per platform admin. |
| `RateLimiting:AdminSignedIn:TokensPerPeriod` | `5` | |
| `RateLimiting:AdminSignedIn:ReplenishmentPeriodSeconds` | `1` | Sustained **300 per minute per admin**. |

Every number must be at least 1. If the values at startup are invalid, the API refuses to start.
If a reload has invalid values, the API logs an error and keeps the last good limits.

Token bucket in short: a full bucket holds `TokenLimit` tokens and each request takes one. Every
`ReplenishmentPeriodSeconds`, `TokensPerPeriod` tokens are added back, up to `TokenLimit`. Raise
`TokenLimit` to allow bigger bursts, and raise `TokensPerPeriod` to allow a higher sustained rate.

### Changing limits without a restart

The settings are read through `IOptionsMonitor`, so a reloaded configuration source
(`appsettings.json` with `reloadOnChange`, which is the default, or any reloadable provider)
applies to the next request. Each change bumps a settings version. That version is part of every
limiter key (for example `user:{tenant}:{user}:v2`), so the new limits start right away with
fresh counters, and old limiter instances are dropped when idle. A reload that changes nothing
keeps the version and the counters.

The limits are read through `IRateLimitSettingsProvider`
(`src/ONEVO.Application/Common/ServiceInterfaces`). The default implementation,
`OptionsRateLimitSettingsProvider`, reads this section. Since task 46 the registered provider is
`DatabaseRateLimitSettingsProvider` (`src/ONEVO.Infrastructure/Services/SharedPlatform/RateLimiting`).
It starts from this section and applies the override rows in `rate_limit_settings`:

- Precedence: tenant row, then plan row, then global row, then this section. Plan and tenant rows
  count only for `SignedIn`, `TrayDevice` and `TenantCeiling`.
- `Enabled`, `ReplenishmentPeriodSeconds`, `WindowSeconds` and `SegmentsPerWindow` always come from
  this section. A row gives "per minute" and "burst": `TokenLimit = burst`,
  `TokensPerPeriod = perMinute x ReplenishmentPeriodSeconds / 60`, and
  `PermitLimit = perMinute x WindowSeconds / 60`. Values are clamped to `RateLimitBounds` first.
- Requests are answered from memory. The rows are loaded at startup (at most 15 s, then the API
  starts on this section's limits), straight after an admin change on the same instance
  (`IRateLimitSettingsInvalidator`), and within 30 s on other instances, which check
  `COUNT`/`MAX(updated_at)` of the table and the tenants' plans.
- The version is kept per tenant, so a change to one tenant resets only that tenant's counters.
  If loading fails, the last good limits stay; with none yet, this section's limits apply.

With no rows in `rate_limit_settings`, the limits are exactly this section's.

## Rejected requests

A rejected request gets HTTP 429 with a `Retry-After` header (in seconds) and this body, which
matches the earlier hand-written limiter:

```json
{
  "type": "urn:crownus:error:rate-limit",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Too many requests. Please try again later."
}
```

The API also logs a warning, `API rate limit exceeded`, with the partition kind (`PreAuthentication`,
`Anonymous`, `User`, `TrayDevice`, `AdminAnonymous`, `Admin`), the tenant id when there is one, the path and the
retry delay. It does not log the client IP or the user id.

## Tuning for UAT

- **Many users behind one office IP:** signed-in traffic no longer shares the IP's budget, so
  this needs no change. Only the login page and other anonymous calls share `Anonymous`.
- **One user gets 429 on a heavy page:** raise `SignedIn:TokenLimit` (burst) first.
- **A large tenant hits the ceiling:** raise `TenantCeiling:TokensPerPeriod`.
- **A whole office gets 429 with partition `PreAuthentication` in the log:** the office IP sends
  more than 6000 requests a minute in total. Raise `PreAuthentication:PermitLimit`.

## Auth endpoint limits (not configurable)

These are in `AuthRateLimitingMiddleware`, apply to POST requests only, and run before the global
limiter. Each line is a separate counter in a fixed window.

| Path | Per IP | Per identity |
|---|---|---|
| `/api/v1/auth/login` | 20 / 5 min | 5 / 5 min per email |
| `/api/v1/auth/login/select-workspace` | 20 / 5 min | 10 / 5 min per challenge |
| `/api/v1/auth/login/google` | 20 / 5 min | 5 / 5 min per Google token |
| `/api/v1/auth/mfa/verify` | 20 / 5 min | 5 / 10 min per MFA cookie |
| `/api/v1/auth/forgot-password` | 10 / 15 min | 3 / 15 min per email |
| `/api/v1/auth/reset-password` | 10 / 15 min | 5 / 15 min per token |
| `/api/v1/auth/force-change-password` | 10 / 15 min | 5 / 15 min per email |
| `/admin/v1/auth/login` | 20 / 5 min | 5 / 5 min per email |
| `/admin/v1/auth/google-callback` | 20 / 5 min | 5 / 5 min per Google token |
| `/admin/v1/auth/mfa/verify` | 20 / 5 min | 5 / 10 min per MFA cookie |
| `/admin/v1/auth/forgot-password` | 10 / 15 min | 3 / 15 min per email |
| `/admin/v1/auth/reset-password` | 10 / 15 min | 5 / 15 min per token |
| `/admin/v1/auth/accept-invite` | 10 / 15 min | 5 / 15 min per token |
| `/api/v1/auth/invitations/...` | 20 / 15 min | 5 / 15 min per invitation path |
| `/api/v1/monitoring/screenshots/request` | 10 / 1 min | |
| `/api/v1/monitoring/tray/upload` | 5 / 1 min | |

Note: `/api/v1/auth/login` allows 20 attempts per IP in 5 minutes. A UAT office of 50 people on
one IP who all sign in within 5 minutes will hit this limit. Task 07 leaves it unchanged by design.
