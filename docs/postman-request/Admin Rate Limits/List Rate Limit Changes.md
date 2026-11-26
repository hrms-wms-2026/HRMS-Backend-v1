# List Rate Limit Changes

**GET** `/admin/v1/system-config/rate-limits/changes?scope=&tenantId=&planId=&page=&pageSize=`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.read`

## Description

Change history, newest first. Optional filters: `scope` (`global`, `plan`, `tenant`), `tenantId`, `planId`. `page` starts at 1; `pageSize` defaults to 25, maximum 50 (task 04's shared contract is not built yet). Old values are `null` for a new override, new values `null` for a removal.

## Response

`200 OK`:

```json
{
  "items": [
    {
      "id": "guid",
      "changedAt": "2026-11-20T10:00:00+00:00",
      "platformUserId": "guid",
      "platformUserName": "Ops Admin",
      "scope": "tenant",
      "subscriptionPlanId": null,
      "tenantId": "guid",
      "limitName": "SignedIn",
      "oldPerMinute": 600,
      "oldBurst": 100,
      "newPerMinute": 1200,
      "newBurst": 200,
      "reason": "Customer import week, approved by ops.",
      "correlationId": "guid"
    }
  ],
  "pageNumber": 1,
  "pageSize": 25,
  "totalCount": 1,
  "totalPages": 1,
  "hasNext": false,
  "hasPrevious": false
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Unknown `scope`, `page` below 1, `pageSize` outside 1-50 |
| `401` | Not signed in |
| `403` | Missing `platform.system_config.read` |

## Source

Controllers: `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/RateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Subscriptions/SubscriptionPlanRateLimitsController.cs`, `src/ONEVO.Api/Controllers/Admin/DevPlatform/Tenants/AdminTenantRateLimitsController.cs`
Application: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/`
Spec: `docs/Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md`; guide `docs/superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md`
