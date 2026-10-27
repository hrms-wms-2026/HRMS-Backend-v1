# Entity → file → data map (HRMS-Backend-v1)

Snapshot taken **2026-10-22** from the local database `OnevoDb` (all tenants, admin user, row counts only).
Entities come from `ApplicationDbContextModelSnapshot.cs`; file paths are the Domain classes.
Regenerate rather than trusting old numbers — data changes every day.

**199 entities** in 16 features · **49,544 rows** in total · **106 tables are empty**.

## Summary by feature

| Feature | Entities | Rows | Empty tables |
|---|---:|---:|---:|
| [Auth](#auth) | 20 | 356 | 11 |
| [Calendar](#calendar) | 11 | 0 | 11 |
| [Conversations](#conversations) | 9 | 151 | 6 |
| [CoreHr](#corehr) | 17 | 127 | 12 |
| [Dashboard](#dashboard) | 3 | 46 | 0 |
| [DevPlatform](#devplatform) | 20 | 117 | 7 |
| [Inbox](#inbox) | 1 | 10 | 0 |
| [InfrastructureModule](#infrastructuremodule) | 3 | 31 | 1 |
| [Leave](#leave) | 12 | 358 | 3 |
| [Lookups](#lookups) | 4 | 18 | 0 |
| [Monitoring](#monitoring) | 26 | 43,424 | 19 |
| [OrgStructure](#orgstructure) | 6 | 67 | 1 |
| [SharedPlatform](#sharedplatform) | 23 | 337 | 12 |
| [Storage](#storage) | 4 | 0 | 4 |
| [TimeAttendance](#timeattendance) | 13 | 3,847 | 7 |
| [WorkManagement](#workmanagement) | 27 | 655 | 12 |

Tables in the database with no entity class: `__EFMigrationsHistory`, `global_email_directory`.

## Auth

| Entity | Table | Rows | File |
|---|---|---:|---|
| AuditLog | `audit_logs` | 0 | [Features/Auth/Login/Entities/AuditLog.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/AuditLog.cs) |
| FeatureAccessGrant | `feature_access_grants` | 0 | [Features/Auth/Permission/Entities/FeatureAccessGrant.cs](../../../src/ONEVO.Domain/Features/Auth/Permission/Entities/FeatureAccessGrant.cs) |
| InvitationToken | `invitation_tokens` | 0 | [Features/Auth/Invite/Entities/InvitationToken.cs](../../../src/ONEVO.Domain/Features/Auth/Invite/Entities/InvitationToken.cs) |
| LegalAcceptanceRecord | `legal_acceptance_records` | 8 | [Features/Auth/Login/Entities/LegalAcceptanceRecord.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/LegalAcceptanceRecord.cs) |
| LegalLoginChallenge | `legal_login_challenges` | 0 | [Features/Auth/Login/Entities/LegalLoginChallenge.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/LegalLoginChallenge.cs) |
| LoginWorkspaceSelectionChallenge | `login_workspace_selection_challenges` | 0 | [Features/Auth/Login/Entities/LoginWorkspaceSelectionChallenge.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/LoginWorkspaceSelectionChallenge.cs) |
| MfaChallenge | `mfa_challenges` | 0 | [Features/Auth/Login/Entities/MfaChallenge.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/MfaChallenge.cs) |
| PasswordResetToken | `password_reset_tokens` | 0 | [Features/Auth/Login/Entities/PasswordResetToken.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/PasswordResetToken.cs) |
| Permission | `permissions` | 83 | [Features/Auth/Permission/Entities/Permission.cs](../../../src/ONEVO.Domain/Features/Auth/Permission/Entities/Permission.cs) |
| RefreshToken | `refresh_tokens` | 0 | [Features/Auth/Login/Entities/RefreshToken.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/RefreshToken.cs) |
| Role | `roles` | 9 | [Features/Auth/Roles/Entities/Role.cs](../../../src/ONEVO.Domain/Features/Auth/Roles/Entities/Role.cs) |
| RolePermission | `role_permissions` | 195 | [Features/Auth/Roles/Entities/RolePermission.cs](../../../src/ONEVO.Domain/Features/Auth/Roles/Entities/RolePermission.cs) |
| RoleTemplate | `role_templates` | 2 | [Features/Auth/Roles/Entities/RoleTemplate.cs](../../../src/ONEVO.Domain/Features/Auth/Roles/Entities/RoleTemplate.cs) |
| Session | `sessions` | 2 | [Features/Auth/Login/Entities/Session.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/Session.cs) |
| TenantAuthPolicy | `tenant_auth_policies` | 2 | [Features/Auth/Login/Entities/TenantAuthPolicy.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/TenantAuthPolicy.cs) |
| TenantSessionExchangeChallenge | `tenant_session_exchange_challenges` | 2 | [Features/Auth/Entities/TenantSessionExchangeChallenge.cs](../../../src/ONEVO.Domain/Features/Auth/Entities/TenantSessionExchangeChallenge.cs) |
| UserExternalIdentity | `user_external_identities` | 0 | [Features/Auth/Login/Entities/UserExternalIdentity.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/UserExternalIdentity.cs) |
| UserMfa | `user_mfa` | 0 | [Features/Auth/Login/Entities/UserMfa.cs](../../../src/ONEVO.Domain/Features/Auth/Login/Entities/UserMfa.cs) |
| UserPermissionOverride | `user_permission_overrides` | 0 | [Features/Auth/Permission/Entities/UserPermissionOverride.cs](../../../src/ONEVO.Domain/Features/Auth/Permission/Entities/UserPermissionOverride.cs) |
| UserRole | `user_roles` | 53 | [Features/Auth/Roles/Entities/UserRole.cs](../../../src/ONEVO.Domain/Features/Auth/Roles/Entities/UserRole.cs) |

## Calendar

| Entity | Table | Rows | File |
|---|---|---:|---|
| CalendarEvent | `personal_calendar_events` | 0 | [Features/Calendar/Entities/CalendarEvent.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs) |
| CalendarEventGuest | `calendar_event_guests` | 0 | [Features/Calendar/Entities/CalendarEventGuest.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventGuest.cs) |
| CalendarEventMeeting | `calendar_event_meetings` | 0 | [Features/Calendar/Entities/CalendarEventMeeting.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs) |
| CalendarEventMeetingAttendance | `calendar_event_meeting_attendances` | 0 | [Features/Calendar/Entities/CalendarEventMeetingAttendance.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeetingAttendance.cs) |
| CalendarEventParticipant | `calendar_event_participants` | 0 | [Features/Calendar/Entities/CalendarEventParticipant.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventParticipant.cs) |
| CalendarEventParticipantRsvpHistory | `calendar_event_participant_rsvp_history` | 0 | [Features/Calendar/Entities/CalendarEventParticipant.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventParticipant.cs) |
| CalendarEventReminderLog | `calendar_event_reminders_sent` | 0 | [Features/Calendar/Entities/CalendarEventReminderLog.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventReminderLog.cs) |
| CalendarMeetingIncident | `calendar_meeting_incidents` | 0 | [Features/Calendar/Entities/CalendarMeetingIncident.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/CalendarMeetingIncident.cs) |
| ExternalCalendarConnection | `external_calendar_connections` | 0 | [Features/Calendar/Entities/ExternalCalendarConnection.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarConnection.cs) |
| ExternalCalendarEventLink | `external_calendar_event_links` | 0 | [Features/Calendar/Entities/ExternalCalendarEventLink.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/ExternalCalendarEventLink.cs) |
| HolidayCalendarSettings | `holiday_calendar_settings` | 0 | [Features/Calendar/Entities/HolidayCalendarSettings.cs](../../../src/ONEVO.Domain/Features/Calendar/Entities/HolidayCalendarSettings.cs) |

## Conversations

| Entity | Table | Rows | File |
|---|---|---:|---|
| Conversation | `conversations` | 23 | [Features/Conversations/Entities/Conversation.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/Conversation.cs) |
| ConversationFavorite | `conversation_favorites` | 0 | [Features/Conversations/Entities/ConversationFavorite.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationFavorite.cs) |
| ConversationMessage | `conversation_messages` | 76 | [Features/Conversations/Entities/ConversationMessage.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationMessage.cs) |
| ConversationMessageAttachment | `conversation_message_attachments` | 0 | [Features/Conversations/Entities/ConversationMessageAttachment.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationMessageAttachment.cs) |
| ConversationMessageHide | `conversation_message_hides` | 0 | [Features/Conversations/Entities/ConversationMessageHide.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationMessageHide.cs) |
| ConversationMessageMention | `conversation_message_mentions` | 0 | [Features/Conversations/Entities/ConversationMessageMention.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationMessageMention.cs) |
| ConversationMessageReaction | `conversation_message_reactions` | 0 | [Features/Conversations/Entities/ConversationMessageReaction.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationMessageReaction.cs) |
| ConversationMessageRevision | `conversation_message_revisions` | 0 | [Features/Conversations/Entities/ConversationMessageRevision.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationMessageRevision.cs) |
| ConversationParticipant | `conversation_participants` | 52 | [Features/Conversations/Entities/ConversationParticipant.cs](../../../src/ONEVO.Domain/Features/Conversations/Entities/ConversationParticipant.cs) |

## CoreHr

| Entity | Table | Rows | File |
|---|---|---:|---|
| AccessGrantRequest | `access_grant_requests` | 0 | [Features/CoreHr/Onboarding/Entities/AccessGrantRequest.cs](../../../src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/AccessGrantRequest.cs) |
| BulkOnboardingBatch | `bulk_onboarding_batches` | 0 | [Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatch.cs](../../../src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatch.cs) |
| BulkOnboardingBatchRow | `bulk_onboarding_batch_rows` | 0 | [Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatchRow.cs](../../../src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatchRow.cs) |
| ChecklistTemplate | `checklist_templates` | 0 | [Features/CoreHr/Onboarding/Entities/ChecklistTemplate.cs](../../../src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/ChecklistTemplate.cs) |
| Employee | `employees` | 29 | [Features/CoreHr/Employee/Entities/Employee.cs](../../../src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs) |
| EmployeeAddress | `employee_addresses` | 4 | [Features/CoreHr/Employee/Entities/EmployeeAddress.cs](../../../src/ONEVO.Domain/Features/CoreHr/Employee/Entities/EmployeeAddress.cs) |
| EmployeeBankDetail | `employee_bank_details` | 0 | [Features/CoreHr/Employee/Entities/EmployeeBankDetail.cs](../../../src/ONEVO.Domain/Features/CoreHr/Employee/Entities/EmployeeBankDetail.cs) |
| EmployeeChecklistTask | `employee_checklist_tasks` | 0 | [Features/CoreHr/Onboarding/Entities/EmployeeChecklistTask.cs](../../../src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/EmployeeChecklistTask.cs) |
| EmployeeDependent | `employee_dependents` | 0 | [Features/CoreHr/Employee/Entities/EmployeeDependent.cs](../../../src/ONEVO.Domain/Features/CoreHr/Employee/Entities/EmployeeDependent.cs) |
| EmployeeEmergencyContact | `employee_emergency_contacts` | 4 | [Features/CoreHr/Employee/Entities/EmployeeEmergencyContact.cs](../../../src/ONEVO.Domain/Features/CoreHr/Employee/Entities/EmployeeEmergencyContact.cs) |
| EmployeeHierarchyClosure | `employee_hierarchy_closure` | 64 | [Features/CoreHr/EmployeeHierarchyClosure/Entities/EmployeeHierarchyClosure.cs](../../../src/ONEVO.Domain/Features/CoreHr/EmployeeHierarchyClosure/Entities/EmployeeHierarchyClosure.cs) |
| OffboardingRecord | `offboarding_records` | 0 | [Features/CoreHr/Offboarding/Entities/OffboardingRecord.cs](../../../src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingRecord.cs) |
| OffboardingTaskBypassRequest | `offboarding_task_bypass_requests` | 0 | [Features/CoreHr/Offboarding/Entities/OffboardingTaskBypassRequest.cs](../../../src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingTaskBypassRequest.cs) |
| OnboardingDraft | `onboarding_drafts` | 0 | [Features/CoreHr/OnboardingDraft/Entities/OnboardingDraft.cs](../../../src/ONEVO.Domain/Features/CoreHr/OnboardingDraft/Entities/OnboardingDraft.cs) |
| PositionAssignment | `position_assignments` | 26 | [Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs](../../../src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs) |
| ReportingManagerChange | `reporting_manager_changes` | 0 | [Features/CoreHr/PositionAssignment/Entities/ReportingManagerChange.cs](../../../src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/ReportingManagerChange.cs) |
| SeatIncreaseRequest | `seat_increase_requests` | 0 | [Features/CoreHr/Onboarding/Entities/SeatIncreaseRequest.cs](../../../src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/SeatIncreaseRequest.cs) |

## Dashboard

| Entity | Table | Rows | File |
|---|---|---:|---|
| DashboardWidget | `dashboard_widgets` | 11 | [Features/Dashboard/Entities/DashboardWidget.cs](../../../src/ONEVO.Domain/Features/Dashboard/Entities/DashboardWidget.cs) |
| DashboardWidgetLayout | `dashboard_widget_layouts` | 33 | [Features/Dashboard/Entities/DashboardWidgetLayout.cs](../../../src/ONEVO.Domain/Features/Dashboard/Entities/DashboardWidgetLayout.cs) |
| UserDashboard | `user_dashboards` | 2 | [Features/Dashboard/Entities/UserDashboard.cs](../../../src/ONEVO.Domain/Features/Dashboard/Entities/UserDashboard.cs) |

## DevPlatform

| Entity | Table | Rows | File |
|---|---|---:|---|
| ConfigurationTemplate | `configuration_templates` | 7 | [Features/DevPlatform/ConfigurationTemplates/Entities/ConfigurationTemplate.cs](../../../src/ONEVO.Domain/Features/DevPlatform/ConfigurationTemplates/Entities/ConfigurationTemplate.cs) |
| GlobalApplication | `global_app_catalog` | 6 | [Features/DevPlatform/AppCatalog/Entities/GlobalApplication.cs](../../../src/ONEVO.Domain/Features/DevPlatform/AppCatalog/Entities/GlobalApplication.cs) |
| IntegrationCatalogEntry | `integration_catalog` | 1 | [Features/DevPlatform/SystemConfig/IntegrationCatalog/Entities/IntegrationCatalogEntry.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/IntegrationCatalog/Entities/IntegrationCatalogEntry.cs) |
| LegalDocumentVersion | `legal_document_versions` | 2 | [Features/DevPlatform/Compliance/Entities/LegalDocumentVersion.cs](../../../src/ONEVO.Domain/Features/DevPlatform/Compliance/Entities/LegalDocumentVersion.cs) |
| ModuleIntegrationLink | `module_integration_links` | 1 | [Features/DevPlatform/SystemConfig/IntegrationCatalog/Entities/ModuleIntegrationLink.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/IntegrationCatalog/Entities/ModuleIntegrationLink.cs) |
| PlatformAuthEvent | `platform_auth_events` | 0 | [Features/DevPlatform/PlatformAccess/Entities/PlatformAuthEvent.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformAuthEvent.cs) |
| PlatformOAuthApp | `platform_oauth_apps` | 4 | [Features/DevPlatform/SystemConfig/PlatformOAuthApps/Entities/PlatformOAuthApp.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Entities/PlatformOAuthApp.cs) |
| PlatformOAuthAppCredential | `platform_oauth_app_credentials` | 0 | [Features/DevPlatform/SystemConfig/PlatformOAuthApps/Entities/PlatformOAuthAppCredential.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Entities/PlatformOAuthAppCredential.cs) |
| PlatformPermission | `platform_permissions` | 37 | [Features/DevPlatform/PlatformAccess/Entities/PlatformPermission.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformPermission.cs) |
| PlatformProvider | `platform_providers` | 12 | [Features/DevPlatform/SystemConfig/PlatformProviders/Entities/PlatformProvider.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/PlatformProviders/Entities/PlatformProvider.cs) |
| PlatformRole | `platform_roles` | 7 | [Features/DevPlatform/PlatformAccess/Entities/PlatformRole.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformRole.cs) |
| PlatformRolePermission | `platform_role_permissions` | 37 | [Features/DevPlatform/PlatformAccess/Entities/PlatformRolePermission.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformRolePermission.cs) |
| PlatformServiceKey | `platform_service_keys` | 0 | [Features/DevPlatform/SystemConfig/PlatformServiceKeys/Entities/PlatformServiceKey.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/PlatformServiceKeys/Entities/PlatformServiceKey.cs) |
| PlatformUser | `platform_users` | 1 | [Features/DevPlatform/PlatformAccess/Entities/PlatformUser.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUser.cs) |
| PlatformUserCredential | `platform_user_credentials` | 1 | [Features/DevPlatform/PlatformAccess/Entities/PlatformUserCredential.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserCredential.cs) |
| PlatformUserInvite | `platform_user_invites` | 0 | [Features/DevPlatform/PlatformAccess/Entities/PlatformUserInvite.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserInvite.cs) |
| PlatformUserRole | `platform_user_roles` | 1 | [Features/DevPlatform/PlatformAccess/Entities/PlatformUserRole.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserRole.cs) |
| PlatformUserSession | `platform_user_sessions` | 0 | [Features/DevPlatform/PlatformAccess/Entities/PlatformUserSession.cs](../../../src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserSession.cs) |
| TenantConfigurationTemplateApplication | `tenant_configuration_template_applications` | 0 | [Features/DevPlatform/ConfigurationTemplates/Entities/TenantConfigurationTemplateApplication.cs](../../../src/ONEVO.Domain/Features/DevPlatform/ConfigurationTemplates/Entities/TenantConfigurationTemplateApplication.cs) |
| TrayAppRelease | `tray_app_releases` | 0 | [Features/DevPlatform/SystemConfig/TrayReleases/Entities/TrayAppRelease.cs](../../../src/ONEVO.Domain/Features/DevPlatform/SystemConfig/TrayReleases/Entities/TrayAppRelease.cs) |

## Inbox

| Entity | Table | Rows | File |
|---|---|---:|---|
| InboxDecisionJournalEntry | `inbox_decision_journal` | 10 | [Features/Inbox/Entities/InboxDecisionJournalEntry.cs](../../../src/ONEVO.Domain/Features/Inbox/Entities/InboxDecisionJournalEntry.cs) |

## InfrastructureModule

| Entity | Table | Rows | File |
|---|---|---:|---|
| Tenant | `tenants` | 2 | [Features/InfrastructureModule/Tenancy/Entities/Tenant.cs](../../../src/ONEVO.Domain/Features/InfrastructureModule/Tenancy/Entities/Tenant.cs) |
| TenantStatusHistory | `tenant_status_histories` | 0 | [Features/InfrastructureModule/Tenancy/Entities/TenantStatusHistory.cs](../../../src/ONEVO.Domain/Features/InfrastructureModule/Tenancy/Entities/TenantStatusHistory.cs) |
| User | `users` | 29 | [Features/InfrastructureModule/User/Entities/User.cs](../../../src/ONEVO.Domain/Features/InfrastructureModule/User/Entities/User.cs) |

## Leave

| Entity | Table | Rows | File |
|---|---|---:|---|
| LeaveApprovalDelegate | `leave_approval_delegates` | 0 | [Features/Leave/Request/Entities/LeaveApprovalDelegate.cs](../../../src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveApprovalDelegate.cs) |
| LeaveBalanceAudit | `leave_balance_audits` | 112 | [Features/Leave/BalanceAudit/Entities/LeaveBalanceAudit.cs](../../../src/ONEVO.Domain/Features/Leave/BalanceAudit/Entities/LeaveBalanceAudit.cs) |
| LeaveEntitlement | `leave_entitlements` | 78 | [Features/Leave/Entitlement/Entities/LeaveEntitlement.cs](../../../src/ONEVO.Domain/Features/Leave/Entitlement/Entities/LeaveEntitlement.cs) |
| LeavePolicy | `leave_policies` | 3 | [Features/Leave/Policy/Entities/LeavePolicy.cs](../../../src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicy.cs) |
| LeavePolicyAssignment | `leave_policy_assignments` | 3 | [Features/Leave/Policy/Entities/LeavePolicyAssignment.cs](../../../src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyAssignment.cs) |
| LeavePolicyBlackoutPeriod | `leave_policy_blackout_periods` | 0 | [Features/Leave/Policy/Entities/LeavePolicyBlackoutPeriod.cs](../../../src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyBlackoutPeriod.cs) |
| LeavePolicyVersion | `leave_policy_versions` | 3 | [Features/Leave/Policy/Entities/LeavePolicyVersion.cs](../../../src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyVersion.cs) |
| LeaveRequest | `leave_requests` | 52 | [Features/Leave/Request/Entities/LeaveRequest.cs](../../../src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequest.cs) |
| LeaveRequestApprover | `leave_request_approvers` | 52 | [Features/Leave/Request/Entities/LeaveRequestApprover.cs](../../../src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestApprover.cs) |
| LeaveRequestDayAllocation | `leave_request_day_allocations` | 52 | [Features/Leave/Request/Entities/LeaveRequestDayAllocation.cs](../../../src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestDayAllocation.cs) |
| LeaveRequestDocument | `leave_request_documents` | 0 | [Features/Leave/Request/Entities/LeaveRequestDocument.cs](../../../src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestDocument.cs) |
| LeaveType | `leave_types` | 3 | [Features/Leave/Type/Entities/LeaveType.cs](../../../src/ONEVO.Domain/Features/Leave/Type/Entities/LeaveType.cs) |

## Lookups

| Entity | Table | Rows | File |
|---|---|---:|---|
| ApprovalStatus | `approval_statuses` | 4 | [Lookups/ApprovalStatus.cs](../../../src/ONEVO.Domain/Lookups/ApprovalStatus.cs) |
| EmploymentStatus | `employment_statuses` | 6 | [Lookups/EmploymentStatus.cs](../../../src/ONEVO.Domain/Lookups/EmploymentStatus.cs) |
| EmploymentType | `employment_types` | 4 | [Lookups/EmploymentType.cs](../../../src/ONEVO.Domain/Lookups/EmploymentType.cs) |
| Severity | `severities` | 4 | [Lookups/Severity.cs](../../../src/ONEVO.Domain/Lookups/Severity.cs) |

## Monitoring

| Entity | Table | Rows | File |
|---|---|---:|---|
| ActivityDailySummary | `activity_daily_summary` | 1,110 | [Features/Monitoring/ActivityMonitoring/Entities/ActivityDailySummary.cs](../../../src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivityDailySummary.cs) |
| ActivityRawBuffer | `activity_raw_buffer` | 0 | [Features/Monitoring/ActivityMonitoring/Entities/ActivityRawBuffer.cs](../../../src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivityRawBuffer.cs) |
| ActivitySnapshot | `activity_snapshots` | 13,320 | [Features/Monitoring/ActivityMonitoring/Entities/ActivitySnapshot.cs](../../../src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivitySnapshot.cs) |
| AgentCommand | `agent_commands` | 0 | [Features/Monitoring/Screenshots/Entities/AgentCommand.cs](../../../src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/AgentCommand.cs) |
| AppUsageSnapshot | `app_usage_snapshots` | 13,320 | [Features/Monitoring/AppUsage/Entities/AppUsageSnapshot.cs](../../../src/ONEVO.Domain/Features/Monitoring/AppUsage/Entities/AppUsageSnapshot.cs) |
| BiometricEnrollmentAttempt | `biometric_enrollment_attempts` | 0 | [Features/Monitoring/Biometrics/Entities/BiometricEnrollmentAttempt.cs](../../../src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricEnrollmentAttempt.cs) |
| BiometricProfile | `biometric_profiles` | 0 | [Features/Monitoring/Biometrics/Entities/BiometricProfile.cs](../../../src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricProfile.cs) |
| DeviceChangeRequest | `device_change_requests` | 0 | [Features/Monitoring/TrayActivation/Entities/DeviceChangeRequest.cs](../../../src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/DeviceChangeRequest.cs) |
| DeviceStateSnapshot | `device_state_snapshots` | 13,320 | [Features/Monitoring/DeviceState/Entities/DeviceStateSnapshot.cs](../../../src/ONEVO.Domain/Features/Monitoring/DeviceState/Entities/DeviceStateSnapshot.cs) |
| EmployeeCheckIn | `employee_check_ins` | 0 | [Features/Monitoring/CheckIn/Entities/EmployeeCheckIn.cs](../../../src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/EmployeeCheckIn.cs) |
| EmployeeMonitoringOverride | `employee_monitoring_overrides` | 0 | [Features/Monitoring/Settings/Entities/EmployeeMonitoringOverride.cs](../../../src/ONEVO.Domain/Features/Monitoring/Settings/Entities/EmployeeMonitoringOverride.cs) |
| EmployeeWorkSession | `employee_work_sessions` | 0 | [Features/Monitoring/WorkSessions/Entities/EmployeeWorkSession.cs](../../../src/ONEVO.Domain/Features/Monitoring/WorkSessions/Entities/EmployeeWorkSession.cs) |
| Exception | `exceptions` | 52 | [Features/Monitoring/Exceptions/Entities/Exception.cs](../../../src/ONEVO.Domain/Features/Monitoring/Exceptions/Entities/Exception.cs) |
| FaceVerificationAttempt | `face_verification_attempts` | 2,250 | [Features/Monitoring/CheckIn/Entities/FaceVerificationAttempt.cs](../../../src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/FaceVerificationAttempt.cs) |
| InactivityCaptureAttempt | `inactivity_capture_attempts` | 0 | [Features/Monitoring/Screenshots/Entities/InactivityCaptureAttempt.cs](../../../src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/InactivityCaptureAttempt.cs) |
| MeetingSignal | `meeting_signals` | 0 | [Features/Monitoring/Meetings/Entities/MeetingSignal.cs](../../../src/ONEVO.Domain/Features/Monitoring/Meetings/Entities/MeetingSignal.cs) |
| MonitoringEvidenceAsset | `monitoring_evidence_assets` | 0 | [Features/Monitoring/Screenshots/Entities/MonitoringEvidenceAsset.cs](../../../src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/MonitoringEvidenceAsset.cs) |
| MonitoringFaceScan | `monitoring_face_scans` | 0 | [Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs](../../../src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs) |
| MonitoringFeatureToggles | `monitoring_feature_toggles` | 0 | [Features/Monitoring/Settings/Entities/MonitoringFeatureToggles.cs](../../../src/ONEVO.Domain/Features/Monitoring/Settings/Entities/MonitoringFeatureToggles.cs) |
| MonitoringPermissionConsent | `monitoring_permission_consents` | 0 | [Features/Monitoring/PermissionConsent/Entities/MonitoringPermissionConsent.cs](../../../src/ONEVO.Domain/Features/Monitoring/PermissionConsent/Entities/MonitoringPermissionConsent.cs) |
| MonitoringPolicyOverride | `monitoring_policy_overrides` | 0 | [Features/Monitoring/Settings/Entities/MonitoringPolicyOverride.cs](../../../src/ONEVO.Domain/Features/Monitoring/Settings/Entities/MonitoringPolicyOverride.cs) |
| Notification | `monitoring_notifications` | 52 | [Features/Monitoring/Notifications/Entities/Notification.cs](../../../src/ONEVO.Domain/Features/Monitoring/Notifications/Entities/Notification.cs) |
| TrayActivationCode | `tray_activation_codes` | 0 | [Features/Monitoring/TrayActivation/Entities/TrayActivationCode.cs](../../../src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/TrayActivationCode.cs) |
| TrayDeviceAuthorization | `tray_device_authorizations` | 0 | [Features/Monitoring/TrayActivation/Entities/TrayDeviceAuthorization.cs](../../../src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/TrayDeviceAuthorization.cs) |
| TrayDeviceRefreshToken | `tray_device_refresh_tokens` | 0 | [Features/Monitoring/TrayActivation/Entities/TrayDeviceRefreshToken.cs](../../../src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/TrayDeviceRefreshToken.cs) |
| TrayDeviceRegistration | `tray_device_registrations` | 0 | [Features/Monitoring/TrayActivation/Entities/TrayDeviceRegistration.cs](../../../src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/TrayDeviceRegistration.cs) |

## OrgStructure

| Entity | Table | Rows | File |
|---|---|---:|---|
| Department | `departments` | 8 | [Features/OrgStructure/Department/Entities/Department.cs](../../../src/ONEVO.Domain/Features/OrgStructure/Department/Entities/Department.cs) |
| LegalEntity | `legal_entities` | 4 | [Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs](../../../src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs) |
| ManagementCoverageRecord | `management_coverage_records` | 23 | [Features/OrgStructure/Position/Entities/ManagementCoverageRecord.cs](../../../src/ONEVO.Domain/Features/OrgStructure/Position/Entities/ManagementCoverageRecord.cs) |
| Position | `positions` | 16 | [Features/OrgStructure/Position/Entities/Position.cs](../../../src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs) |
| PositionAccessTemplate | `position_access_templates` | 0 | [Features/OrgStructure/Position/Entities/PositionAccessTemplate.cs](../../../src/ONEVO.Domain/Features/OrgStructure/Position/Entities/PositionAccessTemplate.cs) |
| PositionReportingHistory | `position_reporting_history` | 16 | [Features/OrgStructure/Position/Entities/PositionReportingHistory.cs](../../../src/ONEVO.Domain/Features/OrgStructure/Position/Entities/PositionReportingHistory.cs) |

## SharedPlatform

| Entity | Table | Rows | File |
|---|---|---:|---|
| BillingAuditLog | `billing_audit_logs` | 0 | [Features/SharedPlatform/Billing/Entities/BillingAuditLog.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Billing/Entities/BillingAuditLog.cs) |
| IdempotencyRecord | `idempotency_records` | 0 | [Features/SharedPlatform/Idempotency/Entities/IdempotencyRecord.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Idempotency/Entities/IdempotencyRecord.cs) |
| ModuleCatalogItem | `module_catalog` | 27 | [Features/SharedPlatform/ModuleCatalog/Entities/ModuleCatalogItem.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/ModuleCatalog/Entities/ModuleCatalogItem.cs) |
| ModuleCatalogPriceHistory | `module_catalog_price_history` | 0 | [Features/SharedPlatform/ModuleCatalog/Entities/ModuleCatalogPriceHistory.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/ModuleCatalog/Entities/ModuleCatalogPriceHistory.cs) |
| ModuleFeature | `module_features` | 64 | [Features/SharedPlatform/ModuleCatalog/Entities/ModuleFeature.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/ModuleCatalog/Entities/ModuleFeature.cs) |
| ModulePermissionOwnership | `module_permission_ownership` | 37 | [Features/SharedPlatform/ModuleCatalog/Entities/ModulePermissionOwnership.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/ModuleCatalog/Entities/ModulePermissionOwnership.cs) |
| Notification | `notifications` | 34 | [Features/SharedPlatform/Notifications/Entities/Notification.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/Notification.cs) |
| NotificationDelivery | `notification_deliveries` | 34 | [Features/SharedPlatform/Notifications/Entities/NotificationDelivery.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/NotificationDelivery.cs) |
| NotificationTemplate | `notification_templates` | 56 | [Features/SharedPlatform/Notifications/Entities/NotificationTemplate.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/NotificationTemplate.cs) |
| NotificationTemplateVersion | `notification_template_versions` | 56 | [Features/SharedPlatform/Notifications/Entities/NotificationTemplateVersion.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/NotificationTemplateVersion.cs) |
| OutboxMessage | `outbox_messages` | 25 | [Features/SharedPlatform/Outbox/Entities/OutboxMessage.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Outbox/Entities/OutboxMessage.cs) |
| PaymentGatewayConfig | `payment_gateway_configs` | 0 | [Features/SharedPlatform/PaymentGateway/Entities/PaymentGatewayConfig.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/PaymentGateway/Entities/PaymentGatewayConfig.cs) |
| PaymentGatewayCountryRoute | `payment_gateway_country_routes` | 0 | [Features/SharedPlatform/PaymentGateway/Entities/PaymentGatewayCountryRoute.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/PaymentGateway/Entities/PaymentGatewayCountryRoute.cs) |
| PaymentGatewayCredential | `payment_gateway_credentials` | 0 | [Features/SharedPlatform/PaymentGateway/Entities/PaymentGatewayCredential.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/PaymentGateway/Entities/PaymentGatewayCredential.cs) |
| SubscriptionInvoice | `subscription_invoices` | 0 | [Features/SharedPlatform/Billing/Entities/SubscriptionInvoice.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Billing/Entities/SubscriptionInvoice.cs) |
| SubscriptionPlan | `subscription_plans` | 1 | [Features/SharedPlatform/Subscription/Entities/SubscriptionPlan.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Subscription/Entities/SubscriptionPlan.cs) |
| TenantIntegrationCredential | `tenant_integration_credentials` | 1 | [Features/SharedPlatform/TenantIntegrations/Entities/TenantIntegrationCredential.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/TenantIntegrations/Entities/TenantIntegrationCredential.cs) |
| TenantNotificationRule | `tenant_notification_rules` | 0 | [Features/SharedPlatform/Notifications/Entities/TenantNotificationRule.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Notifications/Entities/TenantNotificationRule.cs) |
| TenantOneTimeCharge | `tenant_one_time_charges` | 0 | [Features/SharedPlatform/Billing/Entities/TenantOneTimeCharge.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Billing/Entities/TenantOneTimeCharge.cs) |
| TenantProvisioningState | `tenant_provisioning_states` | 0 | [Features/SharedPlatform/Provisioning/Entities/TenantProvisioningState.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Provisioning/Entities/TenantProvisioningState.cs) |
| TenantSetupSelection | `tenant_setup_selections` | 0 | [Features/SharedPlatform/Provisioning/Entities/TenantSetupSelection.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Provisioning/Entities/TenantSetupSelection.cs) |
| TenantSubscription | `tenant_subscriptions` | 2 | [Features/SharedPlatform/Subscription/Entities/TenantSubscription.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/Subscription/Entities/TenantSubscription.cs) |
| UserIntegrationConnection | `user_integration_connections` | 0 | [Features/SharedPlatform/TenantIntegrations/Entities/UserIntegrationConnection.cs](../../../src/ONEVO.Domain/Features/SharedPlatform/TenantIntegrations/Entities/UserIntegrationConnection.cs) |

## Storage

| Entity | Table | Rows | File |
|---|---|---:|---|
| EntityAsset | `entity_assets` | 0 | [Features/Storage/EntityAssets/Entities/EntityAsset.cs](../../../src/ONEVO.Domain/Features/Storage/EntityAssets/Entities/EntityAsset.cs) |
| FileRecord | `file_records` | 0 | [Features/Storage/File/Entities/FileRecord.cs](../../../src/ONEVO.Domain/Features/Storage/File/Entities/FileRecord.cs) |
| FileUploadReservation | `file_upload_reservations` | 0 | [Features/Storage/File/Entities/FileUploadReservation.cs](../../../src/ONEVO.Domain/Features/Storage/File/Entities/FileUploadReservation.cs) |
| TenantStorageStats | `tenant_storage_stats` | 0 | [Features/Storage/Quota/Entities/TenantStorageStats.cs](../../../src/ONEVO.Domain/Features/Storage/Quota/Entities/TenantStorageStats.cs) |

## TimeAttendance

| Entity | Table | Rows | File |
|---|---|---:|---|
| AttendanceAlert | `attendance_alerts` | 177 | [Features/TimeAttendance/Alerts/AttendanceAlert.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Alerts/AttendanceAlert.cs) |
| AttendanceAlertDeduction | `attendance_alert_deductions` | 0 | [Features/TimeAttendance/Alerts/AttendanceAlertDeduction.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Alerts/AttendanceAlertDeduction.cs) |
| AttendanceCorrection | `attendance_corrections` | 0 | [Features/TimeAttendance/Entities/AttendanceCorrection.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceCorrection.cs) |
| AttendanceRecord | `attendance_records` | 1,525 | [Features/TimeAttendance/Entities/AttendanceRecord.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs) |
| BreakRecord | `break_records` | 1,066 | [Features/TimeAttendance/Entities/AttendanceRecord.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs) |
| ClockInLateDeductionRule | `clock_in_late_deduction_rules` | 0 | [Features/TimeAttendance/Entities/ClockInLateDeductionRule.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/ClockInLateDeductionRule.cs) |
| ClockInPolicy | `clock_in_policies` | 1 | [Features/TimeAttendance/Entities/ClockInPolicy.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/ClockInPolicy.cs) |
| DailyWorkLocationConfirmation | `daily_work_location_confirmations` | 0 | [Features/TimeAttendance/Entities/DailyWorkLocationConfirmation.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/DailyWorkLocationConfirmation.cs) |
| EmployeeWorkLocation | `employee_work_locations` | 0 | [Features/TimeAttendance/Entities/EmployeeWorkLocation.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/EmployeeWorkLocation.cs) |
| LocationChangeRequest | `location_change_requests` | 0 | [Features/TimeAttendance/Entities/LocationChangeRequest.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/LocationChangeRequest.cs) |
| PresenceSession | `presence_sessions` | 1,066 | [Features/TimeAttendance/Entities/AttendanceRecord.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs) |
| WorkAreaChangeRequest | `work_area_change_requests` | 0 | [Features/TimeAttendance/Entities/WorkAreaChangeRequest.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/WorkAreaChangeRequest.cs) |
| WorkMode | `tenant_work_modes` | 12 | [Features/TimeAttendance/Entities/WorkMode.cs](../../../src/ONEVO.Domain/Features/TimeAttendance/Entities/WorkMode.cs) |

## WorkManagement

| Entity | Table | Rows | File |
|---|---|---:|---|
| Label | `labels` | 0 | [Features/WorkManagement/Labels/Entities/Label.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Labels/Entities/Label.cs) |
| ModuleHourSummary | `wm_module_hour_summaries` | 3 | [Features/WorkManagement/Objectives/Entities/ModuleHourSummary.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/ModuleHourSummary.cs) |
| MonitorAlert | `wm_monitor_alerts` | 22 | [Features/WorkManagement/Monitoring/Entities/MonitorAlert.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Monitoring/Entities/MonitorAlert.cs) |
| Objective | `objectives` | 84 | [Features/WorkManagement/Objectives/Entities/Objective.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs) |
| Project | `projects` | 11 | [Features/WorkManagement/Projects/Entities/Project.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/Project.cs) |
| ProjectCategory | `project_categories` | 10 | [Features/WorkManagement/Projects/Entities/ProjectCategory.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/ProjectCategory.cs) |
| ProjectMember | `project_members` | 110 | [Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs](../../../src/ONEVO.Domain/Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs) |
| ProjectMemberInvitation | `project_member_invitations` | 0 | [Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs](../../../src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs) |
| ProjectVersion | `versions` | 6 | [Features/WorkManagement/Versions/Entities/ProjectVersion.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/ProjectVersion.cs) |
| ReleaseCalendarEntry | `release_calendar` | 6 | [Features/WorkManagement/ReleaseCalendar/Entities/ReleaseCalendarEntry.cs](../../../src/ONEVO.Domain/Features/WorkManagement/ReleaseCalendar/Entities/ReleaseCalendarEntry.cs) |
| Sprint | `sprints` | 0 | [Features/WorkManagement/Sprints/Entities/Sprint.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs) |
| SprintActivityLog | `sprint_activity_logs` | 0 | [Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs) |
| TaskAssignment | `task_assignments` | 158 | [Features/WorkManagement/Tasks/Entities/TaskAssignment.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskAssignment.cs) |
| TaskCategory | `task_categories` | 20 | [Features/WorkManagement/Tasks/Entities/TaskCategory.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs) |
| TaskClockingSession | `task_clocking_sessions` | 0 | [Features/WorkManagement/Tasks/Entities/TaskClockingSession.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskClockingSession.cs) |
| TaskComment | `task_comments` | 0 | [Features/WorkManagement/Tasks/Entities/TaskComment.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskComment.cs) |
| TaskCommentLog | `task_comment_logs` | 0 | [Features/WorkManagement/Tasks/Entities/TaskCommentLog.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentLog.cs) |
| TaskCommentReaction | `task_comment_reactions` | 0 | [Features/WorkManagement/Tasks/Entities/TaskCommentReaction.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentReaction.cs) |
| TaskDraft | `task_drafts` | 0 | [Features/WorkManagement/Tasks/Entities/TaskDraft.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskDraft.cs) |
| TaskEditLog | `task_edit_logs` | 0 | [Features/WorkManagement/Tasks/Entities/TaskEditLog.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditLog.cs) |
| TaskPercentageLog | `task_percentage_logs` | 0 | [Features/WorkManagement/Tasks/Entities/TaskPercentageLog.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskPercentageLog.cs) |
| TaskStatus | `task_statuses` | 20 | [Features/WorkManagement/Tasks/Entities/TaskStatus.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs) |
| TaskStatusChangeLog | `task_status_change_logs` | 0 | [Features/WorkManagement/Tasks/Entities/TaskStatusChangeLog.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatusChangeLog.cs) |
| VersionStatus | `version_statuses` | 3 | [Features/WorkManagement/Versions/Entities/VersionStatus.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs) |
| WorkApprovalRequest | `wm_approval_requests` | 20 | [Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs) |
| WorkNotificationLog | `wm_notification_log` | 24 | [Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs) |
| WorkTask | `tasks` | 158 | [Features/WorkManagement/Tasks/Entities/WorkTask.cs](../../../src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs) |
