namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>
/// Internal application-logic classifier for why IEmployeeAuthorityResolver is being asked to
/// resolve visibility or an approver. Never persisted - purely a call-site hint (e.g. for future
/// per-purpose caching or logging), it does not change resolver behavior in Part 0. Callers pick
/// the closest fit; adding a new value never requires a migration.
/// </summary>
public enum EmployeeAuthorityPurpose
{
    EmployeeListRead,
    TimeTrackingRead,
    AttendanceCorrectionApproval,
    WorkAreaChangeApproval,
    TimeOffApproval,
    OnboardingApproval,
    OffboardingApproval,
    EmployeeLifecycleApproval,
    AttendanceLateNotification,
}
