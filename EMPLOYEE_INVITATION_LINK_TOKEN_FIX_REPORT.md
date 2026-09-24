# Employee Invitation Link/Token Fix — Backend Report

## Scope

Employee onboarding invitation links only (`FinalizeOnboardingDraftCommandHandler`,
`ApproveAccessGrantRequestCommandHandler`, `ResendEmployeeInvitationCommandHandler`, the
`employee_onboarding_invite` email template, and the preview/accept read paths). Tenant-owner
(`InviteTenantAdminCommandHandler`) and platform-admin (`InvitePlatformManagerCommandHandler`)
invites were left untouched per instructions, and are called out explicitly in "Remaining risks"
below since they share some of the same infrastructure.

## Root causes found

1. **Non-URL-safe tokens in a URL path segment.** `SecureTokenGenerator.GenerateOpaqueToken()`
   returned standard Base64 (`Convert.ToBase64String`), which can contain `/`, `+`, `=`. All three
   employee-invitation handlers used this for the raw invite token, and the token is placed
   directly in a path segment: `/auth/invitations/{token}`. A `/` in the token cannot survive a
   single ASP.NET Core route parameter — either literally (extra path segment, 404) or
   percent-encoded (`%2F`, which routing does not reliably treat as part of the segment). `+`
   and `=` are not path separators, so those two were already transportable — the token generator
   fix was needed specifically because of `/`.

2. **Immediate-finalize invites had no tenant host.** `FinalizeOnboardingDraftCommandHandler`
   built `EmployeeOnboardingInviteEmailPayload` without a `TenantSlug`, while
   `ApproveAccessGrantRequestCommandHandler` and `ResendEmployeeInvitationCommandHandler` already
   looked up the tenant and passed `tenant?.Slug`. `EmailTemplateRenderer.ApplyTenantSlug` no-ops
   when the slug is null, so immediate-finalize invites rendered a link on the **root** host
   (`https://localhost:4200/...`) instead of the tenant subdomain
   (`https://{slug}.localhost:4200/...`). `HostTenantResolutionMiddleware` can't resolve the
   tenant from the root host, so the accept flow (which requires `ITenantContext` to resolve to
   the invitation's tenant) would fail even if the link opened.

3. **Validity was 72h, not the intended 24h.** All three handlers hardcoded
   `InvitationValidityHours = 72`, and the email template said "This link expires in 72 hours."

## Files changed

- `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/ISecureTokenGenerator.cs` — added
  `GenerateUrlSafeOpaqueToken()`.
- `src/ONEVO.Infrastructure/Identity/Tokens/SecureTokenGenerator.cs` — implemented it
  (Base64Url: `+`→`-`, `/`→`_`, padding stripped).
- `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs`
  — added `ITenantRepository`, looks up the tenant and passes `tenant?.Slug` into the payload;
  switched to `GenerateUrlSafeOpaqueToken()`; `InvitationValidityHours` 72 → 24.
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs`
  — switched to `GenerateUrlSafeOpaqueToken()`; `InvitationValidityHours` 72 → 24 (tenant-slug
  wiring was already correct here).
- `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ResendEmployeeInvitation/ResendEmployeeInvitationCommandHandler.cs`
  — switched to `GenerateUrlSafeOpaqueToken()`; `InvitationValidityHours` 72 → 24; updated the
  stale "72h" doc comment (tenant-slug wiring was already correct here).
- `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs` — "72 hours" → "24
  hours" in `RenderEmployeeOnboardingInvite` only (both HTML and text bodies). Did **not** touch
  `RenderPlatformManagerInvite`'s or `RenderTenantOwnerInvite`'s copy.
- Tests: `tests/ONEVO.Tests.Unit/Features/Infrastructure/SecureTokenGeneratorTests.cs` (new),
  `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDrafts/FinalizeOnboardingDraftCommandHandlerTests.cs`,
  `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs`,
  `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ResendEmployeeInvitationCommandHandlerTests.cs`,
  `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs`.

`AuthInvitationController.cs`, `GetInvitationByTokenQueryHandler.cs`,
`AcceptEmployeeInvitationCommandHandler.cs`, and `InvitationTokenHasher.cs` were inspected but
**not modified** — see "Preview/accept behavior" below.

## Old vs new behavior

| | Before | After |
|---|---|---|
| Token charset | Base64 (`/`, `+`, `=` possible) | Base64Url, no `/`, `+`, `=` |
| Immediate-finalize link host | Root host (`localhost:4200`) | Tenant host (`{slug}.localhost:4200`) |
| Validity | 72 hours | 24 hours |
| Email copy | "This link expires in 72 hours." | "This link expires in 24 hours." |

Resolved local link shape (confirmed against `src/ONEVO.Api/appsettings.Development.json`, where
`Email:AppBaseUrl` = `https://localhost:4200`):
`https://{tenantSlug}.localhost:4200/auth/invitations/{token}`.

## Legacy Base64 token compatibility

**Decision: not supported for tokens containing `/`; supported for tokens that happen not to.**
`InvitationTokenHasher.Hash` / `GetByTokenHashAsync` only look at the raw token string's SHA-256
hash, so charset is otherwise irrelevant to lookup. The break is structural: a single
`[HttpGet("invitations/{token}")]` route parameter cannot carry a literal `/` (extra path
segment) and does not reliably accept an encoded `%2F` in its place either, on either the
ASP.NET Core routing side or the Angular route-matching side. `+` and `=` are not path
separators and continue to route correctly (already covered by
`EmailTemplateRendererTests.RenderEmployeeOnboardingInvite_WithBase64TokenContainingUrlUnsafeCharacters_EscapesToken`).
**Operational consequence:** any employee invitation already sent before this deploy whose token
happens to contain `/` cannot be opened; those employees need `ResendEmployeeInvitation`. No
migration was written to reissue old tokens — resend covers it and is already user-triggered.

## Preview/accept behavior

Inspected and found already correct, no changes made:
- `GetInvitationByTokenQueryHandler` returns a 404 with a generic "Invitation not found." /
  "Tenant not found." only when the row genuinely doesn't exist; otherwise it always returns 200
  with a `status` field computed by `InviteMapper.ComputeStatus` (`accepted` / `revoked` /
  `expired` / `pending`) — the frontend fix (see the frontend report) branches on this field.
- `AcceptEmployeeInvitationCommandHandler.CheckInvitationUsable` already returns four distinct,
  safe messages ("already been accepted", "has been revoked", "has expired") ahead of the
  not-found check, none of which expose token hashes, route internals, CSRF, cookies, or headers.

## Tests run

- `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Debug` — succeeded (1 pre-existing unrelated
  warning).
- `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Debug` — succeeded.
- `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Invit|FullyQualifiedName~Onboarding|FullyQualifiedName~SecureTokenGenerator|FullyQualifiedName~EmailTemplateRenderer"`
  — 280/280 passed.
- `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` (full suite) — 2063/2063 passed.
- `git diff --check` — clean (only pre-existing LF/CRLF autocrlf notices, no whitespace errors).
- `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj` — **fails**, but on
  a pre-existing, unrelated error: `EmployeesListIntegrationTests.cs(220,20)` is missing the new
  `currentUser` constructor argument that a separate in-flight change to
  `GetEmployeeQueryHandler` (visible in this branch's git status, not touched by this task)
  introduced. Confirmed no error in that build output references `SecureTokenGenerator`,
  `ISecureTokenGenerator`, or any file this task touched.

## Skipped checks

- Integration test suite was not run (needs a live Postgres via Testcontainers, and the project
  doesn't currently build for the unrelated reason above).
- No live verification: I did not send a real employee invite through a running dev tenant and
  click the resulting link end-to-end. All verification above is build/unit-test level.

## Remaining risks

- `InviteTenantAdminCommandHandler` → `TenantOwnerInvitationService.GenerateInviteToken()`
  already implements its own inline Base64Url-safe token generation (identical scheme, `+`→`-`,
  `/`→`_`, no padding) — it does **not** share `SecureTokenGenerator`, so it was never affected by
  the bug this task fixes, but it is now duplicate logic that could be consolidated onto
  `ISecureTokenGenerator.GenerateUrlSafeOpaqueToken()` in a follow-up. Left as-is per "don't touch
  unrelated tenant-owner/platform-admin flows."
- `InvitePlatformManagerCommandHandler` still calls `GenerateOpaqueToken()` (standard Base64), but
  its link is `/auth/accept-invite?token=...` (query string, not a path segment), so it isn't
  exposed to the same routing break — left untouched per scope.
- The Integration test project has a pre-existing, unrelated compile error (see above) blocking a
  full integration run of anything in this branch, invitation-related or not.
