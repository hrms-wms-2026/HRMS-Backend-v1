# Feature-Key Phase 1 — Inventory

**Date:** 2026-11-16
**Source:** `origin/development` at `21f7d18e` (read-only), tenant frontend `origin/development`
**Spec:** `docs/superpowers/specs/next/2026-11-16-feature-key-management-design.md` (DEC-038)
**Plan:** `docs/superpowers/plans/next/2026-11-16-feature-key-phase1-plan.md`

Nothing in this report was run against a database. The SQL in §5 is for an operator to run read-only per environment.

## 1. Headline numbers

| Item | Count |
|---|---|
| `module_catalog` rows (reference-data migration) | 27 (14 used by `ModuleCatalogSeeder`, 13 legacy) |
| Module identities that must merge into another key | 13 legacy keys → 6 canonical modules (§2.2) |
| Seeded `ModuleFeature` keys | 64, all `IsActive=true`, all `IsDefaultIncluded=true` |
| Features whose owner module differs from the key prefix | 1 (`org.structure_management` → `org_structure`) |
| Features with at least one implementation surface | 50 |
| Features with **no implementation** found (gaps) | 14 (§3.3) |
| Customer-facing capabilities with **no feature key** | 2 product areas: Meetings, Zoom (§3.4) |
| Duplicate keys for one surface | 1 pair (`integrations.github` / `work_management.github_integration`) |
| `RequireAnyModule` usages | 20, all with the six legacy WorkSync keys |
| Callers of `IModuleEntitlementService` | 21 call sites in 18 files (§4) |
| Trial end sets `access_ends_at`? | **No** (§4.3) |
| `FeatureAccessGrant` consumers | 0 (entity, configuration, repository and DI registration only) |

## 2. Module identities

### 2.1 Where each key is used

Legend: **Cat** catalog row · **Feat** owns `module_features` rows · **Perm** `permissions.module` (`PermissionSeeder`) · **Own** `module_permission_ownership` (`ModuleCatalogSeeder`) · **Auto** `ModuleAutoGrants` · **Base** `PlatformBaselineModules` · **RAM** `RequireAnyModule` and the Application constant arrays · **Plan** default plan `included_modules_json` (`SubscriptionPlanConfiguration` HasData, then `SeedReferenceData` UPDATE) · **FE** tenant frontend `requiredModules`/`hasModule`.

| Key | Cat | Feat | Perm | Own | Auto | Base | RAM | Plan | FE |
|---|---|---|---|---|---|---|---|---|---|
| `auth` | ✓ | 2 | 2 | ✓ | | ✓ | | | |
| `configuration` | ✓ | 1 | 5 | ✓ | | ✓ | | | |
| `roles` | ✓ | 1 | 2 | ✓ | | ✓ | | | |
| `notifications` | ✓ | 2 | 1 | ✓ | | ✓ | | | |
| `org` | ✓ | — | | | | | | | |
| `org_structure` | ✓ | 1 (`org.*`) | 6 | ✓ | | | | ✓ | 4 |
| `core_hr` | ✓ | 7 | 8 (incl. `attendance:*`) | ✓ | ✓ | | | ✓ | 6 |
| `time_off` | ✓ | 6 | 4 | ✓ | | | | | |
| `leave` | **no row** | | 5 | | ✓ | | | ✓ | 5 |
| `calendar` | ✓ | 4 | 3 | ✓ | ✓ | | | ✓ | 5 |
| `time_attendance` | ✓ | 1 | 0 | ✓ (`attendance:*`) | | | | ✓ | 9 |
| `attendance` / `employees` / `workforce` | no row | | `workforce` 2 | | ✓ | | | | |
| `monitoring` | ✓ | 13 | 0 | ✓ | | | | | |
| `activity_monitoring` | ✓ | | 4 | | ✓ | | `TrayPresenceRequirementEvaluator` | ✓ | 2 |
| `verification` | ✓ | 5 | 3 | ✓ | | | | | |
| `identity_verification` | ✓ | | | | | | | ✓ | |
| `analytics` | ✓ | 6 | 4 | ✓ | | | | | |
| `productivity_analytics` | ✓ | | | | | | | ✓ | |
| `desktop_agent_gateway` / `discrepancy_engine` / `exception_engine` | ✓ | | (`exceptions` 3) | | | | | ✓ | |
| `work_management` | ✓ | 10 | | | | | | | |
| `worksync_foundation`, `projects`, `objectives_milestones`, `tasks`, `boards`, `planning_sprints` | ✓ | | `projects` 1 | `projects` 1 | | | 20 + 6 arrays | ✓ | 2 each |
| `integrations` | ✓ | 5 | | ✓ | | | | | |

Other persisted module-key holders: `tenant_subscriptions.selected_modules` (copied from the plan by `CreateTenantCommandHandler` and `DevSmokeTestTenantSeeder`), `subscription_plans.included_modules_json` (writers: `CreateSubscriptionPlanCommandHandler`, `UpdateSubscriptionPlanCommandHandler`), `role_templates.module_keys_json`, `configuration_templates.module_keys_json`, `module_integration_links.module_key`, `module_catalog_price_history.module_key`, `feature_access_grants.module`. The platform-admin frontend uses module keys only in specs.

### 2.2 Proposed canonical keys (question Q1–Q4 for Dabi)

Feature keys are immutable and must carry their module as prefix (spec §6). The 64 feature keys use `org`, `time_off`, `monitoring`, `verification`, `analytics`, `work_management`. The recommendation therefore keeps those as canonical and migrates every legacy reference to them.

| Legacy key(s) | Proposed canonical | Note |
|---|---|---|
| `org_structure` | `org` | Re-own `org.structure_management`; move `org:*`/`legal_entity:*` permissions |
| `leave` | `time_off` | `leave` has no catalog row; `leave:*` permissions move module, codes unchanged |
| `activity_monitoring` | `monitoring` | Fixes today's split: permissions say `activity_monitoring`, ownership says `monitoring` |
| `identity_verification` | `verification` | Fixes a live bug: `verification:*` permissions are never effective for default-plan tenants |
| `worksync_foundation`, `projects`, `objectives_milestones`, `tasks`, `boards`, `planning_sprints` | `work_management` | Every gate already treats the six as "any of" one module |
| `productivity_analytics` | `analytics` | Needs confirmation (Q4) |
| `desktop_agent_gateway`, `discrepancy_engine`, `exception_engine` | `monitoring` (recommended) or keep as feature-less modules | Q4 |
| `attendance` (auto-grant), `employees` (auto-grant) | `time_attendance`, `core_hr` | Auto-grant keys only |
| `attendance:*` permissions `module=core_hr` | `time_attendance` | Ownership already says `time_attendance`; resolver reads `permissions.module` |

Permission modules with no catalog row (`chat`, `documents`, `exceptions`, `expense`, `grievance`, `payroll`, `performance`, `reports`, `skills`, `workforce`, `system`) are outside Phase 1 feature keys. They are listed as a finding only; their permissions are effective today only through baseline/owner paths.

## 3. Feature keys

### 3.1 Customer-facing classification

All 64 keys are `IsDefaultIncluded=true` and `IsActive=true`; none is `IsDefaultIncluded=false`, so spec §11 step 8 has no rows to protect today. All 64 are treated as customer-facing. No metadata-only/internal exclusion is proposed by default; Q6 asks about three baseline keys.

### 3.2 Gate map (surfaces each key must gate)

`C` = controller (route class), `J` = background job, `O` = outbox handler, `W` = dashboard widget provider/team source, `H` = in-handler gate (tenant resolved inside the handler: tray-token, ingest or login flows).

| Feature key | Surfaces |
|---|---|
| `auth.optional_google_oauth` | H: `BaseGoogleLoginCommandHandler` → gate at workspace selection (`SelectWorkspaceCommandHandler`, `LoginContinuationService`) because base login has no tenant |
| `auth.mfa_enforcement` | C: `AuthMfa` (tenant policy endpoints only); H: `VerifyMfaCommandHandler` — see Q6 |
| `configuration.tenant_settings` | C: tenant settings endpoints (Q6) |
| `roles.permission_management` | C: `Roles`, `Permissions` (Q6) |
| `notifications.email_delivery` | O: `NotificationDeliveryOutboxHandler` (tenant notification e-mail only; security mails excluded — Q6) |
| `notifications.in_app_delivery` | C: `SharedPlatform/Notifications`, `NotificationSettings`, `TrayNotificationDeliveries`; O: `NotificationTrayPushOutboxHandler`, `InboxChangedOutboxHandler`; J: `NotificationTrayExpiryJob` |
| `org.structure_management` | C: `Departments`, `LegalEntities`, `Positions`, `PositionTemplatePacks`; O: `PositionChangeApprovalRequestEmailOutboxHandler` |
| `core_hr.employee_profiles` | C: `Employees` (profile reads/writes); W: employee overview queries |
| `core_hr.employee_lifecycle` | C: `Employees` status endpoints, `WorkModes`, `AccessGrantRequests` |
| `core_hr.onboarding` | C: `OnboardingDrafts`, `BulkOnboarding`, `OnboardingChecklistTemplates`, `OnboardingEmployeeNumber`, `ChecklistTemplates`, `EmployeeChecklistTasks`, `PeopleChecklistAssignees`; J: `BulkOnboardingBatchProcessor`; O: `EmployeeOnboardingInviteEmailOutboxHandler` |
| `core_hr.offboarding` | C: `EmployeeOffboarding`, `OffboardingBypassRequests`, `OffboardingOverview` |
| `core_hr.dependents_contacts` | C: `Employees` emergency-contact/dependent endpoints |
| `time_off.requests` | C: `LeaveRequests`; O: `LeaveApprovalEmailOutboxHandler` |
| `time_off.approvals` | C: `LeaveApprovals`, `LeaveApprovalDelegates` |
| `time_off.balances` | C: `LeaveBalances`, `LeaveBalanceAudit`, `LeaveEntitlements` |
| `time_off.accrual_rules` | C: `LeavePolicies`, `EmployeeLeavePolicies`; J: `LeaveCreditJob` |
| `time_off.types` | C: `LeaveTypes` |
| `time_off.calendar_integration` | C: `LeaveCalendar` |
| `calendar.company_calendar` | C: `Calendar/Calendar`, `WorkManagement/Calendar`; J: `CalendarEventReminderJob`, `CalendarIncidentJob`; O: `CalendarEventInviteEmailOutboxHandler`, `CalendarIncidentEvaluateOutboxHandler`, `CalendarMeetingCancelOutboxHandler` |
| `calendar.holidays` | C: `LeaveHolidays` |
| `calendar.time_off_visibility` | H: leave overlay inside calendar queries (`CheckCalendarConflicts`) |
| `calendar.event_sync` | J: `CalendarSyncJob`; O: `CalendarOutlookPushOutboxHandler`; C: `Public/Calendar` connection callbacks |
| `time_attendance.work_schedules` | C: `ClockInPolicies`, `LegalEntityClockInPolicies`, `LegalEntityWorkModes`, `WorkAreaChangeRequests`, `Attendance/LocationChangeRequests`, `TimeTracking`; J: `AttendanceAlertMaterializerJob`; W: `TeamAttendanceWidgetDataProvider`, `TeamScheduleWidgetDataProvider` |
| `monitoring.activity_tracking` | C: `MonitoringActivity`; H: `IngestActivitySnapshotsCommandHandler`; J: `ActivityDailySummaryJob`; W: `TeamStatusWidgetDataProvider` |
| `monitoring.app_usage` | C: `MonitoringAppUsage`; H: `IngestAppUsageSnapshotsCommandHandler` |
| `monitoring.idle_detection` | H: idle fields inside activity ingest; J: `WellnessRuleEvaluatorJob` |
| `monitoring.screenshot_on_demand` | C: `Screenshot`, `TrayScreenshot`; H: `SubmitPeriodicScreenshotCommandHandler`, `SubmitInactivityCaptureAttemptCommandHandler`; J: `AgentCommandExpiryJob` |
| `monitoring.productivity_classification` | H: `AppCategoryClassifier` use in `ActivityDailySummaryJob` |
| `monitoring.raw_data_processing` | H: all ingest handlers; J: `MonitoringRetentionPurgeJob` (purge must keep running — retention, see plan Task 15) |
| `monitoring.presence_sessions` | C: `MonitoringWorkSessions`, `MonitoringCheckIn`, `TrayAttendance`, `MonitoringActivation`, `MonitoringDeviceAuthorization`; H: tray clock-in/out handlers, `SubmitWorkSessionCommandHandler`; J: `LocationRuleEvaluatorJob`, `ExceptionDetectionJob`; C: `MonitoringExceptions`, `TrayWorkLocation`, `MonitoringMeeting(Ingest)`, `TrayMonitoringPolicy`, `MonitoringPermissionConsent`, `Monitoring/Notifications` |
| `monitoring.break_tracking` | H: `TrayStartBreakCommandHandler`, `TrayEndBreakCommandHandler` |
| `monitoring.attendance_corrections` | C: `Attendance/AttendanceCorrections` — module-boundary question Q5 |
| `monitoring.device_sessions` | C: `MonitoringDeviceState(Ingest)`, `DeviceChangeRequests` |
| `monitoring.biometric_devices` | C: `BiometricEnrollment`; H: `EnrollFacePhotosCommandHandler` |
| `verification.identity_checks` | H: `SubmitCheckInCommandHandler` face check |
| `verification.face_match` | H: `UploadFaceScanCommandHandler`, `ValidateFacePhotoCommandHandler`, `GetFaceReferenceStatusQueryHandler` |
| `analytics.daily_reports` | C: `MonitoringDailyReport` |
| `analytics.monitoring_snapshots` | C: `MonitoringReports` |
| `analytics.productivity_dashboard` | W: `WellbeingWidgetDataProvider`; C: `MonitoringSettings` dashboard reads (verify during Task 18) |
| `work_management.projects` | C: `Projects`, `ProjectCategories`, `ProjectMonitor`; J: `ProjectMonitorJob`; W: `ActiveProjectsWidgetDataProvider` |
| `work_management.tasks` | C: `Tasks`, `TaskDrafts`, `TaskStatusChangeRequests`, `Comments`, `WorkApprovals`; O: `WorkNotificationOutboxHandler`; W: `MyTasksWidgetDataProvider`, four `Work*TeamActionSource`, `GetMyTeamCapabilitiesQueryHandler`, `EmployeeOverviewWorkModules`; C: `Employees` work endpoints (6 `RequireAnyModule` sites), `EmployeeOffboarding` (1) |
| `work_management.sprints` | C: `Sprints` |
| `work_management.boards` | No distinct surface — board views read `Tasks` endpoints (gate = `work_management.tasks`; documented in plan coverage manifest) |
| `work_management.okrs` | C: `Objectives` |
| `work_management.resource_planning` | C: `Planner`, `WorkForecast` |
| `work_management.work_analytics` | C: `WorkLeadership`, `MyWorkProgress`; W: `TeamProgressWidgetDataProvider`, `WeeklyWorkProgressWidgetDataProvider` |
| `work_management.github_integration` | H: task/PR linking (`GitHubUserIntegrationAvailability`) — Q8 |
| `integrations.github` | C: `Integrations/GitHubIntegration` (connection) — Q8 |
| `integrations.microsoft_teams` | J: `TeamsAttendanceSyncJob`; O: `MeetingLinkProvisionOutboxHandler` (Teams branch) |
| `integrations.google_workspace` | J/O: Google branch of `CalendarSyncJob` and calendar connection completion (`CompleteCalendarConnectionCommandHandler`) |

### 3.3 Gaps — no implementation found (14)

`core_hr.compensation`, `core_hr.qualifications`, `monitoring.website_usage`, `monitoring.app_allowlist`, `verification.verification_policies`, `verification.manual_review`, `verification.photo_challenge`, `analytics.monthly_reports`, `analytics.data_export`, `analytics.scheduled_reports`, `work_management.roadmaps`, `work_management.time_tracking`, `integrations.webhooks`, `integrations.api_access`.

An active customer-facing key without a gate would fail DEC-033's architecture test. Recommended (Q7): backfill these 14 to lifecycle `draft` (deny, nothing to deny today) instead of `active`; each moves to `active` together with its first gated surface. Spec §7.1 allows "a safer reviewed mapping" — this needs Dabi's confirmation because it changes the default lifecycle mapping.

### 3.4 Capabilities with no key

- **Meetings** (`WorkManagement/Meetings`, `EmployeeMeetingAttendance`, `MeetingAttendanceJob`, `MeetingOccurrenceGeneratorJob`, `MeetingLinkProvisionOutboxHandler`).
- **Zoom** (`ZoomAttendanceSyncJob`, Zoom meeting client).

Recommended (Q9): add `work_management.meetings` and `integrations.zoom`, both `IsDefaultIncluded=true`, so no tenant loses access. Cross-cutting shell surfaces (Inbox, Conversations, Files, Dashboard layout, Support, Legal, Auth session) are not product features and stay ungated by feature keys.

## 4. Module entitlement and subscription rule

### 4.1 Callers of `IModuleEntitlementService`

| Method | Callers |
|---|---|
| `GetActiveModuleKeysForTenantAsync` | `EndpointGate`, `RequireAnyModuleAttribute`, `GetCurrentSessionQueryHandler`, `RoleEffectivePermissions`, `GetMyTeamCapabilitiesQueryHandler`, `WorkObjectiveChangeTeamActionSource`, `WorkStatusTemplateChangeTeamActionSource`, `WorkTaskCreationTeamActionSource`, `WorkTaskEditTeamActionSource`, `GitHubUserIntegrationAvailability`, `LoginSessionMaterialFactory`, `PermissionResolver` |
| `IsModuleEnabledAsync` | `EmployeeOverviewWorkModules`, `ApplyConfigurationTemplateToTenantCommandHandler`, `TrayPresenceRequirementEvaluator` (`activity_monitoring`), `ListPositionTemplatePacksQueryHandler` |
| `GetAssignablePermissionsForTenantAsync` | `RolePermissionAssignability`, `ApplyRoleTemplateCommandHandler`, `TenantPermissionCatalogService` |
| `GetEntitledPermissionsAsync` | `DefaultRoleSeeder` (×2; plan module keys + baseline) |

Other `SubscriptionStatusRules.ActiveStatuses` users not in Phase 1 scope (finding): `EfSubscriptionRepository.GetLatestActiveByTenantIdAsync` (storage quota) and `TenantModuleStatusReader`. After Phase 1 they disagree with module entitlement for grace-window and expired-access tenants; proposed as a follow-up, not changed here.

### 4.2 Rule change

- Old: latest row whose `status ∈ {active, trialing, maintenance_included, subscription_included}`; `access_ends_at` ignored.
- New: latest row by `created_at` (ties by `id`), entitled iff `HasActiveAccess(status, access_ends_at, now)`.
- Tenants change when: (a) `past_due`/`cancelled` with future `access_ends_at` → **gain**; (b) active-status row with past `access_ends_at` → **lose**; (c) newest row is non-active but an older row is active-status → **lose** (old rule fell back to the older row).

### 4.3 Trial end

`CreateTenantCommandHandler` sets `status='trialing'`, `trial_end_date`, and leaves `access_ends_at` null ("set by billing system when subscription lapses"). No job or handler moves a trial past `trial_end_date`. Stripe `invoice.paid` sets `active` and clears `access_ends_at`. Result: **trial end does not set `access_ends_at`**, so `HasActiveAccess` keeps an ended trial entitled (today's rule does too). The spec's UAT case "an ended trial denies access" cannot pass without a change — Q5.

## 5. Read-only SQL (per environment, operator-run)

Run as an operator with read access. These statements only read. Do not run against a shared database without the environment owner's approval.

### 5.1 Affected tenants for the `HasActiveAccess` move

```sql
EXEC sys.sp_set_session_context @key = N'app.tenant_context_mode', @value = N'admin';
DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();

WITH old_rule AS (
    SELECT tenant_id, id AS old_subscription_id
    FROM (SELECT s.tenant_id, s.id,
                 ROW_NUMBER() OVER (PARTITION BY s.tenant_id ORDER BY s.created_at DESC, s.id DESC) AS rn
          FROM dbo.tenant_subscriptions s
          WHERE s.status IN (N'active', N'trialing', N'maintenance_included', N'subscription_included')) x
    WHERE rn = 1),
new_rule AS (
    SELECT tenant_id, id AS new_subscription_id, status, access_ends_at, trial_end_date,
           CASE WHEN access_ends_at IS NOT NULL AND access_ends_at <= @now THEN 0
                WHEN status IN (N'active', N'trialing', N'maintenance_included', N'subscription_included') THEN 1
                WHEN status IN (N'past_due', N'cancelled') AND access_ends_at > @now THEN 1
                ELSE 0 END AS new_access
    FROM (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.tenant_id ORDER BY s.created_at DESC, s.id DESC) AS rn
          FROM dbo.tenant_subscriptions s) x
    WHERE rn = 1),
compared AS (
    SELECT t.id AS tenant_id, o.old_subscription_id, n.new_subscription_id, n.status,
           n.access_ends_at, n.trial_end_date,
           CASE WHEN o.old_subscription_id IS NULL THEN 0 ELSE 1 END AS old_access,
           ISNULL(n.new_access, 0) AS new_access,
           CASE WHEN n.status = N'trialing' AND n.trial_end_date <= @now AND n.access_ends_at IS NULL
                THEN 1 ELSE 0 END AS ended_trial_without_access_end
    FROM dbo.tenants t
    LEFT JOIN old_rule o ON o.tenant_id = t.id
    LEFT JOIN new_rule n ON n.tenant_id = t.id)
SELECT *,
       CASE WHEN old_access = 0 AND new_access = 1 THEN 'gains_access'
            WHEN old_access = 1 AND new_access = 0 THEN 'loses_access'
            WHEN old_subscription_id <> new_subscription_id THEN 'different_subscription_row'
            ELSE 'unchanged' END AS change
FROM compared
WHERE old_access <> new_access
   OR old_subscription_id <> new_subscription_id
   OR ended_trial_without_access_end = 1
ORDER BY change, tenant_id;
```

### 5.2 Persisted module keys (feeds the alias migration)

```sql
EXEC sys.sp_set_session_context @key = N'app.tenant_context_mode', @value = N'admin';
SELECT 'subscription_plans' AS source, j.value AS module_key, COUNT(*) AS rows_
FROM dbo.subscription_plans p CROSS APPLY OPENJSON(p.included_modules_json) j GROUP BY j.value
UNION ALL SELECT 'tenant_subscriptions', j.value, COUNT(*)
FROM dbo.tenant_subscriptions s CROSS APPLY OPENJSON(s.selected_modules) j GROUP BY j.value
UNION ALL SELECT 'role_templates', j.value, COUNT(*)
FROM dbo.role_templates r CROSS APPLY OPENJSON(r.module_keys_json) j GROUP BY j.value
UNION ALL SELECT 'configuration_templates', j.value, COUNT(*)
FROM dbo.configuration_templates c CROSS APPLY OPENJSON(c.module_keys_json) j GROUP BY j.value
UNION ALL SELECT 'permissions', module, COUNT(*) FROM dbo.permissions GROUP BY module
UNION ALL SELECT 'module_permission_ownership', module_key, COUNT(*) FROM dbo.module_permission_ownership GROUP BY module_key
UNION ALL SELECT 'module_features', module_key, COUNT(*) FROM dbo.module_features GROUP BY module_key
UNION ALL SELECT 'module_integration_links', module_key, COUNT(*) FROM dbo.module_integration_links GROUP BY module_key
UNION ALL SELECT 'feature_access_grants', module, COUNT(*) FROM dbo.feature_access_grants GROUP BY module
ORDER BY source, module_key;
```

A `tenant_subscriptions` row whose JSON fails `ISJSON` is a corrupt legacy row (spec §11 step 10): `SELECT tenant_id, id FROM dbo.tenant_subscriptions WHERE ISJSON(selected_modules) = 0;`

## 6. Questions raised (answers go to DECISIONS.md)

1. **Q1 Canonical keys:** use the feature-prefix keys (`org`, `time_off`, `monitoring`, `verification`, `analytics`, `work_management`) and migrate all legacy references (recommended), or keep the legacy live keys and relax the spec's module-prefix rule?
2. **Q2 WorkSync:** merge the six legacy WorkSync keys into `work_management`? (recommended yes)
3. **Q3 Leave:** merge `leave` into `time_off`, permission codes unchanged? (recommended yes)
4. **Q4 Feature-less modules:** `productivity_analytics` → `analytics`; `desktop_agent_gateway`, `discrepancy_engine`, `exception_engine` → `monitoring`, or keep them as separate feature-less modules?
5. **Q5 Trial end:** `access_ends_at` is never set at trial end. Set `access_ends_at = trial_end_date` for trialing subscriptions plus a one-off backfill (recommended), or change `HasActiveAccess` to also read `trial_end_date`?
6. **Q6 Baseline keys:** may `configuration.tenant_settings`, `roles.permission_management` and the security e-mails under `notifications.email_delivery` be documented exclusions (never switchable)? Must disabling `auth.mfa_enforcement` leave existing MFA enrolment untouched?
7. **Q7 Gaps:** backfill the 14 unimplemented keys to `draft` instead of `active`?
8. **Q5b / boundary:** attendance-correction endpoints sit under time & attendance but the key is `monitoring.attendance_corrections`. Gate with that key, or deprecate it with replacement `time_attendance.attendance_corrections`?
9. **Q8 GitHub:** `integrations.github` gates the connection, `work_management.github_integration` gates task/PR linking?
10. **Q9 New keys:** add `work_management.meetings` and `integrations.zoom` (default-included)?

Pending row: DEC-039 in `onexso/todo/DECISIONS.md`.
