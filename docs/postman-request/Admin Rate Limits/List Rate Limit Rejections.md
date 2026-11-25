# List Rate Limit Rejections

**GET** `/admin/v1/system-config/rate-limits/rejections?minutes=60`

**Auth:** Platform admin session cookie (`admin_session`) + `X-CSRF-Token` header on writes. Policy: `AdminPolicy`.
**Permission:** `platform.system_config.read`

## Description

The 50 tenants with the most 429 responses on this API server in the last `minutes` minutes (1 to 60, default 60), so an admin can check that a tenant really hits its limits before raising them. Added with Improvements task 49.

- Counts are held in memory on each API server: they cover **this server only** and start again when it restarts. `scope` is always `instance`; `countingSince` says when counting started. Until task 21 adds shared counters, with two API servers this shows one of them.
- `byKind` uses the limiter partition: `User`, `TrayDevice`, `Anonymous`, `AdminAnonymous`, `Admin`, `PreAuthentication`. A `User` or `TrayDevice` rejection can come from the person's own limit or from the tenant ceiling; the limiter does not say which. If one tenant shows many rejections across many users, the ceiling is the likely cause.
- Requests with no tenant (signed-out, pre-authentication and admin requests) are one row with `tenantId: null`, no name and no `effectiveLimits`.
- `effectiveLimits` are the tenant's limits now (tenant, plan, global or appsettings value).
- No user id, IP address or session is counted or returned.

## Response

`200 OK`:

```json
{
  "scope": "instance",
  "note": "Counts from this API server only, for the last 60 minutes, since it last started. Other API servers keep their own counts until shared counters are added (task 21).",
  "minutes": 60,
  "generatedAt": "2026-11-24T10:00:00+00:00",
  "countingSince": "2026-11-24T06:12:40+00:00",
  "totalRejections": 27,
  "tenants": [
    {
      "tenantId": "guid",
      "tenantName": "Acme",
      "totalRejections": 26,
      "byKind": [
        { "kind": "User", "count": 25 },
        { "kind": "TrayDevice", "count": 1 }
      ],
      "effectiveLimits": [
        { "limitName": "SignedIn", "perMinute": 1200, "burst": 200 },
        { "limitName": "TrayDevice", "perMinute": 300, "burst": 60 },
        { "limitName": "TenantCeiling", "perMinute": 12000, "burst": 3000 }
      ]
    },
    {
      "tenantId": null,
      "tenantName": null,
      "totalRejections": 1,
      "byKind": [ { "kind": "Anonymous", "count": 1 } ],
      "effectiveLimits": []
    }
  ]
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | `minutes` outside 1-60 |
| `401` | Not signed in |
| `403` | Missing `platform.system_config.read` |

## Source

Controller: `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/RateLimitsController.cs`
Application: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/Queries/GetRateLimitRejections/`
Counter: `src/ONEVO.Infrastructure/Services/SharedPlatform/RateLimiting/RateLimitRejectionStats.cs`
Spec: `docs/Improvements/person-2-performance-and-db/task-49 Rate limit rejection counts for admins.md`
