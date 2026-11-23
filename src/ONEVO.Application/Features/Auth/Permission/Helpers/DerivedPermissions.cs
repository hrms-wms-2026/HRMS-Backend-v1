using ONEVO.Application.Common.Authorization;

namespace ONEVO.Application.Features.Auth.Permission;

public static class DerivedPermissions
{
    public static readonly HashSet<string> InboxTriggers = new(StringComparer.Ordinal)
    {
        PermissionCatalog.Leave.Approve, PermissionCatalog.Leave.Manage,
        PermissionCatalog.Attendance.Approve,
        PermissionCatalog.Payroll.Approve, PermissionCatalog.Payroll.Run,
        PermissionCatalog.Performance.Write, PermissionCatalog.Performance.Manage,
        PermissionCatalog.Expense.Approve,
        PermissionCatalog.Documents.Approve, PermissionCatalog.Grievance.Manage,
        PermissionCatalog.Monitoring.AlertsRead, PermissionCatalog.Monitoring.AlertsResolve,
        PermissionCatalog.Verification.Review, PermissionCatalog.Workflows.Execute
    };

    public static readonly HashSet<string> NotificationTriggers = new(StringComparer.Ordinal)
    {
        PermissionCatalog.Leave.Approve, PermissionCatalog.Leave.Manage,
        PermissionCatalog.Attendance.Approve, PermissionCatalog.Employees.Write,
        PermissionCatalog.Payroll.Approve, PermissionCatalog.Performance.Manage,
        PermissionCatalog.Monitoring.AlertsRead
    };
}
