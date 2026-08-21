# Legal Entity Access / Selector Permission Fix

## Root cause

- `GET /api/v1/org/legal-entities` was the selector data source and required `org:read`.
- The frontend rendered and loaded the selector only with the `org_structure` module plus `org:read` or `org:manage`.
- The regular-user repository branch returned only the first active employee legal entity even though the current schema permits one employee row per user per legal entity.
- Active-company switching ambiguously accepted either an employee id or a legal-entity id, and the frontend updated local selection even after backend failure.
- Legal-entity creation persists only the entity. Creator employee membership has no safe employee-number contract.

## Endpoint contract

### `GET /api/v1/session/companies`

- Requires the controller-level tenant session policy.
- Does not require `org:read`, `org:manage`, or `legal_entity:update`.
- Derives tenant and user ids from `ICurrentUser`.
- Regular users receive every active legal entity connected through their own active employee rows.
- Management access continues to use `LegalEntityAccessPolicy.HasManagementAccess` and receives the policy-allowed active tenant legal entities.
- Inactive legal entities are never returned by this selector endpoint.

### `POST /api/v1/session/active-company`

Request body:

```json
{ "legalEntityId": "00000000-0000-0000-0000-000000000000" }
```

- Does not accept tenant id or employee id.
- Returns `404` for a missing/cross-tenant company, `409` for an inactive company, and `403` when the caller has no active employee membership.
- On success, stores the caller's matching employee id in `Session.ActiveEmployeeId`; permission/session refresh occurs on the next request.

## Permission matrix

| Capability | Module gate | Permission |
|---|---|---|
| Company selector/list own accessible companies | None beyond authenticated tenant session | None |
| Create legal entity | None added | `legal_entity:create` |
| Legal Entity General Settings | None added | `legal_entity:update` |
| Departments read/navigation | `org_structure` in frontend | `org:read` |
| Positions read/navigation/templates | `org_structure` in frontend | `org:read` |

## Backend files changed

- `src/ONEVO.Api/Controllers/Tenant/Auth/SessionController.cs`
- `src/ONEVO.Api/Contracts/Auth/Session/SwitchActiveCompanyRequest.cs`
- `src/ONEVO.Application/Features/Auth/ActiveCompany/Commands/SwitchActiveCompany/*`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs`
- Focused unit, architecture, and integration test files for legal-entity access and switching.

## Frontend files changed

- Company selector component and tests under `src/app/layouts/main-layout/top-navbar/company-selector/`.
- Legal-entity API/store and tests under `src/app/modules/organization/`.
- Organization and Settings navigation gates/tests under `src/app/app.routes.ts` and `src/app/layouts/main-layout/`.
- Existing store-consumer tests updated to expect the session company endpoint.

## Creator membership

Not implemented. The schema now supports multiple employee rows per user through the unique `(user_id, legal_entity_id)` index, but `employees.employee_number` is required and unique per tenant. Legal-entity creation allows `companyCode = null`, accepts no employee number, and has no transactional allocator/reservation contract. Creating a random/default employee number or cloning the creator's existing number would be invalid or race-prone. Required evidence for implementation is an approved employee-number allocation contract for this flow (including companies without a code and concurrency handling).

## Verification

- API build with isolated output: passed, 0 errors. The exact requested default-output build was blocked by another running process locking `ONEVO.Api/bin` DLLs.
- Focused unit tests: 283 passed, 0 failed (isolated output used because of the same lock).
- Focused selector architecture tests: 2 passed, 0 failed.
- Full architecture suite: 613 passed, 2 failed for pre-existing/unrelated worktree issues (`OnboardingEmployeeNumberControllerArchitectureTests` path resolution and `EfEmployeeRepository.cs` `IgnoreQueryFilters` allowlist).
- Focused integration tests: attempted with Docker 29.6.2, but the integration project did not compile because unrelated bulk-onboarding tests pass `BulkOnboardingRowValidator` where `IBulkOnboardingValidationRunner` is now required.
- Frontend focused suite: 307 passed, 0 failed.
- Frontend production build: passed; existing compiler/style budget/CommonJS warnings remain.
- Backend `git diff --check`: passed.
- Frontend `git diff --check`: blocked only by a pre-existing blank line at EOF in `src/app/modules/people/feature/bulk-onboarding/bulk-onboarding.component.css:361`.

## Remaining risks / blockers

- Management-only switching without a personal employee row is not safely representable in the current session model: the selector policy can list more companies, but `Session` stores only `ActiveEmployeeId`. Assigning another user's employee id would be a security defect. Supporting this case requires an approved `ActiveLegalEntityId` session contract/migration (and permission-resolution changes) or a requirement that management users also have an employee membership in every selectable company.
- Creator membership remains blocked by the employee-number contract above.
- Dependency scans reported existing high-severity advisories for `SQLitePCLRaw.lib.e_sqlite3` (unit project) and `SSH.NET` (integration project).

No commits or pushes were made.
