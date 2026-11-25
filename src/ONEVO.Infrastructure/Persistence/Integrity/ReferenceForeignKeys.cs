using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Dashboard.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using Cal = ONEVO.Domain.Features.Calendar.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;
using MonitoringNotification = ONEVO.Domain.Features.Monitoring.Notifications.Entities.Notification;
using SharedPlatformNotification = ONEVO.Domain.Features.SharedPlatform.Notifications.Entities.Notification;
using WorkTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Integrity;

/// <summary>
/// Foreign keys for id columns that had none (task 39). They have no navigation properties, so the domain
/// model stays as it is. Every key is NO ACTION (Restrict where the table already uses it; the same DDL on SQL Server): none of these parents is hard-deleted by the application,
/// people are never deleted with their history, and NO ACTION cannot create multiple cascade paths.
/// The one hard-deleted parent with keys here, leave_policy_versions, is removed only after
/// ILeavePolicyRepository.IsVersionInUseAsync finds no row pointing at it (task 39b). Roles are soft-deleted.
/// Columns still without a key are listed, with a reason, in
/// tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt.
/// </summary>
public static class ReferenceForeignKeys
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ApplyCoreHr(modelBuilder);
        ApplyLeave(modelBuilder);
        ApplyTimeAttendance(modelBuilder);
        ApplyWorkManagement(modelBuilder);
        ApplyMonitoring(modelBuilder);
        ApplyAuth(modelBuilder);
        ApplyCalendar(modelBuilder);
        ApplyConversations(modelBuilder);
        ApplyInbox(modelBuilder);
        ApplySharedPlatform(modelBuilder);
        ApplyInfrastructureModule(modelBuilder);
        ApplyDashboard(modelBuilder);
        ApplyAuditColumns(modelBuilder);
        ApplyTenantColumns(modelBuilder);
    }

    /// <summary>
    /// Task 39d: every <c>tenant_id</c> column gets a NO ACTION key to <c>tenants</c>. A tenant is cancelled, never
    /// deleted, so no cascade is needed. A nullable <c>tenant_id</c> (the PlatformShared RLS tables, such as
    /// <c>outbox_messages</c>) stays nullable; the key checks only values that are not null.
    ///
    /// RLS predicates do not apply to a key check, so the keys hold under every predicate, TenantStrict included.
    /// The loop matches the <c>TenantId</c> property (not <see cref="ITenantOwnedEntity"/>, whose
    /// <c>Guid TenantId</c> leaves out the nullable tables), skips a column that already has its key (the Auth
    /// challenges, billing, support and tenancy tables configure theirs), and covers tables added later.
    /// </summary>
    private static void ApplyTenantColumns(ModelBuilder b)
    {
        var tenantType = b.Model.FindEntityType(typeof(Tenant))!;
        foreach (var entityType in b.Model.GetEntityTypes().ToList())
        {
            if (entityType == tenantType || entityType.IsOwned() || entityType.BaseType is not null
                || entityType.GetTableName() is null)
            {
                continue;
            }

            var tenantId = entityType.FindProperty("TenantId");
            if (tenantId is null || (tenantId.ClrType != typeof(Guid) && tenantId.ClrType != typeof(Guid?)))
            {
                continue;
            }

            var hasKey = entityType.GetForeignKeys().Any(fk =>
                fk.PrincipalEntityType == tenantType && fk.Properties.Count == 1 && fk.Properties[0] == tenantId);
            if (hasKey)
            {
                continue;
            }

            b.Entity(entityType.ClrType)
                .HasOne(typeof(Tenant))
                .WithMany()
                .HasForeignKey("TenantId")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    /// <summary>
    /// Task 39c: <c>created_by_id</c> audit columns.
    ///
    /// Every <see cref="BaseEntity"/> table gets a key to <c>users</c>: <see cref="AuditableEntityInterceptor"/>
    /// writes the signed-in tenant user or <c>NULL</c> (seeders, jobs, anonymous paths and platform admins), never
    /// <c>Guid.Empty</c>. The loop also covers <see cref="BaseEntity"/> tables added later, so a new table cannot
    /// miss the key.
    ///
    /// Tables with their own <c>CreatedById</c> are listed one by one: tenant tables written by a tenant user point
    /// at <c>users</c> (project <c>calendar_events</c>, which stores an employee, at <c>employees</c>), platform tables written by a platform admin point at <c>platform_users</c> (like
    /// <c>configuration_templates</c>). <c>invitation_tokens.created_by_id</c> holds both (a platform admin for the
    /// tenant owner's invitation, a tenant user otherwise), so it has no key and is on the allow-list.
    /// </summary>
    private static void ApplyAuditColumns(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes().ToList())
        {
            if (entityType.IsOwned() || entityType.BaseType is not null
                || !typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)
                || entityType.GetTableName() is null
                || entityType.FindProperty(nameof(BaseEntity.CreatedById)) is null)
            {
                continue;
            }

            b.Entity(entityType.ClrType)
                .HasOne(typeof(User))
                .WithMany()
                .HasForeignKey(nameof(BaseEntity.CreatedById))
                .OnDelete(DeleteBehavior.NoAction);
        }

        Fk<ClockInPolicy, User>(b, x => x.CreatedById);
        Fk<MeetingSeries, User>(b, x => x.CreatedById);
        // Project calendar events store the creating employee (CreateCalendarEventCommandHandler), not a user.
        Fk<CalendarEvent, Employee>(b, x => x.CreatedById);

        Fk<TenantSubscription, PlatformUser>(b, x => x.CreatedById);
        Fk<GlobalApplication, PlatformUser>(b, x => x.CreatedById);
        Fk<PaymentGatewayConfig, PlatformUser>(b, x => x.CreatedById);
        Fk<PaymentGatewayCountryRoute, PlatformUser>(b, x => x.CreatedById);
        Fk<TrayAppRelease, PlatformUser>(b, x => x.CreatedById);
    }

    // Task 39a: core HR and org structure.
    private static void ApplyCoreHr(ModelBuilder b)
    {
        Fk<Employee, Department>(b, x => x.DepartmentId);
        Fk<Employee, LegalEntity>(b, x => x.LegalEntityId);
        Fk<EmployeeHierarchyClosure, Employee>(b, x => x.AncestorEmployeeId);
        Fk<EmployeeHierarchyClosure, Employee>(b, x => x.DescendantEmployeeId);
        Fk<PositionAssignment, Employee>(b, x => x.ReportsToEmployeeId);
        // Restrict, like the other keys of this table (EfPositionRepositoryTests).
        Fk<ManagementCoverageRecord, Employee>(b, x => x.ResponsibleEmployeeId, DeleteBehavior.Restrict);
        Fk<ReportingManagerChange, LegalEntity>(b, x => x.LegalEntityId);
        Fk<AccessGrantRequest, PositionAssignment>(b, x => x.ReservedPositionAssignmentId);
        Fk<OnboardingDraft, Employee>(b, x => x.ReportsToEmployeeId);

        // Task 39b. Onboarding, offboarding and position history write ICurrentUser.UserId, so those keys go to users.
        Fk<Employee, User>(b, x => x.UserId);
        Fk<EmployeeHierarchyClosure, PositionAssignment>(b, x => x.SourcePositionAssignmentId);
        Fk<BulkOnboardingBatch, User>(b, x => x.CreatedByUserId);
        Fk<BulkOnboardingBatch, WorkMode>(b, x => x.DefaultWorkModeId);
        Fk<BulkOnboardingBatch, ChecklistTemplate>(b, x => x.DefaultChecklistTemplateId);
        Fk<BulkOnboardingBatchRow, Department>(b, x => x.ResolvedDepartmentId);
        Fk<BulkOnboardingBatchRow, Position>(b, x => x.ResolvedPositionId);
        Fk<BulkOnboardingBatchRow, Employee>(b, x => x.ResolvedReportsToEmployeeId);
        Fk<BulkOnboardingBatchRow, ChecklistTemplate>(b, x => x.ResolvedTemplateId);
        Fk<BulkOnboardingBatchRow, WorkMode>(b, x => x.ResolvedWorkModeId);
        Fk<OnboardingDraft, FileRecord>(b, x => x.AvatarFileId);
        Fk<OnboardingDraft, ChecklistTemplate>(b, x => x.SelectedTemplateId);
        Fk<OnboardingDraft, User>(b, x => x.StartedById);
        Fk<OffboardingRecord, User>(b, x => x.InitiatedById);
        Fk<OffboardingTaskBypassRequest, User>(b, x => x.RequestedById);
        Fk<OffboardingTaskBypassRequest, User>(b, x => x.ApproverId);
        // Restrict, like the other keys of this table (EfPositionRepositoryTests).
        Fk<PositionReportingHistory, User>(b, x => x.CreatedByUserId, DeleteBehavior.Restrict);
        // Roles are soft-deleted (SoftDeleteInterceptor), so the row a position points at stays.
        Fk<Position, Role>(b, x => x.DefaultRoleId);
    }

    // Task 39a: leave.
    private static void ApplyLeave(ModelBuilder b)
    {
        Fk<LeaveBalanceAudit, LeavePolicy>(b, x => x.LeavePolicyId);
        Fk<LeaveEntitlement, LeavePolicy>(b, x => x.LeavePolicyId);

        // Task 39b. A future policy version is hard-deleted (DeleteLeavePolicyVersion, and AddLeavePolicyVersion on
        // the same date); both now refuse when a row below points at the version.
        Fk<LeaveRequest, LeavePolicyVersion>(b, x => x.LeavePolicyVersionId);
        Fk<LeaveEntitlement, LeavePolicyVersion>(b, x => x.LeavePolicyVersionId);
        Fk<LeaveBalanceAudit, LeavePolicyVersion>(b, x => x.LeavePolicyVersionId);
        // An employee id: the leave inbox adds it to the conversation's people.
        Fk<LeaveRequestApprover, Employee>(b, x => x.DelegatedFromApproverId);
    }

    // Task 39a: time and attendance. attendance_alert_deductions.alert_id has no key: the alert materializer
    // deletes an alert whose day no longer qualifies and keeps its refunded deduction (see missing-foreign-keys.txt).
    private static void ApplyTimeAttendance(ModelBuilder b)
    {
        Fk<AttendanceRecord, Employee>(b, x => x.EmployeeId);
        Fk<AttendanceRecord, WorkMode>(b, x => x.ExpectedWorkModeId);
        Fk<BreakRecord, Employee>(b, x => x.EmployeeId);
        Fk<PresenceSession, Employee>(b, x => x.EmployeeId);
        Fk<AttendanceAlert, Employee>(b, x => x.EmployeeId);
        Fk<AttendanceAlert, LegalEntity>(b, x => x.LegalEntityId);
        Fk<AttendanceAlert, Employee>(b, x => x.ResolvedByEmployeeId);
        Fk<AttendanceAlert, LeaveRequest>(b, x => x.ConvertedLeaveRequestId);
        Fk<AttendanceAlertDeduction, Employee>(b, x => x.EmployeeId);
        Fk<AttendanceAlertDeduction, LeaveType>(b, x => x.LeaveTypeId);
        Fk<AttendanceAlertDeduction, LeaveBalanceAudit>(b, x => x.ChargeAuditId);
        Fk<AttendanceAlertDeduction, LeaveBalanceAudit>(b, x => x.RefundAuditId);
        Fk<ClockInLateDeductionRule, LeaveType>(b, x => x.TimeOffTypeId);
    }

    // Task 39a: work management. Tasks, sprints, task categories and task statuses are hard-deleted,
    // so no key points at them.
    private static void ApplyWorkManagement(ModelBuilder b)
    {
        Fk<WorkTask, Project>(b, x => x.ProjectId);
        Fk<WorkTask, Objective>(b, x => x.ObjectiveId);
        Fk<Objective, Employee>(b, x => x.OwnerId);
        Fk<Objective, Employee>(b, x => x.ReportingManagerId);
        Fk<Project, ProjectCategory>(b, x => x.CategoryId);
        Fk<Project, Employee>(b, x => x.LeadId);
        Fk<ProjectMember, Employee>(b, x => x.EmployeeId);
        Fk<Sprint, Project>(b, x => x.ProjectId);
        Fk<TaskCategory, Project>(b, x => x.ProjectId);
        Fk<WorkTaskStatus, Project>(b, x => x.ProjectId);
        Fk<WorkTaskStatus, Objective>(b, x => x.ObjectiveId);
        Fk<TaskAssignment, Employee>(b, x => x.EmployeeId);
        Fk<TaskComment, Employee>(b, x => x.EmployeeId);
        Fk<TaskCommentLog, Employee>(b, x => x.EmployeeId);
        Fk<TaskCommentReaction, Employee>(b, x => x.EmployeeId);
        Fk<TaskEditLog, Employee>(b, x => x.EmployeeId);
        Fk<TaskPercentageLog, Employee>(b, x => x.EmployeeId);
        Fk<TaskStatusChangeLog, Employee>(b, x => x.EmployeeId);
        Fk<TaskClockingSession, Employee>(b, x => x.EmployeeId);
        Fk<SprintActivityLog, Employee>(b, x => x.EmployeeId);
        Fk<WorkApprovalRequest, Project>(b, x => x.ProjectId);
        Fk<WorkApprovalRequest, Employee>(b, x => x.ApproverEmployeeId);
        Fk<WorkApprovalRequest, Employee>(b, x => x.RequestedByEmployeeId);
        Fk<WorkApprovalRequest, Employee>(b, x => x.DecidedByEmployeeId);
        Fk<WorkApprovalRequest, Employee>(b, x => x.RevertedByEmployeeId);
        Fk<WorkNotificationLog, Project>(b, x => x.ProjectId);
        Fk<WorkNotificationLog, Employee>(b, x => x.RecipientEmployeeId);
        Fk<WorkNotificationLog, Employee>(b, x => x.ActorEmployeeId);
        Fk<WorkNotificationLog, WorkApprovalRequest>(b, x => x.ApprovalRequestId);
        Fk<MonitorAlert, Project>(b, x => x.ProjectId);
        Fk<MonitorAlert, Employee>(b, x => x.SubjectEmployeeId);
        Fk<ModuleHourSummary, Project>(b, x => x.ProjectId);
        Fk<TaskRateSetting, LegalEntity>(b, x => x.LegalEntityId);
        Fk<TaskRateSetting, Employee>(b, x => x.UpdatedByEmployeeId);
        Fk<MeetingSeries, Project>(b, x => x.ProjectId);

        // Task 39b. Objectives and approval requests are never hard-deleted.
        Fk<Objective, Objective>(b, x => x.CreatorPositionObjectiveId);
        Fk<Sprint, Objective>(b, x => x.CreatorPositionObjectiveId);
        Fk<WorkTask, Objective>(b, x => x.CreatorPositionObjectiveId);
        Fk<WorkApprovalRequest, Objective>(b, x => x.PositionObjectiveId);
        Fk<MeetingSeries, WorkApprovalRequest>(b, x => x.ApprovalRequestId);
        Fk<TaskEditLog, WorkApprovalRequest>(b, x => x.EditRequestId);
        Fk<MeetingActionItem, Employee>(b, x => x.CreatedByEmployeeId);
        Fk<MeetingOccurrenceSuppression, Employee>(b, x => x.CreatedByEmployeeId);
        Fk<CalendarEventActivityLog, Employee>(b, x => x.PerformedById);
        Fk<CalendarEvent, Employee>(b, x => x.ArchivedById);
        Fk<ProjectMemberInvitation, Employee>(b, x => x.InvitedEmployeeId);
        Fk<ProjectMemberInvitation, Employee>(b, x => x.InvitedById);
        Fk<ReleaseCalendarEntry, User>(b, x => x.RecipientUserId);
        Fk<TaskAssignment, User>(b, x => x.UserId);
        // An employee id in the application; the demo seeders wrote the owner's user id until task 39b.
        Fk<TaskAssignment, Employee>(b, x => x.AssignedById);
        Fk<TaskDraft, User>(b, x => x.OwnerUserId);
        Fk<TaskDraft, Project>(b, x => x.ProjectId);
    }

    // Task 39e: Monitoring.
    private static void ApplyMonitoring(ModelBuilder b)
    {
        Fk<ActivityDailySummary, Employee>(b, x => x.EmployeeId);
        Fk<ActivityRawBuffer, TrayDeviceRegistration>(b, x => x.AgentDeviceId);
        Fk<ActivitySnapshot, TrayDeviceRegistration>(b, x => x.AgentDeviceId);
        Fk<ActivitySnapshot, Employee>(b, x => x.EmployeeId);
        Fk<AgentCommand, User>(b, x => x.RequestedById);
        Fk<AppUsageSnapshot, TrayDeviceRegistration>(b, x => x.AgentDeviceId);
        Fk<AppUsageSnapshot, Employee>(b, x => x.EmployeeId);
        Fk<BiometricEnrollmentAttempt, TrayDeviceRegistration>(b, x => x.AgentDeviceId);
        Fk<BiometricEnrollmentAttempt, Employee>(b, x => x.EmployeeId);
        Fk<BiometricProfile, Employee>(b, x => x.EmployeeId);
        Fk<BiometricProfile, FileRecord>(b, x => x.LeftReferencePhotoFileId);
        Fk<BiometricProfile, FileRecord>(b, x => x.ReferencePhotoFileId);
        Fk<BiometricProfile, FileRecord>(b, x => x.RightReferencePhotoFileId);
        Fk<DeviceChangeRequest, Employee>(b, x => x.EmployeeId);
        Fk<DeviceChangeRequest, LegalEntity>(b, x => x.LegalEntityId);
        Fk<DeviceStateSnapshot, TrayDeviceRegistration>(b, x => x.AgentDeviceId);
        Fk<DeviceStateSnapshot, Employee>(b, x => x.EmployeeId);
        Fk<EmployeeCheckIn, TrayDeviceRegistration>(b, x => x.DeviceRegistrationId);
        Fk<EmployeeCheckIn, User>(b, x => x.UserId);
        Fk<EmployeeMonitoringOverride, Employee>(b, x => x.EmployeeId);
        Fk<EmployeeMonitoringOverride, User>(b, x => x.SetById);
        Fk<EmployeeWorkSession, TrayDeviceRegistration>(b, x => x.DeviceRegistrationId);
        Fk<EmployeeWorkSession, User>(b, x => x.UserId);
        Fk<MonitoringException, User>(b, x => x.AcknowledgedById);
        Fk<MonitoringException, Employee>(b, x => x.EmployeeId);
        Fk<MonitoringException, User>(b, x => x.EscalatedById);
        Fk<MonitoringException, User>(b, x => x.ResolvedById);
        Fk<FaceVerificationAttempt, Employee>(b, x => x.EmployeeId);
        Fk<FaceVerificationAttempt, FileRecord>(b, x => x.PhotoFileId);
        Fk<InactivityCaptureAttempt, Employee>(b, x => x.EmployeeId);
        Fk<InactivityCaptureAttempt, EmployeeWorkSession>(b, x => x.WorkSessionId);
        Fk<MeetingSignal, TrayDeviceRegistration>(b, x => x.AgentDeviceId);
        Fk<MeetingSignal, Employee>(b, x => x.EmployeeId);
        Fk<MonitoringEvidenceAsset, Employee>(b, x => x.EmployeeId);
        Fk<MonitoringFaceScan, EmployeeCheckIn>(b, x => x.CheckInId);
        Fk<MonitoringNotification, Employee>(b, x => x.EmployeeId);
        Fk<MonitoringPermissionConsent, TrayDeviceRegistration>(b, x => x.DeviceRegistrationId);
        Fk<MonitoringPermissionConsent, Employee>(b, x => x.EmployeeId);
        Fk<MonitoringPolicyOverride, User>(b, x => x.SetById);
        Fk<TrayActivationCode, User>(b, x => x.UserId);
        Fk<TrayDeviceAuthorization, Tenant>(b, x => x.ApprovedTenantId);
        Fk<TrayDeviceAuthorization, User>(b, x => x.ApprovedUserId);
        Fk<TrayDeviceRefreshToken, TrayDeviceRegistration>(b, x => x.DeviceRegistrationId);
        Fk<TrayDeviceRefreshToken, User>(b, x => x.UserId);
        Fk<TrayDeviceRegistration, User>(b, x => x.UserId);
    }

    // Task 39e: Auth.
    private static void ApplyAuth(ModelBuilder b)
    {
        Fk<AuditLog, User>(b, x => x.UserId);
        Fk<InvitationToken, Employee>(b, x => x.EmployeeId);
        Fk<InvitationToken, LegalEntity>(b, x => x.LegalEntityId);
        Fk<InvitationToken, OnboardingDraft>(b, x => x.OnboardingDraftId);
        Fk<InvitationToken, PositionAssignment>(b, x => x.PositionAssignmentId);
        Fk<InvitationToken, Position>(b, x => x.PositionId);
        Fk<InvitationToken, User>(b, x => x.RevokedById);
        Fk<InvitationToken, Role>(b, x => x.RoleId);
        Fk<InvitationToken, User>(b, x => x.UserId);
        Fk<LegalAcceptanceRecord, User>(b, x => x.UserId);
        Fk<PasswordResetToken, User>(b, x => x.UserId);
        Fk<RefreshToken, User>(b, x => x.UserId);
        Fk<Session, Employee>(b, x => x.ActiveEmployeeId);
        Fk<Session, User>(b, x => x.UserId);
        Fk<UserExternalIdentity, User>(b, x => x.UserId);
        Fk<UserMfa, User>(b, x => x.UserId);
        Fk<UserPermissionOverride, User>(b, x => x.UserId);
        Fk<UserRole, PositionAccessTemplate>(b, x => x.SourcePositionAccessTemplateId);
        Fk<UserRole, Position>(b, x => x.SourcePositionId);
        Fk<UserRole, User>(b, x => x.UserId);
    }

    // Task 39e: Calendar.
    private static void ApplyCalendar(ModelBuilder b)
    {
        Fk<Cal.CalendarEventGuest, Cal.CalendarEvent>(b, x => x.EventId);
        Fk<Cal.CalendarEventMeetingAttendance, Cal.CalendarEventMeeting>(b, x => x.CalendarEventMeetingId);
        Fk<Cal.CalendarEventMeetingAttendance, Employee>(b, x => x.EmployeeId);
        Fk<Cal.CalendarEventMeeting, Cal.CalendarEvent>(b, x => x.CalendarEventId);
        Fk<Cal.CalendarEventParticipantRsvpHistory, Employee>(b, x => x.EmployeeId);
        Fk<Cal.CalendarEventParticipantRsvpHistory, Cal.CalendarEventParticipant>(b, x => x.ParticipantId);
        Fk<Cal.CalendarEventParticipant, Employee>(b, x => x.EmployeeId);
        Fk<Cal.CalendarEventParticipant, Cal.CalendarEvent>(b, x => x.EventId);
        Fk<Cal.CalendarEventReminderLog, Employee>(b, x => x.EmployeeId);
        Fk<Cal.CalendarMeetingIncident, Employee>(b, x => x.EmployeeId);
        Fk<Cal.CalendarMeetingIncident, MonitoringException>(b, x => x.ExceptionId);
        Fk<Cal.ExternalCalendarConnection, User>(b, x => x.UserId);
        Fk<Cal.ExternalCalendarEventLink, Cal.CalendarEvent>(b, x => x.CalendarEventId);
        Fk<Cal.ExternalCalendarEventLink, Cal.ExternalCalendarConnection>(b, x => x.ExternalCalendarConnectionId);
        Fk<Cal.HolidayCalendarSettings, LegalEntity>(b, x => x.LegalEntityId);
        Fk<Cal.HolidayCalendarSettings, User>(b, x => x.UpdatedById);
    }

    // Task 39e: Conversations.
    private static void ApplyConversations(ModelBuilder b)
    {
        Fk<ConversationFavorite, Employee>(b, x => x.EmployeeId);
        Fk<ConversationMessageAttachment, FileRecord>(b, x => x.FileRecordId);
        Fk<ConversationMessageHide, Employee>(b, x => x.EmployeeId);
        Fk<ConversationMessageMention, Employee>(b, x => x.EmployeeId);
        Fk<ConversationMessageReaction, Employee>(b, x => x.EmployeeId);
        Fk<ConversationMessageRevision, Employee>(b, x => x.RevisedByEmployeeId);
        Fk<ConversationMessage, Employee>(b, x => x.AuthorEmployeeId);
        Fk<ConversationMessage, Employee>(b, x => x.DeletedByEmployeeId);
        Fk<ConversationMute, Employee>(b, x => x.EmployeeId);
        Fk<ConversationParticipant, Employee>(b, x => x.AddedByEmployeeId);
        Fk<ConversationParticipant, Employee>(b, x => x.EmployeeId);
        Fk<ConversationParticipant, Employee>(b, x => x.RemovedByEmployeeId);
    }

    // Task 39e: Inbox.
    private static void ApplyInbox(ModelBuilder b)
    {
        Fk<InboxDecisionJournalEntry, Employee>(b, x => x.ActorEmployeeId);
        Fk<InboxDecisionJournalEntry, InboxDecisionJournalEntry>(b, x => x.RevertedByJournalId);
    }

    // Task 39e: SharedPlatform.
    private static void ApplySharedPlatform(ModelBuilder b)
    {
        Fk<NotificationDelivery, SharedPlatformNotification>(b, x => x.NotificationId);
        Fk<NotificationDelivery, User>(b, x => x.RecipientUserId);
        Fk<NotificationDelivery, NotificationTemplateVersion>(b, x => x.TemplateVersionId);
        Fk<NotificationTemplateVersion, PlatformUser>(b, x => x.CreatedByUserId);
        Fk<NotificationTemplateVersion, PlatformUser>(b, x => x.PublishedByUserId);
        Fk<NotificationTemplateVersion, PlatformUser>(b, x => x.UpdatedByUserId);
        Fk<SharedPlatformNotification, User>(b, x => x.RecipientUserId);
        Fk<TenantNotificationRule, User>(b, x => x.UpdatedByUserId);
        Fk<TenantSubscription, SubscriptionPlan>(b, x => x.PlanId);
    }

    // Task 39e: InfrastructureModule.
    private static void ApplyInfrastructureModule(ModelBuilder b)
    {
        Fk<Tenant, SubscriptionPlan>(b, x => x.SubscriptionPlanId);
        Fk<User, User>(b, x => x.SuspendedById);
    }

    // Task 39e: Dashboard.
    private static void ApplyDashboard(ModelBuilder b)
    {
        Fk<UserDashboard, User>(b, x => x.UserId);
    }

    private static void Fk<TChild, TParent>(
        ModelBuilder modelBuilder, Expression<Func<TChild, object?>> key, DeleteBehavior onDelete = DeleteBehavior.NoAction)
        where TChild : class
        where TParent : class
        => modelBuilder.Entity<TChild>()
            .HasOne<TParent>()
            .WithMany()
            .HasForeignKey(key)
            .OnDelete(onDelete);
}
