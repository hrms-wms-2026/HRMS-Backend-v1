# Tenant TOTP MFA Setup Confirmation Flow

## 1. Problem Summary

`POST /api/v1/auth/mfa/enable` created an unverified TOTP secret (`UserMfa` row with `IsVerified = false`) and returned the plaintext secret + QR code URI, but there was no authenticated way to confirm that setup. The only place that could flip `IsVerified` to `true` was `POST /api/v1/auth/mfa/verify` - an `AllowAnonymous` endpoint gated by the `onevo_mfa` login-challenge cookie, which is only ever issued by `LoginContinuationService` when a user **already has a verified TOTP record** (see Section 6 below). There was consequently no working path at all to complete first-time MFA setup.

This work adds a new authenticated endpoint, `POST /api/v1/auth/mfa/confirm-setup`, that lets an already-logged-in user (via `onevo_session` + CSRF) prove possession of the TOTP secret returned by `mfa/enable` and mark that pending `UserMfa` row verified - without touching login's MFA challenge mechanics.

**Update:** a follow-up pass removed the dead `isVerified: false` fallback that previously lived inside `VerifyMfaCommandHandler` (see Section 6) now that `mfa/confirm-setup` is the one and only way to complete first-time TOTP setup.

## 2. Files Changed

### New
| File | Purpose |
|---|---|
| `src/ONEVO.Api/Contracts/Auth/ConfirmMfaSetupRequest.cs` | `record ConfirmMfaSetupRequest(string Code)` |
| `src/ONEVO.Application/Features/Auth/Login/Commands/MfaConfirmSetup/ConfirmMfaSetupCommand.cs` | `record ConfirmMfaSetupCommand(string Code) : IRequest<Result>` |
| `src/ONEVO.Application/Features/Auth/Login/Commands/MfaConfirmSetup/ConfirmMfaSetupCommandHandler.cs` | Core logic (see Section 3) |
| `src/ONEVO.Application/Features/Auth/Login/Commands/MfaConfirmSetup/ConfirmMfaSetupCommandValidator.cs` | `Code` required, must match `^[0-9]{6}$` |
| `tests/ONEVO.Tests.Unit/Features/Auth/ConfirmMfaSetupCommandHandlerTests.cs` | Handler unit tests |
| `tests/ONEVO.Tests.Unit/Features/Auth/EnableMfaCommandHandlerTests.cs` | Characterization tests for pre-existing, untouched `EnableMfaCommandHandler` (none existed before) |
| `tests/ONEVO.Tests.Architecture/MfaConfirmSetupArchitectureTests.cs` | Contract/route/security architecture rules (see Section 7) |

### Modified
| File | Change |
|---|---|
| `src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs` | Added `ConfirmMfaSetup` action; `EnableMfa`/`VerifyMfa` route/attribute behavior untouched |
| `src/ONEVO.Application/Features/Auth/Login/Commands/MfaVerify/VerifyMfaCommandHandler.cs` | Removed the dead `isVerified: false` fallback and the now-pointless `mfaRecord.IsVerified = true` flip (see Section 6) |
| `tests/ONEVO.Tests.Unit/Features/Auth/AuthMfaControllerTests.cs` | Added 2 controller tests for the new action |
| `tests/ONEVO.Tests.Unit/Features/Auth/VerifyMfaCommandHandlerTests.cs` | Added `NoVerifiedMfaRecord_ReturnsMfaNotConfigured_WithoutFallingBackToUnverifiedSetup` pinning the fallback's removal |
| `tests/ONEVO.Tests.Unit/Features/Auth/LoginContinuationServiceTests.cs` | Added 1 test proving a pending (unverified) TOTP setup never triggers an `onevo_mfa` challenge - see Section 6 |
| `tests/ONEVO.Tests.Architecture/AuthControllerSplitArchitectureTests.cs` | Added the new route to `AllOriginalAuthRoutesStillResolve`'s expected-route table |

**Not touched:** `EnableMfaCommandHandler.cs`, `UserMfa` entity/table, System Config, any SMS/email provider. `VerifyMfaCommandHandler.cs`'s externally observable behavior for every reachable login path is unchanged - only unreachable dead code was removed (see Section 6).

## 3. `ConfirmMfaSetupCommandHandler` Logic

```
1. pending = _userMfas.GetTotpAsync(_currentUser.UserId, isVerified: false)
   -> null? return Failure("No pending MFA setup exists.", 400)
2. secret = _encryption.Decrypt(pending.Secret)
3. _totpService.Verify(secret, request.Code)
   -> false? return Failure("Invalid MFA code.", 400)
4. pending.IsVerified = true
5. _unitOfWork.SaveChangesAsync()
6. return Success()
```

`UserId` comes only from `ICurrentUser` (populated from the authenticated session's claims by `CurrentUserService`); the command carries no `user_id`/`tenant_id` field, so a caller cannot target another account.

Controller response on success: `{"success": true}` (plain anonymous object, not `MfaSetupDto` - no secret is ever returned from this endpoint).

## 4. Endpoint Table

| Route | Before | After |
|---|---|---|
| `POST /api/v1/auth/mfa/enable` | `[Authorize(TenantPolicy)]`, unchanged | unchanged |
| `POST /api/v1/auth/mfa/verify` | `[AllowAnonymous]`, requires `onevo_mfa` cookie, unchanged | unchanged |
| `POST /api/v1/auth/mfa/confirm-setup` | did not exist | **new** - `[Authorize(Policy = "TenantPolicy")]`, requires `onevo_session` + `X-CSRF-Token`, does **not** require or read `onevo_mfa` |

CSRF: `confirm-setup` needed no controller-level code. `CsrfProtectionMiddleware.ExemptPaths` does not list it, and it is a POST under `/api/v1`, so the existing global middleware enforces `X-CSRF-Token` against the `onevo_session` principal's `csrf_token_hash` claim automatically - the same mechanism protecting `mfa/enable` today.

## 5. Postman Flow (manual verification procedure - not yet executed against a running server; no live DB/API instance was available in this session)

```
A. POST http://localhost:5139/api/v1/auth/login
   { "email": "<user>", "password": "<password>" }

B. If legal acceptance required, complete it first via
   POST /api/v1/legal/acceptances/complete-login

C. POST http://acme.localhost:5139/api/v1/auth/mfa/enable
   Headers: X-CSRF-Token: <onevo_csrf>
   Cookie:  onevo_session=<...>; onevo_csrf=<...>
   Body: {}
   -> 200 { "secret": "...", "qrCodeUri": "otpauth://totp/ONEVO:<email>?secret=...&issuer=ONEVO" }

D. Add secret/QR to an authenticator app, get current 6-digit code.

E. POST http://acme.localhost:5139/api/v1/auth/mfa/confirm-setup
   Headers: X-CSRF-Token: <onevo_csrf>
   Cookie:  onevo_session=<...>; onevo_csrf=<...>
   Body: { "code": "123456" }
   -> 200 { "success": true }

F. POST /api/v1/auth/logout

G. POST http://localhost:5139/api/v1/auth/login
   { "email": "<email>", "password": "<password>" }
   -> 202 Accepted, mfa_required = true, Set-Cookie: onevo_mfa=...

H. POST http://localhost:5139/api/v1/auth/mfa/verify
   Cookie: onevo_mfa=<from step G>
   Body: { "code": "123456" }
   -> 200 OK (legal already accepted) or 202 with legal_acceptance_required = true
```

## 6. `mfa/verify`'s Dead Fallback - Found, Investigated, and Removed

`VerifyMfaCommandHandler` originally contained:

```csharp
var mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: true, ct);
if (mfaRecord is null)
{
    // First-time verification (not yet marked verified)
    mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: false, ct);
    ...
}
...
if (!mfaRecord.IsVerified) mfaRecord.IsVerified = true;
```

Taken alone this looks like `mfa/verify` doubled as a setup-confirmation path, which would violate requirement 1 ("do not repurpose this endpoint for setup confirmation"). Traced one level up: `LoginContinuationService.ContinueAsync` (the **only** place that creates the `onevo_mfa` challenge that `mfa/verify` requires) checks exclusively `GetTotpAsync(user.Id, isVerified: true, ...)`. A user with only a pending/unverified setup gets no challenge at all and logs straight through. So the `isVerified: false` fallback was **unreachable in the running application** - no code path ever created an `onevo_mfa` cookie for a user without an already-verified TOTP record.

`LoginContinuationServiceTests.ContinueAsync_OnlyUnverifiedTotpSetupExists_DoesNotRequireMfa_SoLoginNeverCompletesFirstTimeSetup` pins that invariant at the point that actually matters (challenge creation): it arranges a real pending `UserMfa` record, asserts login proceeds without a challenge, and - the assertion that actually matters - that `GetTotpAsync(_, isVerified: false, _)` is never even called by the login path.

Given the dead branch was confirmed unreachable and had a latent bug on top (it mutated `mfaRecord.IsVerified = true` without ever calling `SaveChangesAsync` in that branch - if it had ever been reached, verification would have appeared to succeed for the caller while silently failing to persist), it was removed in a follow-up change:

```csharp
var mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: true, cancellationToken);
if (mfaRecord is null)
    return Result<LoginResponseDto>.Failure("MFA is not configured.", 400);

var secret = _encryption.Decrypt(mfaRecord.Secret);
```

The now-pointless `if (!mfaRecord.IsVerified) mfaRecord.IsVerified = true;` block was removed too, since `mfaRecord` is only ever fetched with `isVerified: true` now - it is always already verified at that point.

This is a genuine behavior change to `VerifyMfaCommandHandler`, but not a weakening: every request that could previously succeed through the reachable code path still succeeds identically (the fallback never fired for them); the only change in observable behavior is that the unreachable branch is now formally gone rather than latent. New test `VerifyMfaCommandHandlerTests.NoVerifiedMfaRecord_ReturnsMfaNotConfigured_WithoutFallingBackToUnverifiedSetup` was written first (TDD), confirmed to fail against the old code (it returned `"Invalid MFA code."` from the fallback path instead of `"MFA is not configured."`), then the fallback was deleted and the test went green. `mfa/verify` remains `[AllowAnonymous]` and still requires the `onevo_mfa` cookie - only the internal MFA-record lookup changed.

## 7. Tests Added

**Unit - `ConfirmMfaSetupCommandHandlerTests`**
- `ValidCode_MarksPendingRecordVerifiedAndSaves`
- `InvalidCode_ReturnsFailureAndLeavesRecordUnverified`
- `NoPendingSetup_ReturnsSafeFailureWithoutDecryptingOrVerifying`
- `AlwaysLooksUpPendingSetupForCurrentUserOnly`

**Unit - `EnableMfaCommandHandlerTests`** (new file; behavior pre-existing/untouched)
- `NoExistingSetup_CreatesUnverifiedTotpAndReturnsSecretAndQrCodeUri`
- `ExistingUnverifiedSetup_IsRemovedBeforeAddingNewOne`
- `VerifiedMfaAlreadyExists_ReturnsConflictWithoutCreatingNewSetup`

**Unit - `AuthMfaControllerTests`** (extended)
- `ConfirmMfaSetup_Success_ReturnsSuccessTrueWithoutRequiringMfaCookie`
- `ConfirmMfaSetup_HandlerFailure_ReturnsProblemWithHandlerStatusCode`
- (pre-existing) `VerifyMfa_MissingChallengeCookie_Returns401` - still proves `mfa/verify` requires the cookie

**Unit - `VerifyMfaCommandHandlerTests`** (extended)
- `NoVerifiedMfaRecord_ReturnsMfaNotConfigured_WithoutFallingBackToUnverifiedSetup` - pins the removal of the dead fallback (Section 6); written first, watched fail against the old code, then went green after the fallback was deleted

**Unit - `LoginContinuationServiceTests`** (extended)
- `ContinueAsync_OnlyUnverifiedTotpSetupExists_DoesNotRequireMfa_SoLoginNeverCompletesFirstTimeSetup` - arranges an actual pending `UserMfa` (`IsVerified = false`) returned from `GetTotpAsync(userId, false, ...)`, then asserts login proceeds (`RequiresMfa == false`), the record is left unverified, no `onevo_mfa` challenge is created, and - the invariant that actually matters - `GetTotpAsync(_, false, _)` is never called by the login path in the first place

**Architecture - `MfaConfirmSetupArchitectureTests`** (new file)
- `ConfirmMfaSetupRequest_ContainsOnlyCode`
- `NoMfaContract_AcceptsTenantIdOrUserId`
- `ConfirmMfaSetupRoute_RequiresTenantPolicy`
- `VerifyMfaRoute_RemainsAllowAnonymous`
- `EnableMfaRoute_StillRequiresTenantPolicy`
- `MfaSetupDto_NeverExposesAnEncryptedSecretField`
- `MfaFeatureCode_NeverReferencesSystemConfigOrServiceKeys`

**Architecture - `AuthControllerSplitArchitectureTests`** (extended)
- `AllOriginalAuthRoutesStillResolve` now also asserts `POST api/v1/auth/mfa/confirm-setup` resolves

## 8. Verification Results

All commands run from repo root (`C:\onevoNew\HRMS-Backend-v1`):

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore` | **Build succeeded**, 0 errors |
| `dotnet build tests\ONEVO.Tests.Unit\...csproj` | **Build succeeded**, 0 errors (pre-existing `NU1903` SQLitePCLRaw package-advisory warning, unrelated to this change) |
| `dotnet build tests\ONEVO.Tests.Architecture\...csproj` | **Build succeeded**, 0 errors |
| `dotnet build tests\ONEVO.Tests.Integration\...csproj` | **Build succeeded**, 0 errors (pre-existing `CS0618` Testcontainers obsolete-constructor warnings, unrelated to this change) |
| `dotnet test tests\ONEVO.Tests.Unit\...csproj` | **861/861 passed** (860 after the initial endpoint, +1 for the fallback-removal pinning test) |
| `dotnet test tests\ONEVO.Tests.Architecture\...csproj` | **228/228 passed** |
| `dotnet test tests\ONEVO.Tests.Integration\...csproj` | **4/80 passed, 76 failed** - all 76 failures are `Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'` (Testcontainers has no Docker daemon in this environment). None of the failures are MFA-related; this is a pre-existing environment limitation unrelated to this change. **No MFA integration coverage was executed** because the integration suite could not spin up its Postgres container. |
| `git diff --check` | No conflict markers/whitespace errors; only benign LF->CRLF line-ending warnings for the files this change touched |

Focused filters (all green):
- `Mfa` (unit): 41/41
- `Mfa` (architecture): 24/24
- `AuthMfaController|VerifyMfa|ConfirmMfaSetup|EnableMfa` (unit): 16/16

The Postman flow in Section 5 documents the manual verification **procedure** - it was not run against a live server in this session (no running instance / Postgres was available), so it should be exercised manually before sign-off.

## 9. Remaining Risks / Blockers

1. **No end-to-end/integration coverage executed.** Docker is unavailable in this environment, so the Testcontainers-backed integration suite could not run any MFA scenarios. The Postman flow in Section 5 should be run manually against a real environment before this is considered fully verified.
2. **Status code choice:** `confirm-setup` returns 400 for both "no pending setup" and "invalid code" (spec allowed 400 or 401); this differs from `mfa/verify`'s 401 for the same "invalid code" concept. Intentional - setup confirmation is a plain authenticated mutation, not a login-security boundary, so 400 (bad request) was chosen over 401. Flagging in case product wants parity with login's 401.

The dead `isVerified: false` fallback previously flagged as risk #2 in the original version of this report has since been removed from `VerifyMfaCommandHandler` (Section 6) - no longer an open item.
