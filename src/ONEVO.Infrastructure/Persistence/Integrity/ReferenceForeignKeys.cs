using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using WorkTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Integrity;

/// <summary>
/// Foreign keys for id columns that had none (task 39). They have no navigation properties, so the domain
/// model stays as it is. Every key is NO ACTION: none of these parents is hard-deleted by the application,
/// people are never deleted with their history, and NO ACTION cannot create multiple cascade paths.
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
        Fk<Employee, User>(b, x => x.UserId);
        Fk<Employee, Department>(b, x => x.DepartmentId);
        Fk<Employee, LegalEntity>(b, x => x.LegalEntityId);
        Fk<EmployeeHierarchyClosure, Employee>(b, x => x.AncestorEmployeeId);
        Fk<EmployeeHierarchyClosure, Employee>(b, x => x.DescendantEmployeeId);
        Fk<EmployeeHierarchyClosure, PositionAssignment>(b, x => x.SourcePositionAssignmentId);
        Fk<PositionAssignment, Employee>(b, x => x.ReportsToEmployeeId);
        Fk<ManagementCoverageRecord, Employee>(b, x => x.ResponsibleEmployeeId);
        Fk<ReportingManagerChange, LegalEntity>(b, x => x.LegalEntityId);
        Fk<AccessGrantRequest, PositionAssignment>(b, x => x.ReservedPositionAssignmentId);
        Fk<OnboardingDraft, Employee>(b, x => x.ReportsToEmployeeId);
    }

    // Task 39a: leave. leave_policy_version_id is left for task 39b: a future version can be deleted
    // while a leave request already points at it.
    private static void ApplyLeave(ModelBuilder b)
    {
        Fk<LeaveBalanceAudit, LeavePolicy>(b, x => x.LeavePolicyId);
        Fk<LeaveEntitlement, LeavePolicy>(b, x => x.LeavePolicyId);
    }

    // Task 39a: time and attendance. attendance_alert_deductions.alert_id is left for task 39b: the
    // alert materializer deletes an alert whose day no longer qualifies and keeps its refunded deduction.
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

    // Task 39a: work management. Parents only: tasks, sprints, task categories and task statuses are
    // hard-deleted, so keys pointing at them wait for task 39b.
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
        Fk<TaskDraft, Project>(b, x => x.ProjectId);
        Fk<TaskAssignment, Employee>(b, x => x.EmployeeId);
        Fk<TaskAssignment, User>(b, x => x.UserId);
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
    }

    private static void Fk<TChild, TParent>(ModelBuilder modelBuilder, Expression<Func<TChild, object?>> key)
        where TChild : class
        where TParent : class
        => modelBuilder.Entity<TChild>()
            .HasOne<TParent>()
            .WithMany()
            .HasForeignKey(key)
            .OnDelete(DeleteBehavior.NoAction);
}
