using ONEVO.Application.Common.Authorization;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

/// <summary>
/// Who is allowed to see and act on exception alerts. The reporting manager is whoever approves
/// the employee's attendance and HR is whoever can edit employee records - the same pair the
/// face-verification override alert already uses - so a default Manager / HR role can work the
/// alerts without an admin first granting the separate exceptions:* codes. Holding exceptions:view
/// or exceptions:acknowledge on its own still grants access, scoped to the employees that
/// permission covers.
/// </summary>
public static class ExceptionPermissions
{
    public const string ManagerReview = PermissionCatalog.Attendance.Approve;
    public const string HrReview = PermissionCatalog.Employees.Write;
    public const string HrManage = PermissionCatalog.Exceptions.Manage;
    public const string View = PermissionCatalog.Exceptions.View;
    public const string Acknowledge = PermissionCatalog.Exceptions.Acknowledge;

    public const string DetectedTemplate = "exception_alert_detected";
    public const string EscalatedTemplate = "exception_alert_escalated";
    /// <summary>The bell opens the Inbox item (inbox plan S4.2): inbox:monitoring:exception_case.</summary>
    public const string RelatedEntityType = "inbox:monitoring:exception_case";
}
