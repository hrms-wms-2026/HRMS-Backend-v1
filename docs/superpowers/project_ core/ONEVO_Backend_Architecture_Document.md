# ONEVO Backend Architecture Document

## 1. Introduction

ONEVO backend is designed as a production-ready, multi-tenant SaaS backend for HRMS and Work Management modules. The architecture must protect tenant isolation, security, privacy, maintainability, and long-term scalability while keeping the development model clear for the backend team.

This document explains what the backend architecture is, how requests flow through the system, how code must be placed, how non-functional requirements are handled, how the system is tested, and how production operations such as deployment, monitoring, and disaster recovery should work.

### 1.1 Goals

- Keep controllers thin and business logic inside application use cases.
- Keep Domain independent from API, Infrastructure, and external providers.
- Enforce tenant isolation at multiple levels, not only at the UI.
- Support secure authentication, authorization, CSRF protection, and session control.
- Provide a clear folder structure for new features and subfeatures.
- Define production-level NFRs for performance, security, availability, reliability, privacy, deployment, monitoring, and disaster recovery.
- Make the backend easy to review, test, extend, and operate.

### 1.2 Technology Stack

| Area | Technology / Approach |
|---|---|
| Backend framework | ASP.NET Core Web API |
| Architecture | Clean Architecture, CQRS, MediatR |
| Database | PostgreSQL |
| ORM / DB driver | EF Core + Npgsql |
| Tenant isolation | Host tenant resolution, application tenant context, EF filters, PostgreSQL RLS |
| Authentication | Tenant secure cookies, admin secure cookies |
| Authorization | Server-side roles, policies, permissions, and tenant/business scope checks |
| Validation | FluentValidation |
| Logging | Serilog with correlation ID |
| API documentation | Swagger / OpenAPI |
| Cache | In-memory cache |
| Object storage | Cloudflare R2 for private files and documents |
| Testing | Unit tests, integration tests, architecture tests, Testcontainers for PostgreSQL |
| CI/CD | Restore, build, unit tests, integration tests, architecture tests |

### 1.3 Main Architecture Decision

ONEVO backend uses **Clean Architecture with CQRS through MediatR**.

```text
HTTP Request
  -> ONEVO.Api middleware
  -> Tenant/Auth/Security checks
  -> Controller
  -> MediatR Command or Query
  -> Application pipeline behaviors
  -> Application handler
  -> Application-owned interface
  -> Infrastructure implementation
  -> EF Core/PostgreSQL or external dependency
  -> Result/DTO
  -> HTTP response
```

Every backend change must fit into this flow. If a new feature cannot be placed cleanly into this flow, the design is incomplete and must be reviewed before implementation.

---

## 2. Functional Requirements

### 2.1 Modules

ONEVO backend is organized into layers and feature/subfeature folders. New work must reuse an existing feature/subfeature when the capability belongs there.

#### 2.1.1 ONEVO.Api

`ONEVO.Api` is the HTTP boundary.

**Responsible for:**

- App startup and dependency registration.
- Middleware pipeline ordering.
- HTTP routing and versioned endpoints.
- Request and response translation.
- Authentication and authorization enforcement.
- Tenant, admin, and system route enforcement.
- CSRF protection.
- Rate limiting.
- Swagger/OpenAPI.
- Health check endpoints.

**Not responsible for:**

- Business workflow decisions.
- Database access.
- Entity persistence.
- Cross-feature orchestration outside HTTP concerns.

**Canonical folder structure:**

```text
src/ONEVO.Api/
  Auth/
  Configuration/
  Controllers/
    Admin/
      {Feature}/
        {SubFeature}/
    Customer/
      {Feature}/
        {SubFeature}/
  Extensions/
  Filters/
  Middleware/
  Properties/
  Program.cs
```

Folder purpose:

| Folder | Why it exists |
|---|---|
| `Auth/` | API authentication boundary helpers live here when the code is specific to HTTP authentication. |
| `Configuration/` | API-specific configuration models and setup helpers live here. |
| `Controllers/Admin/` | Platform/admin endpoints live here. These endpoints use `/admin/v1/...` routes and admin policies. |
| `Controllers/Customer/` | Tenant/customer endpoints should live here when the controller structure is migrated to customer/admin separation. These endpoints use `/api/v1/...` routes and tenant policies. |
| `Extensions/` | Startup registrations are grouped here, such as authentication, authorization, CORS, and Swagger setup. This keeps `Program.cs` small. |
| `Filters/` | Controller/action-level HTTP checks live here when middleware is too broad for the rule. |
| `Middleware/` | Request pipeline checks live here, such as exception handling, correlation ID, tenant resolution, CSRF, rate limiting, permission version checks, and tenant enforcement. |
| `Properties/` | ASP.NET launch/runtime project properties live here. |
| `Program.cs` | API host startup, middleware ordering, dependency registration, and endpoint mapping live here. |

Current code note: the backend currently has `Controllers/Admin`, `Controllers/Auth`, `Controllers/DevPlatform`, and `Controllers/Webhooks`. Admin APIs are already separated under `Controllers/Admin`; tenant/customer APIs are still feature-grouped under folders such as `Auth` and `DevPlatform`.

Controller rules:
- Use `[ApiController]`.
- Use versioned routes such as `/api/v1/...` and `/admin/v1/...`.
- Admin controllers must live under `Controllers/Admin/{Feature}/{SubFeature}/`.
- Customer controllers must live under `Controllers/Customer/{Feature}/{SubFeature}/`.
- Admin routes use `/admin/v1/...`.
- Customer routes use `/api/v1/...`.
- Accept request DTOs or route/query parameters.
- Create a command or query.
- Call `await _mediator.Send(...)`.
- Convert `Result` into the correct HTTP response.
- Keep permission requirements visible through policy attributes or permission filters.
- Do not inject or use `ApplicationDbContext` inside controllers.

#### 2.1.2 ONEVO.Application

`ONEVO.Application` is the use-case layer. Most business behavior belongs here.

**Responsible for:**

- Commands and command handlers.
- Queries and query handlers.
- FluentValidation validators.
- DTOs.
- Mappers.
- Pure application helpers.
- Repository interfaces.
- Service interfaces.
- MediatR pipeline behaviors.
- Application-level security rules.

**Not responsible for:**

- EF Core implementation.
- HTTP middleware.
- Concrete email, payment, storage, or external providers.
- Database migrations.
- ASP.NET-specific request handling.

**Canonical folder structure:**

```text
src/ONEVO.Application/
  Common/
    Behaviors/
    Exceptions/
    Models/
    RepositoryInterfaces/
    Security/
    ServiceInterfaces/
  Features/
    {Feature}/
      {SubFeature}/
        Commands/
          {UseCase}/
        Queries/
          {UseCase}/
        DTOs/
          Requests/
          Responses/
        Mappers/
        Helpers/
        RepositoryInterfaces/
        ServiceInterfaces/
  DependencyInjection.cs
```

Folder purpose:

| Folder | Why it exists |
|---|---|
| `Common/Behaviors/` | MediatR pipeline behaviors live here. The current backend uses this for validation, logging, performance warnings, and unhandled exception logging. |
| `Common/Exceptions/` | Shared application exceptions live here. |
| `Common/Models/` | Shared application models live here, such as result/auth/session-related models. |
| `Common/RepositoryInterfaces/` | Shared repository contracts live here. Application defines the contract; Infrastructure implements it. |
| `Common/Security/` | Application-level security contracts/rules live here when they are not tied to ASP.NET or EF implementation. |
| `Common/ServiceInterfaces/` | Shared service contracts live here for email, auth, encryption, tenant context, and other system services. |
| `Features/{Feature}/{SubFeature}/Commands/` | Mutating use cases live here, such as create, update, delete, login, logout, assign, approve, or revoke. |
| `Features/{Feature}/{SubFeature}/Queries/` | Read-only use cases live here. Queries must not change database state. |
| `DTOs/Requests/` | Request objects used by API/application use cases live here. |
| `DTOs/Responses/` | Response objects returned to API clients live here. |
| `Mappers/` | Entity-to-DTO or result mapping lives here so handlers stay focused on use-case flow. |
| `Helpers/` | Pure reusable application logic lives here. It must not contain EF, HTTP, or provider code. |
| `RepositoryInterfaces/` | Feature-specific data access contracts live here. |
| `ServiceInterfaces/` | Feature-specific system/external service contracts live here. |
| `DependencyInjection.cs` | Application services, MediatR, validators, and pipeline behaviors are registered here. |

Application rules:
- Commands mutate state and may call `IUnitOfWork.SaveChangesAsync`.
- Queries are read-only and must not mutate state.
- Queries should return DTOs, not tracked EF entities.
- Repeated or non-trivial mapping belongs in `Mappers/`.
- Helpers must be pure calculations or reusable logic only.
- Repository and service contracts are owned by Application and implemented in Infrastructure.

#### 2.1.3 ONEVO.Domain

`ONEVO.Domain` contains the business model.

**Responsible for:**

- Entities.
- Domain events.
- Domain errors/exceptions.
- Base entity types.
- Tenant-owned entity markers.

**Not responsible for:**

- EF Core configuration.
- Repository implementation.
- HTTP concerns.
- External providers.
- Application DTOs.

**Canonical folder structure:**

```text
src/ONEVO.Domain/
  Common/
  Errors/
  Features/
    {Feature}/
      {SubFeature}/
        Entities/
        Events/
  Lookups/
```

Folder purpose:

| Folder | Why it exists |
|---|---|
| `Common/` | Shared domain primitives live here. The current backend uses this for `BaseEntity`, `IDomainEvent`, and `ITenantOwnedEntity`. |
| `Errors/` | Domain/business error definitions live here. |
| `Features/{Feature}/{SubFeature}/Entities/` | Business entities live here, grouped by feature and subfeature. |
| `Features/{Feature}/{SubFeature}/Events/` | Domain events live here when a business action needs a domain-level event. |
| `Lookups/` | Shared lookup/master data domain objects live here. |

Domain rules:
- Domain must not reference Application, Infrastructure, or API.
- Tenant-scoped entities must be reviewed for `ITenantOwnedEntity`.
- Domain exceptions should represent business rule violations, not infrastructure failures.

#### 2.1.4 ONEVO.Infrastructure

`ONEVO.Infrastructure` implements persistence and external integrations.

**Responsible for:**

- `ApplicationDbContext`.
- EF Core configurations and migrations.
- Repository implementations.
- Unit of Work implementation.
- Identity, token, and session implementations.
- Email, payment, storage, and external service adapters.
- Security implementations.
- Cache implementation.
- Seeders.
- Health check dependency registrations.

**Not responsible for:**

- API endpoint definitions.
- Business use-case orchestration.
- Controller behavior.

**Canonical folder structure:**

```text
src/ONEVO.Infrastructure/
  Caching/
  Configuration/
  ExternalServices/
    Email/
    Messaging/
  Identity/
  Migrations/
  Persistence/
    Configurations/
      {Feature}/
        {SubFeature}/
    Interceptors/
    Repositories/
      {Feature}/
        {SubFeature}/
    Seeders/
  Security/
  Services/
    {Feature}/
      {SubFeature}/
  DependencyInjection.cs
```

Folder purpose:

| Folder | Why it exists |
|---|---|
| `Caching/` | Cache implementations live here. Application code should depend on cache contracts, not direct cache implementation. |
| `Configuration/` | Infrastructure-specific options and configuration classes live here. |
| `ExternalServices/` | Third-party provider adapters live here, such as email, storage, payment, calendar, messaging, or Teams integrations. |
| `Identity/` | Authentication implementation details live here, such as session/token handling, password hashing, MFA, current user, and external identity validation. |
| `Migrations/` | EF Core database migration files live here. Schema changes must be tracked here. |
| `Persistence/Configurations/` | EF Core entity mapping, table names, relationships, constraints, and indexes live here. This keeps database mapping outside Domain entities. |
| `Persistence/Interceptors/` | EF Core/database interceptors live here for cross-cutting persistence behavior. The current backend uses this for audit fields, soft delete, domain event dispatch, and PostgreSQL RLS tenant session variables. |
| `Persistence/Repositories/` | EF Core repository implementations live here. These implement Application repository interfaces. |
| `Persistence/Seeders/` | Default or initial data seeding logic lives here. |
| `Security/` | Infrastructure security implementations live here, such as encryption and security helper implementations. |
| `Services/` | Infrastructure implementations of Application service interfaces live here. |
| `DependencyInjection.cs` | Infrastructure services, DbContext, repositories, interceptors, identity services, security services, and external providers are registered here. |

Infrastructure rules:
- Repository implementations go under `Persistence/Repositories/{Feature}/{SubFeature}`.
- EF configurations go under `Persistence/Configurations/{Feature}/{SubFeature}`.
- Non-EF feature service implementations go under `Services/{Feature}/{SubFeature}`.
- External provider adapters go under `ExternalServices/{ProviderArea}`.
- Raw SQL touching tenant-owned data must be reviewed for tenant filtering and RLS behavior.

### 2.2 API Specifications

- Public APIs must be versioned.
- Prefer URL-based versioning:

```text
/api/v1/...
/api/v2/...
/admin/v1/...
```

- Breaking changes require a new API version.
- Existing API versions must have a support window.
- Minimum support window target is 12 months.
- Deprecation notice target is at least 90 days.
- API contracts must be documented through Swagger/OpenAPI.

Admin and customer APIs must be separated:

| API area | Route pattern | Auth scheme | Policy |
|---|---|---|---|
| Customer API | `/api/v1/...` | `TenantScheme` | `TenantPolicy` |
| Admin API | `/admin/v1/...` | `AdminScheme` | `AdminPolicy`, `AdminWritePolicy`, `AdminSuperPolicy` |

Rules:

- Customer users access customer APIs through secure session/cookie authentication.
- Admin users access admin APIs through secure session/cookie authentication.
- Tenant host must not access admin routes.
- Admin host must not access customer API routes.
- Tenant token `tenant_id` must match the resolved request host tenant.

### 2.3 Error Handling

All errors must use structured responses with correlation IDs. Stack traces must not be leaked to clients.

Required error shape:

```json
{
  "type": "https://onevo.com/errors/example",
  "title": "Error title",
  "status": 400,
  "detail": "Human-readable detail",
  "correlationId": "..."
}
```

Validation failures must include field-level details. Business rule failures must return clear domain/application errors. Unexpected failures must return safe generic messages.

---

## 3. Development Architecture

### 3.1 Layer Structure

```text
Client / Browser / Admin Console
        |
        v
ONEVO.Api
HTTP host, controllers, middleware, auth, CORS, Swagger, health endpoints
        |
        v
ONEVO.Application
Use cases, commands, queries, handlers, validators, DTOs, mappers, interfaces
        |
        v
ONEVO.Infrastructure
EF Core, PostgreSQL, repositories, identity, security, external service adapters
        |
        v
ONEVO.Domain
Entities, domain contracts, shared domain primitives
```

Dependency direction:

```text
ONEVO.Api
  -> ONEVO.Application
  -> ONEVO.Domain

ONEVO.Infrastructure
  -> ONEVO.Application
  -> ONEVO.Domain
```

Rules:

- Domain must not reference Application, Infrastructure, or API.
- Application must not reference Infrastructure implementations.
- Application owns repository and service interfaces.
- Infrastructure implements Application interfaces.
- API calls business use cases through MediatR.
- Controllers must not contain business workflows.
- Handlers must not depend on controllers, middleware, or `HttpContext`.

### 3.2 Request Processing Pattern

Current request flow:

```text
1. Client sends request
2. ONEVO.Api receives request
3. ExceptionHandlerMiddleware
4. CorrelationIdMiddleware
5. Swagger in Development
6. Serilog request logging
7. HTTPS redirection outside Development
8. CORS
9. Forwarded headers
10. HostTenantResolutionMiddleware
11. AuthRateLimitingMiddleware
12. Authentication
13. CsrfProtectionMiddleware
14. TenantEnforcementMiddleware
15. PermissionVersionMiddleware
16. Authorization
17. Controller action
18. IMediator.Send(...)
19. MediatR behaviors
20. Command/Query handler
21. Application interface
22. Infrastructure implementation
23. EF Core / PostgreSQL / external service
24. Result/DTO returned
25. HTTP response returned
```

MediatR pipeline:

```text
Controller
  -> IMediator.Send(command/query)
    -> UnhandledExceptionBehavior
    -> ValidationBehavior
    -> LoggingBehavior
    -> PerformanceBehavior
    -> Handler
```

| Behavior | Required purpose |
|---|---|
| UnhandledExceptionBehavior | Log unexpected use-case errors and allow API middleware to shape the response |
| ValidationBehavior | Run FluentValidation before handlers |
| LoggingBehavior | Log request execution with user and tenant context |
| PerformanceBehavior | Warn on slow application requests |

### 3.3 Tenant Isolation Pattern

Tenant isolation must be layered. ONEVO must not rely on only one layer.

```text
Host / Subdomain
  -> HostTenantResolutionMiddleware
  -> TenantContextAccessor
  -> TenantEnforcementMiddleware
  -> Session/Auth tenant claim check
  -> Application handlers use current tenant
  -> EF Core query filter on ITenantOwnedEntity
  -> TenantRlsInterceptor sets DB session variables
  -> PostgreSQL RLS policy enforces tenant_id boundary
```

Tenant context modes:

| Mode | Meaning |
|---|---|
| tenant | Tenant API request. Tenant data must be scoped |
| admin | Platform/admin context. Admin routes are allowed |
| system | Root/system host context |

Rules:

- Tenant APIs must run under tenant context.
- Admin APIs must run under admin context.
- Tenant host must not access admin routes.
- Admin host must not access customer API routes.
- Tenant authenticated requests must match the resolved host tenant.
- Tenant-owned entities must implement `ITenantOwnedEntity`.
- Raw SQL must preserve tenant boundaries.

### 3.4 Authentication and Authorization

#### Tenant User Authentication

Tenant users use secure cookies.

```text
Login request
  -> tenant resolved from host
  -> email/password validated
  -> optional MFA challenge
  -> ASP.NET Core Cookie Authentication signs in the user
  -> database-backed session state is created through ITicketStore
  -> onevo_session HttpOnly cookie set
  -> onevo_csrf readable cookie set
```

Implementation mechanism: browser authentication uses ASP.NET Core Cookie Authentication with `ITicketStore`. The cookie is protected by ASP.NET Core's cookie/data-protection system and acts as opaque session material. The authoritative session record remains server-side in the database.

**Verified end-to-end flow (code-verified, see `docs/superpowers/workflow/authentication.md`):**

Tenant browser login is base-domain credential-first, not tenant-host password login:

```text
1. Browser submits email/password to the base/system host: POST /api/v1/auth/login
2. BaseLoginCommandHandler fetches all tenant/user candidates for that email across
   every tenant and verifies the password with a fixed-work-factor timing-safe check
   (always exactly 8 BCrypt comparisons, padded with a dummy hash)
3. Zero/overflow matches -> generic 401 (enumeration-safe)
   Multiple matches (2-8) -> workspace-selection challenge (5-minute, single-use)
   Exactly one match -> LoginContinuationService.ContinueAsync
4. Continuation order: must_change_password -> MFA challenge (if verified TOTP exists)
   -> legal-acceptance gate -> finalize
5. Finalization is explicit, not host-inferred:
     BaseDomainExchange  -> issues a 2-minute opaque one-time exchange code, no cookie set yet
     TenantHostDirect    -> sets the real onevo_session/onevo_csrf cookies immediately
6. Browser follows continue_url to the tenant subdomain's /auth/continue?code=...
   -> POST /api/v1/auth/session-exchange consumes the code and finally sets
      onevo_session + onevo_csrf on the correct tenant host
```

Session lifecycle values (tenant `sessions` table and admin `platform_user_sessions`, same policy for both): sliding window 30 minutes, renewal threshold 15 minutes, absolute lifetime 8 hours hard cap regardless of activity, revocation is DB-flag based (`IsRevoked=true`) and immediate on logout.

Rules:

- Tenant web auth uses HttpOnly secure session cookies.
- Admin web auth uses separate HttpOnly secure admin/platform session cookies.
- Tenant/admin frontend JSON responses must not contain accessToken, refreshToken, jwt, tokenType, or bearer token fields.
- Session cookies must be HttpOnly.
- CSRF token may be frontend-readable only so the frontend can send X-CSRF-Token.
- CSRF token must be bound to the server-side session or otherwise validated against server-side session state.
- JWTs are allowed only for separately documented non-browser device/agent/IDE/API-token flows. They are not used for tenant/admin browser sessions.
- Browser session renewal is handled by ASP.NET Core Cookie Authentication sliding expiration and the database-backed `ITicketStore` session store. The frontend does not call a refresh endpoint and never receives token renewal material.
- Passwords must be hashed with BCrypt or a reviewed stronger replacement.
- MFA state must come from MFA records, not a loose user profile flag.

#### Admin Authentication

Admin users use secure cookies through the `AdminScheme`. Admin authentication must create an admin session and set secure admin cookies.

| Policy | Requirement |
|---|---|
| AdminPolicy | Authenticated admin with `platform_role` |
| AdminWritePolicy | `platform_role` is `super_admin` or `admin` |
| AdminSuperPolicy | `platform_role` is `super_admin` |

Authorization rules:

- Do not authorize based on frontend state.
- Do not rely only on profile fields for roles.
- Roles and permissions must be checked server-side.
- Tenant-facing permission checks should use permission claims/resolver patterns.

#### Permission Handling

Permission handling is defined in `permission-handling.md` and must be followed by backend features.

Purpose:

- The backend must make the final permission decision.
- Frontend button visibility is only for user experience.
- Hidden buttons must never be treated as security.
- Protected APIs must return `403 Forbidden` when the authenticated user does not have the required permission.
- APIs must return `401 Unauthorized` when the user is not authenticated or the session is expired.

Permission flow:

```text
API request
  -> resolve tenant or admin context
  -> validate tenant if tenant user request
  -> set tenant context
  -> authenticate user/admin using secure cookie session
  -> validate CSRF token where required
  -> apply tenant isolation and PostgreSQL RLS for tenant data
  -> read trusted identity from validated session/claims
  -> resolve tenant user permissions or admin policy
  -> compare required permission/policy
  -> allow or return 403 Forbidden
```

Tenant user permission model:

```text
User
  -> Position
  -> Role
  -> RolePermissions
  -> Permissions
  -> Feature
```

Rules:

- Tenant permission checks must include `tenant_id`.
- Do not find roles, positions, or permissions by ID only when tenant scope is required.
- Correct tenant queries must include the current tenant context.
- Permission resolver must calculate final permissions after tenant, module, role, and permission rules.
- Permissions should use consistent seeded permission codes such as `employee.read`, `employee.create`, `employee.update`, and `employee.delete`.
- Each protected endpoint should declare the required permission or policy through authorization policy/filter attributes.
- Admin APIs use admin policies such as `AdminPolicy`, `AdminWritePolicy`, and `AdminSuperPolicy`.
- Tenant user cookies and admin cookies must be separate.

Cookie separation:

```text
Tenant browser cookies:
  onevo_session
  onevo_csrf

Admin/platform browser cookies:
  admin_session
  admin_csrf
```

Frontend rule:

```text
Frontend:
  -> show or hide actions for user experience
  -> call backend API

Backend:
  -> validate session, CSRF, tenant, RLS, and permission
  -> allow request or return 401/403
```

#### Documented Gaps (verified against code, see `docs/superpowers/workflow/authentication.md` §12)

These are known, unresolved items — not proposed changes:

- `IJwtTokenService.GenerateDeviceToken` is registered in DI but has zero call sites in `src/` — unused scaffolding for an unbuilt device/agent auth surface.
- The legacy `RefreshToken`/`IRefreshTokenRepository` table is only touched by `ResetPasswordCommandHandler` (revocation on password reset) — no login path ever issues one.
- `ForcePasswordChangeCommandHandler` requires tenant-host context, but the continue-URL is built from the host where login started — a base-domain-triggered forced password change can produce an unreachable `continue_url`.
- No multi-session/device management exists on either tenant or admin side (no listing or selective revocation of concurrent sessions).
- No permission-gating UI consumer exists yet; `permissions[]`/`activeModules[]` are fetched and stored by the frontend but not read for conditional rendering.

Per [[PROCESS_RULES]] rule 4, this list is sourced from the code-verified report and must be kept in sync with it — update both together if either changes.

### 3.5 Database Architecture

Persistence flow:

```text
Application Handler
  -> Repository/Service Interface
  -> Infrastructure EF Repository/Service
  -> ApplicationDbContext
  -> EF Core Configuration
  -> Npgsql
  -> PostgreSQL
```

Rules:

- Application defines interfaces.
- Infrastructure implements interfaces.
- `ApplicationDbContext` stays in Infrastructure.
- Database changes require migrations.
- Mutating use cases should call `IUnitOfWork.SaveChangesAsync` once at the end where practical.
- Queries should return DTOs, not tracked EF entities.
- Tenant-owned tables must be covered by tenant query filters and PostgreSQL RLS where applicable.

### 3.6 Outbox Pattern

Some HRMWS actions update database data and also need a side effect, such as sending a notification, revoking access, generating a report, or informing another module. For those workflows, ONEVO must use the Outbox Pattern.

Required cases:

- Employee lifecycle changes that trigger access, payroll, leave, or notification actions.
- Leave, expense, document, workflow, and payroll events that trigger notifications or background processing.
- Data export, retention purge, compliance deletion, and integration events.
- Email, notification, calendar, Teams, payment, and future integration events.

Architecture approach:

- The business data change and outbox event are saved in the same database transaction.
- A background worker reads pending outbox events and publishes them.
- Failed events are retried safely.
- Events must not contain secrets, raw tokens, or unnecessary personal data.
- Consumers must handle duplicate delivery safely.

Code-level foundation:

```text
Application command handler
  -> update business data
  -> add outbox event
  -> save transaction once

Background worker
  -> read pending events
  -> publish notification/integration/background action
  -> mark event as published or failed
```

Rule:

- If a committed database change needs an external side effect, the side effect must not be published directly from the controller.

### 3.7 Caching

Caching is used to reduce repeated database reads for stable, read-heavy data. ONEVO uses in-memory cache for the early/single-instance stage.

Caching is not the source of truth. PostgreSQL remains the source of truth.

#### Why We Use Caching

| Purpose | Explanation |
|---|---|
| Reduce repeated database reads | Same stable data can be read many times, so cache avoids repeated database calls. |
| Improve API response time | Read-heavy endpoints can return faster when cached data is available. |
| Support permission checks | Permission, role, and module lookups are used often during authorization. |
| Reduce load during peak usage | Cache helps reduce database pressure for stable lookup and configuration data. |

#### What We Cache

| Data | Why cache it |
|---|---|
| Tenant basic profile/settings | Used often during tenant resolution and request validation. |
| Tenant enabled modules/features | Used for module access and authorization checks. |
| Permission catalog | System permission list changes rarely and is read often. |
| Role/permission lookup result | Used during permission checks; cache with tenant-aware keys and short TTL. |
| Subscription plans/module catalog | Platform-level data that changes rarely. |
| Reference/lookups | Countries, enum-backed statuses, dropdown values, and stable configuration lookups. |

#### What We Do Not Cache

| Data | Reason |
|---|---|
| Passwords | Security risk. |
| Raw tokens or refresh tokens | Must never be exposed or stored in cache. |
| Bank details, NIC/passport, national identifiers | Sensitive PII. |
| Medical, biometric, grievance, or investigation data | Restricted personal data. |
| Payroll/salary details | Confidential and high-risk. |
| Screenshots or verification photos | Sensitive file data; use secure object storage rules. |
| Large employee lists | Use pagination, filtering, and indexing instead. |
| Frequently changing workflow states | Stale cache can cause wrong decisions. |

#### Cache Key Rules

Cache keys must be predictable and tenant-aware.

Examples:

```text
tenant:{tenantId}:settings
tenant:{tenantId}:modules
tenant:{tenantId}:permissions:user:{userId}
tenant:{tenantId}:role:{roleId}:permissions
platform:subscription-plans
platform:permission-catalog
lookup:{tenantId}:job-levels
```

Rules:

- Include `tenantId` for tenant-specific data.
- Use `platform:` prefix only for global platform data.
- Do not include raw secrets, tokens, email addresses, NIC/passport numbers, or other sensitive values in cache keys.
- User-specific permission cache must include both tenant and user identity.
- Cache key format must be consistent across features.

#### Cache Expiry and Invalidation

Cache must expire automatically and must also be invalidated when related data changes.

Suggested TTL:

| Data | Suggested TTL |
|---|---:|
| Tenant settings | 5-15 minutes |
| Tenant enabled modules/features | 5-15 minutes |
| User permission result | 2-5 minutes |
| Role permission result | 5-15 minutes |
| Permission catalog | 30-60 minutes |
| Subscription plans/module catalog | 30-60 minutes |
| Reference/lookups | 30-60 minutes |

Invalidation rules:

- When tenant settings change, clear `tenant:{tenantId}:settings`.
- When tenant modules/subscription changes, clear `tenant:{tenantId}:modules`.
- When role permissions change, clear role and affected user permission cache.
- When user position/role changes, clear that user's permission cache.
- When platform permission catalog changes, clear `platform:permission-catalog`.
- When lookup data changes, clear the related lookup cache.

#### Code-Level Foundation

```text
Application handler/query
  -> check cache using tenant-aware key
  -> if cache hit, return cached DTO/result
  -> if cache miss, read from repository/database
  -> store safe result in cache with TTL
  -> return result
```

Rules:

- Cache DTOs/read models, not tracked EF entities.
- Cache only data that is safe to reuse.
- Do not use cache to bypass authorization or tenant isolation.
- Permission cache must still be scoped by tenant and user.
- In-memory cache is acceptable for early/single-instance deployment.

### 3.8 New Feature Build Checklist

1. Identify `{Feature}` and `{SubFeature}`.
2. Confirm whether Domain entity/event/lookup is needed.
3. Add Application command/query folders.
4. Add request/response DTOs.
5. Add validators for non-trivial input.
6. Add handlers.
7. Add mappers for repeated or non-trivial mapping.
8. Add repository/service interfaces only when needed.
9. Add Infrastructure implementations under matching feature/subfeature path.
10. Add EF configurations and migrations for schema changes.
11. Add or update controller endpoints.
12. Add authorization policy or permission filter.
13. Check tenant context and tenant isolation.
14. Check error response behavior.
15. Add rate limiting, health/readiness, idempotency, or file rules when relevant.
16. Add unit tests.
17. Add integration tests if API, DB, auth, tenant, file, or idempotency behavior is involved.
18. Update Swagger/OpenAPI when endpoint behavior changes.

---

## 4. Non Functional Requirements

### 4.1 Performance

Targets:

| Metric | Target |
|---|---:|
| p50 latency | <= 150 ms |
| p95 latency | <= 400 ms |
| p99 latency | <= 800 ms |
| Warm-cache end-to-end interaction | <= 1.5 s p95 |

Rules:

- Use async database and external service calls.
- Avoid N+1 database queries.
- Add indexes for new query patterns.
- Use pagination for list endpoints.
- Cache read-heavy stable data where appropriate.
- Use background jobs for long-running operations.
- Use read replicas later for reporting/read-heavy queries only when primary DB read load becomes a bottleneck.

### 4.2 Security

Rules:

- HTTPS only in production.
- TLS 1.2 or later.
- HSTS enabled in production.
- CORS must use allow-listed frontend domains.
- Add security headers such as Content-Security-Policy, X-Frame-Options, and Referrer-Policy.
- Do not log secrets, raw tokens, payment data, or excessive PII.
- Log access to sensitive audit/security logs.

### 4.3 Scalability

Capacity targets:

| Scenario | Concurrent users | Sustained traffic | Burst traffic |
|---|---:|---:|---:|
| Baseline | 200 | 10 req/s | 30 req/s |
| Peak | 2,000 | 50 req/s | 150 req/s |

Architecture approach:

- API instances must be stateless for horizontal scaling.
- Use async database and external dependency calls.
- Use pagination for all list endpoints.
- Use tenant-aware caching for stable read-heavy data.
- Use background jobs for payroll, imports, exports, reports, and retention cleanup.
- Use database connection pooling with explicit production sizing.
- Use read replicas only for reporting/read-heavy queries when primary DB read load becomes a bottleneck.

### 4.4 Availability

Target: production API availability is **99.9% per month**, unless a stricter customer contract applies.

Architecture approach:

- Run API instances behind a load balancer or platform router.
- Keep `/health` for lightweight liveness.
- Keep `/health/ready` for dependency readiness.
- Use logs and health checks to monitor availability and errors.
- Planned maintenance must be announced at least 48 hours before the maintenance window when customer impact is expected.
- Dependency failures must return structured error responses with correlation IDs.

### 4.5 Reliability and Resilience

#### Rate Limiting

| Endpoint type | Limit |
|---|---|
| Public APIs | 100 requests/minute/IP |
| Auth APIs | 30 requests/minute/IP or stricter endpoint-specific rules |

Rules:

- Use broad public API rate limiting for production.
- Keep auth-specific limits stricter.
- Consider tenant-aware or user-aware policies for authenticated tenant APIs.

#### Idempotency

Critical mutating endpoints must accept a client-generated idempotency key.

Recommended header:

```text
Idempotency-Key: <client-generated-guid-or-random-key>
```

Use cases:

- Payments.
- Subscription upgrades/renewals.
- Payroll runs.
- Booking or allocation flows if introduced.
- Webhooks and critical external callbacks.

Rules:

- Duplicate requests with same key and same body return the original response.
- Same key with a different body returns `409 Conflict`.
- Keys must be scoped by tenant and user where applicable.

#### Session Timeout and Invalidation

Targets:

| Item | Target |
|---|---|
| Session cookie sliding window | 15-30 minutes |
| Inactive session timeout | 30 minutes |
| Absolute session lifetime | 8 hours |

Browser sessions do not use frontend refresh tokens or browser refresh endpoints. Session renewal is server-controlled through Cookie Authentication sliding expiration and the database-backed session store.

Rules:

- Sessions must track start time, last activity, inactive expiry, absolute expiry, and revoked state.
- Authentication/session middleware must validate session status on every authenticated request.
- Password change, account lock, manual logout, and security events must revoke active sessions.
- Expired or revoked sessions must return `401 Unauthorized` and clear auth cookies where applicable.

### 4.6 Privacy and Data Protection

HRMWS stores employee, payroll, identity, document, monitoring, and compliance data. These areas must be protected at architecture level before feature implementation.

#### Data Retention Policy

Retention must be defined by data category, not as one global setting.

Rules:

- Active employee data is kept while the employee is active.
- Former employee, payroll, tax, compensation, and audit records must follow legal retention requirements; default architecture minimum is 7 years where applicable.
- Short-lived files such as compliance exports, screenshots, verification photos, and session records must have shorter retention rules.
- Legal holds override deletion and anonymization.
- Retention jobs must be auditable.
- When a file is deleted, both database metadata and object storage data must be handled.

#### Personal Data Privacy Rules

Rules:

- Classify data as public, internal, confidential, sensitive PII, restricted, or secret.
- Enforce tenant isolation through application checks, EF query filters, and PostgreSQL RLS.
- Enforce access with RBAC and business scope checks.
- Do not log secrets, tokens, bank details, national identifiers, medical data, biometric data, photos, screenshots, or excessive PII.
- Sensitive exports require explicit permission.
- Consent must be captured where PDPA/GDPR processing requires it.

#### File and Document Storage Security

Rules:

- Store HRMS files in private object storage such as Cloudflare R2.
- Store only file metadata in PostgreSQL.
- Do not expose permanent public URLs for HR, payroll, identity, monitoring, report, or compliance files.
- File downloads must be authorized by tenant and domain permission.
- Private file access must use signed URLs or authorized API streaming.
- Uploads must validate size, MIME/content type, extension, and generated storage name.
- Uploaded files must be scanned before download availability.
- Unsafe files must stay quarantined.

Required file states:

```text
pending_scan
available
quarantined
deleted
```

#### Storage Quota and File Storage Handling

Storage quota management controls how much file storage each tenant can use. The backend must enforce quota; the frontend may only display usage.

Core calculation:

```text
Total Allowed Storage = Plan Storage + Extra Purchased Storage
Total Used Storage = Used R2 Storage + Counted DB Storage + Reserved R2 Storage
Remaining Storage = Total Allowed Storage - Total Used Storage
```

What counts as storage:

| Storage type | Rule |
|---|---|
| R2 file storage | Count exact file size for active tenant files. |
| Generated files | Count reports, exports, imports, and attachments stored in R2. |
| Normal database rows | Do not count normal relational rows such as employees, roles, permissions, settings, and attendance rows. |
| Large/generated DB records | Count only if strict billing or large payload storage requires it. |

Default architecture decision:

- Enforce tenant storage quota mainly on Cloudflare R2 file storage.
- Store file metadata and quota statistics in PostgreSQL.
- Keep normal database storage as monitoring unless billing requires strict DB-size accounting.
- Do not call Cloudflare R2 `ListObjects` during every upload.
- Use PostgreSQL quota/stat tables for upload-time quota checks.

Required data concepts:

```text
subscription_plans.feature_limits_json
tenant_subscriptions
tenant_storage_addons
tenant_storage_stats
tenant_files
```

Required quota fields:

```text
tenant_id
plan_storage_bytes
extra_storage_bytes
used_r2_bytes
used_db_bytes
reserved_r2_bytes
last_calculated_at
updated_at
```

Required file metadata fields:

```text
id
tenant_id
storage_key
original_file_name
content_type
file_size_bytes
uploaded_by_user_id
status
created_at
deleted_at
```

Storage key rule:

```text
tenants/{tenantId}/files/{fileId}/{safeFileName}
```

Rules:

- Every file metadata row must include `tenant_id`.
- Every file query must filter by `tenant_id`.
- R2 object keys must include tenant partitioning.
- Backend must generate storage keys; never trust client-provided storage paths.
- Upload, download, and delete must check tenant access and domain permission.
- Quota exceeded responses must use a structured error such as `storage_quota_exceeded`.

Upload quota flow:

```text
Upload request
  -> resolve tenant context
  -> validate upload permission
  -> read uploaded file size
  -> lock tenant_storage_stats row
  -> check allowed storage
  -> reserve R2 bytes
  -> upload file to Cloudflare R2
  -> save file metadata in PostgreSQL
  -> move reserved bytes to used_r2_bytes
  -> return success
```

Concurrency rule:

- Quota validation and reservation must happen inside a database transaction.
- Use a row lock on `tenant_storage_stats` for the tenant before reserving storage.
- `reserved_r2_bytes` prevents simultaneous uploads from exceeding tenant limits.

Failure handling:

| Failure point | Required action |
|---|---|
| R2 upload fails | Release reserved storage and do not create file metadata. |
| DB save fails after R2 upload | Try to delete the uploaded R2 object, release reserved storage, and write an audit/failure log. |
| R2 delete fails | Retry or let background sync reconcile usage later. |

Delete flow:

```text
Delete request
  -> validate tenant owns file
  -> validate delete permission
  -> mark file as deleting/deleted
  -> delete R2 object
  -> decrease used_r2_bytes
  -> write audit log
```

Plan and add-on handling:

- Plan storage can be stored in `subscription_plans.feature_limits_json` as `storage_gb` or converted into `plan_storage_bytes` on subscription.
- Extra purchased storage must be stored in `tenant_storage_addons`.
- Upload checks must use `plan_storage_bytes + extra_storage_bytes`.
- Downgrades must not automatically delete existing files; new uploads should be blocked until usage is within the new limit.

Warning thresholds:

| Usage | Action |
|---|---|
| 80% | Show warning notification. |
| 90% | Show stronger warning notification. |
| 100% | Block new uploads. |

Background sync:

- A background sync job should recalculate tenant storage usage daily or weekly.
- The job should compare `tenant_files` metadata with `tenant_storage_stats`.
- R2 object listing may be used by sync jobs, not by normal upload requests.
- Sync corrections must be audited.

Code-level foundation:

```text
ONEVO.Application
  Common/ServiceInterfaces/
    IStorageService
    IStorageQuotaService
  Features/Storage/File/Commands/UploadFile
  Features/Storage/File/Commands/DeleteFile
  Features/Storage/Quota/Queries/GetStorageQuota

ONEVO.Domain
  Features/Storage/File/Entities/TenantFile
  Features/Storage/Quota/Entities/TenantStorageStats
  Features/Storage/Quota/Entities/TenantStorageAddon

ONEVO.Infrastructure
  ExternalServices/Storage/R2StorageService
  Persistence/Configurations/Storage/File
  Persistence/Configurations/Storage/Quota
  Persistence/Repositories/Storage/File
  Persistence/Repositories/Storage/Quota
```

Main rule:

```text
Frontend shows usage.
Backend enforces quota.
Cloudflare R2 stores files.
PostgreSQL stores metadata and quota statistics.
```
#### Encryption at Rest

Rules:

- PostgreSQL storage must be encrypted at rest by the hosting/storage layer.
- Object storage must be encrypted at rest.
- Backups must be encrypted at rest.
- Production keys must be stored in an approved secrets manager.
- Integration secrets must never be returned raw through APIs or logs.

#### Field-Level Protection

High-risk fields need protection beyond normal database encryption.

Protected field groups:

- NIC, passport, national identifiers, and tax IDs.
- Bank account and payment details.
- Salary, compensation, pension, and tax values.
- Medical, grievance, disciplinary, and investigation data.
- Biometric hashes, verification photos, and screenshots.
- SSO, OAuth, SMTP, payment, terminal, and integration secrets.
- Emergency contacts, dependents, addresses, and personal phone/email.

Rules:

- Sensitive fields must be masked, encrypted, restricted, or audited based on risk.
- Field protection must be enforced in API query/DTO shaping, not only in the UI.
- Export endpoints must follow the same field-level rules as normal API reads unless stronger export permission is granted.

#### Data Export and Deletion Workflow

Export and deletion must be controlled compliance workflows, not ad hoc endpoint behavior.

Architecture approach:

```text
Request received
  -> Verify requester identity and permission
  -> Determine scope and legal basis
  -> Check retention and legal hold rules
  -> Queue export/delete/anonymize job
  -> Execute through auditable background process
  -> Store export privately if needed
  -> Provide short-lived authorized access
  -> Audit every state change
```

Rules:

- Export files must not be public.
- Deletion must cover database records, object storage files, generated exports, caches, and integration-linked data where applicable.
- Legal holds must block deletion or anonymization until released.

### 4.7 Concurrency Control

Mutable HRMWS aggregates must define concurrency behavior.

Default model:

- Use EF Core optimistic concurrency with PostgreSQL `xmin` or an explicit row version where supported.
- Return HTTP `409 Conflict` for stale writes or state-transition conflicts.
- Conflict responses must tell the client to refresh and retry.
- State transitions must be validated in the same transaction that writes the change.
- Background jobs must be safe to retry.

Required coverage:

- Employee profile and employment status.
- Salary, compensation, payroll, tax, and pension records.
- Leave approval, rejection, cancellation, and balance adjustment.
- Expense approval and reimbursement state.
- Workflow step approval/rejection.
- Role, permission, and policy assignment.
- Document version number increments.
- Retention policy updates.
- Payroll run execution.

### 4.8 Database Indexing Strategy

Rules:

- Tenant-scoped tables must include tenant-aware indexes for common lookups.
- Foreign keys used in joins must be indexed where query volume justifies it.
- Date-range tables such as attendance, payroll, activity, leave, audit, and reports must have date-supporting indexes or partitions.
- Workflow/status-heavy tables must index tenant plus status and assigned user/employee where used by dashboards.
- New list endpoints must define pagination and supported sort/filter columns.
- New high-volume queries must be checked with `EXPLAIN ANALYZE` before production release.
- Avoid over-indexing write-heavy ingestion tables.

### 4.9 Accessibility Backend Support

Backend APIs must support accessible frontend experiences through predictable API behavior.

Rules:

- Validation failures must return structured field-level errors.
- Error responses must use consistent status codes and problem details.
- Backend error messages must be clear enough for UI display without exposing internal details.
- APIs must provide metadata required by accessible UI flows where backend-driven forms or workflows are used.

### 4.10 Internationalisation and Localisation

Backend business logic must not assume one locale, timezone, or currency format.

Rules:

- Store timestamps in UTC.
- Store tenant timezone, locale, and currency settings as tenant configuration.
- Return machine-readable date/time values from APIs.
- Presentation formatting belongs to UI or a dedicated localization layer.
- Business rules that depend on local dates must use tenant timezone explicitly.
- Do not use server local time for tenant business rules.
- Do not hardcode currency symbols or date formats in backend business logic.

---

## 5. Testing Strategy

### 5.1 Current Testing Foundation

Current backend test project structure:

```text
tests/
  ONEVO.Tests.Unit/
  ONEVO.Tests.Integration/
  ONEVO.Tests.Architecture/
```

Current implemented testing foundation:

- Unit testing.
- Integration testing.
- Architecture testing.

### 5.2 Unit Testing

Unit tests must cover:

- Command handlers.
- Query handlers.
- Validators.
- Mappers.
- Helpers.
- Pure business logic.

### 5.3 Integration Testing

Integration tests must cover:

- API behavior.
- Database behavior.
- Authentication/session behavior.
- Tenant isolation.
- Authorization.
- File/quota/idempotency behavior when applicable.

Integration tests for critical persistence and tenant isolation should use real PostgreSQL/Testcontainers.

### 5.4 Architecture Testing

Architecture tests must protect:

- Dependency direction.
- Clean Architecture boundaries.
- Module boundaries.
- Tenant-isolation rules.
- No controller-to-DbContext access.
- No Application dependency on Infrastructure implementations.

Coverage target: at least 70% on business logic, with higher coverage expected for critical backend modules.

---

## 6. Deployment Architecture

### 6.1 CI/CD

Backend changes should pass the basic CI checks before merge or deployment.

Current CI gate scope:

- Restore packages.
- Build solution.
- Run unit tests.
- Run integration tests.
- Run architecture tests.

Rules:

- Failed build or failed tests must block merge/deployment.
- Additional security scans can be added later when the deployment pipeline matures.

### 6.2 Deployment and Rollback

Deployment must be controlled and repeatable.

Architecture approach:

- Deployments should run through the CI/CD pipeline.
- Database migrations must be reviewed before production deployment.
- Rollback steps must be documented for production releases.

Rules:

- Do not deploy code when build or tests fail.
- Do not make database changes without a migration and review.

## 7. Monitoring & Observability

### 7.2 Health Checks

Required endpoints:

| Endpoint | Purpose |
|---|---|
| `/health` | Liveness: process is running. Lightweight |
| `/health/ready` | Readiness: app can safely receive traffic. Checks dependencies |

Required checks:

- API process liveness.
- PostgreSQL connectivity.
- Required external dependencies.
- In-memory cache if used by the deployed workload.
- Object/file storage if introduced.
- Payment/email dependencies if required for deployed workload.

Rules:

- Liveness should not fail just because the database is briefly down.
- Readiness should fail when the app cannot safely serve traffic.
- Health check output must not expose secrets.

### 7.3 Logging

Logging must include:

- Correlation ID.
- Request path and method.
- Tenant ID where applicable.
- User ID where applicable.
- Dependency failures.
- Slow request warnings.
- Security-sensitive events.

Rules:

- Do not log raw tokens, secrets, passwords, bank details, national identifiers, sensitive medical data, screenshots, verification photos, or excessive PII.
- Logs must be searchable by correlation ID.

### 7.4 Audit Trail

Required audit events:

- Authentication events.
- Authorization changes.
- Administrative actions.
- Failed access attempts.
- Data export/deletion requests.
- Sensitive document access/download/delete.
- Booking creation/modification/cancellation when booking exists.

Required audit fields:

```text
timestamp_utc
user_id
tenant_id where applicable
action
resource
ip_address
correlation_id
```

Rules:

- Audit logs must be tamper-resistant.
- Audit retention target is minimum 7 years.
- Audit records must not contain secrets or raw tokens.

---

## 8. Disaster Recovery

### 8.1 Backup and Disaster Recovery

Backup and recovery must be planned for production data.

Architecture approach:

- Production PostgreSQL must use automated backups.
- Backups must be encrypted at rest.
- Restore procedure must be documented.
- Critical configuration and secrets must be recoverable from the approved secret/configuration store.
- Restore testing must be done before production release.

### 8.2 Database Availability

Database availability must be handled through the production hosting/database platform.

Architecture approach:

- Application code must use the configured PostgreSQL connection string.
- EF Core/Npgsql retry can handle short transient database connection failures.
- `/health/ready` should fail when the database is not reachable.

## Final Architecture Rule

Every backend change must preserve this flow:

```text
HTTP Request
  -> ONEVO.Api middleware
  -> Tenant/Auth/Security checks
  -> Controller
  -> MediatR Command or Query
  -> Application pipeline behaviors
  -> Application handler
  -> Application-owned interface
  -> Infrastructure implementation
  -> EF Core/PostgreSQL or external dependency
  -> Result/DTO
  -> HTTP response
```

If a team member or IDE agent cannot place new code into this flow cleanly, the design must be clarified before implementation.














