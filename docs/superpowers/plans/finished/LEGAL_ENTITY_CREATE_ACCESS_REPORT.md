# Legal Entity Create/Access Report

## Scope note

A prior session (see `LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT.md` and the plan it came from, `docs/superpowers/plans/2026-08-06-legal-entity-permission-access-filter.md`) already implemented the `legal_entity:create/update/delete` permission model, the `ListAccessibleAsync`-based accessible-company filter for the company list endpoint, and the `CreateLegalEntityCommandHandler`/validator/contract exactly as this task's spec requires. This was confirmed by reading the current code directly, not inferred from prior reports.

This task's real remaining work was:
1. Closing the accessible-company gap on the General Settings `GET`/`PUT` routes (they used a tenant-only lookup, not the same accessible-company rule the list endpoint uses).
2. Evaluating whether "creator membership" (auto-attaching the creator of a new company to that company) can be safely implemented under the current schema.

## Files changed

- **Created:** `src/ONEVO.Application/Features/OrgStructure/LegalEntity/LegalEntityAccessPolicy.cs`
- **Modified:**
  - `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs` (refactor only — extracted the inline `hasManagementAccess` computation into the shared policy class; behavior unchanged, confirmed by the pre-existing test suite passing unmodified)
  - `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs` (added `GetAccessibleByIdAsync`)
  - `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs` (implemented `GetAccessibleByIdAsync`; extracted `ResolveOwnActiveLegalEntityIdAsync` shared by both this method and `ListAccessibleAsync`'s non-management branch, so the "own active employee's legal entity" join exists in exactly one place)
  - `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityGeneralSettings/GetLegalEntityGeneralSettingsQueryHandler.cs` (swapped `GetByIdForTenantAsync` → `GetAccessibleByIdAsync`)
  - `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommandHandler.cs` (same swap)
  - `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/EfLegalEntityRepositoryTests.cs` (6 new tests for `GetAccessibleByIdAsync`)
  - `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/GetLegalEntityGeneralSettingsQueryHandlerTests.cs` (2 existing tests' mocks updated to `GetAccessibleByIdAsync`; 2 new tests added)
  - `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandHandlerTests.cs` (9 existing `Setup` mocks + 1 `Verify` updated to `GetAccessibleByIdAsync`; 2 new tests added)
  - `tests/ONEVO.Tests.Architecture/LegalEntityPart2BArchitectureTests.cs` (updated the textual source-inspection assertion in `UpdateLegalEntityHandler_FetchesExistingEntityBeforeMutatingOrSaving` from `"GetByIdForTenantAsync"` to `"GetAccessibleByIdAsync"` — this test does a literal string search over the handler's source file and would have failed for the wrong reason if left unchanged, even though the handler was correct)
  - `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs` (3 new HTTP-level tests)

No other file was touched. In particular, `CreateLegalEntityCommandHandler.cs`, `CreateLegalEntityCommandValidator.cs`, `CreateLegalEntityRequest.cs`, and `LegalEntitiesController.cs` are unchanged — see "Create endpoint" below for why.

## Endpoints / permissions used

`GET /api/v1/org/legal-entities` (`org:read`), `POST /api/v1/org/legal-entities` (`legal_entity:create`), `GET`/`PUT /api/v1/org/legal-entities/{id}/general-settings` (`legal_entity:update`), `DELETE /api/v1/org/legal-entities/{id}` (`legal_entity:delete`) — all unchanged. No permission was added, renamed, or removed. No `legal_entity:read` or `company:*` permission exists.

## Access-filter behavior

The fix is implemented correctly, but it is **behavior-neutral over HTTP today**. Both the GET and PUT general-settings routes are gated by `[RequirePermission("legal_entity:update")]` at the controller level, and `LegalEntityAccessPolicy.HasManagementAccess` treats holding `legal_entity:update` (or `legal_entity:delete`) as sufficient for "management access." Consequently, every caller who can even reach `GetLegalEntityGeneralSettingsQueryHandler`/`UpdateLegalEntityGeneralSettingsCommandHandler` already satisfies `hasManagementAccess == true` and takes the management branch of `GetAccessibleByIdAsync` (which is — correctly — identical to the old `GetByIdForTenantAsync` behavior: any entity in the tenant, active or not).

There is currently no way to construct an HTTP-reachable caller who passes the controller's permission gate but should be denied by the new non-management branch, because the two are defined in terms of the same permission. This is why:

- **Integration tests** (`GetGeneralSettings_RegularEmployeeAnotherCompany_Returns403_ViaPermissionGate`, `UpdateGeneralSettings_RegularEmployeeAnotherCompany_Returns403_ViaPermissionGate`) prove the required "regular user cannot GET/PUT another company" behavior end-to-end, but the mechanism is the pre-existing `RequirePermission` attribute, not the new filter. `GetGeneralSettings_ManagerWithLegalEntityUpdate_AnotherCompanyInTenant_Returns200` confirms management users still work across companies over HTTP.
- **Unit tests** (`Handle_RegularUser_AnotherCompanyInSameTenant_ReturnsNotFound` in both handler test files) are the only tests that actually exercise `GetAccessibleByIdAsync`'s non-management branch, by calling the handler directly with a mocked `ICurrentUser` that reports `hasManagementAccess == false` — a state no real HTTP request to this route can currently produce.

This was implemented anyway because: (a) the task explicitly required it, (b) it is real defense-in-depth — if the permission model is ever extended with a scoped permission that grants `legal_entity:update`-gated route access without full management rights, the non-management branch becomes live and already-correct, and (c) it makes the General Settings routes consistent with the List endpoint's own accessible-company rule instead of leaving them on the older tenant-only check.

## Creator membership: NOT implemented

The product requirement — "after creating a company, the authenticated creator should be able to access that company" — was evaluated and **deliberately not implemented**, per this task's own instructions to stop and report rather than invent a schema.

Evidence:
- `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Employee/EmployeeConfiguration.cs:24`: `builder.HasIndex(e => e.UserId).IsUnique();` — a **global**, non-tenant-scoped unique index. One `User` can have at most one `Employee` row, full stop, not one per tenant or one per legal entity.
- No membership, authority, or multi-company-assignment table exists anywhere in the current schema for `Employee`.
- Employee rows are created at invitation-acceptance time (`AcceptInvitationPasswordCommandHandler`/`AcceptInvitationGoogleCommandHandler` under `Features/Auth/Invite/Commands/`), not at tenant or company creation. `CreateTenantCommandHandler` itself does not create an employee row for the tenant owner either — this is an existing, unrelated precedent, not something this task introduced.

**"Company created" and "creator membership" are two separate outcomes, and only the first happened.** `POST /api/v1/org/legal-entities` succeeds and returns the new company exactly as before. The creator is not automatically attached to it — they do not get a new `Employee` row, and the new company will not appear as *their own accessible company* via `ListAccessibleAsync`'s non-management branch unless they separately hold `legal_entity:update`/`legal_entity:delete` (in which case they already saw it in the full-tenant list, not because of any new membership).

No duplicate user or employee rows were created by this work — none were created at all, which is the point: doing so safely would require either violating the unique `UserId` index or designing a new multi-company membership/authority model, both explicitly out of scope for this task. This should be raised as a product/database design decision before any future attempt to implement it.

## Create endpoint: no changes required

`CreateLegalEntityCommandHandler.cs` already derives the tenant strictly server-side:

```csharp
var tenantId = _currentUser.TenantId;
```

and applies exactly the required defaults:

```csharp
IsPrimary = false,
IsActive = true,
Timezone = "UTC",
FinancialYearStartMonth = 1,
FirstDayOfWeek = 1,
StandardWorkingDays = LegalEntityMapper.SerializeStandardWorkingDays([1, 2, 3, 4, 5]),
DefaultLanguage = "en-US",
DateFormat = "DD MMM YYYY",
TimeFormat = "12h"
```

`CreateLegalEntityCommandValidator.cs` already enforces exactly the required field rules — notably `CompanyCode` is `.NotEmpty()` (required, max 20), not optional, matching the task's explicit instruction to defer to "the current backend validator" rather than the request DTO's nullable C# type. Name/RegistrationNumber/CountryCode/CurrencyCode/TaxRegistrationNumber limits all match spec exactly. No logo, `tenantId`, `userId`, `isPrimary`, `isActive`, or General-Settings-only field is accepted by the request contract (`CreateLegalEntityRequest`), confirmed by both reading the DTO and the passing `LegalEntityPart2BArchitectureTests.RequestContracts_DoNotExposeTenantId`/`CommandsAndQueries_DoNotAcceptTenantId` tests.

## Tests / build commands run

All run from `C:\onevoNew\HRMS-Backend-v1`, branch `feature/mkcert-tenant-subdomain-https`:

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | Build succeeded, 0 errors |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~LegalEntity"` | **168/168 passed** |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --filter "FullyQualifiedName~LegalEntity"` | **72/72 passed** |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntitiesIntegrationTests"` | **Skipped — see below** |
| `git diff --check` | Clean, no whitespace errors |

## Skipped checks

The integration test run could not execute in this environment: Docker is installed (client v29.6.2) but the daemon is not running, and `ONEVO_TEST_DB` is not set. `LegalEntitiesIntegrationTests.InitializeAsync` fails immediately on `PostgreSqlBuilder.Build()`/Docker ping (`Failed! - Failed: 33, Passed: 0, Skipped: 0, Total: 33`) — this is an environment limitation, not a code defect: the test project compiled cleanly against the new integration tests, meaning the 3 new tests (`GetGeneralSettings_RegularEmployeeAnotherCompany_Returns403_ViaPermissionGate`, `UpdateGeneralSettings_RegularEmployeeAnotherCompany_Returns403_ViaPermissionGate`, `GetGeneralSettings_ManagerWithLegalEntityUpdate_AnotherCompanyInTenant_Returns200`) are syntactically and referentially correct against the current fixtures (`_tenantAPrimaryLegalEntityId`, `_tenantASecondLegalEntityId`, `_tenantAManager`, `_tenantARegularEmployee`). They have not been run against a real database in this session. Re-run with Docker available (or `ONEVO_TEST_DB` pointing at a local PostgreSQL instance) before merging.

## Remaining risks

- The ~15 other call sites of `GetByIdForTenantAsync` (Position, Department, Delete/Logo LegalEntity handlers, etc.) are untouched and remain tenant-scoped-only by design — that was explicitly out of this task's scope and each would need its own accessible-company evaluation if ever required.
- If the permission model is later extended so that a caller can hold a permission gating these routes without also satisfying `LegalEntityAccessPolicy.HasManagementAccess`, the non-management branch of `GetAccessibleByIdAsync` becomes reachable over HTTP for the first time. At that point, re-verify with real HTTP integration tests, not just the unit tests added here.
- Integration tests were not actually executed in this environment (see "Skipped checks") — run them before merging.
- Creator membership remains an open product/database design question; no code in this repo currently gives a company creator any special access to the company they created beyond what their existing permissions already grant.

## Repo state

Branch `feature/mkcert-tenant-subdomain-https`, 4 local commits ahead of `origin/feature/mkcert-tenant-subdomain-https`, none pushed:

```
f870d98 test: cover cross-company General Settings GET/PUT rejection over HTTP
5093ed0 fix: scope General Settings GET/PUT to the caller's accessible company
1e48f1b feat: add ILegalEntityRepository.GetAccessibleByIdAsync
8986233 refactor: extract shared LegalEntityAccessPolicy.HasManagementAccess helper
```

`git diff --check` is clean. This report file itself is uncommitted, per the task's "do not commit or push" instruction — left for the user to review and decide on committing.
