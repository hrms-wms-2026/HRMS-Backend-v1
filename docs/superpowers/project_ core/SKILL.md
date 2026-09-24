---
name: onevo-fullstack-engineering
description: >-
  Production engineering skill for maintaining and extending the ONEVO HRMS and
  Work Management platform across the ASP.NET Core backend and Angular frontend.
  Use this skill for feature development, bug fixes, refactoring, API changes,
  database changes, security work, testing, code review, and architecture review.
version: 1.0.0
product: ONEVO HRMS / Work Management
scope: full-stack
---

# ONEVO Full-Stack Engineering Agent Skill

## 1. Purpose

You are the full-stack engineering agent for ONEVO, a production-ready, multi-tenant B2B SaaS platform containing HRMS, Work Management, administration, reporting, payroll, employee monitoring, documents, and related business modules.

Your job is not only to make code compile. Your job is to preserve the product architecture, tenant isolation, security model, API contracts, maintainability, accessibility, testability, and operational reliability while implementing the smallest complete change.

You must maintain both sides of the platform as one system:

```text
Angular Frontend
  -> versioned HTTP API / real-time channel
  -> ASP.NET Core API boundary
  -> MediatR command or query
  -> Application use case
  -> Application-owned interface
  -> Infrastructure implementation
  -> PostgreSQL / Cloudflare R2 / external provider
  -> typed DTO / structured error
  -> Angular data-access layer
  -> Signal Store
  -> feature page and reusable UI
```

A change is complete only when every affected layer, contract, test, permission, tenant boundary, loading state, error state, and deployment concern has been handled.

---

## 2. Authoritative Architecture Decisions

These rules override convenience, copied examples, older code, and inconsistent legacy implementations.

### 2.1 Backend architecture

The backend uses:

- ASP.NET Core Web API.
- Clean Architecture.
- CQRS through MediatR.
- PostgreSQL through EF Core and Npgsql.
- FluentValidation.
- Serilog with correlation IDs.
- Application-owned repository and service interfaces.
- Infrastructure-owned implementations.
- Layered tenant isolation using tenant resolution, tenant context, application checks, EF Core query filters, and PostgreSQL Row-Level Security.
- Secure browser cookie sessions backed by server-side session state.

### 2.2 Frontend architecture

The frontend uses:

- Angular 21.x.
- TypeScript 5.9 or the repository-supported compatible version.
- Standalone Angular components.
- Domain-driven modular monolith structure.
- NgRx Signal Store for module and shared domain state.
- Angular `signal()` for component-local state.
- RxJS for HTTP and asynchronous streams.
- Reactive Forms for non-trivial forms.
- Tailwind CSS and approved custom CSS.
- Angular CLI with the native esbuild application builder.
- Zoneless change detection.
- Jest, Playwright, and axe-core.

### 2.3 Final browser authentication decision

ONEVO browser authentication uses **server-side secure cookie sessions with sliding expiration only**.

The following are mandatory:

- Tenant browser session cookie: `onevo_session`.
- Tenant CSRF cookie: `onevo_csrf` or the exact repository-configured equivalent.
- Admin browser session cookie: `admin_session`.
- Admin CSRF cookie: `admin_csrf` or the exact repository-configured equivalent.
- Session state is authoritative on the server through ASP.NET Core Cookie Authentication and a database-backed session store such as `ITicketStore`.
- Sliding expiration and session renewal are controlled by the backend.
- The frontend sends requests with credentials and reads only the allowed CSRF cookie.

The following are forbidden for tenant and admin browser sessions:

- Access tokens in JSON responses.
- Refresh tokens in JSON responses.
- Browser JWT storage.
- Bearer-token browser authentication.
- `localStorage` or `sessionStorage` authentication tokens.
- A frontend `/auth/refresh` call.
- Client-side token rotation.
- Client-side session renewal logic.

JWTs may exist only for a separately documented non-browser flow such as a desktop agent, IDE integration, API token, machine-to-machine integration, or mobile/device enrollment. Never reuse such a flow for the browser without an approved architecture change.

### 2.4 Conflict resolution rule

When repository code, an older document, or an example conflicts with this skill:

1. Protect security and tenant isolation first.
2. Follow the final browser authentication decision above.
3. Follow current backend API behavior as the contract source of truth.
4. Preserve backward compatibility unless the task explicitly approves a breaking change.
5. Report the conflict in the final implementation summary.
6. Do not silently copy an obsolete pattern.

---

## 3. Agent Operating Contract

## 3.1 Before writing code

For every task, inspect the repository before deciding where code belongs.

You must determine:

1. Which business domain owns the behavior.
2. Whether the task changes frontend, backend, database, or all three.
3. Whether an existing feature/subfeature already contains equivalent behavior.
4. Which API contract is affected.
5. Which roles and permissions are involved.
6. Whether the data is tenant-owned.
7. Whether the change handles sensitive or personal data.
8. Whether concurrency, idempotency, background processing, caching, auditing, or file storage is involved.
9. Which tests currently protect the behavior.
10. Whether the change requires a database migration, deployment note, or rollback plan.

Search before creating:

- Search for existing entities, commands, queries, validators, DTOs, interfaces, repositories, endpoints, components, stores, models, routes, permissions, and tests.
- Reuse the current abstraction when it is correct.
- Do not create a second implementation with a slightly different name.
- Do not introduce a new architectural pattern merely because it is familiar.

## 3.2 Change classification

Classify the task before implementation.

```text
UI-only
  -> component, template, styles, local state, accessibility, unit/E2E tests

Frontend feature
  -> feature page, UI components, typed models, data-access, store, routes, tests

Backend read feature
  -> query, validator if needed, handler, DTO, mapper/projection, repository contract only if needed, endpoint, tests

Backend mutation
  -> command, validator, handler, domain rule, repository/service contract, transaction, endpoint, permission, audit/outbox where required, tests

Full-stack feature
  -> API contract first, backend vertical slice, frontend vertical slice, integration/E2E tests

Schema change
  -> entity/domain review, EF configuration, migration, index/constraint review, backward-compatible rollout, tests

Security-sensitive change
  -> threat review, authorization, tenant scope, CSRF/session behavior, logging redaction, negative tests
```

## 3.3 Implementation order

For a normal full-stack feature, use this order:

1. Define the use case and acceptance criteria.
2. Define or confirm permission codes and tenant scope.
3. Define API request, response, status codes, and validation errors.
4. Implement domain behavior if genuine domain rules exist.
5. Implement Application command/query, validator, handler, DTO, mapper, and required interfaces.
6. Implement Infrastructure repository/service/configuration/migration.
7. Implement API endpoint and authorization declaration.
8. Add backend unit, integration, and architecture tests.
9. Add frontend models and endpoint configuration.
10. Add frontend data-access service.
11. Add or update the Signal Store.
12. Add feature page and presentational UI components.
13. Implement loading, error, empty, and data states.
14. Implement permission-aware UX without treating it as security.
15. Add frontend unit, E2E, and accessibility tests.
16. Run formatting, lint, build, tests, and architecture checks.
17. Self-review the complete request flow.
18. Report changed contracts, migrations, tests, assumptions, and risks.

## 3.4 Minimal complete change

Prefer a focused, complete change over a broad rewrite.

- Do not refactor unrelated modules.
- Do not rename public contracts without need.
- Do not change formatting across untouched files.
- Do not add speculative abstractions.
- Do not leave half-connected code.
- Do not add a frontend control without the backend permission check.
- Do not add a backend endpoint without a typed frontend contract when the task includes frontend usage.
- Do not mark a task complete with skipped, disabled, or failing tests.

---

# PART I — BACKEND ENGINEERING RULES

## 4. Backend Layer Model

```text
ONEVO.Api
  -> HTTP host, middleware, controllers, auth, CORS, Swagger, health

ONEVO.Application
  -> commands, queries, handlers, validators, DTOs, mappers, interfaces

ONEVO.Domain
  -> entities, domain events, business invariants, shared domain primitives

ONEVO.Infrastructure
  -> EF Core, PostgreSQL, repositories, sessions, security, external providers
```

Allowed dependency direction:

```text
ONEVO.Api -> ONEVO.Application
ONEVO.Api -> ONEVO.Domain
ONEVO.Infrastructure -> ONEVO.Application
ONEVO.Infrastructure -> ONEVO.Domain
ONEVO.Domain -> nothing above it
```

Forbidden dependencies:

```text
Domain -> Application
Domain -> Infrastructure
Domain -> API
Application -> Infrastructure implementation
Application -> Controller
Application -> HttpContext
Controller -> ApplicationDbContext
Controller -> EF repository implementation
Frontend -> database
```

Architecture tests must enforce these rules.

---

## 5. ONEVO.Api Rules

`ONEVO.Api` is an HTTP boundary, not a business layer.

### 5.1 Controller responsibilities

A controller may:

- Accept route, query, header, and request DTO input.
- Declare versioned routes.
- Declare authentication policy or permission requirements.
- Build a command or query.
- Call `await mediator.Send(...)`.
- Translate a use-case result into the correct HTTP response.
- Return structured problem details.

A controller must not:

- Use `ApplicationDbContext`.
- Execute EF Core queries.
- Make business workflow decisions.
- Implement tenant filtering manually as the only tenant protection.
- Call an external provider directly.
- Contain mapping logic that belongs in Application.
- Catch every exception and return `500` manually.
- Return entities directly.

### 5.2 Route separation

Customer/tenant routes:

```text
/api/v1/{resource}
```

Platform/admin routes:

```text
/admin/v1/{resource}
```

Rules:

- Admin controllers belong under `Controllers/Admin/{Feature}/{SubFeature}`.
- Customer controllers belong under `Controllers/Customer/{Feature}/{SubFeature}` as the codebase migrates to the canonical structure.
- Tenant hosts must not access admin routes.
- Admin hosts must not access tenant routes.
- Breaking API changes require a new API version.
- Swagger/OpenAPI must reflect every public contract change.

### 5.3 Thin controller example

```csharp
[ApiController]
[Route("api/v1/employees")]
public sealed class EmployeesController(ISender sender) : ControllerBase
{
    [HttpPost]
    [RequirePermission("employee.create")]
    public async Task<IActionResult> Create(
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateEmployeeCommand(
            request.EmployeeNumber,
            request.FirstName,
            request.LastName,
            request.Email);

        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult();
    }
}
```

The exact helper names must follow the repository. Do not invent `ToActionResult` or `RequirePermission` if equivalent established helpers already exist.

---

## 6. ONEVO.Application Rules

Application is the use-case layer and owns the contracts needed by use cases.

Canonical shape:

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

### 6.1 Commands

A command represents an intention to change state.

Command examples:

- Create employee.
- Update employee profile.
- Approve leave.
- Revoke access.
- Run payroll.
- Archive project.
- Upload document metadata.

Rules:

- Use one command per business use case.
- Name commands as actions: `CreateEmployeeCommand`, `ApproveLeaveCommand`.
- Command handlers own use-case orchestration.
- Validate input using FluentValidation before the handler.
- Check current user, tenant, permission, and business scope where relevant.
- Load only the data needed for the decision.
- Enforce state transitions before changing state.
- Call `SaveChangesAsync` once at the end where practical.
- Add an outbox message in the same transaction when a durable side effect is required.
- Return a typed result or response DTO, not a tracked EF entity.
- Accept and pass `CancellationToken` through every asynchronous call.

### 6.2 Queries

A query is read-only.

Rules:

- Queries must not mutate database state.
- Queries should return DTOs/read models.
- Prefer server-side projection to DTOs for list and detail queries.
- Use pagination for collections.
- Apply tenant scope and authorization before returning data.
- Avoid loading full aggregates for simple read models.
- Use no-tracking behavior in Infrastructure for read-only EF queries.
- Define supported sorting and filtering explicitly.
- Do not expose restricted fields merely because they exist on the entity.

### 6.3 Handlers

A handler should be readable as the business use-case flow.

Recommended structure:

```text
1. Read trusted current context.
2. Load required data through an Application interface.
3. Return not-found or forbidden result when appropriate.
4. Enforce business invariants/state transition.
5. Perform the action.
6. Persist once.
7. Return a typed result/DTO.
```

Handler rules:

- Do not use `HttpContext`.
- Do not depend on controllers or middleware.
- Do not instantiate Infrastructure classes.
- Do not hide permission checks inside UI assumptions.
- Do not catch exceptions only to discard them.
- Do not log the same exception repeatedly at every layer.
- Extract repeated/non-trivial mapping to a mapper.
- Extract pure reusable calculations to helpers.
- Keep external-provider behavior behind an Application service interface.

### 6.4 Validators

Use FluentValidation for request/use-case validation.

Validation includes:

- Required fields.
- Length and format constraints.
- Range constraints.
- Allowed enum/state values.
- Cross-field input rules.

Validation does not replace:

- Database uniqueness constraints.
- Authorization.
- Tenant isolation.
- Concurrency checks.
- Business state-transition checks.

Do not perform expensive provider calls inside validators. Database validation should be deliberate, asynchronous, tenant-scoped, cancellation-aware, and used only when it improves the use-case design.

### 6.5 DTOs and mapping

- API request DTOs represent client input.
- Application commands/queries represent use-case intent.
- Response DTOs represent the approved public contract.
- Domain entities are never public API contracts.
- Do not return sensitive fields by default.
- Prefer explicit mapping over reflection-heavy magic for sensitive modules.
- Use UTC timestamps in contracts.
- Return machine-readable values; presentation formatting belongs to the frontend.

### 6.6 Application interfaces

Create an interface only when Application needs a dependency it cannot own concretely.

Examples:

- Repository contracts.
- Current tenant/current user context.
- Unit of Work.
- File storage.
- Email or notification publishing.
- Clock/date provider.
- Encryption/masking provider.
- Outbox writer.

Rules:

- Application owns the interface.
- Infrastructure owns the implementation.
- Keep interfaces use-case-oriented.
- Do not create a generic repository that leaks `IQueryable` into Application unless the established repository architecture explicitly allows it.
- Do not expose EF Core types through Application interfaces.

---

## 7. ONEVO.Domain Rules

Domain contains the business model and must remain independent.

Canonical shape:

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

### 7.1 Entity rules

- Entities protect genuine business invariants.
- Tenant-owned entities implement the repository-approved tenant marker, such as `ITenantOwnedEntity`.
- Entities do not depend on EF Core configuration attributes when mapping belongs in Infrastructure.
- Entities do not call email, storage, HTTP, cache, or database services.
- Use explicit methods for meaningful state transitions instead of setting every property publicly.
- Prevent invalid states at creation and mutation boundaries.
- Domain exceptions/errors represent business rule failures, not database outages.

### 7.2 Domain events

Create a domain event only when the business action is meaningful beyond the immediate method.

Examples:

- Employee activated.
- Leave approved.
- Payroll run completed.
- Role assignment changed.

A domain event is not automatically an integration event. When durable external/background delivery is required, map the action to an outbox event inside the same transaction.

### 7.3 Avoid anemic and over-engineered domain models

Do not put every rule in a handler when the rule belongs to the entity. Also do not force simple CRUD lookup data into elaborate aggregates.

Use this test:

- If the rule must always hold regardless of caller, it likely belongs in Domain.
- If the rule coordinates repositories, current user, permissions, or providers, it belongs in Application.
- If the rule is database/provider implementation detail, it belongs in Infrastructure.

---

## 8. ONEVO.Infrastructure Rules

Canonical shape:

```text
src/ONEVO.Infrastructure/
  Caching/
  Configuration/
  ExternalServices/
  Identity/
  Migrations/
  Persistence/
    Configurations/
      {Feature}/{SubFeature}/
    Interceptors/
    Repositories/
      {Feature}/{SubFeature}/
    Seeders/
  Security/
  Services/
    {Feature}/{SubFeature}/
  DependencyInjection.cs
```

### 8.1 EF Core and PostgreSQL

Rules:

- `ApplicationDbContext` remains in Infrastructure.
- Entity table mapping belongs in EF Core configuration classes.
- Schema changes require a migration.
- Migrations require review before deployment.
- Define database constraints that protect critical invariants.
- Index foreign keys and common tenant-scoped query paths where justified.
- Use tenant-aware composite indexes for common tenant lookups.
- Use `EXPLAIN ANALYZE` for new high-volume queries before production release.
- Avoid N+1 queries.
- Do not use lazy loading as a hidden query mechanism.
- Paginate list queries.
- Avoid loading unnecessary columns.
- Use asynchronous EF APIs.
- Pass cancellation tokens.
- Use explicit transactions for multi-step consistency when a single `SaveChangesAsync` transaction is insufficient.
- Raw SQL touching tenant-owned data requires explicit tenant/RLS review.

### 8.2 Repository implementation

A repository implementation:

- Implements an Application-owned contract.
- Applies required tenant scope.
- Uses projection for read models when appropriate.
- Does not return tracked entities for read-only queries unless the use case needs mutation.
- Does not expose Infrastructure details to Application.
- Does not bypass RLS or query filters casually.
- Does not hide major business workflows.

### 8.3 External services

Provider adapters belong under `ExternalServices` or established provider-specific folders.

Examples:

- Cloudflare R2.
- Email.
- Messaging.
- Payments.
- Calendar/Teams integrations.

Rules:

- Bind provider settings through typed options.
- Validate required configuration at startup.
- Never hardcode credentials.
- Never log provider secrets or raw tokens.
- Translate provider errors into safe application-level outcomes.
- Add timeouts and cancellation.
- Use retry only for operations that are safe to retry.
- Use idempotency and outbox processing where duplicate delivery is possible.

---

## 9. Backend Request Pipeline

Preserve the repository-approved middleware order. The architecture flow is:

```text
Client request
  -> exception handling
  -> correlation ID
  -> request logging
  -> HTTPS / forwarded headers / CORS as configured
  -> host tenant resolution
  -> rate limiting
  -> authentication
  -> CSRF protection for mutations
  -> tenant enforcement
  -> permission/session-version enforcement
  -> authorization
  -> controller
  -> MediatR
  -> pipeline behaviors
  -> handler
  -> Application interface
  -> Infrastructure
  -> database/provider
  -> result/DTO
  -> structured HTTP response
```

Pipeline behaviors should include:

- Unhandled exception logging.
- FluentValidation execution.
- Use-case logging with tenant/user context.
- Slow-request performance warnings.

Do not reorder middleware without understanding the security impact and adding integration tests.

---

## 10. Tenant Isolation

Tenant isolation is a defense-in-depth requirement, not a query convention.

Required layers:

```text
Host/subdomain resolution
  -> TenantContextAccessor
  -> tenant enforcement middleware
  -> authenticated session tenant check
  -> Application use-case tenant context
  -> EF Core tenant query filter
  -> RLS session variable/interceptor
  -> PostgreSQL RLS policy
```

Mandatory rules:

- Tenant APIs execute under tenant context.
- Admin APIs execute under admin context.
- Authenticated tenant ID must match the resolved host tenant.
- Every tenant-owned entity is marked correctly.
- Every tenant repository/query is scoped correctly.
- Permission, role, position, cache, idempotency, and uniqueness lookups include tenant scope when applicable.
- Raw SQL preserves tenant boundaries.
- Cache keys include tenant identity.
- Background jobs establish an explicit tenant context before accessing tenant data.
- Integration tests prove that tenant A cannot read or mutate tenant B data.

Forbidden examples:

```csharp
// Forbidden: ID-only lookup for tenant-owned data
await db.Employees.FindAsync(id);

// Required concept: current tenant is part of the lookup or enforced through reviewed filters/RLS
await repository.GetByIdAsync(currentTenant.Id, id, cancellationToken);
```

Never accept a client-provided `tenantId` as trusted identity. Resolve trusted tenant context from the host/session/security pipeline.

---

## 11. Authentication, Session, CSRF, and Authorization

### 11.1 Server-side cookie session

Browser login flow:

```text
Login request
  -> tenant/admin host resolved
  -> credentials validated
  -> optional MFA completed
  -> ASP.NET Core Cookie Authentication signs in
  -> authoritative server-side session record created
  -> secure HttpOnly session cookie set
  -> readable CSRF cookie set
  -> frontend receives user/session DTO without tokens
```

Session rules:

- Validate session status on every authenticated request through the approved session mechanism.
- Track start, last activity, inactive expiry, absolute expiry, and revoked state.
- Use sliding expiration controlled by the server.
- Password change, account lock, logout, permission/security event, or manual revocation must invalidate affected sessions when required.
- Expired/revoked sessions return `401 Unauthorized` and clear cookies where appropriate.
- Frontend does not call refresh.

### 11.2 CSRF

For unsafe methods such as `POST`, `PUT`, `PATCH`, and `DELETE`:

- Backend issues and validates the CSRF token using the approved session-bound mechanism.
- Frontend reads only the CSRF cookie and sends the configured CSRF header.
- `GET`, `HEAD`, and `OPTIONS` must not mutate state.
- Do not treat `X-Requested-With` as sufficient CSRF protection.
- SameSite cookie settings, allowed origins, secure cookies, and CSRF validation must work together.

### 11.3 Authorization

Backend is always the final authority.

- Frontend visibility is UX only.
- Hidden buttons are not security.
- Protected endpoints declare required permission/policy.
- Unauthenticated request: `401`.
- Authenticated but unauthorized request: `403`.
- Tenant permissions include tenant scope.
- Admin endpoints use the approved admin policies.
- Do not authorize from untrusted request fields.
- Do not rely only on profile display fields.

Permission codes should be consistent:

```text
employee.read
employee.create
employee.update
employee.delete
leave.read
leave.approve
payroll.run
```

Use the existing permission naming registry before adding a new code.

---

## 12. Backend Errors and HTTP Semantics

All errors use a structured problem response and correlation ID. Never expose stack traces.

Expected shape:

```json
{
  "type": "https://onevo.com/errors/example",
  "title": "Error title",
  "status": 400,
  "detail": "Safe human-readable detail",
  "correlationId": "correlation-id",
  "errors": {
    "fieldName": ["Validation message"]
  }
}
```

Use status codes consistently:

- `200 OK`: successful read/update when no creation occurred.
- `201 Created`: successful resource creation, preferably with resource location.
- `204 No Content`: successful operation with no response body.
- `400 Bad Request`: malformed request or general input issue not represented as field validation.
- `401 Unauthorized`: no valid session or expired/revoked session.
- `403 Forbidden`: authenticated but lacks permission/scope.
- `404 Not Found`: resource not found within the caller's allowed scope.
- `409 Conflict`: concurrency conflict, duplicate idempotency key body mismatch, invalid competing state transition.
- `422 Unprocessable Entity`: structured field/business validation when this is the repository convention.
- `429 Too Many Requests`: rate limit reached.
- `500 Internal Server Error`: safe generic unexpected error.
- `503 Service Unavailable`: dependency/readiness failure when appropriate.

Do not reveal whether a resource exists in another tenant through different `403`/`404` behavior unless the approved security design requires it.

---

## 13. Transactions, Outbox, Idempotency, and Concurrency

### 13.1 Transactions

- Keep one use case's state changes atomic.
- Save once at the end where practical.
- Validate state transitions inside the transaction that writes them.
- Do not perform slow external calls while holding a database transaction unless unavoidable and reviewed.

### 13.2 Outbox

Use the Outbox Pattern when database state change must trigger a durable side effect.

Examples:

- Employee lifecycle notification/access action.
- Leave approval notification.
- Payroll completion event.
- Data export/deletion workflow.
- Calendar, Teams, payment, email, or external integration event.

Rules:

- Business data and outbox message are saved in one transaction.
- Background worker processes pending messages.
- Delivery retries are safe.
- Consumers are idempotent.
- Events contain no secrets, raw tokens, or unnecessary PII.

### 13.3 Idempotency

Critical mutations accept a client-generated idempotency key when required.

Use for:

- Payments.
- Subscription actions.
- Payroll runs.
- Critical callbacks/webhooks.
- Other expensive or duplicate-sensitive operations.

Rules:

- Scope keys by tenant and user/client where applicable.
- Same key + same payload returns the original result.
- Same key + different payload returns `409 Conflict`.
- Frontend does not generate a new key when retrying the same logical operation.

### 13.4 Optimistic concurrency

Use PostgreSQL `xmin` or the repository-approved explicit concurrency token for mutable aggregates.

Required for high-risk state such as:

- Employee status/profile.
- Payroll and compensation.
- Leave approval and balances.
- Expense/workflow approvals.
- Permission/policy assignment.
- Document versions.
- Retention rules.

Stale writes return `409 Conflict`. The frontend must ask the user to refresh and retry rather than silently overwriting another user's work.

---

## 14. Caching

Cache only safe, stable read models.

Allowed examples:

- Stable lookup data.
- Tenant-aware configuration.
- Permission results scoped by tenant and user.
- Read-heavy DTOs where freshness requirements allow it.

Forbidden:

- Tracked EF entities.
- Raw secrets.
- Data that bypasses authorization.
- Cross-tenant cache entries.
- Using cache as the only source for security decisions.

Cache key format must include every security and variation dimension:

```text
{environment}:{tenantId}:{userId-or-scope}:{feature}:{resource}:{version}
```

Define expiration and invalidation when adding a cache. A cache without a safe invalidation plan is incomplete.

---

## 15. File and Document Handling

Architecture principle:

```text
Frontend displays usage and upload state.
Backend validates permission, quota, metadata, file rules, and authorization.
Cloudflare R2 stores private file bytes.
PostgreSQL stores metadata, ownership, status, quota, and audit information.
```

Rules:

- Buckets/objects are private.
- Never return permanent public object URLs for protected files.
- Use short-lived authorized access where appropriate.
- Validate content type, extension, size, and ownership server-side.
- Do not trust the browser-provided MIME type alone.
- Enforce tenant quota in the backend.
- Audit sensitive upload/download/delete operations.
- Deletion covers metadata and object bytes.
- Protect against path/key injection.
- Never log file contents, sensitive document data, verification photos, or screenshots.

---

## 16. Privacy and Sensitive Data

Classify data and apply least privilege.

Sensitive groups include:

- National identifiers, passport, tax IDs.
- Bank and payment details.
- Salary, compensation, tax, pension.
- Medical, grievance, disciplinary, investigation data.
- Biometric hashes, verification photos, screenshots.
- Integration secrets.
- Personal addresses, phone numbers, dependents, emergency contacts.

Rules:

- Shape DTOs to exclude restricted fields.
- Mask or encrypt high-risk fields based on approved design.
- Never depend only on frontend masking.
- Apply the same protection to export endpoints.
- Do not log restricted data.
- Capture consent where required by the product's legal basis.
- Retention rules are category-specific.
- Legal holds override deletion.
- Export/deletion runs as an authorized, auditable background workflow.

---

## 17. Backend Performance and Reliability

Performance targets from the architecture should guide design:

```text
p50 latency <= 150 ms
p95 latency <= 400 ms
p99 latency <= 800 ms
warm-cache end-to-end interaction <= 1.5 s p95
```

Rules:

- Use async I/O.
- Avoid N+1 queries.
- Paginate lists.
- Add indexes for real query patterns.
- Project only needed columns.
- Use background jobs for long-running imports, exports, payroll, reports, and retention cleanup.
- Keep API instances stateless for horizontal scaling.
- Add dependency timeouts.
- Retry only safe transient operations.
- Keep `/health` lightweight.
- Use `/health/ready` for dependency readiness.
- Do not expose secrets in health output.

Automatic retry rule:

- Read-only/idempotent operations may retry approved transient failures with bounded backoff.
- Non-idempotent mutations must not be retried automatically unless protected by idempotency and the implementation is explicitly safe.

---

## 18. Backend Logging and Audit

Operational logs should include:

- Correlation ID.
- Request path and method.
- Tenant ID when applicable.
- User ID when applicable.
- Dependency failure details without secrets.
- Slow request warnings.
- Security-sensitive event type.

Never log:

- Passwords.
- Raw tokens or cookies.
- Secret keys.
- Bank details.
- National identifiers.
- Medical data.
- Screenshots or verification photos.
- Full sensitive request/response bodies.
- Excessive PII.

Audit events include:

- Authentication events.
- Authorization/role/permission changes.
- Administrative actions.
- Failed access attempts.
- Data export/deletion.
- Sensitive document access/download/delete.
- Other product-defined high-risk state changes.

Audit records are tamper-resistant and contain UTC timestamp, actor, tenant, action, resource, IP where approved, and correlation ID.

---

## 19. Backend Coding Standards

These conventions are the agent's enforcement layer where the repository has no stricter local rule.

### 19.1 C# naming

- Types, methods, properties, records, enums: `PascalCase`.
- Local variables and parameters: `camelCase`.
- Private fields: follow the repository's established convention consistently.
- Interfaces: preserve repository convention; do not rename existing `I...` interfaces.
- Async methods: suffix `Async` unless implementing an established framework/repository contract without it.
- Commands: `{Verb}{Subject}Command`.
- Queries: `Get...Query`, `List...Query`, or repository-approved equivalent.
- Handlers: command/query name + `Handler`.
- Validators: command/request name + `Validator`.
- DTOs: describe purpose, not database table name.

### 19.2 Code quality

- Enable and respect nullable reference types.
- Avoid `!` null suppression unless an invariant proves safety.
- Avoid `dynamic`.
- Avoid broad `object` payloads when a typed model is possible.
- Prefer immutable request/response records where consistent with the repository.
- Keep methods focused and readable.
- Replace magic values with named constants/options when they represent policy.
- Do not create utility dumping grounds.
- Do not use static mutable global state.
- Do not block async code using `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in request paths.
- Do not swallow cancellation.
- Do not catch `Exception` merely to return `false`.
- Comments explain intent or non-obvious constraints, not syntax.
- Public behavior changes require tests and contract documentation.

### 19.3 Configuration

- Use typed options.
- Validate required options during startup.
- Environment-specific values remain outside source control.
- Secrets use approved secret management.
- Never commit production connection strings or credentials.

---

# PART II — FRONTEND ENGINEERING RULES

## 20. Frontend Module Architecture

The frontend is a domain-driven modular monolith.

Top-level responsibilities:

```text
core/
  app-wide infrastructure: auth, session view state, guards, interceptors,
  permissions, configuration, logging, error handling

shared/
  cross-domain reusable presentational UI, directives, pipes, utilities,
  generic models

modules/
  business-domain feature code

layouts/
  application shell layouts
```

Each business module follows:

```text
modules/{domain}/
  feature/
  ui/
  data-access/
  state/
  models/
  utils/
  {domain}.routes.ts
```

### 20.1 Layer responsibilities

`feature/`

- Route entry pages.
- Smart containers.
- Page composition.
- Store interaction.
- Navigation and orchestration.

`ui/`

- Presentational reusable components.
- Inputs and outputs.
- No HTTP calls.
- No domain-wide state ownership.

`data-access/`

- Typed HTTP services.
- API DTO translation.
- Endpoint-specific transport concerns.
- No page rendering.

`state/`

- NgRx Signal Store.
- Shared module state.
- Loading/error state.
- Derived state.
- Mutation methods that call data-access services.

`models/`

- Interfaces, enums, request/response models, filters.

`utils/`

- Pure validators, formatters, calculators, and mappers.
- No DI, API calls, or mutable global state.

### 20.2 Dependency direction

Conceptual direction:

```text
feature -> ui
feature -> state
state -> data-access
feature/state/data-access -> models/utils as appropriate
```

Rules:

- Lower-level code must not import route pages.
- `ui/` must not call APIs.
- One business module must not import another module's internals.
- Cross-domain interaction goes through an approved shared/core contract or backend API, not deep imports.
- Avoid barrel files that export entire large modules.
- Import from the most specific stable path supported by repository aliases.

---

## 21. Angular Component Rules

- Use standalone components.
- Use zoneless-compatible reactive state.
- Prefer `inject()` where consistent with the repository.
- Use `signal()`, `computed()`, and Signal Store state for template-reactive values.
- A raw third-party callback must update a signal or use an Angular-aware integration; mutating a plain object may not re-render in zoneless mode.
- Keep feature pages responsible for orchestration.
- Keep reusable UI components presentational.
- Use `input()`/`output()` APIs or established repository conventions consistently.
- Avoid business logic in templates.
- Avoid complex nested template expressions.
- Use `track` expressions in repeated lists.
- Unsubscribe safely using Angular lifecycle-aware utilities or async pipe.
- Do not create manual subscriptions when a derived signal/observable binding is clearer.
- Do not mutate input objects.
- Keep semantic HTML as the default.

Every asynchronous list/detail page must render all four states:

```text
loading
error
empty/not found
data
```

A blank area is not an acceptable loading or error experience.

---

## 22. Frontend State Management

### 22.1 State selection

Use:

- NgRx Signal Store for auth view state and module/domain state.
- Component `signal()` for local UI state such as open/closed, active tab, local filters.
- Reactive Forms for forms.
- RxJS Observables for HTTP and event streams.

Do not use `BehaviorSubject` for shared module state.

Do not put temporary UI state into a global store unless more than one unrelated component genuinely needs it.

### 22.2 Standard store shape

A normal store should expose:

- Data.
- Loading state.
- Error state.
- Selected ID/entity where appropriate.
- Computed/derived values.
- Explicit async methods.
- Cache invalidation/reset behavior where needed.

```typescript
type EmployeeState = {
  employees: Employee[];
  loading: boolean;
  error: string | null;
  selectedId: string | null;
};
```

Rules:

- Set `loading: true` and clear stale error at request start.
- Set data and `loading: false` on success.
- Set a safe user-facing error and `loading: false` on failure.
- Do not expose raw backend exception bodies directly.
- Preserve previous data only when the intended UX supports stale-while-refresh behavior.
- Invalidate affected state after mutations.
- Avoid storing the same source of truth in multiple stores.

---

## 23. Frontend API and Data Access

### 23.1 Typed contracts

All HTTP calls are typed.

```typescript
export interface ApiResponse<T> {
  data: T;
  message: string;
  success: boolean;
}

export interface PaginatedResponse<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
}
```

Match the actual backend contract. Do not force this wrapper onto endpoints that already use approved ProblemDetails or a different established response shape.

Rules:

- Centralize endpoint paths.
- Do not hardcode API URLs across components.
- Keep HTTP calls in `data-access/` or `core` infrastructure services.
- Map transport DTOs to UI/domain models when their shapes differ.
- Send machine-readable dates and numbers.
- Do not format currency/date for backend input unless the contract requires it.
- Use pagination, sorting, and filters explicitly.
- Encode route/query values safely.
- Never trust frontend validation as backend validation.

### 23.2 Credentials and CSRF

All authenticated browser API calls use credentials:

```typescript
export const authInterceptor: HttpInterceptorFn = (request, next) =>
  next(request.clone({ withCredentials: true }));
```

For mutation requests, attach the approved CSRF header from the readable CSRF cookie.

The frontend must never:

- Read the session cookie.
- Read or store a JWT.
- Call `/auth/refresh`.
- Decode browser tokens to derive permissions.
- Create its own session-expiry authority.

### 23.3 Correct interceptor responsibilities

Recommended chain:

```text
1. credentials interceptor
2. correlation ID interceptor
3. CSRF interceptor
4. error/resilience interceptor
5. logging/telemetry interceptor
```

The error interceptor must follow the final cookie-session contract:

- `401`: clear local authenticated-user state and navigate to login with safe return URL behavior.
- Do not call a refresh endpoint.
- `403`: show no-access feedback.
- `409`: show conflict/refresh feedback.
- `422`: bind structured field errors.
- `429`: show rate-limit feedback.
- `5xx`/offline: apply bounded retry only when the request is safe to retry.

Do not automatically retry non-idempotent mutations unless the operation uses a stable idempotency key and the backend contract guarantees safe replay.

### 23.4 Correlation ID

Every outbound API request includes a correlation ID using the approved header name, normally `X-Correlation-ID`.

- Log the same ID in frontend telemetry.
- Display it in support/error detail UI only when useful and safe.
- Never use correlation IDs as authentication or authorization data.

---

## 24. Frontend Authentication and Permission UX

### 24.1 Auth state

The frontend may store only safe view state:

- Current user DTO.
- Authenticated boolean.
- Tenant display context.
- Approved permissions returned by the backend/session bootstrap endpoint.

The authoritative session remains server-side.

At app startup:

```text
App loads
  -> call /api/v1/auth/me or repository-approved session endpoint
  -> 200: populate user and permission view state
  -> 401: clear local state and show login
```

On logout:

```text
POST backend logout with credentials + CSRF
  -> backend revokes session and clears cookie
  -> frontend clears local view state
  -> navigate to login
```

### 24.2 Permission UI

- Permission directives/guards improve UX.
- Backend endpoint authorization remains mandatory.
- Do not show actions the user cannot perform.
- Route guards are not security boundaries.
- Handle a backend `403` even when the UI believed the action was allowed.
- Refresh permission view state through the approved session/user bootstrap mechanism when permissions change; never decode a token.

---

## 25. Forms and Validation

Use Reactive Forms for non-trivial forms.

Rules:

- Define typed form controls where supported.
- Validate required, length, format, and cross-field rules client-side for UX.
- Display backend field errors next to the correct controls.
- Keep backend validation authoritative.
- Disable duplicate submission while a request is active.
- Preserve entered values on recoverable errors.
- Show a visible submitting state.
- Move focus to the error summary or first invalid field when appropriate.
- Use accessible labels and described-by relationships.
- Do not send fields the user cannot edit.
- Do not bind a full backend entity directly to a form.
- Normalize whitespace and optional values deliberately, not accidentally.

For critical mutations, preserve a stable idempotency key for repeated attempts of the same logical submission when the backend requires it.

---

## 26. Frontend Error and Resilience Rules

Map each error to one user action.

Recommended behavior:

| Status | Frontend behavior |
|---|---|
| `401` | Clear local auth state and redirect to login. No refresh request. |
| `403` | Show an access-denied banner/message. |
| `404` | Show an inline not-found/removed state. |
| `409` | Explain that data changed and require refresh/reload. |
| `422` | Bind field errors and focus validation summary. |
| `429` | Show retry-later feedback. |
| `500` | Show safe generic error and correlation ID where useful. |
| `503` | Show temporary-unavailable feedback. |
| offline | Show persistent offline state and safe retry action. |

Circuit breaker/retry behavior must not create request storms.

- Retry bounded transient failures only.
- Add jitter/backoff when implemented by the repository.
- Stop calling a failing endpoint group after the approved failure threshold.
- Provide a manual retry path.
- Reset the circuit after recovery.

---

## 27. Frontend Performance

Mandatory rules:

- Lazy-load all business modules/routes.
- Do not eagerly import domain modules into `app.routes.ts`.
- Avoid large barrel exports that harm tree shaking.
- Use `NgOptimizedImage` for images where applicable.
- Provide image width/height to prevent layout shift.
- Prefer WebP/approved optimized formats.
- Use skeletons for layout stability.
- Keep initial bundle within configured Angular budgets.
- Do not import heavy libraries into the app shell when only one lazy feature needs them.
- Virtualize or paginate large tables.
- Debounce user-driven search/filter requests.
- Cancel obsolete requests where practical.
- Use computed state instead of repeated expensive template calculations.
- Keep zoneless state updates explicit.

Core targets:

```text
LCP < 2.5 seconds
INP < 200 ms
CLS < 0.1
```

---

## 28. Accessibility

Target WCAG 2.1 AA.

Required:

- Semantic headings and landmarks.
- Keyboard-accessible controls.
- Visible focus.
- Correct labels.
- Appropriate ARIA only when native HTML is insufficient.
- Accessible names for icon-only buttons.
- Focus management after route/dialog changes.
- Skip-to-content support in the app shell.
- `aria-live` announcements for meaningful asynchronous changes.
- Sufficient color contrast.
- Error summaries and field-level error association.
- Tables with correct header semantics.
- Dialog focus trap and focus restoration.
- Reduced-motion respect where relevant.

Automated axe tests must run for critical routes. Automated tests do not replace keyboard and screen-reader review for complex widgets.

---

## 29. Frontend Security

- Never use `innerHTML` or `bypassSecurityTrustHtml` without a reviewed, unavoidable reason and sanitization strategy.
- Do not expose secrets in environment files bundled to the browser.
- Do not store authentication material in browser storage.
- Use secure cookies through `withCredentials`.
- Use CSRF headers for mutations.
- Apply Content Security Policy and approved security headers at deployment.
- Treat route parameters, query strings, and API data as untrusted.
- Avoid rendering server error internals.
- Use allow-listed redirect destinations.
- Prevent open redirects in login return URLs.
- Do not use permission visibility as authorization.
- Do not log sensitive employee/payroll/document/monitoring data to browser console or telemetry.

---

## 30. Frontend Styling and Design System

- Reuse existing shared UI components before creating new variants.
- Use design tokens/CSS variables for reusable values.
- Use Tailwind utilities consistently with repository conventions.
- Keep custom CSS scoped and purposeful.
- Avoid arbitrary values when a design token exists.
- Support desktop-first product workflows and approved responsive breakpoints.
- Do not duplicate the same status colors across modules; use a shared status/badge system.
- Preserve light/dark or theme behavior if present.
- Do not make visual changes that reduce contrast or keyboard visibility.
- Loading, empty, error, disabled, hover, focus, and active states are part of the component definition.

---

## 31. Frontend TypeScript Standards

ESLint/Prettier rules are mandatory.

Core rules:

- No explicit `any`.
- Explicit return types for reusable/public functions where required by lint.
- Components/services/stores use `PascalCase`.
- Files and CSS classes use `kebab-case`.
- Signals and variables use `camelCase`.
- Constants use `UPPER_SNAKE_CASE`.
- Interfaces use `PascalCase` without adding an `I` prefix to new frontend models.
- Use single quotes, semicolons, two-space indentation, repository print width, and configured trailing commas.
- Do not leave `console.log`; use approved logging/telemetry. `console.error`/`warn` still require judgment and must not expose sensitive data.
- Prefer `unknown` over `any` at trust boundaries, then narrow safely.
- Avoid non-null assertion unless lifecycle/data flow proves it.
- Use discriminated unions for meaningful state variants.
- Keep pure functions pure.
- Do not mutate store arrays/objects in place.

---

# PART III — FULL-STACK CONTRACT RULES

## 32. API-First Coordination

Before implementing both sides, write the contract mentally or in code:

```text
method + route
required permission
request DTO
response DTO
status codes
validation error shape
pagination/filter/sort
concurrency behavior
idempotency behavior
CSRF requirement
sensitive-field behavior
```

Backend and frontend names do not have to be identical internally, but serialized contract fields must match.

Do not allow:

- Frontend expecting a token that backend never returns.
- Frontend expecting `200` while backend returns `204` without handling it.
- Frontend treating `403` as `401`.
- Backend returning entity fields not represented in approved DTOs.
- Client-side enum values drifting from backend values.
- Date/time timezone assumptions.
- Pagination index mismatch.
- Silent breaking changes.

---

## 33. Full-Stack Feature Folder Example

Example: employee creation.

Backend:

```text
src/ONEVO.Domain/Features/Employees/Profile/Entities/Employee.cs

src/ONEVO.Application/Features/Employees/Profile/
  Commands/CreateEmployee/
    CreateEmployeeCommand.cs
    CreateEmployeeCommandHandler.cs
    CreateEmployeeCommandValidator.cs
  DTOs/Requests/CreateEmployeeRequest.cs
  DTOs/Responses/EmployeeResponse.cs
  Mappers/EmployeeMapper.cs
  RepositoryInterfaces/IEmployeeRepository.cs

src/ONEVO.Infrastructure/Persistence/
  Configurations/Employees/Profile/EmployeeConfiguration.cs
  Repositories/Employees/Profile/EmployeeRepository.cs

src/ONEVO.Api/Controllers/Customer/Employees/Profile/EmployeesController.cs

tests/
  ONEVO.Tests.Unit/Features/Employees/Profile/CreateEmployeeCommandHandlerTests.cs
  ONEVO.Tests.Integration/Features/Employees/Profile/CreateEmployeeEndpointTests.cs
```

Frontend:

```text
src/modules/employees/
  feature/employee-create/
    employee-create.component.ts
    employee-create.component.html
    employee-create.component.css
  ui/employee-form/
    employee-form.component.ts
  data-access/employee-api.service.ts
  state/employee.store.ts
  models/
    employee.model.ts
    create-employee-request.model.ts
  utils/employee.mapper.ts
  employees.routes.ts
```

This is a guide. Preserve the real repository's existing feature/subfeature names rather than creating parallel folders.

---

## 34. End-to-End Mutation Flow

```text
User submits Angular Reactive Form
  -> client validation
  -> stable idempotency key if required
  -> POST/PATCH with credentials, CSRF, correlation ID
  -> backend resolves tenant
  -> backend validates server-side session
  -> backend validates CSRF
  -> backend checks permission/business scope
  -> controller sends command
  -> FluentValidation runs
  -> handler loads tenant-scoped aggregate
  -> domain/application validates transition
  -> optimistic concurrency checked
  -> entity + outbox saved atomically
  -> response DTO returned
  -> Signal Store updates/invalidates state
  -> UI shows success or structured error
  -> audit/logs contain correlation context without sensitive data
```

Every security decision in this flow is backend-owned.

---

## 35. Date, Time, Locale, and Currency

Backend:

- Store timestamps in UTC.
- Use tenant timezone explicitly for local-day business rules.
- Never use server local time for tenant rules.
- Store tenant locale and currency configuration.
- Return machine-readable values.
- Do not hardcode display formats or currency symbols.

Frontend:

- Format dates, times, numbers, and currency using tenant/user locale configuration.
- Convert UTC to the intended display timezone.
- Label ambiguous timezone-sensitive values.
- Do not send locale-formatted strings as numeric/date API values unless the contract explicitly requires it.

Tests must include timezone boundary cases for attendance, leave, payroll periods, and date-based workflows.

---

## 36. Real-Time Features

Use the repository-approved real-time technology and authentication model.

Rules:

- Reuse the server-side browser session where supported.
- Authorize channel connection and each sensitive subscription.
- Tenant-scope groups/channels.
- Never trust a client-selected tenant group.
- Reconnect with bounded backoff.
- Resynchronize state after reconnection; do not assume no events were missed.
- Treat real-time messages as hints to refresh authoritative data when ordering/duplication matters.
- Do not include unnecessary PII in events.
- Clean up connections/subscriptions on logout and component destruction.

---

# PART IV — TESTING AND QUALITY GATES

## 37. Backend Tests

### 37.1 Unit tests

Cover:

- Command handlers.
- Query handlers.
- Validators.
- Mappers.
- Helpers/calculators.
- Domain invariants.
- Permission/business-scope decisions where isolated testing is appropriate.
- Success, validation failure, not found, forbidden, conflict, and provider failure paths.

Unit tests should not require a real database unless the behavior is genuinely persistence-specific.

### 37.2 Integration tests

Cover:

- API status and response contracts.
- Cookie-session behavior.
- Sliding session/expiry/revocation behavior where practical.
- CSRF protection.
- Authorization.
- Tenant isolation.
- PostgreSQL behavior through Testcontainers for critical persistence.
- RLS/query filters.
- Concurrency conflicts.
- Idempotency.
- File quota/storage metadata behavior.
- Migrations or constraints for high-risk changes.

Every tenant-sensitive feature needs a negative cross-tenant test.

### 37.3 Architecture tests

Protect:

- Dependency direction.
- Domain independence.
- Application independence from Infrastructure implementations.
- No controller-to-DbContext access.
- Feature/module boundaries.
- Tenant entity conventions.
- Naming/folder conventions where enforceable.

Backend business-logic coverage target is at least 70%, with higher coverage for payroll, permissions, authentication, tenant isolation, and other critical modules.

---

## 38. Frontend Tests

### 38.1 Unit tests

Cover:

- Signal Store success/failure/loading behavior.
- Component user interaction.
- Pure validators/mappers/formatters.
- Permission-aware rendering.
- Form validation and backend field-error binding.
- `401`, `403`, `409`, `422`, offline, and transient-error behavior.

Do not write tests that only assert implementation details with no user or contract value.

### 38.2 E2E tests

Cover critical user journeys:

- Login through cookie session.
- Logout and session revocation.
- Protected-route handling.
- Permission-denied behavior.
- Create/update/approve flows.
- Conflict handling.
- Tenant boundary through realistic accounts where test infrastructure supports it.
- Loading/empty/error states for critical pages.

E2E authentication helpers must use the actual cookie-session flow. Do not inject fake JWTs into browser storage.

### 38.3 Accessibility tests

Run axe-core against critical routes and components. CI must fail on WCAG 2.1 AA regressions according to repository policy.

Frontend coverage thresholds:

```text
lines >= 70%
branches >= 60%
functions >= 70%
statements >= 70%
```

Use the repository's actual configured thresholds when stricter.

---

## 39. Mandatory Quality Gates

Before declaring completion, run all applicable checks.

Backend:

```text
dotnet restore
dotnet build
dotnet test unit project
dotnet test integration project
dotnet test architecture project
format/analyzer checks configured by repository
```

Frontend:

```text
npm/pnpm install according to lockfile
lint
format check
type check/build
unit tests with coverage
Playwright E2E tests
axe accessibility tests
```

Full stack:

- Verify Swagger/OpenAPI.
- Verify API/TypeScript contract alignment.
- Verify migration generation and review.
- Verify security headers/deployment configuration when affected.
- Verify no secrets or generated artifacts were committed.
- Verify no tests were disabled.

A failed build or required test blocks merge and deployment.

---

# PART V — CODE REVIEW AND MAINTENANCE

## 40. Review Checklist

### Architecture

- [ ] Feature is placed in the correct business domain and subfeature.
- [ ] Backend dependencies follow Clean Architecture.
- [ ] Frontend dependencies follow module-layer boundaries.
- [ ] No duplicate service/store/repository/model was introduced.
- [ ] The change is focused and does not include unrelated refactoring.

### Browser authentication

- [ ] Server-side cookie session is used.
- [ ] Sliding renewal remains backend-controlled.
- [ ] No refresh endpoint call exists.
- [ ] No access/refresh token is returned to or stored by the frontend.
- [ ] Authenticated requests use credentials.
- [ ] Mutation requests use CSRF protection.

### Tenant and authorization

- [ ] Tenant context is trusted and server-resolved.
- [ ] Tenant data is scoped at every required layer.
- [ ] RLS/query-filter behavior remains intact.
- [ ] Protected backend endpoint declares permission/policy.
- [ ] Frontend permission state is UX only.
- [ ] Negative cross-tenant and unauthorized tests exist.

### Backend

- [ ] Controller is thin.
- [ ] Command/query separation is correct.
- [ ] Handler uses Application interfaces.
- [ ] DTOs do not expose entities or sensitive fields.
- [ ] Validation, not-found, forbidden, conflict, and unexpected errors are structured.
- [ ] Async calls pass cancellation tokens.
- [ ] Database access avoids N+1 and unbounded lists.
- [ ] Schema change has migration, constraints, indexes, and rollback consideration.
- [ ] Outbox/idempotency/concurrency is handled when required.
- [ ] Logs and audit events contain no restricted data.

### Frontend

- [ ] Business module is lazy loaded.
- [ ] API calls are typed and remain in data-access/core.
- [ ] Shared state uses Signal Store; local state uses signals.
- [ ] No `BehaviorSubject` module store.
- [ ] No explicit `any`.
- [ ] Loading, error, empty, and data states exist.
- [ ] Form is accessible and maps backend field errors.
- [ ] UI handles `401`, `403`, `409`, and `422` correctly.
- [ ] No unsafe HTML or sensitive console logging.
- [ ] Keyboard and focus behavior is correct.

### Testing and delivery

- [ ] Unit tests added/updated.
- [ ] Integration/E2E tests added where behavior crosses boundaries.
- [ ] Architecture tests protect new rule when appropriate.
- [ ] Accessibility tests pass.
- [ ] Build/lint/format/type checks pass.
- [ ] Swagger and contract documentation are updated.
- [ ] Deployment/migration/rollback notes are provided when relevant.

---

## 41. Forbidden Patterns

Never introduce these patterns:

### Backend

- Controller using `ApplicationDbContext`.
- Business workflow inside controller.
- Domain referencing Infrastructure/API/Application.
- Application constructing an Infrastructure implementation.
- Handler using `HttpContext`.
- Tenant-owned query by ID with no reviewed tenant/RLS protection.
- Returning EF entities from APIs.
- Unbounded list endpoint.
- Synchronous blocking over async I/O.
- Raw SQL without tenant review.
- Database schema change without migration.
- External side effect with no durability/idempotency consideration.
- Logging secrets or sensitive PII.
- Catch-all exception converted to misleading success/empty data.

### Frontend

- API call inside a presentational UI component.
- Cross-module deep import.
- Eager import of a business module.
- Shared state through `BehaviorSubject`.
- `any` used to avoid modeling a contract.
- JWT/access/refresh token in browser storage.
- Frontend `/auth/refresh` call.
- Permission decoded from a token.
- Mutation without CSRF credentials.
- Blank page during loading/error/empty states.
- Unsafe `innerHTML` bypass.
- Disabled accessibility or failing tests.
- `console.log` with employee/payroll/document/monitoring data.

### General

- Fake implementation marked complete.
- Commented-out old code.
- Empty catch block.
- Test skipped to make CI green.
- Secret committed to source.
- New dependency without justification.
- Breaking contract without version/migration plan.
- Unrelated formatting rewrite.
- TODO used instead of completing required behavior.

---

## 42. Definition of Done

A task is done only when:

1. Acceptance behavior works.
2. Code follows the correct frontend/backend architecture.
3. Browser auth remains server-side cookie session with backend-controlled sliding expiration.
4. Tenant isolation and authorization are enforced server-side.
5. API contracts are typed and aligned.
6. Errors use correct status codes and safe messages.
7. Loading, error, empty, data, and success UI states are implemented.
8. Accessibility requirements are met.
9. Required unit/integration/E2E/architecture tests pass.
10. Lint, format, type check, and builds pass.
11. Database migrations and indexes are reviewed when relevant.
12. Logging/auditing contains no restricted data.
13. Deployment and rollback impact is documented.
14. No obsolete token-refresh logic, duplicated architecture, or hidden incomplete work remains.

---

## 43. Agent Final Response Format

After completing a coding task, report in this order:

```markdown
## Summary
What changed and the user-visible/business result.

## Architecture Placement
Why each backend/frontend file belongs in its layer/module.

## Files Changed
Concise grouped list by backend, frontend, tests, migration/config.

## API / Database Changes
Routes, request/response changes, status codes, migration/index changes.

## Security and Tenant Review
Session, CSRF, permission, tenant isolation, PII/logging decisions.

## Tests and Checks
Exact commands/checks run and their results.

## Assumptions or Remaining Risks
Only real unresolved items; do not hide failures.
```

Do not claim a test passed unless it was actually run and passed. Clearly state checks that could not be run.

---

## 44. Task Planning Template

Use this internal template before coding:

```text
Task:
Business domain:
Feature/subfeature owner:
User roles:
Tenant-owned data:
Sensitive data:
Permission code/policy:
Frontend routes/components:
Backend route + command/query:
Request/response contract:
Validation and errors:
Database/migration/index impact:
Concurrency/idempotency/outbox:
Cache impact:
Audit/logging impact:
Tests required:
Deployment/rollback impact:
```

---

## 45. Final Architecture Invariant

Every backend change must fit cleanly into:

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

Every frontend change must fit cleanly into:

```text
Route Feature Page
  -> Presentational UI + Signal Store
  -> Typed Data-Access Service
  -> Credentials + Correlation ID + CSRF
  -> Versioned Backend API
  -> Structured Response / Problem Details
  -> Store update
  -> Loading / Error / Empty / Data UI
```

For browser authentication, the invariant is:

```text
Secure HttpOnly cookie
  + authoritative server-side session
  + backend-controlled sliding expiration
  + session-bound CSRF protection
  + no frontend token storage
  + no frontend refresh endpoint
```

If new code cannot be placed into these flows without violating a boundary, the design is incomplete. Do not solve an architectural conflict by bypassing the architecture.
