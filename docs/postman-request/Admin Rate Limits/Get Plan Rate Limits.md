# Get Plan Rate Limits

**GET** `/admin/v1/subscription-plans/{id}/rate-limits`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.read`

## Description

The plan's overrides and what each limit inherits from global. Needs `system_config.read`, not `subscriptions.read`: a rate limit is a platform safety setting, not a commercial term. The admin plan detail (`GET /admin/v1/subscription-plans/{id}`) also carries this list read-only as `rateLimits`.

## Response

`200 OK`:

```json
{
  "subscriptionPlanId": "guid",
  "limits": [
    {
      "limitName": "SignedIn",
      "algorithm": "token_bucket",
      "override": { "perMinute": 1200, "burst": 200, "updatedAt": "2026-11-20T10:00:00+00:00", "updatedByPlatformUserId": "guid", "rowVersion": "2001" },
      "inherited": { "perMinute": 600, "burst": 100 },
      "inheritedSource": "appsettings",
      "effective": { "perMinute": 1200, "burst": 200 },
      "effectiveSource": "plan",
      "bounds": { "minPerMinute": 120, "maxPerMinute": 3000, "minBurst": 30, "maxBurst": 500 }
    }
  ]
}
```

`limits` always has `SignedIn`, `TrayDevice` and `TenantCeiling`, in that order.

## Errors

| Status | Cause |
|---|---|
| `401` | Not signed in |
| `403` | Missing `platform.system_config.read` |
| `404` | Plan not found |

## Source

Controllers: `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/RateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlanRateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Tenants/AdminTenantRateLimitsController.cs`
Application: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/`
Spec: `docs/Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md`; guide `docs/superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md`
