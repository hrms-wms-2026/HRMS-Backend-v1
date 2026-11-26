# Remove Global Rate Limit

**DELETE** `/admin/v1/system-config/rate-limits/{limitName}`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.manage`

## Description

Removes the global override, so the limit goes back to `appsettings`. `{limitName}` is one of `Anonymous`, `SignedIn`, `TrayDevice`, `TenantCeiling`, `AdminAnonymous`, `AdminSignedIn` (case-insensitive). Anything else (for example `login` or `Enabled`) returns `400`. No route reaches the login, MFA or password reset limits or the `RateLimiting:Enabled` master switch.

Reason and version go in the query (`?reason=...&rowVersion=...`) or in an optional JSON body:

```json
{ "reason": "Import finished, back to the default.", "rowVersion": "2001" }
```

Removing an override that does not exist is a no-op (`200`, no history row).

## Response

`200 OK` - the same list as **List Rate Limits**.

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
