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
| `work_activity_recorded`, `work_approval_requested`, `work_approval_decided`, `work_monitor_alert` | `Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs` | `work_notification` outbox → `WorkNotificationOutboxHandler`; `work_approval_requested` → `INotificationOrchestrator` | Active milestone assignees named by the event, excluding the actor (UserId via `GetActiveAssigneeAsync`) | `work_approval_requested`: **orchestrator**; others legacy |
| `work_sprint_completed`, `work_sprint_incomplete`, `work_sprint_achieved` | `Features/WorkManagement/Sprints/Services/SprintWriteService.cs` | direct dispatcher call in the business transaction | Sprint milestone members | legacy |
| `work_monitor_alert_escalated` | `Features/WorkManagement/Inbox/WorkAlertEscalation.cs` | direct | Escalation target | legacy |
| `work_project_member_invited` | `Features/WorkManagement/Projects/Commands/AddProjectMember/AddProjectMemberCommandHandler.cs` | `work_notification` outbox | Invited employee | legacy |
| `work_objective_invitation_created` | `Features/WorkManagement/Objectives/Services/MilestoneMembershipCoordinator.cs`, `.../TransferObjectiveHead/TransferObjectiveHeadCommandHandler.cs` | `work_notification` outbox | Invited employee | legacy |
| `work_project_member_accepted` | `Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommandHandler.cs` | `work_notification` outbox | Inviter | legacy |
| `work_task_creation_request_*`, `work_task_edit_request_decided`, `work_task_status_change_request_*`, `work_allocation_extend_request_*`, `work_objective_edit_request_*` (9 codes) | No current producer found (seeded only; superseded by `work_approval_*`) | — | — | legacy (catalogued so existing copy stays editable; no trigger) |
| `conversation_member_added`, `conversation_mentioned` | `Features/Conversations/Services/ConversationNotifier.cs` | direct | Added / mentioned member | legacy |
| `leave_request_approved`, `leave_request_rejected`, `leave_request_information_requested`, `leave_request_next_approval_required` | `Features/Leave/Approval/Commands/LeaveApprovalDecisionService.cs`, `CreateApproverBackdatedLeaveCommand.cs` | direct | Requesting employee / next approver | legacy |
| `leave_request_cancelled_by_employee`, `leave_request_cancelled_by_hr`, `leave_request_partially_cancelled` | `Features/Leave/Cancellation/Commands/CancelLeaveRequestCommand.cs` | direct | Approver(s) / employee | legacy |
| `leave_request_decision_undone` | `Features/Leave/Inbox/LeaveDecisionReverter.cs` | direct | Requesting employee | legacy |
| `face_verification_override` | `Features/Monitoring/CheckIn/Services/FaceVerificationRetryPolicy.cs` | direct | Reviewing manager/HR | legacy |
| `exception_alert_detected`, `exception_alert_escalated` | `Features/Monitoring/Exceptions/Services/ExceptionAlertRouter.cs`, `.../Inbox/ExceptionCaseActions.cs` | direct | Reviewer / HR | legacy |
| `attendance_correction_request_created/decided/cancelled` | `Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs` | direct | Approver / requester | legacy |
| `work_area_change_request_created/decided/cancelled` | `Features/TimeAttendance/Commands/WorkAreaChangeRequests/WorkAreaChangeRequestWorkflow.cs` | direct | Approver / requester | legacy |
| `calendar_event_*` (12 codes), `calendar_connection_*` (3 codes) | `Features/Calendar/Services/CalendarNotificationSender.cs` | direct | Participants / organizer / connection owner | legacy |
| `calendar_exception_detected`, `calendar_exception_raised_for_you`, `calendar_exception_escalated` | `Features/Calendar/Exceptions/CalendarCaseService.cs`, `CalendarExceptionEscalationSweepSource.cs`, `Monitoring/Exceptions/Inbox/ExceptionCaseActions.cs` | direct | Reviewer / subject employee / escalation target | legacy |

Direct producers write in-app rows inside their business transaction. Migrating them requires the
orchestrator to reserve deliveries + outbox work in that same `SaveChanges` (Task 4/8); they are not part
of the first slice.

## B. Monitoring tray alerts (monitoring `Notification` entity, polled by the tray)

| Event key | Producer | Recipients | Status |
|---|---|---|---|
| `monitoring_break_reminder`, `monitoring_break_allowance_exceeded` | `Features/TimeAttendance/Services/BreakAllowanceMonitor.cs`, `Infrastructure/Services/Monitoring/Notifications/WellnessRuleEvaluatorJob.cs` | The employee | legacy (tray-only) |
| `monitoring_long_idle_alert`, `monitoring_low_activity_alert`, `monitoring_focus_nudge` | `WellnessRuleEvaluatorJob.cs`, `Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryJob.cs` | The employee | legacy (tray-only) |
| `monitoring_outside_work_location_alert` | `Infrastructure/Services/Monitoring/Notifications/LocationRuleEvaluatorJob.cs` | The employee | legacy (tray-only) |
| `monitoring_identity_verification_alert` | Monitoring check-in (face verification) | The employee | legacy (tray-only) |

## C. Email producers (`EmailTemplateRenderer` + outbox email handlers)

| Template id / outbox type | Producer | Status |
|---|---|---|
| `password_reset`, `admin_password_reset`, `admin_password_changed` | Auth login commands (`BaseForgotPassword`, `RequestPasswordReset`, `RequestAdminPasswordReset`, `ResetAdminPassword`) | **excluded** — security/account recovery, required delivery, must not be tenant-disableable |
| `tenant_owner_invite`, `platform_manager_invite` | DevPlatform provisioning / `InvitePlatformManagerCommandHandler` | **excluded** — platform-wide, no tenant |
| `invoice_email` | `ResendInvoiceEmailCommandHandler`, billing | **excluded** — platform billing |
| `employee_onboarding_invite` | `OnboardingDraftWriteService`, `ResendEmployeeInvitationCommandHandler` | **excluded (v1)** — account invite with token link; stays required |
| `position_change_approval_request` | `ChangeEmployeePositionCommandHandler`, `ApproveAccessGrantRequestCommandHandler` | legacy — candidate for a later catalog event |
| `calendar_event_invite/updated/cancelled/removed` (+ ICS) | `CalendarNotificationSender.cs` | legacy — carries ICS attachments; migrate after attachment support |
| `leave_request_approved/rejected`, `leave_information_requested`, `leave_request_cancelled` (outbox) | Leave approval / cancellation commands → `LeaveApprovalEmailOutboxHandler` | legacy — these become the Email channel of the matching section-A leave events in Task 8 |

## Migration status

| Event | Owner | Tests |
|---|---|---|
| `work_approval_requested` | orchestrator | `NotificationOrchestratorTests`, `NotificationDeliveryHandlerTests`, `WorkNotificationEngineTests.Requested_*` |
| all others | legacy | — |
