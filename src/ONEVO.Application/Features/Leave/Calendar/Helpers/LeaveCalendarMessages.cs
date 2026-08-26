namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public static class LeaveCalendarMessages
{
    public const string AuthRequired = "Authentication required.";
    public const string TenantMissing = "Tenant context missing.";
    public const string CalendarPermissionRequired = "Permission 'calendar:read' required.";
    public const string LeaveScopeRequired = "A leave read permission is required to view the leave calendar.";
    public const string NoEmployee = "Employee profile was not found for the current user.";
}
