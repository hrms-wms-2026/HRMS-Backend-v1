using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.Lifecycle;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Start-up hook for PermissionCatalogSynchronizer: writes the catalogue below and applies
/// PermissionLifecycle (renames, default grants, retirements). The run is idempotent and serialized
/// across instances by an app lock, so running it at every start is safe. Set
/// PermissionCatalog:SyncOnStartup=false to leave it to the deploy step (task 09).
/// </summary>
public class PermissionSeeder : IHostedService
{
    public const string SyncOnStartupKey = "PermissionCatalog:SyncOnStartup";

    private readonly IServiceProvider _services;
    private readonly ILogger<PermissionSeeder> _logger;

    public PermissionSeeder(IServiceProvider services, ILogger<PermissionSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>The permission catalogue as it is written to the permissions table.</summary>
    public static IReadOnlyList<PermissionDefinition> Catalogue { get; } = GetAllPermissions();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = _services.GetService<IConfiguration>();
        if (configuration is not null && !configuration.GetValue(SyncOnStartupKey, true))
        {
            _logger.LogInformation("Permission catalogue sync at start-up is off ({Key}=false).", SyncOnStartupKey);
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            // role_permissions and user_permission_overrides are under RLS: the sync must see every tenant.
            scope.ServiceProvider.GetService<IWritableTenantContext>()?.SetAdminMode();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await SeedPermissionsAsync(db, cancellationToken);
        }
        catch (PermissionRetirementRefusedException ex)
        {
            _logger.LogCritical(ex, "Permission catalogue sync refused a retirement. Startup will stop.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Permission seeder could not run because the database or migrated schema is unavailable. " +
                "Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedPermissionsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var clock = _services.GetService<IDateTimeProvider>() ?? new SystemDateTimeProvider();
        var synchronizer = new PermissionCatalogSynchronizer(db, clock, _logger);
        await synchronizer.SyncAsync(Catalogue, PermissionLifecycle.Rules, ct);
    }

    private static List<PermissionDefinition> GetAllPermissions() =>
    [
        // System
        Perm("*", "Super Admin bypass â€” all access.", "system"),

        // Employees
        Perm("employees:write", "Create, update employees.", "core_hr"),
        Perm("employees:offboard", "Start, cancel, or complete an employee's offboarding.", "core_hr"),
        Perm("employees:read:sensitive", "View sensitive employee data (bank details) on the employee detail screen.", "core_hr"),
        Perm("employees:delete", "Delete employee records.", "core_hr"),
        Perm("checklists:manage", "Create, edit, and archive onboarding and offboarding checklist templates.", "core_hr"),

        // Organization
        Perm("org:read", "View org structure, departments, hierarchy.", "org_structure"),
        Perm("org:manage", "Create and edit org structure, departments.", "org_structure"),
        Perm("legal_entity:create", "Create a legal entity (company) inside the tenant.", "org_structure"),
        Perm("legal_entity:read", "View a legal entity's general settings (read-only).", "org_structure"),
        Perm("legal_entity:update", "Edit a legal entity's general settings.", "org_structure"),
        Perm("legal_entity:delete", "Deactivate (soft-delete) a legal entity.", "org_structure"),

        // Leave (Legacy compatibility alias - to be migrated to time_off. Not used for Module Catalog ownership)
        Perm("leave:read", "View leave records for all employees in scope.", "leave"),
        Perm("leave:read-team", "View leave records for direct reports only.", "leave"),
        Perm("leave:create", "Apply for leave on behalf of others.", "leave"),
        Perm("leave:approve", "Approve or reject leave requests.", "leave"),
        Perm("leave:manage", "Manage leave types, policies, balances.", "leave"),

        // Time Off (Canonical Developer Platform keys)
        Perm("time_off:read", "View time off records for all employees in scope.", "time_off"),
        Perm("time_off:create", "Apply for time off on behalf of others.", "time_off"),
        Perm("time_off:approve", "Approve or reject time off requests.", "time_off"),
        Perm("time_off:manage", "Manage time off types, policies, balances.", "time_off"),

        // Calendar
        Perm("calendar:read", "View company and team calendars.", "calendar"),

        // Monitoring
        Perm("monitoring:read", "View monitoring data.", "activity_monitoring"),
        Perm("activity:read:self", "View your own activity timeline.", "activity_monitoring"),

        // Attendance
        Perm("attendance:read", "View attendance records for all employees in scope.", "core_hr"),
        Perm("attendance:approve", "Approve requests and alerts for your team and escalated records: attendance corrections, location, work mode and device changes, identity verification alerts.", "core_hr"),
        Perm("attendance:write", "Configure clock-in policy and work modes.", "core_hr"),

        // Payroll
        Perm("payroll:read", "View payroll records and salary data.", "payroll"),
        Perm("payroll:write", "Edit payroll records.", "payroll"),
        Perm("payroll:approve", "Approve payroll runs.", "payroll"),
        Perm("payroll:run", "Execute payroll processing.", "payroll"),

        // Performance
        Perm("performance:read", "View performance records for all employees in scope.", "performance"),
        Perm("performance:read-team", "View performance records for direct reports only.", "performance"),
        Perm("performance:write", "Submit and edit performance reviews.", "performance"),
        Perm("performance:manage", "Manage review cycles, templates, goals.", "performance"),

        // Skills
        Perm("skills:read", "View skills profiles across employees.", "skills"),
        Perm("skills:write", "Update own skills profile.", "skills"),
        Perm("skills:write-team", "Update skills for direct reports.", "skills"),
        Perm("skills:validate", "Validate or endorse skills for employees.", "skills"),
        Perm("skills:manage", "Manage skill library and categories.", "skills"),

        // Expense
        Perm("expense:read", "View expense records in scope.", "expense"),
        Perm("expense:create", "Submit expense claims.", "expense"),
        Perm("expense:approve", "Approve or reject expense claims.", "expense"),
        Perm("expense:manage", "Manage expense categories, policies, limits.", "expense"),

        // Calendar
        Perm("calendar:write", "Create and manage calendar events for others.", "calendar"),
        Perm("calendar:admin", "Manage country holiday sync, legal-entity calendar overrides, and calendar integrations.", "calendar"),

        // Notifications
        Perm("notifications:manage", "Manage notification templates and delivery settings.", "notifications"),

        // Settings
        Perm("settings:device", "View biometric device connection status.", "configuration"),
        Perm("settings:device:configure", "Add, remove, and configure biometric device integrations.", "configuration"),

        // Users & Roles
        Perm("users:read", "View user accounts.", "auth"),
        Perm("users:manage", "Create, suspend, and manage user accounts.", "auth"),
        Perm("roles:read", "View roles and their permission sets.", "roles"),
        Perm("roles:manage", "Create and edit roles, assign permissions.", "roles"),

        // Billing
        Perm("billing:read", "View billing history and invoices.", "configuration"),
        Perm("billing:manage", "Manage billing and subscription changes.", "configuration"),

        // Support (Crownus support tickets raised by the tenant)
        Perm("support:manage", "Raise and follow up support tickets with Crownus support.", "configuration"),

        // Integrations
        Perm(
            "integrations:manage",
            "Manage tenant-wide integration enablement, configuration, and other users' connections.",
            "integrations"),

        // Analytics
        Perm("analytics:read", "View analytics data.", "analytics"),
        Perm("analytics:view", "Access analytics dashboards.", "analytics"),
        Perm("analytics:export", "Export analytics reports.", "analytics"),
        Perm("analytics:write", "Create and save custom analytics views.", "analytics"),

        // Monitoring
        Perm("monitoring:configure", "Enable/disable monitoring features, set employee overrides.", "activity_monitoring"),
        Perm("monitoring:screenshots:request", "Request an on-demand screenshot from an active monitored device.", "activity_monitoring"),

        // Exceptions
        Perm("exceptions:view", "View exception alerts.", "exceptions"),
        Perm("exceptions:acknowledge", "Acknowledge and resolve exception alerts.", "exceptions"),
        Perm("exceptions:manage", "Manage exception rules and thresholds.", "exceptions"),

        // Verification
        Perm("verification:view", "View identity verification results.", "verification"),
        Perm("verification:review", "Escalate verification cases.", "verification"),
        Perm("verification:configure", "Configure verification settings and rules.", "verification"),

        // Workforce Intelligence
        Perm("workforce:view", "View workforce intelligence data and reports.", "workforce"),
        Perm("workforce:manage", "Manage workforce intelligence settings.", "workforce"),

        // Documents
        Perm("documents:read", "View documents.", "documents"),
        Perm("documents:write", "Upload and edit documents.", "documents"),
        Perm("documents:approve", "Approve document submissions.", "documents"),
        Perm("documents:manage", "Manage document categories and policies.", "documents"),
        Perm("documents:admin", "Full document administration including deletion and access control.", "documents"),

        // Grievance
        Perm("grievance:read", "View grievance cases.", "grievance"),
        Perm("grievance:write", "Submit grievance cases.", "grievance"),
        Perm("grievance:manage", "Manage and resolve grievance cases.", "grievance"),

        // Reports
        Perm("reports:read", "View reports.", "reports"),
        Perm("reports:create", "Create and run reports.", "reports"),

        // Chat
        Perm("chat:read", "Read chat messages.", "chat"),
        Perm("chat:write", "Send chat messages.", "chat"),
        Perm("chat:manage", "Manage channels, moderate messages.", "chat"),

        // Work Management uses module entitlement plus contextual relationships for ordinary
        // access. Project creation remains an explicitly delegable elevated operation.
        Perm("projects:create", "Create projects where the caller has local creation authority.", "projects"),
    ];

    private static PermissionDefinition Perm(string code, string description, string module) =>
        new(code, description, module);
}

