# 9. P0-P3 Issue Register

## Table of Contents

1. [Register Summary](#1-register-summary)
2. [P0 Issues](#2-p0-issues)
3. [P1 Issues](#3-p1-issues)
4. [P2 Issues](#4-p2-issues)
5. [P3 Issues](#5-p3-issues)
6. [Informational and Verified-Safe](#6-informational-and-verified-safe)

## 1. Register Summary

| Severity | Count | IDs |
|---|---:|---|
| P0 | 0 | |
| P1 | 4 | UAT-001, UAT-002, UAT-003, UAT-004 |
| P2 | 10 | UAT-006 to UAT-013, UAT-020, UAT-021 |
| P3 | 7 | UAT-014 to UAT-019, UAT-022 |

Column meanings: **Status** shows whether the issue is Verified or Suspected. **AI** shows whether a coding agent can fix it: Yes, Partly (needs a human decision or access), or No.

## 2. P0 Issues

None found. Report 5 lists the security and tenant-isolation checks behind this result.

## 3. P1 Issues

### UAT-001 Planner redesign endpoints missing on backend `development`

| Field | Value |
|---|---|
| Status | Verified (static and runtime 404) |
| Component | Cross-app: FE calendar/planner, BE `PlannerController` |
| Evidence | FE `src/app/modules/work/planner/data-access/planner-api.service.ts:27,33` (commit `ec76d8e8`) calls `GET /planner/me/range` and `/planner/me/focus`. BE `development` `PlannerController.cs:20-36` has only `me`, `employees/{id}` and `me/blocks`. The endpoints exist only on `feature/planner-redesign-backend`. |
| Impact | With `Planner:Enabled=true`, planner range views and the focus panel fail with 404. With the planner off (the current `development` default), the feature is hidden. |
| Remediation | Merge `feature/planner-redesign-backend` (plus `chore/planner-on-in-development` if the planner is in UAT scope) into `development`. Alternatively, declare the planner out of UAT scope and keep `Planner__Enabled=false`. |
| Verification | `GET /api/v1/planner/me/range?from=..&to=..` returns 200. Open `/planner` in month, week and agenda views with no 404 in the network log. |
| AI | Yes. The merge is already prepared and tested on `claude/nice-heisenberg-lcattd`. The scope decision is a human call. |

### UAT-002 Frontend has no CI; quality gates already regressed

| Field | Value |
|---|---|
| Status | Verified |
| Component | FE repository, release process |
| Evidence | No `.github/workflows` folder. `npm run lint` fails with 5 errors on `Development` (UAT-013), two days after the backlog was reported cleared. |
| Impact | Broken lint, warnings or tests can reach the UAT build unnoticed. There is no record that the UAT build passed its gates. |
| Remediation | Add a workflow on push and PR to `Development`/`main`: `npm ci`, `npm run lint`, `npm run check:warnings`, `npx ng test --watch=false`. Make it a required check. |
| Verification | A PR shows a green required "CI" check. A deliberate lint error makes it red. |
| AI | Yes (workflow file). Branch protection needs a repository admin. |

### UAT-003 UAT environment configuration undefined and risky defaults

| Field | Value |
|---|---|
| Status | Verified (code); deployment not observed |
| Component | BE configuration and deployment |
| Evidence | Only `appsettings.json` and `appsettings.Development.json` exist. `appsettings.json:3` `AllowedOrigins` uses `onevo.com`; `:41` `Tenancy:RootDomain` is empty. CORS needs one of them to match (`CorsExtensions.cs:28-39`). Demo seeders guarded by `IsDevelopment()` create accounts with a shared, source-visible password, some with real people's email addresses (`DevSmokeTestTenantSeeder.cs:83-89`). Swagger is on in Development. No deployment runbook (`docs/deployment/` holds only `tray-device-jwt.md`). |
| Impact | A UAT host either rejects every tenant origin (no root domain) or, if run as `Development` to "make it work", exposes known-password accounts and Swagger. |
| Remediation | Write a UAT environment runbook listing the required variables: `ASPNETCORE_ENVIRONMENT=Staging` (or `UAT`), `Tenancy__RootDomain`, `AllowedOrigins`, `ONEVO_DB_*`, `Encryption__MasterKey`, `Jwt__*`, `TrayApp__Jwt__Secret`, `Planner__Enabled`, `Swagger__Enabled=false`, email and storage keys. Optionally add an `appsettings.Staging.json` without secrets. |
| Verification | The UAT API starts with `Environment: Staging` in the log and no `Dapi*`/`DevSmokeTest` seeder lines. `/swagger` returns 404. CORS preflight from `https://{tenant}.{uat-domain}` succeeds. |
| AI | Partly. An agent can draft the runbook and `appsettings.Staging.json`. Real values and hosting need a human. |

### UAT-004 Integration test suite not verified on current HEAD

| Field | Value |
|---|---|
| Status | Verified gap |
| Component | BE tests and CI |
| Evidence | `DockerUnavailableException` in the audit run. The last reported pass (560/0/1) was on 2026-10-27, before PR #202 and later merges. |
| Impact | RLS, repository and real-PostgreSQL behaviour is unproven for the UAT build. |
| Remediation | Run the full integration suite (CI push run, or `pwsh ../dev-env/scripts/verify.ps1 backend -Full`) on the exact UAT commit. |
| Verification | A CI link with 0 failures attached to report 12. |
| AI | Yes, in an environment with Docker. |

## 4. P2 Issues

| ID | Title | Status | Component | Evidence | Impact | Remediation | Verification | AI |
|---|---|---|---|---|---|---|---|---|
| UAT-006 | `positions/coverage/by-target` endpoint missing | Verified | FE org, BE OrgStructure | FE `position-api.service.ts:181`, `position.store.ts:330`; no BE route | The coverage modal silently shows no existing coverage for the target, so duplicate or overlapping coverage is easier to create | Build `GET /org/legal-entities/{id}/positions/coverage/by-target`, or remove the call and UI | The modal lists the current owners of the target; the network log shows 200 | Yes |
| UAT-007 | No authorization-matrix tests for 314 handler-authorized endpoints | Verified gap | BE tests | Report 5, section 4; hardening report "Open items" | A future handler without a check would ship unnoticed | Add per-module negative tests (other tenant, other member, missing permission) | New tests fail when a handler's check is removed | Yes |
| UAT-008 | `/health/ready` has no dependency checks | Verified (reused) | BE ops | Hardening report | A load balancer may route to an API with no database | Add PostgreSQL (and optionally R2) health checks | Stop the database: `/health/ready` returns 503 | Yes |
| UAT-009 | Uploads not malware-scanned; `FileRecord.Status` never gated | Verified (reused, SEC-13) | BE Storage | `pre-uat-audit-wm-sec-findings.md` SEC-13 | Malicious files can be shared between users | Accept the risk for UAT in writing, or apply the planned magic-byte check and status gating | Upload a file with a wrong extension: it is rejected | Partly |
| UAT-010 | 14 recurring jobs without a lease | Suspected | BE jobs | Report 2, section 3; only the meeting jobs and the outbox use `JobLeaseService`/claims | With more than one API instance, alerts, reminders and syncs can run twice (duplicate notifications). Leave credit is protected by `xmin`. | Run UAT with one instance, or add `JobLeaseService` to each job | Two instances on one database produce a single run per job per period | Yes |
| UAT-011 | Checklist template rich text not validated on write | Verified (reused) | BE CoreHr | Hardening report | Stored active HTML. The frontend sanitizes, so the risk is limited to other clients. | Call `RichTextRules.HasActiveContent` in the template validators | A unit test with `<img onerror>` is rejected | Yes |
| UAT-012 | CI workflow hardening | Verified (reused) | BE CI | zizmor: 8 high, 7 medium | Supply-chain risk | Pin actions by SHA, add `permissions: contents: read`, `persist-credentials: false` | zizmor is clean | Yes |
| UAT-013 | 5 ESLint errors on frontend `Development` | Verified | FE calendar, meetings | `calendar-page.component.html:108-109` (4), `meeting-form-modal.component.ts:18` (1) | Breaks the documented lint gate; the 4 accessibility errors mean keyboard users cannot use those elements | Use `role="button" tabindex="0" appKeyboardClick` or a `<button>`; remove the unused import | `npm run lint` reports 0 errors | Yes |
| UAT-020 | No upgrade rehearsal on a copy of the existing database | Verified gap | DB | Only an empty database was migrated | Data-dependent migrations (for example `RemoveLeaveRequestInfoAndForward`) may fail or run slowly on real data | Restore a UAT/shared backup to a scratch DB and run the 4-step deploy order | All migrations apply; timings recorded | Partly (needs a backup) |
| UAT-021 | Tray App end-to-end not verified | Untested | Cross-app | Out of scope here | Activation, check-in, screenshots or policy may break in UAT | Run report 11, section 5 with a real tray build | Checklist passes | No (needs a device) |

## 5. P3 Issues

| ID | Title | Status | Evidence | Remediation | AI |
|---|---|---|---|---|---|
| UAT-014 | Flaky `landing-header.component.spec.ts` | Verified | 6 failures with `requestAnimationFrame is not defined` under full-suite load; 17/17 pass alone | Stub `requestAnimationFrame` or flush it in the spec | Yes |
| UAT-015 | Dashboard mixes browser time and tenant time | Suspected | UTC browser, Asia/Colombo tenant: "Started at 3:30 AM" next to a timeline starting at 9:00 AM | Format all dashboard times in tenant (or employee) time; add a test with a fixed clock and time zone | Yes |
| UAT-016 | Schema migration hard-codes one tenant's data fix | Verified | `20261021140000_RemoveTharmiFutureFaceAttempts.cs` | Keep one-off data fixes in `ops/` scripts in future; no change needed now | Yes |
| UAT-017 | Dead `setTrayAppAccess()` call | Verified | FE `people-api.service.ts:105` | Remove it, or build the planned endpoint | Yes |
| UAT-018 | Nullable warnings in Release build | Verified | `MeetingEditService.cs:114`, `OnboardingDraftWriteService.cs:505,611,639`, `TaskCreateApplier.cs:26,32` | Add null guards and required members | Yes |
| UAT-019 | CORS allows any scheme for tenant subdomains | Verified | `CorsExtensions.cs:28-39` | Also require `originUri.Scheme == "https"` outside Development | Yes |
| UAT-022 | Test-only package CVEs, analyzer and Prettier backlog | Verified (reused) | Hardening reports | Handle in a coordinated clean-up | Yes |

## 6. Informational and Verified-Safe

- Demo and smoke seeders run only in Development and Test (each has an `IsDevelopment()` guard).
- WM-02 sprint concurrency is fixed (`SprintConfiguration.cs:14`).
- WM-01 (dead approval fields) and WM-04 (approval aggregator scope) are product decisions, recorded in the pre-UAT plan.
- The planner availability probe repeats on each full page load (about 4 calls). This is by design.
- Map tiles need `*.tile.openstreetmap.org` to be reachable from UAT browsers.
