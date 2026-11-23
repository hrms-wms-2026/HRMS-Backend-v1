# Concurrency tokens, identity columns and empty tables (task 42, 2026-11-20)

Task 42 (`docs/Improvements/person-4-deployment-and-architecture/task-42 Concurrency tokens, identity columns and unused tables.md`).
Rules that came out of it: `docs/development/architecture-rules.md` (identity rule, concurrency tokens, new-table
checklist).

## 1. Concurrency (lost updates)

**Tokens.** Migration `20261120172929_AddRowVersionTokens` adds a `rowversion` column `xmin` (the name every older token
uses) to 9 tables, configured as the EF concurrency token (`HasRowVersionToken()`):

| Table | Token | Version in the API |
|---|---|---|
| `departments` | yes | update and detail |
| `positions` | yes | update and detail |
| `legal_entities` | yes | general settings update and read |
| `clock_in_policies` | yes | update and detail |
| `leave_policies` | yes | details update (no body returned: reload for the new version) and detail |
| `roles` | yes | update and detail |
| `tasks` | yes | not yet (42b) |
| `objectives` | yes | not yet (42b) |
| `sprints` | yes | not yet (42b) |
| `personal_calendar_events` | **no** | Left out: meeting-link provisioning and external calendar sync load the event, wait on Teams/Zoom/Google and save it back while the user may be editing it, by design (only their own columns change). With a token those saves fail (seen in `MeetingProvisioningIntegrationTests`). Make those writers update only their columns (`ExecuteUpdate`) first (42b) |
| `projects` | **no** | Left out: every new task and meeting increments `next_task_number` / `next_meeting_number` on the project row with raw SQL, so every version a client holds would go stale on every new task. Move those counters to their own table first (42b) |
| `employees`, `leave_requests` | already had one | unchanged |

**How it behaves.**
- The client reads `version` from the detail or update response and sends it back as `version` on the next update.
  If someone saved in between, the save fails and the API returns **409** with
  `type: urn:crownus:error:concurrency-conflict` and the message "This record was changed by someone else. Reload it
  and try again." (`ExceptionHandlerMiddleware` now maps both `DbUpdateConcurrencyException` and
  `ConcurrencyConflictException`). Before, an unhandled conflict was a 500.
- No version sent: the last save wins, exactly as before, so the current frontend keeps working.
- **Deviation from the task:** the task asks for "409 with the current row". A global handler cannot know the row, so
  the 409 carries a message and the client reloads. Agree with the frontend whether a per-screen "their version / your
  version" view is wanted; that is 42b.

**Regression guard.** Many repositories load rows with `AsNoTracking` and later call `Update()`. Such an entity has no
original version, so with a token the UPDATE would match nothing and every save would fail. `ApplicationDbContext`
`SaveChanges(Async)` reads the current version first for those entries only (tokens added by task 42, marked with an
annotation), so they keep today's behaviour. A version set from the client is not touched. Checked on SQL Server: with
the guard removed, the detached-update test fails on all 6 tables it covers; with it, it passes.

Sweeps before adding the tokens:
- The `tasks` trigger (`trg_tasks_module_hour_summary`) only writes `wm_module_hour_summaries`, so task saves do not
  change `objectives` versions.
- No `ExecuteUpdate` or raw `UPDATE` on the 9 tables, except the start-up `OfficeProjectReconciler` (projects and
  objectives, once at start).
- No code changes an existing approver or row through a newly built object; detached updates are covered by the guard.

## 2. Identity: employee_id or user_id

Rule (now in `architecture-rules.md`): business tables use `employee_id`; `user_id` only on login, session and
security tables.

| Table | Today | Decision |
|---|---|---|
| `task_assignments` | `user_id` and `employee_id` | **Keep `employee_id`, drop `user_id`** (derive it from `employees.user_id`). Six reads of `UserId` to move. Do it together with the shape decision below (42b) |
| `invitation_tokens` | `user_id`, `employee_id` (nullable) | **Keep both, reason written.** A security table: the token is issued to a user account (`user_id`), and the onboarding invite also names the employee record the account is joined to on acceptance (`employee_id`) |
| `access_grant_requests` | `employee_id`, `user_id` (both nullable) | **Keep both, reason written.** The request creates both the login and the employee; both stay null until approval creates them (correlated by `onboarding_draft_id` meanwhile), and the approval writes each separately |
| `employee_check_ins` | `user_id` only | **Move to `employee_id`** (42b): add `employee_id`, backfill from `employees.user_id`, switch the reads, drop `user_id`. It sits beside tables that all use `employee_id` |

## 3. task_assignments (decision pending: product owner)

Today a unique index on `task_id` allows one assignment row per task (435 of 754 tasks locally), so the table acts as
extra columns of `tasks`.

| Option | Change | Effect |
|---|---|---|
| A. One assignee per task | Add `assignee_employee_id`, `assigned_by_employee_id`, `assigned_at` to `tasks`; backfill from `task_assignments`; switch reads and writes; drop the table and its RLS entry | One join less on every task list and board query |
| B. Several assignees | Drop the unique index; primary key `(task_id, employee_id)`; every reader that expects one assignee (board cards, filters, notifications) must handle a list | Enables shared tasks; more frontend work |

Nothing changed in this branch. Whichever is chosen, `user_id` goes (section 2).

## 4. Empty tables

108 of 224 tables have no rows in the local database (counted from `sys.partitions`, which row-level security does not
hide; a `COUNT(*)` as `sa` shows 0 for every table under RLS). For each one the code under `src/` was searched for
something that creates its entity (outside configurations and migrations). This is a text search: treat "unused" as
"no writer found, confirm before dropping".

### 4a. Unused: propose dropping (8)

| Table | Why | What goes with it |
|---|---|---|
| `refresh_tokens` | The app uses cookie sessions. Nothing creates a refresh token; only `ResetPasswordCommandHandler` revokes them | Entity, configuration, `IRefreshTokenRepository` and its EF implementation, the revoke call, RLS entry |
| `dashboard_templates`, `dashboard_template_widgets`, `dashboard_template_widget_layouts` | Repository exists, nothing calls it. Confirm with the dashboard owner | Three entities and configurations, `IDashboardTemplateRepository` and its EF implementation, RLS entries |
| `feature_access_grants` | Repository exists, nothing calls it | Entity, configuration, repository pair, RLS entry |
| `module_catalog_price_history` | No repository, no reader, no writer | Entity, configuration |
| `tenant_provisioning_states` | No repository, no reader, no writer | Entity, configuration, RLS entry |
| `tenant_setup_selections` | No repository, no reader, no writer | Entity, configuration, RLS entry |

No table was dropped in this branch; each drop needs its own migration and a check that UAT is empty too.

### 4b. Planned or half-built (3)

| Table | State |
|---|---|
| `user_permission_overrides` | Read by `PermissionResolver`, but nothing writes it: per-user permission overrides have no admin screen yet |
| `tenant_storage_stats` | Read by `StorageQuotaService`, but nothing writes it. **The quota check reads a table that is never filled**; check whether quota enforcement works at all (follow-up) |
| `tenant_one_time_charges` | Retired on purpose, table kept (`TenantOneTimeChargeActiveRetirementArchitectureTests`) |

### 4c. Used, no demo data (97)

Written by the code listed; empty only because the dev data does not exercise them. Of the tables the task named,
`task_comment_logs`, `labels` and `activity_raw_buffer` are here (in use); `audit_logs` has 1 row and `job_leases` 19.

| Table | Entity | Written by (first file) |
|---|---|---|
| `access_grant_requests` | `AccessGrantRequest` | ChangeEmployeePositionCommandHandler.cs |
| `activity_raw_buffer` | `ActivityRawBuffer` | IngestActivitySnapshotsCommandHandler.cs |
| `agent_commands` | `AgentCommand` | RequestScreenshotCommandHandler.cs |
| `attendance_alert_deductions` | `AttendanceAlertDeduction` | AttendanceDeductionService.cs |
| `attendance_corrections` | `AttendanceCorrection` | AttendanceCorrectionWorkflow.cs |
| `billing_audit_logs` | `BillingAuditLog` | CreateInvoiceCommandHandler.cs |
| `biometric_enrollment_attempts` | `BiometricEnrollmentAttempt` | CreateEnrollmentAttemptCommandHandler.cs |
| `biometric_profiles` | `BiometricProfile` | CompleteEnrollmentAttemptCommandHandler.cs |
| `bulk_onboarding_batch_rows` | `BulkOnboardingBatchRow` | UploadBulkOnboardingBatchCommandHandler.cs |
| `bulk_onboarding_batches` | `BulkOnboardingBatch` | UploadBulkOnboardingBatchCommandHandler.cs |
| `calendar_event_activity_logs` | `CalendarEventActivityLog` | CloseCalendarEventCommandHandler.cs |
| `calendar_event_guests` | `CalendarEventGuest` | AddCalendarEventGuestsCommandHandler.cs |
| `calendar_event_meeting_attendances` | `CalendarEventMeetingAttendance` | MeetingAttendanceSyncService.cs |
| `calendar_event_meetings` | `CalendarEventMeeting` | CreateEventMeetingCommandHandler.cs |
| `calendar_event_objectives` | `CalendarEventObjective` | CreateCalendarEventCommandHandler.cs |
| `calendar_event_participant_rsvp_history` | `CalendarEventParticipantRsvpHistory` | RespondToCalendarEventCommandHandler.cs |
| `calendar_event_reminders_sent` | `CalendarEventReminderLog` | CalendarEventReminderService.cs |
| `calendar_event_tasks` | `CalendarEventTask` | CreateCalendarEventCommandHandler.cs |
| `calendar_events` | `CalendarEvent` | CancelRecurringOccurrenceCommandHandler.cs |
| `checklist_templates` | `ChecklistTemplate` | CreateChecklistTemplateCommandHandler |
| `clock_in_late_deduction_rules` | `ClockInLateDeductionRule` | CreateClockInPolicyCommandHandler.cs |
| `conversation_favorites` | `ConversationFavorite` | EfConversationRepository.cs |
| `conversation_message_attachments` | `ConversationMessageAttachment` | PostConversationMessageCommandHandler.cs |
| `conversation_message_hides` | `ConversationMessageHide` | DeleteConversationMessageCommand.cs |
| `conversation_message_mentions` | `ConversationMessageMention` | EditConversationMessageCommand.cs |
| `conversation_message_reactions` | `ConversationMessageReaction` | ReactToConversationMessageCommand.cs |
| `conversation_message_revisions` | `ConversationMessageRevision` | DeleteConversationMessageCommand.cs |
| `conversation_mutes` | `ConversationMute` | EfConversationRepository.cs |
| `daily_work_location_confirmations` | `DailyWorkLocationConfirmation` | ConfirmWorkLocationCommandHandler.cs |
| `device_change_requests` | `DeviceChangeRequest` | TrayEnrollmentService.cs |
| `employee_bank_details` | `EmployeeBankDetail` | UpdateBankDetailsCommandHandler.cs |
| `employee_check_ins` | `EmployeeCheckIn` | SubmitCheckInCommandHandler.cs |
| `employee_checklist_tasks` | `EmployeeChecklistTask` | ChecklistTaskContract.cs |
| `employee_dependents` | `EmployeeDependent` | AddDependentCommandHandler.cs |
| `employee_monitoring_overrides` | `EmployeeMonitoringOverride` | OnboardingFinalizeFactory.cs |
| `employee_work_locations` | `EmployeeWorkLocation` | SubmitCheckInCommandHandler.cs |
| `employee_work_sessions` | `EmployeeWorkSession` | SubmitWorkSessionCommandHandler.cs |
| `entity_assets` | `EntityAsset` | AddMyDocumentCommandHandler.cs |
| `external_calendar_connections` | `ExternalCalendarConnection` | CompleteCalendarConnectionCommandHandler.cs |
| `external_calendar_event_links` | `ExternalCalendarEventLink` | CalendarSyncService.cs |
| `file_records` | `FileRecord` | FileStorageService.cs |
| `file_upload_reservations` | `FileUploadReservation` | FileStorageService.cs |
| `holiday_calendar_settings` | `HolidayCalendarSettings` | UpdateHolidayCalendarSettingsCommandHandler |
| `idempotency_records` | `IdempotencyRecord` | EfIdempotencyStore.cs |
| `inactivity_capture_attempts` | `InactivityCaptureAttempt` | SubmitInactivityCaptureAttemptCommandHandler.cs |
| `invitation_tokens` | `InvitationToken` | ResendEmployeeInvitationCommandHandler.cs |
| `labels` | `Label` | CreateProjectCommandHandler.cs |
| `leave_approval_delegates` | `LeaveApprovalDelegate` | LeaveApprovalDelegateCommands.cs |
| `leave_policy_blackout_periods` | `LeavePolicyBlackoutPeriod` | LeavePolicyVersionFactory.cs |
| `leave_request_documents` | `LeaveRequestDocument` | SubmitLeaveRequestCommandHandler.cs |
| `location_change_requests` | `LocationChangeRequest` | LocationChangeRequestWorkflow.cs |
| `login_workspace_selection_challenges` | `LoginWorkspaceSelectionChallenge` | EfLoginWorkspaceSelectionChallengeRepository.cs |
| `meeting_action_items` | `MeetingActionItem` | MeetingActionItemService.cs |
| `meeting_attendance_sessions` | `MeetingAttendanceSession` | MeetingAttendanceSyncService.cs |
| `meeting_occurrence_attendees` | `MeetingOccurrenceAttendee` | MeetingAttendeeService.cs |
| `meeting_occurrence_suppressions` | `MeetingOccurrenceSuppression` | MeetingMoveService.cs |
| `meeting_series_links` | `MeetingSeriesLink` | EfMeetingLinkRepository.cs |
| `meeting_signals` | `MeetingSignal` | IngestMeetingSignalsCommandHandler |
| `mfa_challenges` | `MfaChallenge` | PostgresMfaChallengeStore.cs |
| `monitoring_evidence_assets` | `MonitoringEvidenceAsset` | CompleteAgentCommandHandler.cs |
| `monitoring_face_scans` | `MonitoringFaceScan` | UploadFaceScanCommandHandler.cs |
| `monitoring_feature_toggles` | `MonitoringFeatureToggles` | UpdateMonitoringFeatureTogglesCommandHandler.cs |
| `monitoring_permission_consents` | `MonitoringPermissionConsent` | RecordPermissionConsentCommandHandler.cs |
| `monitoring_policy_overrides` | `MonitoringPolicyOverride` | MonitoringPolicyConfigurationService.cs |
| `offboarding_records` | `OffboardingRecord` | StartOffboardingCommandHandler.cs |
| `offboarding_task_bypass_requests` | `OffboardingTaskBypassRequest` | CreateBypassRequestCommandHandler.cs |
| `password_reset_tokens` | `PasswordResetToken` | BaseForgotPasswordCommandHandler.cs |
| `payment_gateway_configs` | `PaymentGatewayConfig` | CreatePaymentGatewayCommandHandler.cs |
| `payment_gateway_country_routes` | `PaymentGatewayCountryRoute` | CreatePaymentGatewayCommandHandler.cs |
| `payment_gateway_credentials` | `PaymentGatewayCredential` | CreatePaymentGatewayCommandHandler.cs |
| `personal_calendar_events` | `CalendarEvent` | CancelRecurringOccurrenceCommandHandler.cs |
| `platform_announcements` | `PlatformAnnouncement` | PlatformAnnouncementHandlers.cs |
| `platform_oauth_app_credentials` | `PlatformOAuthAppCredential` | ConfigurePlatformOAuthAppCommandHandler.cs |
| `platform_service_keys` | `PlatformServiceKey` | CreatePlatformServiceKeyCommandHandler.cs |
| `platform_user_invites` | `PlatformUserInvite` | InvitePlatformManagerCommandHandler.cs |
| `position_access_templates` | `PositionAccessTemplate` | SetPositionAccessCommandHandler.cs |
| `project_member_invitations` | `ProjectMemberInvitation` | CreateObjectiveCommandHandler.cs |
| `reporting_manager_changes` | `ReportingManagerChange` | ReportingLineMoveService.cs |
| `seat_increase_requests` | `SeatIncreaseRequest` | OnboardingDraftWriteService.cs |
| `subscription_invoices` | `SubscriptionInvoice` | CreateInvoiceCommandHandler.cs |
| `support_ticket_attachments` | `SupportTicketAttachment` | SupportAttachmentService.cs |
| `support_ticket_comments` | `SupportTicketComment` | SupportTicketCommands.cs |
| `support_tickets` | `SupportTicket` | SupportTicketCommands.cs |
| `task_comment_logs` | `TaskCommentLog` | DeleteTaskCommentCommandHandler.cs |
| `task_drafts` | `TaskDraft` | TaskDraftCommands.cs |
| `tenant_configuration_template_applications` | `TenantConfigurationTemplateApplication` | ApplyConfigurationTemplateToTenantCommandHandler.cs |
| `tenant_status_histories` | `TenantStatusHistory` | ChangeTenantStatusCommandHandler.cs |
| `tray_activation_codes` | `TrayActivationCode` | GenerateActivationCodeCommandHandler.cs |
| `tray_app_releases` | `TrayAppRelease` | CreateTrayReleaseCommandHandler.cs |
| `tray_device_authorizations` | `TrayDeviceAuthorization` | StartDeviceAuthorizationCommandHandler.cs |
| `tray_device_refresh_tokens` | `TrayDeviceRefreshToken` | RefreshTrayTokenCommandHandler.cs |
| `tray_device_registrations` | `TrayDeviceRegistration` | TrayEnrollmentService.cs |
| `user_external_identities` | `UserExternalIdentity` | AcceptInvitationGoogleCommandHandler.cs |
| `user_integration_connections` | `UserIntegrationConnection` | UpsertOwnUserIntegrationConnectionCommand.cs |
| `user_mfa` | `UserMfa` | EnableMfaCommandHandler.cs |
| `wm_task_rate_settings` | `TaskRateSetting` | EfTaskRateSettingRepository.cs |
| `work_area_change_requests` | `WorkAreaChangeRequest` | WorkAreaChangeRequestWorkflow.cs |

## Tests

| Suite | Result |
|---|---|
| Unit (Release) | 8,188 passed. New: `RowVersionTokenModelTests` (9 tables carry the token; `projects` and `personal_calendar_events` do not) and the 409 cases in `ExceptionHandlerMiddlewareTests` |
| Architecture | 713 passed |
| `RowVersionConcurrencyIntegrationTests` (SQL Server, 18 cases over `legal_entities`, `departments`, `positions`, `clock_in_policies`, `objectives`, `tasks`) | Passed: the second of two overlapping saves is rejected; a stale client version is rejected and the current one accepted; a row loaded without tracking and then updated still saves (fails on all 6 without the guard) |
| Regression: WorkManagement, OrgStructure, Leave, Calendar, MyTeam, CoreHr, E2E, Auth, Features suites | 784 of 790 on the first run. 1 real failure, `MeetingProvisioningIntegrationTests` (event renamed while its meeting link is created), caused by the token on `personal_calendar_events`: that table was taken out. 5 `SensitivePositionChangeApprovalIntegrationTests` failed on a SQL Server page I/O error in their cloned test database (storage, not code). All 6 pass on the rerun, with the Calendar suite and the token tests (32 passed) |

## 42b: what is left

1. Version contract for `tasks`, `objectives` and `sprints` (many small inline-edit endpoints).
2. Frontend: send `version` back on the six converted forms and show the 409 ("reload").
3. `personal_calendar_events`: make meeting-link provisioning and calendar sync update only their own columns, then add the token.
4. Move the project counters off `projects`, then add its token.
5. `task_assignments` shape (product owner) and dropping its `user_id`; `employee_check_ins` to `employee_id`.
6. Drop the 8 unused tables after confirming; check `tenant_storage_stats` (quota reads a table nothing fills).
