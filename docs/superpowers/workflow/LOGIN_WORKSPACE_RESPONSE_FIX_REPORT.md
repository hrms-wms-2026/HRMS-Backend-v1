# Login Workspace Response Fix Report

## Root cause

Base-domain login resolves the tenant internally (every continuation path already loads or has
access to the `Tenant` entity), but none of the response DTOs returned to the browser ever exposed
that resolution. `AuthSessionResponseDto`/`LoginResponseDto` carried `User`, `Permissions`,
`ActiveModules`, and the MFA/legal/password-change flags, but no `workspace` object — so after a
single-tenant login (the common case), the frontend had no way to learn which tenant subdomain
(`{slug}.onevo.com`) to redirect the browser to. The one place a slug *was* already returned —
`WorkspaceOptionResponse` on the multi-workspace-selection response — only fires for the
multiple-candidate-per-email edge case, not the common path.

## Files changed

**Response DTOs**
- `src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/AuthSessionResponseDto.cs` — added
  `WorkspaceResponseDto(Slug, DisplayName)` record and a `Workspace` property on
  `AuthSessionResponseDto` (JSON: `slug`, `display_name` — no tenant id).
- `src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/LoginResponseDto.cs` — added
  `Workspace` property; `ToSessionResponse()` now copies it through unconditionally (present on
  every branch, not just the final authenticated one).

**Population chokepoint**
- `src/ONEVO.Application/Features/Auth/Login/Mappers/LoginMapper.cs` — `ToPasswordChangeRequired`,
  `ToMfaRequired`, `ToLegalAcceptanceRequired` now take a `Tenant` parameter and populate
  `Workspace` via a new `ToWorkspaceResponseDto(Tenant)` helper.
- `src/ONEVO.Application/Features/Auth/Login/Services/LoginContinuationService.cs` — `ContinueAsync`
  passes its already-loaded `tenant` into all three mapper calls. `FinishAuthenticatedLoginAsync`
  gained an optional trailing `Tenant? tenant = null` parameter: callers that already loaded the
  tenant (the internal call from `ContinueAsync`, `VerifyMfaCommandHandler`,
  `AcceptPendingLegalDocumentsCommandHandler`) pass it in to avoid a redundant query; callers that
  only have the `User` (`ForcePasswordChangeCommandHandler`, both invitation-acceptance handlers)
  omit it and a fallback `_tenants.GetByIdAsync(user.TenantId, ct)` resolves it. If that lookup
  returns null, the method now fails with 401 rather than throwing later on a null tenant.
- `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/ILoginContinuationService.cs` /
  `ILoginSessionMaterialFactory.cs` — interface signatures updated to match.
- `src/ONEVO.Infrastructure/Identity/Tokens/LoginSessionMaterialFactory.cs` — `PrepareAsync` now
  takes `Tenant tenant` and includes it in the final `LoginResponseDto`.
- `src/ONEVO.Application/Features/Auth/Legal/Commands/AcceptPendingLegalDocuments/AcceptPendingLegalDocumentsCommandHandler.cs`
  / `src/ONEVO.Application/Features/Auth/Login/Commands/MfaVerify/VerifyMfaCommandHandler.cs` —
  both already loaded `Tenant` for their own validation; now pass it through to
  `FinishAuthenticatedLoginAsync`/`LoginMapper.ToLegalAcceptanceRequired`.

Because every login entry point (`BaseLoginCommandHandler`, `BaseGoogleLoginCommandHandler`,
`SelectWorkspaceCommandHandler`, `VerifyMfaCommandHandler`,
`AcceptPendingLegalDocumentsCommandHandler`, `ForcePasswordChangeCommandHandler`, both invitation
handlers) already funnels through this one service, no controller (`AuthLoginController`,
`AuthMfaController`, `AuthPendingLegalController`) needed changes — `TenantAuthResponseWriter`
already just calls `dto.ToSessionResponse()` on every branch.

**`GET /api/v1/auth/me`**
- `src/ONEVO.Application/Features/Auth/Login/Queries/GetCurrentSession/GetCurrentSessionQueryHandler.cs`
  — `ITenantContext` exposes `Slug` but not a display name, so the handler now also injects
  `ITenantRepository` and fetches the `Tenant` by `_tenantContext.TenantId` to populate `Workspace`.

## Response shape

**Before** (single-tenant success):
```json
{
  "authenticated": true,
  "user": { "email": "owner@acme.test" },
  "permissions": [], "active_modules": [],
  "must_change_password": false, "mfa_required": false, "legal_acceptance_required": false,
  "expires_at": "..."
}
```

**After** (identical shape on every tenant-resolved branch — success, MFA-required,
legal-required, password-change-required, and `/auth/me`):
```json
{
  "authenticated": true,
  "user": { "email": "owner@acme.test" },
  "permissions": [], "active_modules": [],
  "must_change_password": false, "mfa_required": false, "legal_acceptance_required": false,
  "expires_at": "...",
  "workspace": { "slug": "acme", "display_name": "Acme" }
}
```

## Affected APIs (all now include `workspace` once a tenant is resolved)

`POST /api/v1/auth/login` (success / MFA-required / legal-required / password-change-required),
`POST /api/v1/auth/login/select-workspace` (all branches, via the same continuation pipeline),
`POST /api/v1/auth/login/google` (single-match + continuation branches — multi-match
`WorkspaceOptionResponse` list left unchanged), `POST /api/v1/auth/mfa/verify`,
`POST /api/v1/legal/acceptances/complete-login`, `POST /api/v1/auth/force-change-password`,
`GET /api/v1/auth/me`.

## Tests run and counts

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | **Build succeeded**, 0 warnings, 0 errors |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --verbosity minimal` | **1000/1000 passed** |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --verbosity minimal` | **279/280 passed** — see note below |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj` | **Not run — Docker unavailable** in this environment (`docker info` fails) |
| `git diff --check` | clean, no output |

**Pre-existing, unrelated architecture failure (not caused by this change):**
`CredentialOwnershipCompletionArchitectureTests.LocalEnvironment_WhenPresent_ContainsNoDeprecatedProviderSettings`
fails on this machine because it reads a local, gitignored `.env` file at the repo root and finds
legacy `AllowedOrigins__0`/`Email__Provider`-style keys in it. Confirmed via `git log` that this
test predates this change (added in commit `5f58ece`, "login clean up"), and the assertion targets
a machine-local `.env` file this change never touches. Verified it fails identically before and
after these edits.

New test coverage added:
- `tests/ONEVO.Tests.Unit/Features/Auth/GetCurrentSessionQueryHandlerTests.cs` — 3 new cases
  (workspace included; unauthenticated path unchanged and never queries the tenant repo;
  tenant_id/user_id never appear in the serialized JSON).
- `tests/ONEVO.Tests.Unit/Features/Auth/LoginContinuationServiceTests.cs` — updated all
  `PrepareAsync`/mapper call sites for the new `Tenant` parameter, added workspace assertions to
  the password-change/MFA/legal-pending cases, and added 2 new cases covering the
  `FinishAuthenticatedLoginAsync` tenant-fallback-fetch path and its "tenant not found" failure.
- `tests/ONEVO.Tests.Unit/Features/Auth/VerifyMfaCommandHandlerTests.cs` — updated 3 existing
  `_continuation` mock setups/verifications for the new trailing `tenant` argument.
- `tests/ONEVO.Tests.Unit/Features/Auth/LoginWorkspaceResponseTests.cs` (new) — controller-level
  coverage: `AuthLoginController.Login` success/MFA-required/legal-required and
  `SelectWorkspace` all include `workspace` in the actual serialized response object, plus a
  JSON-string assertion that no tenant/user id ever appears.
- `tests/ONEVO.Tests.Architecture/WorkspaceResponseArchitectureTests.cs` (new) — 8 checks:
  `WorkspaceResponseDto` exposes only `Slug`/`DisplayName`, has no `Guid` property,
  correct `slug`/`display_name` JSON names; `AuthSessionResponseDto` has no un-ignored `Guid`
  property and none of `LoginResponseDto`'s internal-only fields (`CsrfToken`, `CsrfTokenHash`,
  `MfaChallenge`, `LegalChallenge`, `LegalCsrfToken`); `LoginResponseDto`'s internal-only doc
  comment is still intact; tenant-host password login is still rejected; source-level checks that
  `LoginContinuationService` and `GetCurrentSessionQueryHandler` actually populate workspace.

## Proof tenant_id/user_id/internal ids are not exposed

- `CurrentUserDto.UserId`/`TenantId` are `[JsonIgnore]` (pre-existing, reverified by the existing
  `AuthContractArchitectureTests.CurrentUserInternalIdentifiers_AreNeverSerialized`, untouched).
- New `WorkspaceResponseArchitectureTests.WorkspaceResponseDto_ExposesOnlySlugAndDisplayName` and
  `..._HasNoTenantIdOrOtherGuidProperty` assert the workspace DTO's only properties are `Slug`
  and `DisplayName`, and that neither it nor `AuthSessionResponseDto` has any un-ignored `Guid`
  property anywhere.
- `GetCurrentSessionQueryHandlerTests.Me_DoesNotSerializeTenantIdOrUserId` and
  `LoginWorkspaceResponseTests.Login_WorkspaceJson_NeverExposesTenantIdOrUserId` both
  `JsonSerializer.Serialize` the actual response object and assert the resulting string contains
  neither the `tenant_id`/`user_id` keys nor the raw GUID values themselves.

## Proof tenant-host password login was not reintroduced

- `AuthLoginController.Login` is unmodified; the `if (_tenantContext.ContextMode == TenantContextMode.Tenant) return Problem("Tenant-host password login is not supported.", statusCode: 400);`
  guard is untouched.
- Existing `TenantHostPasswordLoginRetirementArchitectureTests` (source-scan for any reintroduced
  `LoginCommand` usage) and `TenantLoginControllerTests.Login_OnTenantHost_ReturnsSafeRejection_AndNeverCallsMediator`
  both still pass unmodified.
- New `WorkspaceResponseArchitectureTests.TenantHostPasswordLogin_RemainsRejected` re-asserts the
  rejection string and guard condition are still present in `AuthLoginController.cs`.

## Remaining risks

- **Integration tests were not run** (no Docker in this environment) — the task's own integration
  scenarios (base-domain login/legal-pending/MFA/`/auth/me` all returning `workspace.slug`,
  multi-workspace selection still working) are covered at the unit/architecture level here but
  should be run in CI or a Docker-enabled environment before this is considered fully verified
  end-to-end against a real Postgres instance.
- `GetCurrentSessionQueryHandler` now does one additional `ITenantRepository.GetByIdAsync` call
  per `/auth/me` request (previously it only read `ICurrentUser`/`ITenantContext`, no DB round
  trip for tenant data). This is a minor, expected cost of exposing `display_name`, which isn't
  available on `ITenantContext`.
- `LoginContinuationService.FinishAuthenticatedLoginAsync`'s new fallback tenant lookup adds one
  `ITenantRepository.GetByIdAsync` call for the three call sites that don't already have a loaded
  `Tenant` (`ForcePasswordChangeCommandHandler`, `AcceptInvitationPasswordCommandHandler`,
  `AcceptInvitationGoogleCommandHandler`) — deliberately accepted per the task's guidance to prefer
  avoiding the extra query only where a `Tenant` is already in hand.
- Nothing under Postman collections, `OneVo-HR` docs, RLS/schema/migrations, or the MFA/legal/CSRF
  behavior itself was touched. No commit or push was made — all changes are in the working tree,
  matching the constraint.
