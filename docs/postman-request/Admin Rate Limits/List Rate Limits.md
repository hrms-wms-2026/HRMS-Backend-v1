# List Rate Limits

**GET** `/admin/v1/system-config/rate-limits`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.read`

## Description

All six limits. For each: the global override (if any), the `appsettings` value, the effective value with its source (`global` or `appsettings`), and the allowed range.

## Response

`200 OK`:

```json
[
  {
    "limitName": "SignedIn",
    "algorithm": "token_bucket",
    "override": { "perMinute": 1200, "burst": 200, "updatedAt": "2026-11-20T10:00:00+00:00", "updatedByPlatformUserId": "guid", "rowVersion": "2001" },
    "appSettings": { "perMinute": 600, "burst": 100 },
    "effective": { "perMinute": 1200, "burst": 200 },
    "effectiveSource": "global",
    "bounds": { "minPerMinute": 120, "maxPerMinute": 3000, "minBurst": 30, "maxBurst": 500 }
  },
  {
    "limitName": "Anonymous",
    "algorithm": "sliding_window",
    "override": null,
    "appSettings": { "perMinute": 300, "burst": null },
    "effective": { "perMinute": 300, "burst": null },
    "effectiveSource": "appsettings",
    "bounds": { "minPerMinute": 60, "maxPerMinute": 1200, "minBurst": null, "maxBurst": null }
  }
]
```

Always six entries, one per limit.

## Errors

| Status | Cause |
|---|---|
| `401` | Not signed in |
| `403` | Missing `platform.system_config.read` |

## Source

Controllers: `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/RateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlanRateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Tenants/AdminTenantRateLimitsController.cs`
Application: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/`
Spec: `docs/Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md`; guide `docs/superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md`
