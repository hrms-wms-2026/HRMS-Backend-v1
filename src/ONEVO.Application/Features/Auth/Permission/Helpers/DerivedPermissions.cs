namespace ONEVO.Application.Features.Auth.Permission;

public static class DerivedPermissions
{
    public static readonly HashSet<string> InboxTriggers = new(StringComparer.Ordinal)
    {
        "leave:approve", "leave:manage",
        "attendance:approve",
        "payroll:approve", "payroll:run",
        "performance:write", "performance:manage",
        "expense:approve", "tasks:approve",
        "documents:approve", "grievance:manage",
        "monitoring:alerts:read", "monitoring:alerts:resolve",
        "verification:review", "workflows:execute"
    };

    public static readonly HashSet<string> NotificationTriggers = new(StringComparer.Ordinal)
    {
        "leave:approve", "leave:manage",
        "attendance:approve", "employees:write",
        "payroll:approve", "performance:manage",
        "monitoring:alerts:read", "tasks:approve"
    };
}
