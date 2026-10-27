# UAT hardening: code cleaning and security testing (2026-10-27)

Branch `chore/uat-hardening` in both repos, cut from `development` / `Development` with `feature/team-attendance-widget` merged in. Frontend companion report: `docs/reports/code-quality/2026-10-27-uat-hardening-report.md` in the frontend repo.

Tooling used lives outside the repos in `../dev-env/` (see its `README.md`): gitleaks, Semgrep CE, DevSkim, `dotnet list package --vulnerable`, OSV-Scanner, Trivy, actionlint, zizmor, plus `find-unauthorized-endpoints.ps1`.

## Fixed

| Severity | Finding | Fix |
|---|---|---|
| High | **Stored XSS through task descriptions and comments.** The backend stored task descriptions and task comments as raw HTML (only chat messages rejected active content). The frontend editor loads that HTML with `innerHTML`, so `<img src=x onerror=...>` saved through the API ran in the session of anyone who opened the task in edit mode. | New `Common/Helpers/RichTextRules.HasActiveContent` (moved out of `ConversationRules`, same rules) is now checked in `TaskWriteService.ValidateCreateAsync` / `ValidateEditAsync` (covers create, edit and the approval-applied edit path) and in `CreateTaskComment` / `EditTaskComment`. Frontend sanitizes with DOMPurify before every editor `innerHTML` write. 18 new unit tests. |
| Medium | MediatR behaviors called `next()` without the request token (CA2016). | `next(ct)` in all 5 behaviors. |
| Medium | `CloudflareR2ObjectStorageAdapter` (scoped) created an `AmazonS3Client` per scope and never disposed it (CA1001). | Implements `IDisposable`. |
| Low | `StripeWebhookController` had no authorization attribute; it was safe only because the signature is checked in code. | Explicit `[AllowAnonymous]` with a comment on how it is authenticated. |
| Low | Possible null dereference in `AdminAuthController` (CS8602). | Reads the result value once. |

Architecture docs reconciled with the code: see the 2026-10-27 entry in `docs/superpowers/project_ core/SUMMARY.md`.

## Verified safe (no change needed)

- **Raw SQL:** no `FromSqlRaw` / `ExecuteSqlRaw` / `SqlQueryRaw` outside migrations.
- **Tenant filter bypass:** all 21 `IgnoreQueryFilters()` calls in repositories keep an explicit `TenantId` predicate and only drop the soft-delete filter; the cross-tenant bulk-onboarding worker is gated by RLS admin mode.
- **Anonymous endpoints:** after the Stripe change, 0 actions lack an authorization decision. The 30 `[AllowAnonymous]` actions are login, invitation, password, legal, OAuth-callback and tray-activation flows. Spot-checked: Stripe webhooks verify `Stripe-Signature`; tray release ingest compares `X-Release-Token` in constant time; calendar OAuth callback validates a data-protected, expiring `state`.
- **Handler-level authorization:** 301 of 541 tenant actions have no permission attribute because they authorize inside the handler (Work Management membership). All 43 handlers behind `TasksController` have a `Forbidden` path.
- **Secrets in git history:** 17 gitleaks hits, all false positives (tenant GUIDs in docs, a `sk_test_` placeholder, Postman workspace IDs). Recorded in `../dev-env/baselines/`.
- **Weak crypto warnings:** MD5/SHA-1 only derive deterministic GUIDs; HMAC-SHA1 is required by RFC 6238 TOTP.

## Open items (not fixed in this pass)

| Priority | Item | Why not now |
|---|---|---|
| High | **CI workflow hardening** (`.github/workflows/ci.yml`): actions pinned by tag not SHA, no `permissions:` block, `persist-credentials` default (zizmor: 8 high, 7 medium). | Shared CI; needs the team's agreement. |
| Medium | `/health/ready` only has an `"api"` self-check; no PostgreSQL/R2 dependency checks. | Needs a decision on which dependencies gate readiness in each environment. |
| Medium | Checklist template task descriptions are not checked with `RichTextRules` on write. The frontend sanitizes on load, so the editor path is covered. | The checklist JSON parser is also used on read; validation belongs in the template create/update validators. |
| Medium | Authorization-matrix integration tests (cross-tenant and cross-member negative tests for the 301 handler-authorized endpoints). | Larger test-writing task; best done per module. |
| Low | Test-only packages with CVEs: `SSH.NET` 2025.1.0 (via Testcontainers, integration tests) and `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (unit tests). Not shipped. | Upgrade with the next test-dependency bump. |
| Low | 1045 analyzer warnings under `AnalysisMode=Recommended`, mostly style (CA1725 parameter names 522, CA1861 constant arrays 133, CA1848 logging 103). | Mass-fixing would conflict with every open branch. |
| Info | Semgrep cannot parse about 165 C# files, so its C# coverage is partial; DevSkim and review cover the gap. | Tool limitation. |
| Info | No DAST (OWASP ZAP) run yet. | Needs a running staging/local stack with test accounts. |

## Commits

- `3cec87df` chore: fix correctness-class analyzer warnings
- `e084ced8` fix(security): reject active HTML in task descriptions and comments

## Verification

- Release build of `ONEVO.Api`: clean.
- Unit tests: 6809/6809 (includes 18 new `RichTextRules` tests).
- Architecture tests: 736/736.
- Integration tests: NOT RUN in this pass (needs Docker; run `pwsh ../dev-env/scripts/verify.ps1 backend -Full`).
