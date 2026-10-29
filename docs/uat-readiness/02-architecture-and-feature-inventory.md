# 2. Architecture and Feature Inventory

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
   1.1 [Backend Layers](#11-backend-layers)
   1.2 [Frontend Structure](#12-frontend-structure)
   1.3 [Cross-Cutting Mechanisms](#13-cross-cutting-mechanisms)
2. [Feature Inventory](#2-feature-inventory)
3. [Background Jobs](#3-background-jobs)
4. [Feature Flags and Environment-Gated Behaviour](#4-feature-flags-and-environment-gated-behaviour)
5. [Features Documented but Not Built](#5-features-documented-but-not-built)

## 1. Architecture Overview

### 1.1 Backend Layers

| Project | Role |
|---|---|
| `ONEVO.Api` | 134 controllers (Tenant 103, Admin 29, Public 1, Webhooks 1). Filters: `RequirePermission`, `RequireAnyPermission`, `RequireAnyModule`, `RequirePlatformPermission`, `RequireVisibleEmployee`, `Idempotent`. |
| `ONEVO.Application` | MediatR commands, queries and handlers. 15 feature areas: Auth, Calendar, Conversations, CoreHr, Dashboard, DevPlatform, Inbox, InfrastructureModule, Leave, Monitoring, OrgStructure, SharedPlatform, Storage, TimeAttendance, WorkManagement. |
| `ONEVO.Domain` | Entities and domain rules. |
| `ONEVO.Infrastructure` | EF Core persistence (268 migrations), repositories, external adapters (R2, Rekognition, SendGrid/Resend, Google/Microsoft calendar, Zoom, Teams, GitHub, Stripe), hosted jobs and seeders. |
| Tests | Unit (981 files, 7,312 tests), Architecture (86 files, 755 tests), Integration (148 files, Testcontainers). |

### 1.2 Frontend Structure

| Area | Modules under `src/app/modules` |
|---|---|
| Employee self-service | `dashboard`, `profile`, `attendance`, `leave`, `inbox`, `calendar` (planner) |
| Manager and HR | `people`, `organization`, `settings`, `work` |
| Public and device | `public` (landing, legal), `device-activation` (tray pairing) |
| Core | `core/guards` (`authGuard`, `permissionGuard`, `redirectIfAuthenticatedGuard`, `unsavedChangesGuard`), `core/auth`, `core/config` |

### 1.3 Cross-Cutting Mechanisms

| Mechanism | How it works |
|---|---|
| Tenant resolution | Tenant from the request host (`{slug}.{RootDomain}`) via `HostTenantResolutionMiddleware`. The frontend builds the API URL from `window.location` (`environment-url.helper.ts`). |
| Session | HttpOnly `onevo_session` cookie. CSRF protection uses the `onevo_csrf` cookie and the `X-CSRF-Token` header. No tokens are kept in `localStorage`. |
| Tenant isolation | PostgreSQL Row-Level Security (RLS) on tenant-owned tables, plus EF query filters. The architecture tests fail when a tenant-owned table has no RLS coverage. |
| Authorization | Permission attributes on 229 actions. Handler-level checks (visibility scope, membership, ownership) on the rest. See report 5. |
| Tray App | Separate device JWT (`TrayApp__Jwt__Secret`), with endpoints under `Monitoring/*` and `SharedPlatform/TrayNotificationDeliveries`. |

## 2. Feature Inventory

| Area | Backend controllers | Frontend module | UAT relevance |
|---|---:|---|---|
| Authentication, MFA, sessions, legal consent | Auth 8, Legal 2 | `core/auth`, `public` | Critical |
| Core HR (employees, onboarding, offboarding, documents, checklists) | CoreHr 13 | `people`, `profile` | Critical |
| Organisation structure (legal entities, departments, positions, coverage) | OrgStructure 4 | `organization` | Critical |
| Roles and permissions | Roles 1, Permissions 1 | `settings/roles`, `settings/users` | Critical |
| Attendance and time tracking (clock-in, breaks, corrections, work area) | Attendance 7 | `attendance` | Critical |
| Leave (types, policies, entitlements, requests, approvals, calendar) | Leave 11 | `leave` | Critical |
| Work management (projects, modules, tasks, sprints, approvals, meetings, planner) | WorkManagement 16 | `work`, `calendar` | High |
| Calendar and external sync | Calendar 1, Public/Calendar | `calendar` | High |
| Inbox and conversations | Inbox 2, Conversations 1 | `inbox` | High |
| Dashboard (layouts, widgets, team, weekly progress) | Dashboard 2 | `dashboard` | High |
| Monitoring and Tray (activity, screenshots, check-in, face match, policy) | Monitoring 28 | `device-activation` | High (Tray integration) |
| Notifications | SharedPlatform 3 | shell | Medium |
| Storage | Storage 1 | shared | Medium |
| Integrations (GitHub), Billing | 1 + 1 | `settings` | Low |

## 3. Background Jobs

Hosted services that run inside the API process:

| Group | Jobs | Multi-instance protection |
|---|---|---|
| Meetings | `MeetingOccurrenceGeneratorJob`, `MeetingAttendanceJob` | `JobLeaseService` (`job_leases` table) |
| Outbox | `OutboxProcessor` | Claim lease (`OutboxClaimStore`) |
| Attendance and monitoring | `AttendanceAlertMaterializerJob`, `ExceptionDetectionJob`, `WellnessRuleEvaluatorJob`, `LocationRuleEvaluatorJob`, `ActivityDailySummaryJob`, `AgentCommandExpiryJob` | None found (see UAT-010) |
| Calendar and meetings sync | `CalendarSyncJob`, `CalendarEventReminderJob`, `CalendarIncidentJob`, `TeamsAttendanceSyncJob`, `ZoomAttendanceSyncJob` | None found |
| Leave | `LeaveCreditJob` | None. The `xmin` concurrency token on `leave_entitlements` limits the damage. |
| Other | `ProjectMonitorJob`, `InboxEscalationSweepJob`, `NotificationTrayExpiryJob`, `BulkOnboardingBatchProcessor`, `LoginWorkspaceSelectionChallengeCleanupService` | None found |
| Seeders | Permission, RoleTemplate, Lookup, NotificationTemplate, PlatformAccess, PositionTemplatePack, ModuleCatalog, PlatformOAuthProviderMetadata | Idempotent upserts |
| Demo seeders | `DevSmokeTestTenantSeeder`, `Dapi*Seeder`, `WorkManagement*Seeder` | Run only when the environment is `Development` or `Test` (verified: `IsDevelopment()` guard in each one) |

## 4. Feature Flags and Environment-Gated Behaviour

| Setting | Default | Where it is turned on | UAT note |
|---|---|---|---|
| `Planner:Enabled` | off; every `/planner` endpoint answers 503 `planner_disabled` | Not set in `appsettings.Development.json` on `development`. It is set on branch `chore/planner-on-in-development`. | Decide whether the planner is in UAT scope (UAT-001). |
| `Swagger:Enabled` | on in `Development`, or when the flag is set | `SwaggerExtensions.cs:44` | Keep it off in UAT unless needed. |
| `Tenancy:RootDomain` | empty in `appsettings.json` | Must be set per environment | Required for CORS and tenant resolution (UAT-003). |
| `AllowedOrigins` | `http://localhost:5173`, `https://app.onevo.com` | `appsettings.json:3` | Domain is `onevo.com`, not `onexso.com` (UAT-003). |
| Demo seed data | Development and Test only | Seeder guards | UAT must **not** run as `Development` (UAT-003). |

## 5. Features Documented but Not Built

| Feature | Evidence | Status |
|---|---|---|
| Tray app access toggle per employee | FE `people-api.service.ts:105` calls `POST /employees/{id}/tray-app-access`; there is no backend endpoint. | Known (gap audit, 2026-10-28). No UI calls it. UAT-017. |
| Planner range and focus views | FE `planner-api.service.ts:27,33`; the backend endpoints exist only on `feature/planner-redesign-backend`. | UAT-001 |
| Position coverage by target | FE `position-api.service.ts:181`; no backend route. | UAT-006 |
| Malware scanning of uploads | `FileRecord.Status` is never gated (SEC-13). | UAT-009 |
