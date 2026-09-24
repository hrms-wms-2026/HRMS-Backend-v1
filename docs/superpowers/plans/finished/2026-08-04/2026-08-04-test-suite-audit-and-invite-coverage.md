# Test Suite Audit and Invite Flow Coverage

**Goal:** Establish a pass/fail baseline for every existing test suite in both the backend (`HRMS-Backend-v1`) and frontend (`Hrms--Web-application---front-end---v1`) repos, fix any live failures found, and close the confirmed test-coverage gap in the Invitation feature (Accept Invitation via Google/Password, Invite Tenant Admin, Get Invitation by Token) — the gap the user chose to prioritize out of the full list found during the audit.

**Architecture:** No production architecture changes. This plan only touches test files (new xUnit tests, one Playwright assertion fix) and one local, gitignored `.env` file.

**Tech Stack:** Backend — xUnit + Moq + FluentAssertions (`ONEVO.Tests.Unit`), Testcontainers/Postgres (`ONEVO.Tests.Integration`), custom reflection-based checks (`ONEVO.Tests.Architecture`). Frontend — Vitest (`ng test --watch=false`), Playwright (`npx playwright test`).

**Global Constraints:**
- No production/application code changes — findings here are test-only or local-config-only.
- New backend unit tests follow the existing handler-test convention (constructor-injected mocks via Moq, `Result<T>` assertions, `FluentAssertions`), matching the style of `CreateRoleCommandHandlerTests.cs` and `AcceptInvitationDirectoryTests.cs`.
- `.env` is gitignored and local-only; editing it is not a tracked change and carries no review burden for other contributors.

---

## Baseline run results (before fixes)

| Suite | Result |
|---|---|
| Backend `ONEVO.Tests.Unit` | 1125/1125 pass |
| Backend `ONEVO.Tests.Architecture` | 342/343 pass — 1 failure |
| Backend `ONEVO.Tests.Integration` | ran via Testcontainers (see Task 3) |
| Frontend Vitest (26 spec files) | 111/111 pass |
| Frontend Playwright (`e2e/auth-happy-path.spec.ts`) | 0/1 pass — failing |

## Gap inventory (backend, verified — not naming-convention guesses)

Confirmed **zero coverage anywhere** (grepped for both class-name references and route strings, not just filename matching):
- `InviteTenantAdminCommandHandler`
- `GetInvitationByTokenQueryHandler`
- `PermissionsController` (`GET /api/v1/permissions`)
- `ProcessStripeEventCommandHandler` (Stripe webhook processing — controller test exists but doesn't exercise this handler)
- A long tail (~50 handlers) in the DevPlatform admin backoffice (Payment Gateway, Platform OAuth Apps, Service Keys, Tenant Role Templates, etc.)

Confirmed **partial** coverage (initially flagged as fully missing by filename search, corrected after reading the actual test file):
- `AcceptInvitationGoogleCommandHandler` / `AcceptInvitationPasswordCommandHandler` — covered by `tests/ONEVO.Tests.Unit/Features/Auth/AcceptInvitationDirectoryTests.cs`, but only for the directory-upsert-on-success/not-found paths and the missing-OAuth-app case. Not covered: expired/revoked/used invitation, wrong-tenant invitation, Google email-domain-mismatch policy branches, existing-external-identity conflict, position→role auto-assignment.

Frontend: component/service/guard/interceptor unit specs are 1:1 complete (26 files, no gaps). E2E has exactly one scenario (login → legal-consent → tenant redirect → dashboard happy path); no coverage for error paths (MFA isn't built in the UI yet, so that's expected, not a gap).

User decision (via AskUserQuestion): fix the two live failures first, then write new tests **only** for the Invitation flow (`InviteTenantAdminCommandHandler`, `GetInvitationByTokenQueryHandler`). Permissions, Stripe, and the DevPlatform admin long-tail are explicitly deferred, not in scope for this plan.

---

### Task 1: Fix backend architecture test failure — stale `.env` key
- [x] `CredentialOwnershipCompletionArchitectureTests.LocalEnvironment_WhenPresent_ContainsNoDeprecatedProviderSettings` failed because local `.env` still had `Email__Provider=resend`. Confirmed via grep that no code path reads `Email__Provider` anymore — email provider selection moved to `PlatformServiceKeyResolver` / DB-backed `PlatformServiceKeys` as part of the credential-ownership migration this test enforces. Removed the line from `.env` (gitignored, local-only). Verified: `ONEVO.Tests.Architecture` now 343/343.

### Task 2: Fix frontend Playwright e2e failure — stale assertion, not a regression
- [x] `e2e/auth-happy-path.spec.ts` asserted a `getByRole('heading', { name: 'Welcome, ...' })` that the `2026-07-30-dashboard-skeleton-redesign` plan's Task 6 intentionally removed from `DashboardHomeComponent`, replacing it with `HomePageProfileCapsuleComponent` (`.profile-capsule` span) in the top navbar. The e2e test predates that change and was never updated. Fixed the assertion to check `.profile-capsule` text instead of the removed heading. Verified: test passes.

### Task 3: Run backend integration suite (Testcontainers/Postgres) to complete the baseline
- [x] First full run (concurrent with the Unit/Architecture runs and Docker under load from repeated container churn) showed 133/135 passing, 2 failures: `BaseForgotPasswordRestrictedRoleHttpIntegrationTests.BaseDomain_OneEligibleTenant_RestrictedRoleHttp_CreatesTokenAndOutboxRowWithoutRlsViolation` (Postgres connection refused during `WebApplicationFactory` startup) and `LegalEntitiesIntegrationTests.Delete_ConfirmNameMismatch_Returns400` (403 instead of 204 in a setup helper, `ProvisionAndLoginOwnerAsync`). Both failures are inside `InitializeAsync()` setup paths, not test-body assertions — the classic signature of Testcontainers port/resource contention, not a code defect. Re-ran both test classes in isolation (nothing else running against Docker): 22/22 passed, confirming flakiness rather than a regression. No code change made. (Note: the first run's console also printed a bogus `Duration: 7 h 40 m` — real wall-clock time for these two classes alone was 7m40s, confirming the first number was a reporting artifact of the concurrent load, not a real hang.)

### Task 4: Add `InviteTenantAdminCommandHandler` tests
- [x] `tests/ONEVO.Tests.Unit/Features/Auth/Invite/InviteTenantAdminCommandHandlerTests.cs` — not-authenticated → forbidden, tenant-not-found → not-found, tenant not in `Provisioning` status → conflict (all 4 non-provisioning statuses), valid request delegates to `ITenantOwnerInvitationService.InviteOwnerAsync` with the correctly mapped request, and downstream service failure propagates unchanged.

### Task 5: Add `GetInvitationByTokenQueryHandler` tests
- [x] `tests/ONEVO.Tests.Unit/Features/Auth/Invite/GetInvitationByTokenQueryHandlerTests.cs` — token not found → not-found, tenant missing → not-found, pending invitation with no role → empty role name + `pending` status, invitation with a role → role name populated, and status mapping for `expired`/`accepted`/`revoked` invitations (exercises `InviteMapper.ToDto`'s status computation indirectly, since the mapper is internal).

### Task 6: Full regression run across both repos
- [x] Backend: `dotnet test` on all three projects, run sequentially (not concurrently, to avoid the Task 3 contention) — `ONEVO.Tests.Unit` 1170/1170, `ONEVO.Tests.Architecture` 343/343, `ONEVO.Tests.Integration` 135/135, all clean.
- [x] Frontend: `npx ng test --watch=false` (111/111) and `npx playwright test` (1/1), both clean.
- [x] Everything green. Final numbers reported to the user.

## Final baseline (after fixes)

| Suite | Before | After |
|---|---|---|
| Backend `ONEVO.Tests.Unit` | 1125/1125 | 1170/1170 (+45 new) |
| Backend `ONEVO.Tests.Architecture` | 342/343 | 343/343 |
| Backend `ONEVO.Tests.Integration` | 133/135 (2 flaky, confirmed non-reproducing in isolation) | 135/135 |
| Frontend Vitest | 111/111 | 111/111 (unchanged) |
| Frontend Playwright | 0/1 | 1/1 |

## Still open (explicitly deferred, not fixed this round)
- `PermissionsController` (`GET /api/v1/permissions`) — zero test coverage anywhere.
- Stripe webhook event processing (`ProcessStripeEventCommandHandler`) — not exercised by any test.
- ~50-handler long tail in the DevPlatform admin backoffice (Payment Gateway, Platform OAuth Apps, Service Keys, Tenant Role Templates, etc.) — thin or no coverage.

These were confirmed during the audit but the user chose "Invitation flow" as this round's scope; they remain real, tracked gaps for a future pass.
