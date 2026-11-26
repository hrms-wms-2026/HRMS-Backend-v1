# Get Tenant Rate Limits

**GET** `/admin/v1/tenants/{tenantId}/rate-limits`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.read`

## Description

The tenant's overrides and effective per-tenant limits, with where each value comes from.

## Response

`200 OK`:

```json
{
  "tenantId": "guid",
  "subscriptionPlanId": "guid",
  "limits": [
    {
      "limitName": "SignedIn",
      "algorithm": "token_bucket",
      "override": null,
      "effective": { "perMinute": 1200, "burst": 200 },
      "effectiveSource": "plan",
      "bounds": { "minPerMinute": 120, "maxPerMinute": 3000, "minBurst": 30, "maxBurst": 500 }
    }
  ]
}
```

`effectiveSource` is `tenant`, `plan`, `global` or `appsettings`. `limits` always has `SignedIn`, `TrayDevice` and `TenantCeiling`.

## Errors

| Status | Cause |
|---|---|
| `401` | Not signed in |
| `403` | Missing `platform.system_config.read` |
| `404` | Tenant not found |

## Source

Controllers: `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/RateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlanRateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Tenants/AdminTenantRateLimitsController.cs`
Application: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/`
Spec: `docs/Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md`; guide `docs/superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md`
