# Tenant Google login: sub identity, tenant policy, split errors (DEC-062 / GL-01)

Branches: backend `feat/web-google-login` (`_wt-google-be`), frontend `feat/web-google-login` (`_wt-google-fe`).
Both merged with the latest `development` on 2026-11-18 before any change.

## Current state (verified)

- `BaseGoogleLoginCommandHandler` looks users up by verified email only, through
  `auth_internal.auth_lookup_base_login_candidates`. `payload.Subject` is never read.
- `tenant_auth_policies` and `user_external_identities` are tenant-owned with `RlsPredicate.Tenant`,
  so neither can be read before a tenant is chosen.
- Every failure returns 401 "Invalid email or password." The controller's `Problem(...)` drops `ErrorCode`.
- `LoginContinuationService.ContinueAsync` returns 401 for an inactive tenant or user.

## Design

### Pre-tenant lookup (R1.1, R1.2, R1.4, R2)
New allowlisted procedure `auth_internal.auth_lookup_google_login_candidates @provider_subject, @normalized_email`,
`EXECUTE AS` the existing lookup user (`crownus_auth_base_login_fn_owner`). In one query it:
1. returns users linked to the `sub` (`match_kind = 'sub'`), active users in Active/Trial tenants only;
2. if there are none, returns verified-email users (`match_kind = 'email'`), **excluding** users who already
   have a `google` identity with a different subject (no email takeover);
3. LEFT JOINs `tenant_auth_policies` and drops rows where `google_completion_allowed = 0` (in SQL, so
   `TOP (9)` counts after filtering); returns `allowed_login_domains_json` for the C# domain check.

RLS: switch `user_external_identities` and `tenant_auth_policies` to `RlsPredicate.Users` (filter also lets
the lookup user read; block predicates stay `fn_tenant`). The migration drops and re-creates both policies.
Column-level grants for the lookup user and `GRANT EXECUTE` for `crownus_app` go in
`ops/sqlserver/post-migration-grants.sql`. Architecture tests are updated to cover the new procedure.

Domain check in C#: exact match of the email's domain against the list, the same rule
`AcceptInvitationGoogleCommandHandler` uses. Empty or missing list means any domain. No policy row means defaults.

### Linking (R1.3, R1.5, R1.6)
- New record `PendingExternalIdentity(Provider, Subject, Email, EmailVerified, MatchedBySubject)`.
- Optional on `LoginContinuationRequest` and `FinishAuthenticatedLoginAsync`.
- Nullable columns `pending_provider`, `pending_provider_subject`, `pending_provider_email` on
  `mfa_challenges` and `login_workspace_selection_challenges`, so the pending link survives those two steps.
- The link (or the `LastUsedAt` update) is written inside `FinishAuthenticatedLoginAsync`, after the legal
  check passes and before finalization. `TenantSessionExchangeService.CreateAsync` saves, so it is committed.
  This one spot covers single candidate, workspace selection and MFA verify (and workspace then MFA).
- Before adding, re-check in tenant context: if the user already has a different Google link, skip linking.
  The unique index `(tenant_id, provider, provider_subject)` stops a concurrent double insert.
- **Not covered by design:** a login that stops at a legal challenge does not link. The user is linked on
  their next completed Google login. Legal and session-exchange challenges are not extended.

### Errors (R3, replaces the old R3)
- Google failure (token invalid/expired/malformed, email not verified) → 401 `google_auth_failed`,
  "Google sign-in failed. Please try again."
- No application access (no candidate, user/tenant inactive, policy blocks Google or the domain, more than
  8 candidates, continuation 401 for an inactive tenant/user) → 403 `no_access`,
  "This Google account does not have access to Crownus. Ask your administrator for an invitation."
- The controller adds `code` to the problem details, as `MonitoringActivationController` does.
- Password login messages are unchanged.

### Frontend
- Login page maps `google_auth_failed` and `no_access` to the two messages for Google sign-in only.
- Playwright: GIS is stubbed to return a fake credential; the backend is mocked to return 403 `no_access`,
  and the page must show the no-access message. A real unlisted account cannot be automated (Google blocks
  automated sign-in), so that check stays manual (R0).

## Steps (TDD: each test fails first)
1. R3 errors in the handler and controller + unit tests.
2. Procedure, RLS change, grants, migration + architecture tests + integration test under RLS.
3. Handler uses the new lookup: sub first, email fallback, policy and domain filtering + unit tests.
4. Pending link carried through workspace selection and MFA; link written in `FinishAuthenticatedLoginAsync` + unit tests.
5. Frontend messages + unit tests + Playwright stub test.
6. R5 Google Console runbook in `ops/`.
7. `verify.ps1 backend` (and `-Full` if Docker is running), frontend tests and lint.

## Status (2026-11-18)

Steps 1-6 done, not committed. Unit (8049) and architecture (716) tests pass. The integration suite
was compiled but not run (Docker was down). Frontend: store specs and all 10 Google Playwright cases
pass (run against this worktree on port 4210, because 4200 was serving the main checkout).

## Open questions / known gaps

- **Sub-first is exclusive (spec R1.2).** A user linked to the Google subject in tenant A cannot reach an
  unlinked membership in tenant B through Google: email fallback runs only when no eligible user is
  linked. The alternative (subject matches plus unlinked email matches) needs a decision.
- **Invitation-only domains (R1.6).** Invitation acceptance with email mismatch allows the merge of the
  invitation's and the policy's domain lists. Login checks only the policy list, so a user accepted on
  an invitation-only domain gets 403 when the policy has a list that does not include it.
- **Legal stop.** A login that stops at a legal challenge does not link; the next completed Google login does.
- **Frontend bundle budget.** `origin/Development` builds at 498.94 kB; this branch was at 500.73 kB.
  Password login, Google sign-in and workspace selection moved from `AuthStore` into
  `core/auth/state/login-flows.ts`, loaded with `import()` on first use: initial total is now 499.95 kB
  and `npm run check:warnings` is clean. Only about 50 bytes of headroom remain under the 500 kB budget.

## Needs the user
- **R0:** test one real Google account that is not in Test Users, in Chrome with DevTools, and record the
  GIS console error and the `/api/v1/auth/login/google` response.
- **TC-01 to TC-07** from Dabi's final requirement, to check acceptance.
- Migrations are created, not applied. The developer applies them and runs the grants script.
