# Set Global Rate Limit

**PUT** `/admin/v1/system-config/rate-limits/{limitName}`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.manage`

## Description

Sets the platform-wide value of one limit, overriding `appsettings`. `{limitName}` is one of `Anonymous`, `SignedIn`, `TrayDevice`, `TenantCeiling`, `AdminAnonymous`, `AdminSignedIn` (case-insensitive). Anything else (for example `login` or `Enabled`) returns `400`. No route reaches the login, MFA or password reset limits or the `RateLimiting:Enabled` master switch.

## Body

```json
{
  "perMinute": 1200,
  "burst": 200,
  "reason": "Customer import week, approved by ops.",
  "rowVersion": "2001"
}
```

`burst` must be omitted or `null` for `Anonymous` and `AdminAnonymous`. `rowVersion` is the `override.rowVersion` from the last GET, or `null` when there was no override.

Saves the override and one `rate_limit_setting_changes` row in one transaction, then reloads the limiter on this API instance at once (other instances within 30 s). Saving the same value again is a no-op: no history row and no reload.

## Response

`200 OK` - the same list as **List Rate Limits**, with the new `rowVersion`.

## Errors

| Status | Cause |
|---|---|
| `400` | Unknown `{limitName}`; `perMinute` or `burst` outside the bounds; `burst` missing on a token-bucket limit or present on `Anonymous`/`AdminAnonymous`; `perMinute` not a multiple of 60 (token bucket, 1 s refill); `reason` not 10-500 plain-text characters; `TenantCeiling` below the effective `SignedIn` limit of the same scope; platform-wide limit on a plan or tenant; archived plan or cancelled tenant |
| `401` | Not signed in |
| `403` | Missing `platform.system_config.manage` |
| `404` | Plan or tenant not found |
| `409` | Stale or missing `rowVersion` (another admin saved first) |

## Source

Controllers: `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/RateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlanRateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Tenants/AdminTenantRateLimitsController.cs`
Application: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/`
Spec: `docs/Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md`; guide `docs/superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md`
