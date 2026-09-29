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
    public const string ManagerReview = "attendance:approve";
    public const string HrReview = "employees:write";
    public const string HrManage = "exceptions:manage";
    public const string View = "exceptions:view";
    public const string Acknowledge = "exceptions:acknowledge";

    public const string DetectedTemplate = "exception_alert_detected";
    public const string EscalatedTemplate = "exception_alert_escalated";
    public const string RelatedEntityType = "monitoring_exception";
}
