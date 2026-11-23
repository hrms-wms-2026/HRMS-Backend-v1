using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Infrastructure.Persistence.Integrity;

/// <summary>
/// CHECK (column IN (...)) constraints for status and type columns (task 39). The allowed values come from
/// the C# enum or constants class, so adding a value changes the model: a migration must follow, and
/// StatusCheckConstraintTests fails until it does. Columns still without a constraint are listed, with a
/// reason, in tests/ONEVO.Tests.Architecture/Schema/missing-check-constraints.txt.
/// </summary>
public static class StatusCheckConstraints
{
    /// <summary>One constrained column. <paramref name="ConstantPrefix"/> picks the constants of a class
    /// that also holds other constants (AttendanceRecord.StatusLate and so on).</summary>
    public sealed record Rule(Type Entity, string Table, string Column, Type Values, string? ConstantPrefix = null)
    {
        public string Name => $"ck_{Table}_{Column}";

        public IReadOnlyList<string> AllowedValues() => StatusCheckConstraints.AllowedValues(Values, ConstantPrefix);

        public string Sql() =>
            $"{Column} IN ({string.Join(", ", AllowedValues().Select(v => $"'{v.Replace("'", "''", StringComparison.Ordinal)}'"))})";
    }

    public static IReadOnlyList<Rule> Rules { get; } =
    [
        // Enum-backed columns (stored as the enum name).
        new(typeof(BiometricEnrollmentAttempt), "biometric_enrollment_attempts", "status", typeof(BiometricEnrollmentStatus)),
        new(typeof(BiometricProfile), "biometric_profiles", "status", typeof(BiometricProfileStatus)),
        new(typeof(MonitoringException), "exceptions", "status", typeof(ExceptionStatus)),
        new(typeof(MonitoringException), "exceptions", "type", typeof(ExceptionType)),
        new(typeof(Notification), "monitoring_notifications", "type", typeof(NotificationType)),
        new(typeof(Tenant), "tenants", "status", typeof(TenantStatus)),
        new(typeof(TenantStatusHistory), "tenant_status_histories", "from_status", typeof(TenantStatus)),
        new(typeof(TenantStatusHistory), "tenant_status_histories", "to_status", typeof(TenantStatus)),

        // Constants-backed columns: the tables named in task 39, then core HR, leave, attendance and work.
        new(typeof(LeaveRequest), "leave_requests", "status", typeof(LeaveRequestStatuses)),
        new(typeof(AttendanceRecord), "attendance_records", "status", typeof(AttendanceRecord), "Status"),
        new(typeof(OutboxMessage), "outbox_messages", "status", typeof(OutboxMessageStatus)),
        new(typeof(LeaveRequestApprover), "leave_request_approvers", "status", typeof(LeaveRequestApproverStatuses)),
        new(typeof(LeaveRequestDayAllocation), "leave_request_day_allocations", "status", typeof(LeaveRequestDayAllocationStatuses)),
        new(typeof(LeaveBalanceAudit), "leave_balance_audits", "change_type", typeof(LeaveBalanceChangeTypes)),
        new(typeof(AttendanceAlert), "attendance_alerts", "status", typeof(AttendanceAlertStatuses)),
        new(typeof(AttendanceAlertDeduction), "attendance_alert_deductions", "status", typeof(AttendanceAlertDeductionStatuses)),
        new(typeof(PositionAssignment), "position_assignments", "assignment_status", typeof(PositionAssignmentStatus)),
        new(typeof(OnboardingDraft), "onboarding_drafts", "status", typeof(OnboardingDraftStatus)),
        new(typeof(BulkOnboardingBatch), "bulk_onboarding_batches", "status", typeof(BulkOnboardingBatchStatus)),
        new(typeof(BulkOnboardingBatchRow), "bulk_onboarding_batch_rows", "status", typeof(BulkOnboardingBatchRowStatus)),
        new(typeof(Sprint), "sprints", "status", typeof(SprintStatuses)),
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var rule in Rules)
        {
            modelBuilder.Entity(rule.Entity).ToTable(table => table.HasCheckConstraint(rule.Name, rule.Sql()));
        }
    }

    /// <summary>Enum names in declaration order, or the class's public string constants in declaration order.</summary>
    public static IReadOnlyList<string> AllowedValues(Type values, string? constantPrefix = null)
    {
        if (values.IsEnum)
        {
            return Enum.GetNames(values);
        }

        return values.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Where(field => constantPrefix is null || field.Name.StartsWith(constantPrefix, StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
