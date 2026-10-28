# Notification event inventory

Source of truth for `NotificationEventCatalog` and the producer migration (plan Tasks 1 and 8).
Compiled 2026-10-18 from: `NotificationTemplateSeeder` codes resolved to their producers, the email
`OutboxMessageTypes` and their handlers, `EmailTemplateRenderer` template ids, and every producer that
writes the monitoring `Notification` entity.

Status values: **legacy** (still owned by its old path), **orchestrator** (cut over), **excluded** (not
tenant-routable in v1; reason given).

Variables per event are registered in `NotificationEventCatalog` (taken from the seeded copy). Every
shared event currently delivers **in-app only**; email and tray are supported but start OFF. Recipient rule
for every event is `default` = the recipients the producer resolves today (described per group below).
Action for shared events is `open_related` (existing `RelatedEntityType`/`RelatedEntityId` → inbox navigation).

## A. Shared in-app events (shared `INotificationDispatcher.SendTemplatedAsync`)

| Event keys | Producer (trigger file) | Path | Recipients (`default`) | Status |
|---|---|---|---|---|
| `work_activity_recorded`, `work_approval_requested`, `work_approval_decided`, `work_monitor_alert` | `Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs` | `work_notification` outbox → `WorkNotificationOutboxHandler`; `work_approval_requested` → `INotificationOrchestrator` | Active milestone assignees named by the event, excluding the actor (UserId via `GetActiveAssigneeAsync`) | orchestrator |
| `work_sprint_completed`, `work_sprint_incomplete`, `work_sprint_achieved` | `Features/WorkManagement/Sprints/Services/SprintWriteService.cs` | direct dispatcher call in the business transaction | Sprint milestone members | orchestrator |
| `work_monitor_alert_escalated` | `Features/WorkManagement/Inbox/WorkAlertEscalation.cs` | direct | Escalation target | orchestrator |
| `work_project_member_invited` | `Features/WorkManagement/Projects/Commands/AddProjectMember/AddProjectMemberCommandHandler.cs` | `work_notification` outbox | Invited employee | orchestrator |
| `work_objective_invitation_created` | `Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`, `.../TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs` | `work_notification` outbox | Invited employee | orchestrator |
| `work_project_member_accepted` | `Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommandHandler.cs` | `work_notification` outbox | Inviter | orchestrator |
| `work_task_creation_request_*`, `work_task_edit_request_decided`, `work_task_status_change_request_*`, `work_allocation_extend_request_*`, `work_objective_edit_request_*` (9 codes) | No current producer found (seeded only; superseded by `work_approval_*`) | — | — | **not migrated** — no producer; default copy stays editable |
| `conversation_member_added`, `conversation_mentioned` | `Features/Conversations/Services/ConversationNotifier.cs` | direct | Added / mentioned member | orchestrator |
| `leave_request_approved`, `leave_request_rejected`, `leave_request_information_requested`, `leave_request_next_approval_required` | `Features/Leave/Approval/Commands/LeaveApprovalDecisionService.cs`, `CreateApproverBackdatedLeaveCommand.cs` | direct | Requesting employee / next approver | orchestrator |
| `leave_request_cancelled_by_employee`, `leave_request_cancelled_by_hr`, `leave_request_partially_cancelled` | `Features/Leave/Cancellation/Commands/CancelLeaveRequestCommand.cs` | direct | Approver(s) / employee | orchestrator |
| `leave_request_decision_undone` | `Features/Leave/Inbox/LeaveDecisionReverter.cs` | direct | Requesting employee | orchestrator |
| `face_verification_override` | `Features/Monitoring/CheckIn/Services/FaceVerificationRetryPolicy.cs` | direct | Reviewing manager/HR | orchestrator |
| `exception_alert_detected`, `exception_alert_escalated` | `Features/Monitoring/Exceptions/Services/ExceptionAlertRouter.cs`, `.../Inbox/ExceptionCaseActions.cs` | direct | Reviewer / HR | orchestrator |
| `attendance_correction_request_created/decided/cancelled` | `Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs` | direct | Approver / requester | orchestrator |
| `work_area_change_request_created/decided/cancelled` | `Features/TimeAttendance/Commands/WorkAreaChangeRequests/WorkAreaChangeRequestWorkflow.cs` | direct | Approver / requester | orchestrator |
| `calendar_event_*` (15 codes), `calendar_connection_*` (3 codes) | `Features/Calendar/Services/CalendarNotificationSender.cs` | direct | Participants / organizer / connection owner | orchestrator |
| `calendar_exception_detected`, `calendar_exception_raised_for_you`, `calendar_exception_escalated` | `Features/Calendar/Exceptions/CalendarCaseService.cs`, `CalendarExceptionEscalationSweepSource.cs`, `Monitoring/Exceptions/Inbox/ExceptionCaseActions.cs` | direct | Reviewer / subject employee / escalation target | orchestrator |

Direct producers keep calling `INotificationDispatcher.SendTemplatedAsync`; the dispatcher is the cut-over
point and hands owned events to the orchestrator inside the same business transaction (the in-app row lands at
the same commit as before; email/tray work is reserved there too). Every direct caller was checked to run in a
tenant context (request, tenant-switched sweep/outbox job, or tenant-switched tray handler), as the new tables are
under FORCE RLS.

## B. Monitoring tray alerts (monitoring `Notification` entity, polled by the tray)

| Event key | Producer | Recipients | Status |
|---|---|---|---|
| `monitoring_break_reminder`, `monitoring_break_allowance_exceeded` | `Features/TimeAttendance/Services/BreakAllowanceMonitor.cs`, `Infrastructure/Services/Monitoring/Notifications/WellnessRuleEvaluatorJob.cs` | The employee | **excluded (v1)** — see below |
| `monitoring_long_idle_alert`, `monitoring_low_activity_alert`, `monitoring_focus_nudge` | `WellnessRuleEvaluatorJob.cs`, `Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryJob.cs` | The employee | **excluded (v1)** — see below |
| `monitoring_outside_work_location_alert` | `Infrastructure/Services/Monitoring/Notifications/LocationRuleEvaluatorJob.cs` | The employee | **excluded (v1)** — see below |
| `monitoring_identity_verification_alert` | Monitoring check-in (face verification) | The employee | **excluded (v1)** — see below |

Monitoring tray alerts are excluded from the v1 migration: old tray clients only poll the monitoring endpoint,
the alerts also feed the separate monitoring inbox, and their recipients are employee ids. They remain
tray-only and read-only in tenant settings.

## C. Email producers (`EmailTemplateRenderer` + outbox email handlers)

| Template id / outbox type | Producer | Status |
|---|---|---|
| `password_reset`, `admin_password_reset`, `admin_password_changed` | Auth login commands (`BaseForgotPassword`, `RequestPasswordReset`, `RequestAdminPasswordReset`, `ResetAdminPassword`) | **excluded** — security/account recovery, required delivery, must not be tenant-disableable |
| `tenant_owner_invite`, `platform_manager_invite` | DevPlatform provisioning / `InvitePlatformManagerCommandHandler` | **excluded** — platform-wide, no tenant |
| `invoice_email` | `ResendInvoiceEmailCommandHandler`, billing | **excluded** — platform billing |
| `employee_onboarding_invite` | `OnboardingDraftWriteService`, `ResendEmployeeInvitationCommandHandler` | **excluded (v1)** — account invite with token link; stays required |
| `position_change_approval_request` | `ChangeEmployeePositionCommandHandler`, `ApproveAccessGrantRequestCommandHandler` | legacy — candidate for a later catalog event |
| `calendar_event_invite/updated/cancelled/removed` (+ ICS) | `CalendarNotificationSender.cs` | legacy (kept as the email owner) — ICS attachments; `calendar_event_participant_added/updated/cancelled/participant_removed/meeting_link_removed` do not offer Email |
| `leave_request_approved/rejected`, `leave_information_requested`, `leave_request_cancelled` (outbox) | Leave approval / cancellation commands → `LeaveApprovalEmailOutboxHandler` | legacy (kept as the email owner) — the matching leave events do **not** offer an orchestrator Email channel: these emails go to the employee-record address and the cancellation email reaches different people than the in-app notices |

## Migration status

| Group | Owner | Channels offered | Tests |
|---|---|---|---|
| Section A events with a producer (48) | orchestrator | in-app + email + tray, except the 11 "producer sends own email" events: in-app + tray | `NotificationEventMigrationTests` (one regression case per event: recipient, template code, copy, click target), `NotificationDispatcherAdapterTests`, `WorkNotificationEngineTests` |
| Producer-less seeded codes (9) | not migrated | in-app (read-only) | `NotificationEventMigrationTests.EveryProducerBackedSharedEvent_IsOwnedByTheOrchestrator_AndOthersAreNot` |
| Monitoring tray alerts (7) | legacy monitoring transport (excluded) | tray (read-only) | `NotificationEventMigrationTests.MonitoringTrayAlerts_StayOnTheirLegacyTransport` |
| Platform/security emails | excluded (not tenant-routable) | — | — |
