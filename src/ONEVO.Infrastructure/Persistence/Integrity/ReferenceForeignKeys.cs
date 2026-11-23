using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;
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
