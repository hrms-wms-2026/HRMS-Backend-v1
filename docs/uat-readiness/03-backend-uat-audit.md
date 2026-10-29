# 3. Backend UAT Audit

## Table of Contents

1. [Scope and Method](#1-scope-and-method)
2. [Build and Test Results](#2-build-and-test-results)
3. [Area Findings](#3-area-findings)
   3.1 [Authentication and Sessions](#31-authentication-and-sessions)
   3.2 [Authorization](#32-authorization)
   3.3 [Tenant Isolation and Employee Hierarchy](#33-tenant-isolation-and-employee-hierarchy)
   3.4 [Attendance, Leave and Approvals](#34-attendance-leave-and-approvals)
   3.5 [Work Management and Meeting Visibility](#35-work-management-and-meeting-visibility)
   3.6 [Dashboard Summaries](#36-dashboard-summaries)
   3.7 [Background Jobs](#37-background-jobs)
   3.8 [Monitoring and Tray Endpoints](#38-monitoring-and-tray-endpoints)
   3.9 [Deployment Readiness](#39-deployment-readiness)
4. [Backend Findings Summary](#4-backend-findings-summary)

## 1. Scope and Method

- Static review using targeted searches and file reads. Two scripts were written for this audit: an action-by-action authorization scan of all 738 controller actions, and a scan of the handler behind each action that has no permission attribute.
- Test runs: Release build, unit tests and architecture tests on `development` @ `8ce94e88`.
- Runtime: the API ran locally against a fresh PostgreSQL 16 database with all 268 migrations applied. It was driven through the real frontend in Chromium as a tenant owner and as a plain employee.

## 2. Build and Test Results

| Check | Result | Evidence |
|---|---|---|
| `dotnet build src/ONEVO.Api -c Release` | **Pass.** 0 errors, 6 nullable warnings | `MeetingEditService.cs:114` (CS8602), `OnboardingDraftWriteService.cs:505,611,639` (CS8629), `TaskCreateApplier.cs:26,32` (CS8618/CS8601) |
| Architecture tests | **755/755 pass** | `ONEVO.Tests.Architecture` (includes the RLS coverage tests) |
| Unit tests | **7,312/7,312 pass** | `ONEVO.Tests.Unit` |
| Integration tests | **Not run.** `DockerUnavailableException`; there is no Docker daemon in the audit container. | Last known result: 560 pass, 0 fail, 1 skipped (2026-10-27, before PR #202 and other merges). See UAT-004. |
| Fresh-database migration | **Pass.** All 268 migrations applied to an empty database, followed by the post-migration grants. | Report 6 |
| API startup | **Pass** in `Development`. All seeders completed. | Local run log |

## 3. Area Findings

### 3.1 Authentication and Sessions

- **Verified:** login, the legal-consent interstitial and the session exchange work end to end. A first-time employee login returns 202 with `legal_acceptance_required` and goes to `/auth/legal-consent`. After consent, `/auth/session-exchange` returns 200.
- **Verified:** a session issued for tenant `dapi` gets 401 on `acme.onexso.com` (`/auth/me`, `/employees`).
- **Info:** the previous report already checked the 32 `[AllowAnonymous]` actions (login, invitation, password, legal, OAuth callback, tray activation, Stripe webhook). They are unchanged on the current HEAD (32 found).

### 3.2 Authorization

| Metric | Count |
|---|---:|
| Controller actions | 738 |
| With a permission or visibility attribute (`RequirePermission`, `RequireAnyPermission`, `RequirePlatformPermission`, `RequireVisibleEmployee`, `RequireAnyModule`) | 392 |
| `[AllowAnonymous]` | 32 |
| No attribute; authorization done in the handler | 314 (159 of them in Work Management) |
| Handlers with no recognisable check | 35. Nearly all are tray-device endpoints (identity from the device JWT) or "my/me" self-scoped endpoints. |

Handlers reviewed by hand. Each one enforces the right check:

| Endpoint | Check found |
|---|---|
| `GET /employees/{id}`, `/detail`, `/position-history` | `EmployeeVisibilityScope` unless the caller has `org:manage` (`GetEmployeeDetailQueryHandler.cs:83-89`) |
| `GET /employees/{id}/documents` | `_access.CanViewAsync` (`ListEmployeeDocumentsQueryHandler.cs:40`) |
| `POST /employees/{id}/resend-invitation`, `/revoke-invitation` | `_invitationAccess.CanManageAsync` (`ResendEmployeeInvitationCommandHandler.cs:77`, `RevokeEmployeeInvitationCommandHandler.cs:60`) |
| `GET /employees/{employeeId}/checklist-tasks` | `[RequireVisibleEmployee("employeeId")]` |
| Conversation edit, delete, attachment, remove participant | Participant context plus author or adder checks (`RemoveConversationParticipantCommand.cs:55-66`) |
| `POST /session/active-company` | The caller must have an employee record in the target legal entity (`SwitchActiveCompanyCommandHandler`) |
| Tray delivery ack | `delivery.RecipientUserId == _device.UserId` (`AckTrayDeliveryCommandHandler.cs:40`) |
| Inbox export | The caller must be a decider or HR (`ExportInboxItemQuery.cs:64-69`) |

Runtime probe as a plain employee (`roshan.perera@dapi.test`, role "Employee"):

| Request | Result |
|---|---|
| `GET /roles`, `/users`, `/org/legal-entities` | 403 with "Permission ... required" |
| `GET /employees/{ownerId}/detail`, `/documents`, `/overview/activity` | 403 |
| `GET /employees` | 200, only the caller's own record visible |

There is still a gap: no automated negative tests cover the 314 handler-authorized endpoints (UAT-007, carried over from the hardening report).

### 3.3 Tenant Isolation and Employee Hierarchy

- **Verified (runtime):** as the `dapi` owner, `GET /employees/{acmeEmployeeId}`, `/detail` and `/work/projects/{acmeProjectId}` all return 404, not 403. Other tenants' IDs are not confirmed to exist.
- **Verified (tests):** the architecture tests that enforce RLS coverage for every tenant-owned table pass (755/755).
- **Verified (code):** data-fix migrations that write under FORCE RLS switch to `app.tenant_context_mode = admin` explicitly (report 6).

### 3.4 Attendance, Leave and Approvals

- **Verified:** today's attendance, weekly progress and the leave screens load without API errors for the owner.
- **Info:** the WM-04 HR-sourced approval gap and the WM-01 dead fields are documented product decisions in the pre-UAT plan. They are not new.
- **Suspected (P2, UAT-010):** `LeaveCreditJob` has no job lease. If two API instances start crediting at the same time, the `xmin` concurrency token on `leave_entitlements` (`LeaveEntitlementConfiguration.cs:21-28`) makes the second write fail rather than double-credit. The failure path has no test.

### 3.5 Work Management and Meeting Visibility

- **Verified fixed:** WM-02 (sprint double start/complete). `Sprint.Status` is now a concurrency token (`SprintConfiguration.cs:14`).
- **Verified:** WM handlers behind `TasksController` all have a `Forbidden` path (hardening report). A spot check of the meeting edit and organiser flows found membership checks.
- **Verified (runtime):** the planner is off on `development` (`/planner/me` returns 503 `planner_disabled`, and the nav shows "Calendar"). The redesign endpoints `/planner/me/range` and `/me/focus` return 404 (UAT-001).

### 3.6 Dashboard Summaries

- **Verified (runtime):** `GET /work/my-weekly-progress` returns 200 for the owner and for the employee. The Weekly Work Progress card renders. Action Center, Active Projects and Today's Schedule load.
- **Suspected (P3, UAT-015):** with the browser in UTC and the tenant in Asia/Colombo, the hero card shows "Started at 3:30 AM" while the activity timeline starts at 9:00 AM. One widget formats in browser time, the other in tenant time.

### 3.7 Background Jobs

Of 16 recurring jobs, only the meeting jobs and the outbox use a lease (report 2, section 3). In a single-instance UAT this is harmless. In a multi-instance UAT, jobs may run twice (UAT-010).

### 3.8 Monitoring and Tray Endpoints

- Tray endpoints resolve identity from the device JWT (`_device.TenantId`, `_device.UserId`) instead of the request body. Reviewed: tray delivery ack, tray notification ack.
- **Untested:** an end-to-end run with a real Tray App build (activation, screenshots, check-in face match). Covered in the manual checklist (report 11).

### 3.9 Deployment Readiness

- Only `appsettings.json` and `appsettings.Development.json` exist. The repository has no staging or UAT settings file, no container image definition and no deployment runbook (`docs/deployment/` holds only `tray-device-jwt.md`).
- `appsettings.json:3` lists origins on `onevo.com`, while the product domain is `onexso.com`. `Tenancy:RootDomain` is empty (`appsettings.json:41`). CORS falls back to exact `AllowedOrigins` matches when the root domain is empty (`CorsExtensions.cs:28-39`), so a UAT host without `Tenancy__RootDomain` set rejects every tenant origin.
- If UAT runs with `ASPNETCORE_ENVIRONMENT=Development`, the demo seeders create known-password accounts (`Password123!`) for real people's email addresses and turn on Swagger. Recorded as UAT-003.
- `/health/ready` checks only the API itself, not the database or storage (UAT-008, carried over).

## 4. Backend Findings Summary

| ID | Severity | Title |
|---|---|---|
| UAT-001 | P1 | Planner redesign endpoints missing on backend `development` |
| UAT-003 | P1 | UAT environment configuration undefined and risky defaults |
| UAT-004 | P1 | Integration suite not verified on current HEAD |
| UAT-006 | P2 | `positions/coverage/by-target` endpoint missing |
| UAT-007 | P2 | No authorization-matrix integration tests for handler-authorized endpoints |
| UAT-008 | P2 | `/health/ready` has no dependency checks |
| UAT-009 | P2 | Uploads not malware-scanned; file status never gated |
| UAT-010 | P2 | Recurring jobs without lease in multi-instance deployment |
| UAT-011 | P2 | Checklist template rich text not validated on write |
| UAT-012 | P2 | CI workflow hardening (pinning, permissions) |
| UAT-016 | P3 | Data-fix migration hardcodes one tenant's IDs |
| UAT-018 | P3 | Nullable warnings in Release build |
| UAT-019 | P3 | CORS origin check ignores scheme |

Full details are in report 9.
