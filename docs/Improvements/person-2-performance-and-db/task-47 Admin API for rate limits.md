# Task 47: Admin API for rate limits

| | |
|---|---|
| Priority | 47 (lower number = build first). Added 2026-11-17 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | API, Security |
| Depends on | 07, 45 (settings table), 46 (database provider) |
| Guide | [Admin-managed rate limits guide](<../../superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md>) |

## What we found

- Platform config endpoints live in `ONEVO.Api/Controllers/Admin/DevPlatform/SystemConfig/`. For example, `TenantIntegrationCredentialsController` uses `[Authorize(Policy = "AdminPolicy")]`, `[Route("admin/v1/system-config/...")]` and `[RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead | SystemConfigManage)]`, and sends MediatR commands and queries.
- Admin endpoints use `[RequirePlatformPermission]`, not the tenant `[RequirePermission]`.
- `SubscriptionPlansController` (`admin/v1/subscription-plans`) uses `SubscriptionsRead` and `SubscriptionsManage`. Tenant routes use `admin/v1/tenants/{tenantId:guid}/...` (for example `TenantSubscriptionsController`, `AdminTenantSessionsController`).
- `PlatformPermissionCatalog.SystemConfigManage` is already marked high risk. The catalog says not to add codes that are not documented, and tasks 32 and 33 cover adding permissions safely. Reuse the existing codes. Do not add a new permission in this task.
- Admin handlers get the actor from `ICurrentPlatformUserContext.UserId` (see `VoidInvoiceCommandHandler`).

## What to do

### Endpoints

All of them get `[Authorize(Policy = "AdminPolicy")]`. Reads need `SystemConfigRead`, writes need `SystemConfigManage`, on every route, including the plan and tenant routes. A rate limit is a platform safety setting, not a commercial term.

| Method and route | What it does |
|---|---|
| `GET admin/v1/system-config/rate-limits` | Global rows for all five partitions, plus the bounds and code defaults for each, so the screen can show the allowed range |
| `PUT admin/v1/system-config/rate-limits/{partition}` | Set the global value for one partition. Body: `permitsPerMinute`, `reason`, `rowVersion` |
| `GET admin/v1/subscription-plans/{id:guid}/rate-limits` | The plan's overrides for `user`, `tray`, `tenant`, and the value each one inherits from global |
| `PUT admin/v1/subscription-plans/{id:guid}/rate-limits/{partition}` | Set a plan override |
| `DELETE admin/v1/subscription-plans/{id:guid}/rate-limits/{partition}` | Remove a plan override (back to global). Body or query: `reason` |
| `GET admin/v1/tenants/{tenantId:guid}/rate-limits` | The tenant's overrides and the effective value for each partition, with where it comes from (`tenant`, `plan`, `global`, `default`) |
| `PUT admin/v1/tenants/{tenantId:guid}/rate-limits/{partition}` | Set a tenant override |
| `DELETE admin/v1/tenants/{tenantId:guid}/rate-limits/{partition}` | Remove a tenant override |

- Put the plan routes in a new `SubscriptionPlanRateLimitsController` and the tenant routes in `AdminTenantRateLimitsController`, so `SubscriptionPlansController` keeps its current permissions. Also add a read-only `rateLimits` summary to the `GetSubscriptionPlan` response, so the plan detail page can show it without a second call.
- Do not name a route parameter `{action}`. It is reserved (see `CLAUDE.md`).
- `{partition}` must parse to the partition enum. Any other value returns 400. There is no partition for login, MFA or password reset. `AuthRateLimitingMiddleware` stays code-fixed and no endpoint can change it.

### Commands, validation and audit

1. One command per write: `SetGlobalRateLimitCommand`, `SetPlanRateLimitCommand`, `RemovePlanRateLimitCommand`, `SetTenantRateLimitCommand`, `RemoveTenantRateLimitCommand`, in `ONEVO.Application/Features/DevPlatform/SystemConfig/RateLimits/`.
2. A FluentValidation validator for each command:
   - `permitsPerMinute` is inside the per-partition bounds in the guide (section 4). Put the bounds in one static class, `RateLimitBounds`, which the validator, the provider clamp (task 46) and the GET response all use.
   - The `tenant` ceiling must be at least the effective `user` limit for the same scope. One user must never be able to use up a whole tenant's ceiling alone.
   - `anonymous` and `admin` cannot be set on a plan or a tenant.
   - `reason` is required, 10 to 500 characters, plain text. It is shown back in the admin app, so reject markup: run `RichTextRules.HasActiveContent` on it.
   - The plan or tenant exists and is not archived.
3. Use `rowVersion` for optimistic concurrency. Two admins editing at once get 409 for the second save, not a silent overwrite.
4. In the same transaction as the save, write one `rate_limit_setting_changes` row with the old value, new value, actor (`ICurrentPlatformUserContext.UserId`), reason and correlation id.
5. After the transaction commits, call the invalidation from task 46, so the change applies on this instance at once.
6. A no-op save (same value) writes no history row and does not invalidate.

### Change history read

`GET admin/v1/system-config/rate-limits/changes?scope=&tenantId=&planId=&page=&pageSize=` needs `SystemConfigRead`. It returns the history rows newest first and is paginated with the shared contract from task 04. If task 04 is not done yet, use `page` and `pageSize` with a maximum of 50.

## Tests

- Unit: each validator rejects 0, a negative value, a value below the floor and a value above the ceiling, and accepts the floor and the ceiling.
- Unit: `anonymous` or `admin` on a plan or tenant command is rejected. A tenant ceiling below the user limit is rejected.
- Integration (`-Full`):
  - A platform user without `platform.system_config.read` gets 403 on every GET. A user with read but not `platform.system_config.manage` gets 403 on every PUT and DELETE.
  - An anonymous call gets 401 on every route.
  - A successful PUT writes exactly one history row with the right actor, old and new values.
  - A PUT with a stale `rowVersion` returns 409.
  - After a tenant PUT, the next tenant request is limited by the new value (end-to-end check of task 46's invalidation).
- `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` still reports 0 unprotected actions.

## Done when

- All eight routes and the history read work with the permissions above.
- No request can store a limit outside the bounds, and auth limits cannot be changed through the API.
- Every change has a history row.
- Request docs are added in a new `docs/postman-request/Admin Rate Limits/` folder, following `docs/postman-request/README.md`. There is no admin folder there yet.
- `pwsh ../dev-env/scripts/verify.ps1 backend -Full` passes.
