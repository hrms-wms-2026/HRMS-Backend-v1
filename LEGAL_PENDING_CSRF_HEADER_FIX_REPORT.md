# Legal Pending-Acceptance CSRF: Body -> Header

## 1. Problem Summary

`POST /api/v1/legal/acceptances/complete-login` is the one CSRF-protected endpoint in the codebase
that took its token in the JSON body (`csrf_token`) instead of the `X-CSRF-Token` header used
everywhere else, including by `CsrfProtectionMiddleware` for every other authenticated POST. This
endpoint is unauthenticated (no `onevo_session`/`admin_session` cookie exists yet at this point in
the flow), so it is listed in `CsrfProtectionMiddleware.ExemptPaths` and has always validated its own
CSRF token, independently, inside `AuthPendingLegalController` / `AcceptPendingLegalDocumentsCommandHandler`.
This work moves *only* where the controller reads that token from - body to header - and brings the
request shape in line with the rest of the API.

## 2. Files Changed

| File | Change |
|---|---|
| `src/ONEVO.Api/Contracts/Auth/AcceptPendingLegalDocumentsRequest.cs` | Removed `CsrfToken`/`csrf_token`. Record now carries only `Acceptances`. |
| `src/ONEVO.Api/Controllers/Tenant/Auth/AuthPendingLegalController.cs` | Reads the token from `Request.Headers["X-CSRF-Token"]`; returns `403` via `Problem(...)` (existing in-controller style, matching the `401` already used for the missing-cookie case two lines above) when missing/blank; passes the header value to `AcceptPendingLegalDocumentsCommand` instead of `request.CsrfToken`. |
| `tests/ONEVO.Tests.Unit/Features/Auth/AuthPendingLegalControllerTests.cs` | Existing test updated for the new 1-arg request constructor; added 3 new tests (missing header -> 403, blank header -> 403, valid header value flows through to the command). |
| `tests/ONEVO.Tests.Architecture/AuthContractArchitectureTests.cs` | Added: `AcceptPendingLegalDocumentsRequest` has no property other than `Acceptances`; controller source contains `Request.Headers["X-CSRF-Token"]` and no longer contains `request.CsrfToken`. |
| `tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs` | Shared `CompleteLegalAcceptanceAsync` helper (used by 6 existing tests) switched to send `X-CSRF-Token` header, body now only `acceptances`. Added 4 new tests (see Section 4). |

**Not touched:** `AcceptPendingLegalDocumentsCommand`, its validator, or its handler - the command
still has a `LegalCsrfToken` property; only the value's source in the controller changed. Legal
acceptance business rules, `onevo_legal_pending` cookie semantics, and normal `onevo_session` CSRF
(`CsrfProtectionMiddleware`) are all unchanged. OneVo-HR docs were not modified (out of scope by
instruction); see Section 6 for what that leaves undocumented.

## 3. Request Shape: Before vs After

**Before**
```
POST /api/v1/legal/acceptances/complete-login
Content-Type: application/json
Cookie: onevo_legal_pending=<HttpOnly>; onevo_legal_csrf=<readable>

{
  "csrf_token": "<value from onevo_legal_csrf cookie>",
  "acceptances": [ { "document_type": "...", "version": "...", "decision": "..." } ]
}
```

**After**
```
POST /api/v1/legal/acceptances/complete-login
Content-Type: application/json
X-CSRF-Token: <value from onevo_legal_csrf cookie>
Cookie: onevo_legal_pending=<HttpOnly>; onevo_legal_csrf=<readable>

{
  "acceptances": [ { "document_type": "...", "version": "...", "decision": "..." } ]
}
```

Any `csrf_token` field still present in a caller's JSON body is silently ignored by the deserializer
(the property no longer exists on the contract) and is never read - see the
`LegalAcceptance_CsrfTokenInBody_IsIgnored_HeaderIsTheOnlySource` test in Section 4, which proves there is no
body fallback.

## 4. Cookie / Header / Body Behavior (unchanged unless noted)

- `onevo_legal_pending` - still `HttpOnly`, still set/read the same way, still cleared on successful
  completion. Never returned in any response body (verified by a new test, see below).
- `onevo_legal_csrf` - still a readable (non-`HttpOnly`) cookie, still cleared on successful
  completion. Its value is now expected in `X-CSRF-Token`, not the body.
- Normal `onevo_session`/`onevo_csrf` CSRF path (`CsrfProtectionMiddleware`) is untouched; this
  endpoint remains in `ExemptPaths` for the same reason as before (no session cookie exists yet).
- Missing/blank `X-CSRF-Token` -> `403` (`Problem(..., statusCode: 403)`), mediator never invoked -
  same short-circuit pattern already used for the missing-`onevo_legal_pending`-cookie case (`401`).
- `tenant_id`/`user_id` were never accepted in the body and still aren't - no change needed there.

## 5. Tests Added/Updated

**Unit - `AuthPendingLegalControllerTests.cs`** (extracted a `CreateController` helper so each test
only varies the cookie/header inputs it cares about):
- `MissingLegalPendingCookie_Returns401` - updated for the 1-arg request record.
- `MissingCsrfHeader_Returns403AndDoesNotCallMediator` - new.
- `BlankCsrfHeader_Returns403AndDoesNotCallMediator` - new (whitespace-only header treated as missing).
- `ValidCsrfHeader_PassesHeaderValueToCommand` - new; captures the command sent to `IMediator` and
  asserts `LegalCsrfToken` equals the header value (not any body field).

**Architecture - `AuthContractArchitectureTests.cs`**:
- `AcceptPendingLegalDocumentsRequest_HasNoCsrfTokenProperty` - reflects over the contract's public
  properties, asserts the set is exactly `{ Acceptances }`.
- `PendingLegalController_ReadsCsrfTokenFromHeaderNotBody` - source-text assertions: contains
  `Request.Headers["X-CSRF-Token"]`, does not contain `request.CsrfToken`.

**Integration - `BaseDomainLoginIntegrationTests.cs`** (real HTTP through the full ASP.NET Core
pipeline):
- `CompleteLegalAcceptanceAsync` helper (backs 6 pre-existing tests, including the acceptance race
  test) updated to send `X-CSRF-Token` and a body with only `acceptances`.
- `LegalAcceptance_MissingCsrfHeader_IsRejected` - new; no `X-CSRF-Token` header -> `403`.
- `LegalAcceptance_CsrfTokenInBody_IsIgnored_HeaderIsTheOnlySource` - new; the *correct* csrf value is
  placed only in the body (`csrf_token` field) with no `X-CSRF-Token` header at all -> `403`. This is
  the real regression guard against a body fallback (a test that merely serializes a hand-built object
  and checks the string doesn't contain `"csrf_token"` would pass trivially regardless of server
  behavior, so it was deliberately not used).
- `LegalAcceptance_SuccessResponse_NeverLeaksPendingChallengeOrCsrfValue` - new; on a `200 OK`
  completion, asserts the response body contains neither the `onevo_legal_pending` value, the
  `onevo_legal_csrf` value, nor the string `"onevo_legal_pending"`.
- `LegalAcceptance_ValidCsrfHeader_Succeeds` - new; header-only completion reaches `200 OK`.

## 6. Verification Results

Full verification, including the previously-pending full integration run, is complete. All suites pass.

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --filter "AuthPendingLegalControllerTests\|AcceptPendingLegalDocuments"` | **5/5 passed** |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build` (filter `Legal\|Csrf`) | **39/39 passed** |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build` (full) | **884/884 passed** |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build` (full) | **237/237 passed** |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "BaseDomainLoginIntegrationTests"` | **20/20 passed** (2m 23s, real Postgres via Testcontainers) |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build` (full) | **90/90 passed** (5m 53s) |
| `rg -n -P "[^\x00-\x7F]" ...` (report + contract + controller + the three test files) | No matches - all ASCII |
| `rg -n "request\.CsrfToken\|JsonPropertyName\(\"csrf_token\"\)\|csrf_token" ...` | Only the architecture test's `NotContain("request.CsrfToken")` assertion and the integration test's deliberate body-only `csrf_token` regression case - no production binding or fallback |
| `git diff --check` | Clean (only pre-existing LF/CRLF autocrlf notices, exit 0, no errors) |

The full integration suite (90/90) now supersedes the earlier partial run mentioned in prior
drafts of this report; there is no longer a pending or partial result for this change.

## 7. Postman / Manual Flow

```
1. POST /api/v1/auth/login
   { "email": "...", "password": "..." }
   -> 202 Accepted, legal_acceptance_required: true
      Set-Cookie: onevo_legal_pending=<HttpOnly>; onevo_legal_csrf=<readable>
      continue_url: ".../api/v1/legal/acceptances/complete-login"

2. POST /api/v1/legal/acceptances/complete-login
   Headers:
     Content-Type: application/json
     X-CSRF-Token: <value read from the onevo_legal_csrf cookie by the frontend>
   Cookies (sent automatically by the browser):
     onevo_legal_pending=<HttpOnly challenge cookie>
     onevo_legal_csrf=<readable csrf cookie>
   Body:
     {
       "acceptances": [
         { "document_type": "privacy_notice", "version": "1.0", "decision": "accepted" },
         { "document_type": "terms", "version": "1.0", "decision": "accepted" }
       ]
     }
   -> 200 OK, Set-Cookie: onevo_session=...; onevo_csrf=...
      (onevo_legal_pending / onevo_legal_csrf cleared)
```

Failure modes to check manually:
- Omit `X-CSRF-Token` -> `403`.
- Send the old body shape (`csrf_token` in JSON, no header) -> `403` (no body fallback).
- Reuse a challenge after successful completion -> `401` (challenge already consumed; unchanged
  behavior from before this fix).

## 8. Remaining Risks / Blockers

- **No frontend caller exists in this workspace to update.** `C:\onevoNew` contains only
  `HRMS-Backend-v1` (this backend) and `OneVo-HR` (docs-only, explicitly out of scope per
  instructions). A grep for `csrf_token`/`complete-login` across `*.ts` in the whole workspace found
  no Angular/JS source at all. If a separate frontend repo exists elsewhere and currently posts
  `csrf_token` in the body, it will start receiving `403 Forbidden` from this endpoint until updated
  to send `X-CSRF-Token` instead - this is an intentional breaking change per the task, but flagging
  it since it couldn't be verified against real caller code from this workspace.
- **OneVo-HR docs still describe the old body shape** (e.g. `LEGAL_DOCUMENT_VERSIONING_PHASE1_RECONCILIATION_REPORT.md`,
  `modules/auth/gdpr-consent/*`, `security/auth-flow.md`) and were intentionally left untouched per
  instruction ("Do not touch OneVo-HR docs"). They will be stale until updated separately.
  `MFA_SETUP_CONFIRMATION_FLOW_REPORT.md` and `TENANT_SESSION_RLS_CONTEXT_FIX_REPORT.md` (both in this
  repo) mention the endpoint but don't spell out the JSON body shape, so nothing there needed updating.
- **Swagger already documents `X-CSRF-Token` globally** (`SwaggerExtensions.cs` adds it as a global
  `AddSecurityRequirement`, not scoped per-path), so no Swagger change was needed and there's no
  mismatch between this now-required header and what Swagger shows for the endpoint.
- Full integration suite has been confirmed (90/90 passed, see Section 6) - no longer a pending item.
