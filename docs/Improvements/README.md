# Backend improvements (HRMS-Backend-v1)

Written 2026-11-15 from a read-only scan of the whole backend on branch `feature/role-dashboard-templates`. No code was changed. Every file in the person folders is one task for one person for one day.

## How to use this folder

- Four people, four folders. Each person works through their own folder from the lowest task number up.
- The task number is the priority across the whole backend. Task 01 is the most urgent, task 30 the least.
- The first 12 tasks are the ones to finish before UAT opens. Tasks 13 to 21 should land during UAT. The rest can follow.
- If a task turns out bigger than a day, split it and keep the same number with a suffix (for example `task-08b`).
- When a task is finished, add a `## Status` section to its file and update the status table below.

## Status (2026-11-20)

Only tasks that have started are listed. Every other task is not started.

| # | Task | Status | Where |
|---|---|---|---|
| 03 | Cut the per-request session lookups | Done | `development`, PR #235 |
| 07 | Fix the global rate limiter | Done | `development`, PR #237 |
| 11 | Secrets out of the repository and scans in CI | On hold, needs the repository owner's agreement | - |
| 15 | Deny by default for every endpoint | In review | PR #240, not merged |
| 19 | Validation coverage and shared rules | In review | PR #240, not merged |
| 19b | Validation coverage for the remaining commands (the allow-list is now empty) | In review | PR #240, not merged |
| 23 | Security headers, upload checks and request limits (Swagger removed) | In review | PR #240, not merged |
| 27 | Cross-tenant and object access tests for UAT | In review. Open product decision: the tenant-wide productivity report | PR #240, not merged |
| 32 | One permission catalogue in code (`PermissionCatalog`) | In review | PR #240, not merged |
| 33 | Safe add, rename and retire of permissions (migration `AddPermissionLifecycle`) | In review | PR #240, not merged |
| 39a | Foreign keys and status check constraints, first part (migration `AddReferenceForeignKeysAndStatusChecks39a`) | In review | PR #240, not merged |
| 39b to 39g | Rest of task 39, see the files in the person-1 folder | Not started | files arrive with PR #240 |
| 44 | Sensitive data in API responses (steps 1, 2 and 4) | In review. Row 4 and step 3 need owner decisions | PR #240, not merged |

The two new migrations in PR #240 are created, not applied. Apply them to each shared database after the merge.

Person-1 tasks still open: 11 (on hold), 39b to 39g, and the owner decisions in 27 and 44.

Since task 15, the "No fallback authorization policy" finding in the table below is fixed (once PR #240 is merged). Since task 23, the API has no Swagger in any environment; the API description is in `docs/api/openapi/`.

## Targets the team agreed

- Every API answers in 200 ms or less at p95, measured on the UAT server with realistic data.
- Any endpoint that can return more than 50 rows must be paginated.
- No endpoint is reachable without authentication unless it is marked `[AllowAnonymous]` on purpose.
- The API must be able to run as two or more instances behind a load balancer.

## What the scan found (short version)

| Area | Finding | Evidence |
|---|---|---|
| Performance | Every authenticated request does about five database round trips before the handler starts | `TenantDatabaseTicketStore.RetrieveCoreAsync` (session, tenant, user, employee, permissions) |
| Performance | Every EF query likely costs three round trips: set tenant context, query, clear tenant context | `TenantRlsInterceptor` runs on connection open and close, and nothing keeps the connection open for the request |
| Performance | No response compression, no output caching, 13 uses of `IMemoryCache` in total | `Program.cs`, grep over `src` |
| Pagination | 528 `ToListAsync` calls in repositories, only 35 `.Skip(` | grep over `Persistence/Repositories` |
| Pagination | Work Management has 73 GET endpoints and 2 controllers with paging; Leave 21 and 1; Attendance 21 and 1 | grep over `Controllers/Tenant` |
| Pagination | Parameter names differ (`page`, `pageNumber`, `limit`, `take`, `offset`), defaults differ (10, 20, 25, 100), max size is defined per file | grep over controllers |
| Security | The global rate limit is 300 requests per minute per IP for all of `/api/v1`. One office behind one public IP shares that budget | `PublicApiRateLimitingMiddleware` |
| Security | No fallback authorization policy. An action without `[Authorize]` is anonymous | `AuthorizationExtensions`, `CLAUDE.md` |
| Security | 322 commands, 145 validators | file counts in `ONEVO.Application` |
| Security | A tray JWT secret and a personal email are committed in `appsettings.Development.json` | file is tracked in git |
| Scaling | Rate limits, web presence, tray connections and caches live in process memory. SignalR has no backplane | `WebActivityTracker`, `TrayConnectionTracker`, `AddSignalR()` |
| Scaling | About 35 hosted services (jobs and seeders) start inside the web process | `Infrastructure/DependencyInjection.cs` |
| Architecture | `DependencyInjection.cs` is 1,149 lines; several files are 600 to 1,000 lines | line counts |
| Architecture | 649 duplicate code blocks (7,346 lines) and 239 unused-code warnings | dev-env cleanup scan of 2026-11-13 |
| API | Version is a hard-coded string `api/v1` in 103 route attributes. No versioning library | grep over controllers |
| Deployment | No Dockerfile, no deploy workflow, no UAT settings file. `AllowedOrigins` still holds `https://app.example.com` | repo root, `appsettings.json` |
| Deployment | Integration tests are non-blocking in CI. No security scan runs in CI | `.github/workflows/ci.yml` |
| Observability | Console logging only. No metrics, no tracing. Slow-request warning starts at 500 ms | `appsettings.json`, `PerformanceBehavior` |

## What is already good (do not redo)

- Tenant isolation through SQL Server row-level security, enforced by architecture tests.
- Cookie sessions stored server side as a hash, `HttpOnly`, `SameSite=Strict`, CSRF middleware, BCrypt work factor 12.
- CORS is limited to the tenant root domain over https. Forwarded headers are trusted only from known proxies.
- Login, MFA and password reset have their own rate limits.
- Migrations do not run automatically outside Development. Demo seeders are gated to Development or Test.
- Liveness and readiness health checks are separate.

## Limits of this scan

- It was a static read of the code. Nothing was run under load, so no response time in these files is a measured number. Task 06 produces the real numbers.
- The round-trip finding for the RLS interceptor comes from reading the code. Task 02 starts by confirming it with a query trace.
- The platform admin frontend repository was not scanned.

## API change management decision (2026-11-15)

The team manages API changes with **feature keys**: a new or changed API is released behind a feature key and switched on per tenant. A new URL version is used only for a breaking change on an endpoint the tray app calls. Task 43 builds the feature key gate and must be done before task 16, which now holds the rule table. Tasks 32 and 34 and frontend task 25 were updated to match.

## Urgent tasks (added 2026-11-15 after measuring)

The API was measured on 2026-11-15. The report is in [docs/reports/performance/2026-11-15-api-response-timings.md](<../reports/performance/2026-11-15-api-response-timings.md>). Of 50 endpoints, 13 are over 200 ms. The four inbox endpoints take 3.1 to 3.4 seconds each, and inbox counts runs on every page load. Do these three before task 01:

| # | Task | Owner folder |
|---|---|---|
| 00a | [Inbox counts without loading the whole inbox](<person-2-performance-and-db/task-00a Inbox counts without loading the whole inbox.md>) | person-2-performance-and-db |
| 00b | [Fast paginated chat list](<person-3-api-and-pagination/task-00b Fast paginated chat list.md>) | person-3-api-and-pagination |
| 00c | [Inbox list and filters paged in the database](<person-4-deployment-and-architecture/task-00c Inbox list and filters paged in the database.md>) | person-4-deployment-and-architecture |

## Added after the outbox and permission checks (2026-11-15)

Reports: [outbox mapping check](<../reports/outbox/2026-11-15-outbox-mapping-check.md>) and [permission system review](<../reports/security/2026-11-15-permission-system-review.md>). The outbox mapping is correct today. The permission checks work, but adding or renaming a permission is risky. These four tasks are not in the table below:

| # | Task | Owner folder |
|---|---|---|
| 31 | [Outbox mapping guard and clean-up](<person-4-deployment-and-architecture/task-31 Outbox mapping guard and clean-up.md>) | person-4-deployment-and-architecture |
| 32 | [One permission catalogue in code](<person-1-security-and-auth/task-32 One permission catalogue in code.md>) | person-1-security-and-auth |
| 33 | [Safe add, rename and retire of permissions](<person-1-security-and-auth/task-33 Safe add, rename and retire of permissions.md>) | person-1-security-and-auth |
| 34 | [One place for each access rule](<person-3-api-and-pagination/task-34 One place for each access rule.md>) | person-3-api-and-pagination |

Do task 32 before the next new permission is added, whatever its number says.

## Database design tasks (added 2026-11-15)

The live schema was reviewed: [database design review](<../reports/database/2026-11-15-database-design-review.md>). 219 tables. Six places where one concept is split over several tables, and four gaps across the whole schema. These tasks are not in the table below. Suggested order: 37, 35, 40, 39, then the rest.

| # | Task | Owner folder |
|---|---|---|
| 35 | [Merge the three monitoring snapshot tables](<person-2-performance-and-db/task-35 Merge the three monitoring snapshot tables.md>) | person-2-performance-and-db |
| 36 | [One monitoring settings table for every scope](<person-2-performance-and-db/task-36 One monitoring settings table for every scope.md>) | person-2-performance-and-db |
| 37 | [Shared approval request header table](<person-4-deployment-and-architecture/task-37 Shared approval request header table.md>) | person-4-deployment-and-architecture |
| 38 | [One activity log for task, sprint and calendar history](<person-3-api-and-pagination/task-38 One activity log for task, sprint and calendar history.md>) | person-3-api-and-pagination |
| 39 | [Foreign keys and check constraints](<person-1-security-and-auth/task-39 Foreign keys and check constraints.md>) | person-1-security-and-auth |
| 40 | [Index and clustered key fixes](<person-2-performance-and-db/task-40 Index and clustered key fixes.md>) | person-2-performance-and-db |
| 41 | [Id lists and column sizes](<person-3-api-and-pagination/task-41 Id lists and column sizes.md>) | person-3-api-and-pagination |
| 42 | [Concurrency tokens, identity columns and unused tables](<person-4-deployment-and-architecture/task-42 Concurrency tokens, identity columns and unused tables.md>) | person-4-deployment-and-architecture |

## Feature key and module gating (added 2026-11-15)

The feature key APIs were checked. Feature keys are stored (192 rows in `module_features`) but no endpoint enforces them; only module keys and settings flags gate anything. This task is not in the table below:

| # | Task | Owner folder |
|---|---|---|
| 43 | [Feature keys and module gates that are enforced](<person-3-api-and-pagination/task-43 Feature keys and module gates that are enforced.md>) | person-3-api-and-pagination |

## Sensitive data in API responses (added 2026-11-15)

Both repositories were read for sensitive data reaching the browser. No secret, token or stack trace goes out. One anonymous endpoint returns the internal tenant id, two attendance flows return the approver's internal user id, and employee personal data has no permission of its own. The task lists each API, the fix, and three guards to stop it coming back. It covers the small frontend items too. This task is not in the table below; do its step 1 before UAT opens:

| # | Task | Owner folder |
|---|---|---|
| 44 | [Sensitive data in API responses](<person-1-security-and-auth/task-44 Sensitive data in API responses.md>) | person-1-security-and-auth |

## Admin-managed rate limits (added 2026-11-17)

Task 07 moves the API to the built-in rate limiter with limits read from settings. These tasks let the platform admin app change those limits while the API runs: global defaults, per subscription plan and per tenant, with checks, audit and no restart. Login and MFA limits stay fixed in code. Start with the guide: [Admin-managed rate limits guide](<../superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md>). Build in order: 45, 46, 47, 48, then 49 if there is time. These tasks are not in the table below.

| # | Task | Owner folder | Depends on |
|---|---|---|---|
| 45 | [Rate limit settings table](<person-2-performance-and-db/task-45 Rate limit settings table.md>) | person-2-performance-and-db | 07 |
| 46 | [Rate limit settings from the database](<person-2-performance-and-db/task-46 Rate limit settings from the database.md>) | person-2-performance-and-db | 07, 45 |
| 47 | [Admin API for rate limits](<person-2-performance-and-db/task-47 Admin API for rate limits.md>) | person-2-performance-and-db | 07, 45, 46 |
| 48 | [Admin app screens for rate limits](<person-2-performance-and-db/task-48 Admin app screens for rate limits.md>) (admin frontend repo) | person-2-performance-and-db | 07, 47 |
| 49 | [Rate limit rejection counts for admins](<person-2-performance-and-db/task-49 Rate limit rejection counts for admins.md>) (optional) | person-2-performance-and-db | 07, 47, 48 |

## Priority order (all tasks)

Build in this order. Each person works through their own folder from the lowest number up.

| # | Task | Owner folder | Area | Depends on |
|---|---|---|---|---|
| 01 | [Container image for the API](<person-4-deployment-and-architecture/task-01 Container image for the API.md>) | person-4-deployment-and-architecture | Deployment | - |
| 02 | [One database connection per request](<person-2-performance-and-db/task-02 One database connection per request.md>) | person-2-performance-and-db | Performance, DB | - |
| 03 | [Cut the per-request session lookups](<person-1-security-and-auth/task-03 Cut the per-request session lookups.md>) | person-1-security-and-auth | Performance, Security | - |
| 04 | [One pagination contract for the whole API](<person-3-api-and-pagination/task-04 One pagination contract for the whole API.md>) | person-3-api-and-pagination | API, Pagination | - |
| 05 | [UAT settings and secrets](<person-4-deployment-and-architecture/task-05 UAT settings and secrets.md>) | person-4-deployment-and-architecture | Deployment, Security | 01 |
| 06 | [Measure every endpoint against 200 ms](<person-2-performance-and-db/task-06 Measure every endpoint against 200 ms.md>) | person-2-performance-and-db | Performance | - |
| 07 | [Fix the global rate limiter](<person-1-security-and-auth/task-07 Fix the global rate limiter.md>) | person-1-security-and-auth | Security, Scaling | - |
| 08 | [Paginate Work Management lists](<person-3-api-and-pagination/task-08 Paginate Work Management lists.md>) | person-3-api-and-pagination | Pagination, Performance | 04 |
| 09 | [Deploy pipeline and database migration step](<person-4-deployment-and-architecture/task-09 Deploy pipeline and database migration step.md>) | person-4-deployment-and-architecture | Deployment, DB | 01, 05 |
| 10 | [Response compression and smaller payloads](<person-2-performance-and-db/task-10 Response compression and smaller payloads.md>) | person-2-performance-and-db | Performance, API | - |
| 11 | [Secrets out of the repository and scans in CI](<person-1-security-and-auth/task-11 Secrets out of the repository and scans in CI.md>) | person-1-security-and-auth | Security, Deployment | - |
| 12 | [Paginate Leave, Attendance and People lists](<person-3-api-and-pagination/task-12 Paginate Leave, Attendance and People lists.md>) | person-3-api-and-pagination | Pagination, Performance | 04 |
| 13 | [Background jobs safe for more than one instance](<person-4-deployment-and-architecture/task-13 Background jobs safe for more than one instance.md>) | person-4-deployment-and-architecture | Scaling, Architecture | - |
| 14 | [Remove query-in-a-loop patterns](<person-2-performance-and-db/task-14 Remove query-in-a-loop patterns.md>) | person-2-performance-and-db | Performance, DB | 06 |
| 15 | [Deny by default for every endpoint](<person-1-security-and-auth/task-15 Deny by default for every endpoint.md>) | person-1-security-and-auth | Security | - |
| 16 | [API versioning](<person-3-api-and-pagination/task-16 API versioning.md>) | person-3-api-and-pagination | API, Architecture | 43 (feature keys first) |
| 17 | [Logs, metrics and alerts for UAT](<person-4-deployment-and-architecture/task-17 Logs, metrics and alerts for UAT.md>) | person-4-deployment-and-architecture | Deployment, Observability | 05 |
| 18 | [Index review from real query data](<person-2-performance-and-db/task-18 Index review from real query data.md>) | person-2-performance-and-db | DB, Performance | 06, 08, 12 |
| 19 | [Validation coverage and shared rules](<person-1-security-and-auth/task-19 Validation coverage and shared rules.md>) | person-1-security-and-auth | Security, Code quality | - |
| 20 | [Fewer, smaller requests for dashboard and calendar](<person-3-api-and-pagination/task-20 Fewer, smaller requests for dashboard and calendar.md>) | person-3-api-and-pagination | API, Performance | 06 |
| 21 | [Shared state for running more than one instance](<person-4-deployment-and-architecture/task-21 Shared state for running more than one instance.md>) | person-4-deployment-and-architecture | Scaling, Architecture | 13 |
| 22 | [Cache reference data](<person-2-performance-and-db/task-22 Cache reference data.md>) | person-2-performance-and-db | Performance, DB | 03, 06 |
| 23 | [Security headers, upload checks and request limits](<person-1-security-and-auth/task-23 Security headers, upload checks and request limits.md>) | person-1-security-and-auth | Security | - |
| 24 | [One way to return results and errors](<person-3-api-and-pagination/task-24 One way to return results and errors.md>) | person-3-api-and-pagination | API, Code quality | - |
| 25 | [Split service registration by module](<person-4-deployment-and-architecture/task-25 Split service registration by module.md>) | person-4-deployment-and-architecture | Architecture, Code quality | - |
| 26 | [Remove blocking calls and tune EF for hot paths](<person-2-performance-and-db/task-26 Remove blocking calls and tune EF for hot paths.md>) | person-2-performance-and-db | Performance, DB | 02 |
| 27 | [Cross-tenant and object access tests for UAT](<person-1-security-and-auth/task-27 Cross-tenant and object access tests for UAT.md>) | person-1-security-and-auth | Security | 15 |
| 28 | [Remove duplicate and dead code](<person-3-api-and-pagination/task-28 Remove duplicate and dead code.md>) | person-3-api-and-pagination | Code quality | - |
| 29 | [Load test and release gate](<person-4-deployment-and-architecture/task-29 Load test and release gate.md>) | person-4-deployment-and-architecture | Performance, Deployment | 06, 17 |
| 30 | [Data growth, retention and clean-up](<person-2-performance-and-db/task-30 Data growth, retention and clean-up.md>) | person-2-performance-and-db | DB, Scaling | 18 |

## Tasks per person

### person-4-deployment-and-architecture

- 01: [Container image for the API](<person-4-deployment-and-architecture/task-01 Container image for the API.md>)
- 05: [UAT settings and secrets](<person-4-deployment-and-architecture/task-05 UAT settings and secrets.md>)
- 09: [Deploy pipeline and database migration step](<person-4-deployment-and-architecture/task-09 Deploy pipeline and database migration step.md>)
- 13: [Background jobs safe for more than one instance](<person-4-deployment-and-architecture/task-13 Background jobs safe for more than one instance.md>)
- 17: [Logs, metrics and alerts for UAT](<person-4-deployment-and-architecture/task-17 Logs, metrics and alerts for UAT.md>)
- 21: [Shared state for running more than one instance](<person-4-deployment-and-architecture/task-21 Shared state for running more than one instance.md>)
- 25: [Split service registration by module](<person-4-deployment-and-architecture/task-25 Split service registration by module.md>)
- 29: [Load test and release gate](<person-4-deployment-and-architecture/task-29 Load test and release gate.md>)

### person-2-performance-and-db

- 02: [One database connection per request](<person-2-performance-and-db/task-02 One database connection per request.md>)
- 06: [Measure every endpoint against 200 ms](<person-2-performance-and-db/task-06 Measure every endpoint against 200 ms.md>)
- 10: [Response compression and smaller payloads](<person-2-performance-and-db/task-10 Response compression and smaller payloads.md>)
- 14: [Remove query-in-a-loop patterns](<person-2-performance-and-db/task-14 Remove query-in-a-loop patterns.md>)
- 18: [Index review from real query data](<person-2-performance-and-db/task-18 Index review from real query data.md>)
- 22: [Cache reference data](<person-2-performance-and-db/task-22 Cache reference data.md>)
- 26: [Remove blocking calls and tune EF for hot paths](<person-2-performance-and-db/task-26 Remove blocking calls and tune EF for hot paths.md>)
- 30: [Data growth, retention and clean-up](<person-2-performance-and-db/task-30 Data growth, retention and clean-up.md>)
- 45: [Rate limit settings table](<person-2-performance-and-db/task-45 Rate limit settings table.md>)
- 46: [Rate limit settings from the database](<person-2-performance-and-db/task-46 Rate limit settings from the database.md>)
- 47: [Admin API for rate limits](<person-2-performance-and-db/task-47 Admin API for rate limits.md>)
- 48: [Admin app screens for rate limits](<person-2-performance-and-db/task-48 Admin app screens for rate limits.md>)
- 49: [Rate limit rejection counts for admins](<person-2-performance-and-db/task-49 Rate limit rejection counts for admins.md>) (optional)

### person-1-security-and-auth

- 03: [Cut the per-request session lookups](<person-1-security-and-auth/task-03 Cut the per-request session lookups.md>)
- 07: [Fix the global rate limiter](<person-1-security-and-auth/task-07 Fix the global rate limiter.md>)
- 11: [Secrets out of the repository and scans in CI](<person-1-security-and-auth/task-11 Secrets out of the repository and scans in CI.md>)
- 15: [Deny by default for every endpoint](<person-1-security-and-auth/task-15 Deny by default for every endpoint.md>)
- 19: [Validation coverage and shared rules](<person-1-security-and-auth/task-19 Validation coverage and shared rules.md>)
- 23: [Security headers, upload checks and request limits](<person-1-security-and-auth/task-23 Security headers, upload checks and request limits.md>)
- 19b: Validation coverage for the remaining commands (follow-up of 19, file arrives with PR #240)
- 27: [Cross-tenant and object access tests for UAT](<person-1-security-and-auth/task-27 Cross-tenant and object access tests for UAT.md>)
- 32: [One permission catalogue in code](<person-1-security-and-auth/task-32 One permission catalogue in code.md>)
- 33: [Safe add, rename and retire of permissions](<person-1-security-and-auth/task-33 Safe add, rename and retire of permissions.md>)
- 39 (39a): [Foreign keys and check constraints](<person-1-security-and-auth/task-39 Foreign keys and check constraints.md>)
- 39b to 39g: follow-ups of 39 (remaining core keys, audit column keys, tenant keys, other modules, remaining check constraints, cascade delete review), files arrive with PR #240
- 44: [Sensitive data in API responses](<person-1-security-and-auth/task-44 Sensitive data in API responses.md>)

### person-3-api-and-pagination

- 04: [One pagination contract for the whole API](<person-3-api-and-pagination/task-04 One pagination contract for the whole API.md>)
- 08: [Paginate Work Management lists](<person-3-api-and-pagination/task-08 Paginate Work Management lists.md>)
- 12: [Paginate Leave, Attendance and People lists](<person-3-api-and-pagination/task-12 Paginate Leave, Attendance and People lists.md>)
- 16: [API versioning](<person-3-api-and-pagination/task-16 API versioning.md>)
- 20: [Fewer, smaller requests for dashboard and calendar](<person-3-api-and-pagination/task-20 Fewer, smaller requests for dashboard and calendar.md>)
- 24: [One way to return results and errors](<person-3-api-and-pagination/task-24 One way to return results and errors.md>)
- 28: [Remove duplicate and dead code](<person-3-api-and-pagination/task-28 Remove duplicate and dead code.md>)
